namespace PRo3D.Base.Annotation

open System

open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Base

module AnnotationExport =

    // ------------------------------------------------------- coordinates ---

    let private wrap360 (longitude : float) =
        let l = longitude % 360.0
        if l < 0.0 then l + 360.0 else l

    /// Applies the chosen convention, then the chosen notation. The intermediate
    /// value is always wrapped into [0, 360) so the two settings stay
    /// independent and the result does not depend on the raw value's range.
    let normalizeLongitude (convention : LongitudeConvention) (signed : bool) (longitude : float) =
        let converted =
            match convention with
            | LongitudeConvention.Flipped        -> 360.0 - longitude
            | LongitudeConvention.Shifted        -> longitude + 180.0
            | LongitudeConvention.FlippedShifted -> 180.0 - longitude
            | _                                  -> longitude
            |> wrap360

        if signed && converted > 180.0 then converted - 360.0 else converted

    /// Cartesian -> (latitude, longitude, altitude). `None` when the body has no
    /// geographic frame (Planet.None/JPL/ENU) or the native call fails.
    let tryToGeographic (planet : Planet) (settings : AnnotationExportSettings) (p : V3d) =
        CooTransformation.tryGetLatLonAlt planet p
        |> Option.map (fun coo ->
            V3d(coo.latitude,
                normalizeLongitude settings.longitude settings.signedLongitude coo.longitude,
                coo.altitude))

    let private wantsCartesian (mode : CoordinateMode) =
        mode = CoordinateMode.Cartesian || mode = CoordinateMode.Both

    /// Public because the viewer gates the "no .aara data" refusal on it: a
    /// cartesian export writes no lat/lon, so the source setting is irrelevant
    /// there and must not block an otherwise valid export.
    let wantsGeographic (mode : CoordinateMode) =
        mode = CoordinateMode.Geographic || mode = CoordinateMode.Both

    /// The export asks for geographic coordinates, but the scene's body has no
    /// geographic frame (`Planet.None` / `JPL` / `ENU`). The file is still
    /// written — every lat/lon/alt cell simply comes out empty and GeoJSON
    /// features get a `null` geometry — so this is for the caller to warn about,
    /// not a reason to refuse the export.
    let geographicWithoutFrame (settings : AnnotationExportSettings) (planet : Planet) =
        not (AnnotationExportSettings.hasFixedSchema settings.format)
        && wantsGeographic settings.coordinates
        && CooTransformation.getConvention planet = CooTransformation.NonPlanetary

    // ------------------------------------------------ surface properties ---

    /// Surface-property columns are named after the OPC layer they were sampled
    /// from, and which layers exist is only known once a point has actually hit
    /// a patch. The prefix keeps those data-driven names in a namespace of their
    /// own, so a layer called `alt` can never shadow the altitude column.
    [<Literal>]
    let SurfaceColumnPrefix = "surface_"

    let surfaceColumnName (layer : string) = SurfaceColumnPrefix + layer

    /// Column naming the body the geographic coordinates refer to. GIS tools
    /// only surface *feature*-level properties as attributes, so the
    /// collection-level `planet` — which a reader like QGIS never shows in the
    /// attribute table — is not enough on its own.
    [<Literal>]
    let BodyColumn = "body"

    /// Column recording which routine actually produced this record's lat/lon/alt.
    /// The sources disagree about what the third value is — height above the
    /// spheroid, radial distance from the body centre — so without this a reader
    /// cannot tell what `alt` means. Per record, not per file: a single export
    /// can mix them, because a point the per-vertex data does not cover falls
    /// back to SPICE.
    [<Literal>]
    let LatLonAltSourceColumn = "latLonAltSource"

    /// Which routine `CooTransformation.tryGetLatLonAlt` runs for this body.
    let private spiceSourceToken (planet : Planet) =
        match CooTransformation.getConvention planet with
        | CooTransformation.Planetographic -> VText "spice_recpgr"
        | CooTransformation.Spherical _    -> VText "spice_reclat"
        // planetocentric lat/lon like Spherical, but the altitude is referenced
        // to a tri-axial ellipsoid — a different number, so a different token
        | CooTransformation.Ellipsoidal _  -> VText "spice_ellipsoidal"
        // no geographic frame: lat/lon/alt come out empty, so naming a routine
        // that never ran would be a lie
        | CooTransformation.NonPlanetary   -> VMissing

    /// A point's geographic coordinates together with the provenance of those
    /// numbers. Resolved once per record and used for both the lat/lon/alt
    /// columns and the GeoJSON geometry, so the two can never disagree about
    /// where a point is.
    type private ResolvedGeographic = {
        latLonAlt : Option<V3d>
        source    : ExportValue
    }

    /// Nothing was resolved because nothing was asked for (a cartesian export).
    let private noGeographic = { latLonAlt = None; source = VMissing }

    let private cartesianFields (p : V3d) =
        [ "x", VNum p.X; "y", VNum p.Y; "z", VNum p.Z ]

    let private geographicFields (planet : Planet) (resolved : ResolvedGeographic) =
        [ match resolved.latLonAlt with
          | Some g -> yield! [ "lat", VNum g.X; "lon", VNum g.Y; "alt", VNum g.Z ]
          | None   -> yield! [ "lat", VMissing; "lon", VMissing; "alt", VMissing ]
          yield BodyColumn, VText (string planet)
          yield LatLonAltSourceColumn, resolved.source ]

    /// Derive the coordinates from the point's cartesian position.
    let private fromSpice (planet : Planet) (settings : AnnotationExportSettings) (p : V3d) =
        { latLonAlt = tryToGeographic planet settings p
          source    = spiceSourceToken planet }

    /// Take the coordinates from the patch's per-vertex LonLatRad grid, whose
    /// channels are (longitude, latitude, radius-in-metres).
    ///
    /// The longitude still goes through the convention and range settings: those
    /// are notation transforms of a longitude, not part of deriving one, so they
    /// have to apply whichever source produced it. The radius lands in `alt` —
    /// which is what makes `LatLonAltSourceColumn` necessary.
    let private fromAara (settings : AnnotationExportSettings) (lonLatRadius : V3d) =
        { latLonAlt =
            Some (V3d(lonLatRadius.Y,
                      normalizeLongitude settings.longitude settings.signedLongitude lonLatRadius.X,
                      lonLatRadius.Z))
          source = VText "aara_file" }

    /// lat/lon/alt for one position, honouring the configured source. Shared by
    /// both granularities so a per-annotation row resolves exactly as a
    /// per-point one does, including the fallback and its provenance token.
    let private resolveGeographic
        (planet   : Planet)
        (settings : AnnotationExportSettings)
        (sample   : SurfaceSample)
        (position : V3d) =

        if not (wantsGeographic settings.coordinates) then noGeographic
        else
            match settings.latLonAltSource, sample.lonLatRadius with
            | LatLonAltSource.AaraFile, Some lonLatRadius -> fromAara settings lonLatRadius
            // Either SPICE was asked for, or the per-vertex data does not cover
            // this position. Falling back keeps the row rather than blanking it,
            // and its source column says which happened.
            | _ -> fromSpice planet settings position

    // ------------------------------------------------------------ points ---

    /// One exported point: its position plus which segment it came from.
    type private ResolvedPoint = {
        position      : V3d
        segmentIndex  : Option<int>
        segmentLength : Option<float>
    }

    /// Mirrors `Annotation.retrievePoints` (including its duplicated vertices at
    /// segment joints) while additionally tracking segment membership, which is
    /// what makes the per-segment length columns possible.
    let private resolvePoints (useSampledPoints : bool) (a : Annotation) : list<ResolvedPoint> =
        if useSampledPoints && a.segments.Count > 0 then
            a.segments
            |> IndexList.toList
            |> List.mapi (fun segmentIndex segment ->
                let length = Calculations.getSegmentDistance segment
                [ yield segment.startPoint
                  yield! (segment.points |> IndexList.toList)
                  yield segment.endPoint ]
                |> List.map (fun p ->
                    { position = p; segmentIndex = Some segmentIndex; segmentLength = Some length }))
            |> List.concat
        else
            // control points only: point i closes the segment i-1 -> i
            let segmentLengths =
                a.segments |> IndexList.toList |> List.map Calculations.getSegmentDistance

            let points = a.points |> IndexList.toList

            points
            |> List.mapi (fun i p ->
                if i = 0 then
                    { position = p; segmentIndex = None; segmentLength = None }
                else
                    let length =
                        match segmentLengths |> List.tryItem (i - 1) with
                        | Some draped -> Some draped
                        | None ->
                            // No stored segment — an annotation drawn with a linear
                            // projection has none, so the stretch between two picked
                            // points *is* the straight hop. Mirrors the fallback
                            // `Calculations.calcResultsLine` uses for wayLength;
                            // without it the column would just be blank.
                            points
                            |> List.tryItem (i - 1)
                            |> Option.map (fun previous -> Vec.distance previous p)

                    { position      = p
                      segmentIndex  = Some (i - 1)
                      segmentLength = length })

    // ------------------------------------------------------------ schema ---

    let private coordinateColumns (mode : CoordinateMode) =
        [ if wantsCartesian mode then yield! [ "x"; "y"; "z" ]
          if wantsGeographic mode then yield! [ "lat"; "lon"; "alt"; BodyColumn; LatLonAltSourceColumn ] ]

    /// The columns that follow from the settings alone. The surface-property
    /// columns are *not* in here — their names come from the OPC layers the
    /// points turned out to hit, so they can only be discovered from the built
    /// records; use `schemaFor` for the complete list.
    let schemaOf (settings : AnnotationExportSettings) : list<string> =
        [
            yield! settings.annotationFields |> List.map AnnotationFields.columnName

            match settings.granularity with
            | ExportGranularity.PerAnnotation ->
                yield! coordinateColumns settings.coordinates
            | _ ->
                let hasPointField f = settings.pointFields |> List.contains f

                if hasPointField PointField.Index then yield AnnotationFields.pointColumnName PointField.Index
                if hasPointField PointField.SegmentIndex then yield AnnotationFields.pointColumnName PointField.SegmentIndex

                if hasPointField PointField.Cartesian && wantsCartesian settings.coordinates then
                    yield! [ "x"; "y"; "z" ]
                if hasPointField PointField.Geographic && wantsGeographic settings.coordinates then
                    yield! [ "lat"; "lon"; "alt"; BodyColumn; LatLonAltSourceColumn ]

                if hasPointField PointField.StepLength then yield AnnotationFields.pointColumnName PointField.StepLength
                if hasPointField PointField.SegmentLength then yield AnnotationFields.pointColumnName PointField.SegmentLength
                if hasPointField PointField.CumulativeDistance then yield AnnotationFields.pointColumnName PointField.CumulativeDistance
                if hasPointField PointField.GroundDistance then yield AnnotationFields.pointColumnName PointField.GroundDistance

                // The surface-property columns are appended after these by
                // `schemaFor`, since only the records know which layers exist.
        ]

    /// Columns carried by `records` that `schemaOf` does not name — the
    /// surface-property ones. Ordered by first appearance, so the same scene
    /// exported twice produces the same table.
    let private discoveredColumns (schema : list<string>) (records : list<ExportRecord>) =
        let known = System.Collections.Generic.HashSet<string>(schema)
        let extra = ResizeArray<string>()
        for record in records do
            for (column, _) in record.fields do
                // Add returns false for a column already named by the schema or
                // already discovered on an earlier record.
                if known.Add column then extra.Add column
        extra |> List.ofSeq

    /// The exact, ordered column list of the export. The CSV writer uses it as
    /// the header; every record is projected onto it, so a heterogeneous set of
    /// annotations — or points that hit patches with different layers — still
    /// yields a rectangular table.
    let schemaFor (settings : AnnotationExportSettings) (records : list<ExportRecord>) : list<string> =
        let schema = schemaOf settings
        schema @ discoveredColumns schema records

    // ---------------------------------------------------------- geometry ---

    /// `resolved` is the already-resolved geographic value of `p`. Passing it in
    /// rather than recomputing it is what keeps a feature's geometry and its
    /// lat/lon/alt properties from disagreeing once there is more than one
    /// possible source for them.
    let private toGeoJsonPosition
        (settings : AnnotationExportSettings)
        (p        : V3d)
        (resolved : ResolvedGeographic) =

        if wantsGeographic settings.coordinates then
            // GeoJSON positions are [longitude, latitude, altitude]
            resolved.latLonAlt |> Option.map (fun g -> V3d(g.Y, g.X, g.Z))
        else
            Some p

    /// A whole-annotation geometry always comes from SPICE: it spans many points,
    /// so there is no single place to sample a per-vertex layer at.
    let private toGeoJsonPositionFromSpice
        (settings : AnnotationExportSettings)
        (planet   : Planet)
        (p        : V3d) =

        toGeoJsonPosition settings p (fromSpice planet settings p)

    let private annotationGeometry
        (settings : AnnotationExportSettings)
        (planet   : Planet)
        (a        : Annotation)
        (points   : list<V3d>) =

        let positions = points |> List.choose (toGeoJsonPositionFromSpice settings planet)

        if positions.Length <> points.Length then
            Log.warn "[AnnotationExport] coordinate conversion failed for annotation %A; geometry omitted" a.key
            None
        else
            match a.geometry, positions with
            | _, [] -> None
            | Geometry.Point, p :: _ -> Some (GPoint p)
            | Geometry.Line, _ | Geometry.Polyline, _ | Geometry.TT, _ -> Some (GLine positions)
            | _ ->
                // Polygon / DnS / the ellipse variants are all closed rings.
                // Unlike the exporter this replaces, unhandled geometry kinds
                // degrade to a ring instead of throwing mid-export.
                Some (GRing positions)

    // ----------------------------------------------------------- records ---

    let private annotationFieldPairs
        (settings : AnnotationExportSettings)
        (groupPath : HashMap<Guid, list<string>>)
        (up       : V3d)
        (a        : Annotation) =

        settings.annotationFields
        |> List.map (fun field ->
            AnnotationFields.columnName field, AnnotationFields.valueOf groupPath up field a)

    let private perAnnotationRecord
        (settings : AnnotationExportSettings)
        (sampler  : Option<SurfacePropertySampler>)
        (groupPath : HashMap<Guid, list<string>>)
        (planet   : Planet)
        (up       : V3d)
        (a        : Annotation)
        (points   : list<V3d>) =

        // A single record cannot carry the whole polyline, so the position is
        // the bounding-box centre — the same choice the CSV export always made.
        let centre =
            match points with
            | [] -> V3d.NaN
            | _  -> Box3d(points).Center

        // The centre is computed, not picked: for anything but a straight line
        // it floats above the terrain, so the ray cast can miss even where the
        // surface does ship the layer. `resolveGeographic` then falls back to
        // SPICE and records that in the source column, so the row still says
        // where its numbers came from. Only pay for the cast when the file
        // source was actually asked for.
        let sample =
            match sampler with
            | Some sampleAt when settings.latLonAltSource = LatLonAltSource.AaraFile
                                 && wantsGeographic settings.coordinates
                                 && not points.IsEmpty -> sampleAt centre
            | _ -> SurfaceSample.empty

        let coordinates =
            [ if wantsCartesian settings.coordinates then yield! cartesianFields centre
              if wantsGeographic settings.coordinates then
                  yield! geographicFields planet (resolveGeographic planet settings sample centre) ]

        { fields   = annotationFieldPairs settings groupPath up a @ coordinates
          geometry =
            if settings.format = ExportFormat.GeoJson then
                annotationGeometry settings planet a points
            else None }

    let private perPointRecords
        (settings : AnnotationExportSettings)
        (sampler  : Option<SurfacePropertySampler>)
        (groupPath : HashMap<Guid, list<string>>)
        (planet   : Planet)
        (up       : V3d)
        (a        : Annotation)
        (resolved : list<ResolvedPoint>) =

        let annotationPairs = annotationFieldPairs settings groupPath up a
        let hasPointField f = settings.pointFields |> List.contains f

        /// Project a point onto the reference surface, dropping its height. The
        /// old profile export did this before measuring, which is what made its
        /// distance column the horizontal run rather than the slanted path.
        let flatten (p : V3d) =
            CooTransformation.tryGetLatLonAlt planet p
            |> Option.bind (fun coo ->
                CooTransformation.tryGetXYZFromLatLonAlt { coo with altitude = 0.0 } planet)

        let mutable cumulative = 0.0
        let mutable previous = None

        // Tracked separately: a point whose height cannot be removed (no
        // geographic frame, or a failed native call) leaves the ground total
        // untouched and reports missing, rather than aborting the export as the
        // old profile handler did.
        let mutable groundCumulative = 0.0
        let mutable previousGround = None

        resolved
        |> List.mapi (fun index point ->
            let step =
                match previous with
                | Some p -> Vec.distance p point.position
                | None   -> 0.0
            cumulative <- cumulative + step
            previous <- Some point.position

            let ground =
                match flatten point.position with
                | None -> VMissing
                | Some flattened ->
                    match previousGround with
                    | Some p -> groundCumulative <- groundCumulative + Vec.distance p flattened
                    | None   -> ()
                    previousGround <- Some flattened
                    VNum groundCumulative

            // One re-pick per point, serving both the geographic coordinates and
            // the surface-property columns — the KdTree hit is what costs, so
            // asking for both must not ray-cast twice.
            let sample =
                match sampler with
                | Some sampleAt -> sampleAt point.position
                | None          -> SurfaceSample.empty

            let geographic = resolveGeographic planet settings sample point.position

            let pointPairs =
                [ if hasPointField PointField.Index then
                      yield AnnotationFields.pointColumnName PointField.Index, VInt index
                  if hasPointField PointField.SegmentIndex then
                      yield AnnotationFields.pointColumnName PointField.SegmentIndex,
                            (match point.segmentIndex with Some s -> VInt s | None -> VMissing)

                  if hasPointField PointField.Cartesian && wantsCartesian settings.coordinates then
                      yield! cartesianFields point.position
                  if hasPointField PointField.Geographic && wantsGeographic settings.coordinates then
                      yield! geographicFields planet geographic

                  if hasPointField PointField.StepLength then
                      yield AnnotationFields.pointColumnName PointField.StepLength, VNum step
                  if hasPointField PointField.SegmentLength then
                      yield AnnotationFields.pointColumnName PointField.SegmentLength,
                            (match point.segmentLength with Some l -> VNum l | None -> VMissing)
                  if hasPointField PointField.CumulativeDistance then
                      yield AnnotationFields.pointColumnName PointField.CumulativeDistance, VNum cumulative
                  if hasPointField PointField.GroundDistance then
                      yield AnnotationFields.pointColumnName PointField.GroundDistance, ground ]

            // The OPC scalar / texture layers underneath this position, from the
            // same sample. Which layers those are depends on the patch that was
            // hit, so a point that missed every surface simply contributes no
            // pairs and its cells come out empty.
            let surfacePairs = sample.properties

            { fields   = annotationPairs @ pointPairs @ surfacePairs
              geometry =
                if settings.format = ExportFormat.GeoJson then
                    toGeoJsonPosition settings point.position geographic |> Option.map GPoint
                else None })

    /// Turns the selected annotations into the flat, ordered records the writers
    /// consume. `sampler` adds the surface-property columns; `None` leaves them
    /// out, and it is ignored for per-annotation granularity, which has no point
    /// to sample at.
    let buildRecords
        (settings : AnnotationExportSettings)
        (sampler  : Option<SurfacePropertySampler>)
        (groupPath : HashMap<Guid, list<string>>)
        (planet   : Planet)
        (up       : V3d)
        (annotations : list<Annotation>)
        : list<ExportRecord> =

        annotations
        |> List.collect (fun a ->
            let resolved = resolvePoints settings.useSampledPoints a
            match settings.granularity with
            | ExportGranularity.PerAnnotation ->
                [ perAnnotationRecord settings sampler groupPath planet up a (resolved |> List.map (fun r -> r.position)) ]
            | _ ->
                perPointRecords settings sampler groupPath planet up a resolved)

    /// Writes the export. `Attitude` keeps its own fixed-schema writer.
    let write
        (settings : AnnotationExportSettings)
        (sampler  : Option<SurfacePropertySampler>)
        (groupPath : HashMap<Guid, list<string>>)
        (planet   : Planet)
        (up       : V3d)
        (path     : string)
        (annotations : list<Annotation>)
        : unit =

        match settings.format with
        | ExportFormat.Attitude ->
            AttitudeExport.writeAttitudeJson path up annotations
        | ExportFormat.ContinuousGeoJson ->
            // arms a background export instead of writing once, so it is handled
            // by the caller that owns the drawing model — never here
            Log.warn "[AnnotationExport] the continuous export is not written through this path"
        | format ->
            let records = buildRecords settings sampler groupPath planet up annotations
            match format with
            | ExportFormat.GeoJson ->
                let body =
                    if wantsGeographic settings.coordinates then Some (string planet) else None
                ExportWriters.writeGeoJson path body records
            | _ ->
                ExportWriters.writeCsv path (schemaFor settings records) records
