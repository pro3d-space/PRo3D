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

    let private wantsGeographic (mode : CoordinateMode) =
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

    /// Column naming the body the geographic coordinates refer to. GIS tools
    /// only surface *feature*-level properties as attributes, so the
    /// collection-level `planet` — which a reader like QGIS never shows in the
    /// attribute table — is not enough on its own.
    [<Literal>]
    let BodyColumn = "body"

    let private cartesianFields (p : V3d) =
        [ "x", VNum p.X; "y", VNum p.Y; "z", VNum p.Z ]

    let private geographicFields (planet : Planet) (latLonAlt : Option<V3d>) =
        let body = BodyColumn, VText (string planet)
        match latLonAlt with
        | Some g -> [ "lat", VNum g.X; "lon", VNum g.Y; "alt", VNum g.Z; body ]
        | None   -> [ "lat", VMissing; "lon", VMissing; "alt", VMissing; body ]

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
          if wantsGeographic mode then yield! [ "lat"; "lon"; "alt"; BodyColumn ] ]

    /// The exact, ordered column list of the export. The CSV writer uses it as
    /// the header; every record is projected onto it, so a heterogeneous set of
    /// annotations still yields a rectangular table.
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
                    yield! [ "lat"; "lon"; "alt"; BodyColumn ]

                if hasPointField PointField.StepLength then yield AnnotationFields.pointColumnName PointField.StepLength
                if hasPointField PointField.SegmentLength then yield AnnotationFields.pointColumnName PointField.SegmentLength
                if hasPointField PointField.CumulativeDistance then yield AnnotationFields.pointColumnName PointField.CumulativeDistance
                if hasPointField PointField.GroundDistance then yield AnnotationFields.pointColumnName PointField.GroundDistance

                // Per-point surface properties (OPC scalar/texture layers sampled
                // at the point) will add their columns here — see `perPointRecords`.
        ]

    // ---------------------------------------------------------- geometry ---

    let private toGeoJsonPosition (settings : AnnotationExportSettings) (planet : Planet) (p : V3d) =
        if wantsGeographic settings.coordinates then
            // GeoJSON positions are [longitude, latitude, altitude]
            tryToGeographic planet settings p
            |> Option.map (fun g -> V3d(g.Y, g.X, g.Z))
        else
            Some p

    let private annotationGeometry
        (settings : AnnotationExportSettings)
        (planet   : Planet)
        (a        : Annotation)
        (points   : list<V3d>) =

        let positions = points |> List.choose (toGeoJsonPosition settings planet)

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

        let coordinates =
            [ if wantsCartesian settings.coordinates then yield! cartesianFields centre
              if wantsGeographic settings.coordinates then
                  yield! geographicFields planet (tryToGeographic planet settings centre) ]

        { fields   = annotationFieldPairs settings groupPath up a @ coordinates
          geometry =
            if settings.format = ExportFormat.GeoJson then
                annotationGeometry settings planet a points
            else None }

    let private perPointRecords
        (settings : AnnotationExportSettings)
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

            let geographic =
                if wantsGeographic settings.coordinates then
                    tryToGeographic planet settings point.position
                else None

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

            // Placeholder: per-point surface properties (OPC scalar / texture
            // layers at this position) are not sampled yet. They will be
            // appended here once the .aara reader lands, and their column names
            // added in `schemaOf`.
            let surfacePairs : list<string * ExportValue> = []

            { fields   = annotationPairs @ pointPairs @ surfacePairs
              geometry =
                if settings.format = ExportFormat.GeoJson then
                    toGeoJsonPosition settings planet point.position |> Option.map GPoint
                else None })

    /// Turns the selected annotations into the flat, ordered records the writers
    /// consume.
    let buildRecords
        (settings : AnnotationExportSettings)
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
                [ perAnnotationRecord settings groupPath planet up a (resolved |> List.map (fun r -> r.position)) ]
            | _ ->
                perPointRecords settings groupPath planet up a resolved)

    /// Writes the export. `Attitude` keeps its own fixed-schema writer.
    let write
        (settings : AnnotationExportSettings)
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
            let records = buildRecords settings groupPath planet up annotations
            match format with
            | ExportFormat.GeoJson ->
                let body =
                    if wantsGeographic settings.coordinates then Some (string planet) else None
                ExportWriters.writeGeoJson path body records
            | _ ->
                ExportWriters.writeCsv path (schemaOf settings) records
