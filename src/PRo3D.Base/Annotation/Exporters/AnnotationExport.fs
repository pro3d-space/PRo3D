namespace PRo3D.Base.Annotation

open System

open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Base

module AnnotationExport =

    // ------------------------------------------------------- coordinates ---

    let private normalizeLongitude (convention : LongitudeConvention) (longitude : float) =
        match convention with
        | LongitudeConvention.Native -> longitude
        | _ ->
            let flipped =
                let l = (360.0 - longitude) % 360.0
                if l < 0.0 then l + 360.0 else l
            if convention = LongitudeConvention.Signed && flipped > 180.0 then flipped - 360.0
            else flipped

    /// Cartesian -> (latitude, longitude, altitude). `None` when the body has no
    /// geographic frame (Planet.None/JPL/ENU) or the native call fails.
    let tryToGeographic (planet : Planet) (convention : LongitudeConvention) (p : V3d) =
        CooTransformation.tryGetLatLonAlt planet p
        |> Option.map (fun coo ->
            V3d(coo.latitude, normalizeLongitude convention coo.longitude, coo.altitude))

    let private wantsCartesian (mode : CoordinateMode) =
        mode = CoordinateMode.Cartesian || mode = CoordinateMode.Both

    let private wantsGeographic (mode : CoordinateMode) =
        mode = CoordinateMode.Geographic || mode = CoordinateMode.Both

    let private cartesianFields (p : V3d) =
        [ "x", VNum p.X; "y", VNum p.Y; "z", VNum p.Z ]

    let private geographicFields (latLonAlt : Option<V3d>) =
        match latLonAlt with
        | Some g -> [ "lat", VNum g.X; "lon", VNum g.Y; "alt", VNum g.Z ]
        | None   -> [ "lat", VMissing; "lon", VMissing; "alt", VMissing ]

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

            a.points
            |> IndexList.toList
            |> List.mapi (fun i p ->
                if i = 0 then
                    { position = p; segmentIndex = None; segmentLength = None }
                else
                    { position      = p
                      segmentIndex  = Some (i - 1)
                      segmentLength = segmentLengths |> List.tryItem (i - 1) })

    // ------------------------------------------------------------ schema ---

    let private coordinateColumns (mode : CoordinateMode) =
        [ if wantsCartesian mode then yield! [ "x"; "y"; "z" ]
          if wantsGeographic mode then yield! [ "lat"; "lon"; "alt" ] ]

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
                    yield! [ "lat"; "lon"; "alt" ]

                if hasPointField PointField.StepLength then yield AnnotationFields.pointColumnName PointField.StepLength
                if hasPointField PointField.SegmentLength then yield AnnotationFields.pointColumnName PointField.SegmentLength
                if hasPointField PointField.CumulativeDistance then yield AnnotationFields.pointColumnName PointField.CumulativeDistance

                // Per-point surface properties (OPC scalar/texture layers sampled
                // at the point) will add their columns here — see `perPointRecords`.
        ]

    // ---------------------------------------------------------- geometry ---

    let private toGeoJsonPosition (settings : AnnotationExportSettings) (planet : Planet) (p : V3d) =
        if wantsGeographic settings.coordinates then
            // GeoJSON positions are [longitude, latitude, altitude]
            tryToGeographic planet settings.longitude p
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
        (lookUp   : HashMap<Guid, string>)
        (up       : V3d)
        (a        : Annotation) =

        settings.annotationFields
        |> List.map (fun field ->
            AnnotationFields.columnName field, AnnotationFields.valueOf lookUp up field a)

    let private perAnnotationRecord
        (settings : AnnotationExportSettings)
        (lookUp   : HashMap<Guid, string>)
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
                  yield! geographicFields (tryToGeographic planet settings.longitude centre) ]

        { fields   = annotationFieldPairs settings lookUp up a @ coordinates
          geometry =
            if settings.format = ExportFormat.GeoJson then
                annotationGeometry settings planet a points
            else None }

    let private perPointRecords
        (settings : AnnotationExportSettings)
        (lookUp   : HashMap<Guid, string>)
        (planet   : Planet)
        (up       : V3d)
        (a        : Annotation)
        (resolved : list<ResolvedPoint>) =

        let annotationPairs = annotationFieldPairs settings lookUp up a
        let hasPointField f = settings.pointFields |> List.contains f

        let mutable cumulative = 0.0
        let mutable previous = None

        resolved
        |> List.mapi (fun index point ->
            let step =
                match previous with
                | Some p -> Vec.distance p point.position
                | None   -> 0.0
            cumulative <- cumulative + step
            previous <- Some point.position

            let geographic =
                if wantsGeographic settings.coordinates then
                    tryToGeographic planet settings.longitude point.position
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
                      yield! geographicFields geographic

                  if hasPointField PointField.StepLength then
                      yield AnnotationFields.pointColumnName PointField.StepLength, VNum step
                  if hasPointField PointField.SegmentLength then
                      yield AnnotationFields.pointColumnName PointField.SegmentLength,
                            (match point.segmentLength with Some l -> VNum l | None -> VMissing)
                  if hasPointField PointField.CumulativeDistance then
                      yield AnnotationFields.pointColumnName PointField.CumulativeDistance, VNum cumulative ]

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
        (lookUp   : HashMap<Guid, string>)
        (planet   : Planet)
        (up       : V3d)
        (annotations : list<Annotation>)
        : list<ExportRecord> =

        annotations
        |> List.collect (fun a ->
            let resolved = resolvePoints settings.useSampledPoints a
            match settings.granularity with
            | ExportGranularity.PerAnnotation ->
                [ perAnnotationRecord settings lookUp planet up a (resolved |> List.map (fun r -> r.position)) ]
            | _ ->
                perPointRecords settings lookUp planet up a resolved)

    /// Writes the export. `Attitude` keeps its own fixed-schema writer.
    let write
        (settings : AnnotationExportSettings)
        (lookUp   : HashMap<Guid, string>)
        (planet   : Planet)
        (up       : V3d)
        (path     : string)
        (annotations : list<Annotation>)
        : unit =

        match settings.format with
        | ExportFormat.Attitude ->
            AttitudeExport.writeAttitudeJson path up annotations
        | format ->
            let records = buildRecords settings lookUp planet up annotations
            match format with
            | ExportFormat.GeoJson ->
                let body =
                    if wantsGeographic settings.coordinates then Some (string planet) else None
                ExportWriters.writeGeoJson path body records
            | _ ->
                ExportWriters.writeCsv path (schemaOf settings) records
