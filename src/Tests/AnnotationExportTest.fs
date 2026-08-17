module AnnotationExportTest

open System
open System.Globalization
open System.Threading

open Expecto
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core

/// Builds a Polyline annotation whose points and segments are known, so the
/// derived length columns can be checked against hand-computed values.
///
/// Layout: three control points on the X axis, 10 m apart, with one interior
/// sample point per segment offset in Y — the surface-following segments are
/// therefore longer than the straight control-point distance.
let private testAnnotation () =
    let p0 = V3d(0.0, 0.0, 0.0)
    let p1 = V3d(10.0, 0.0, 0.0)
    let p2 = V3d(20.0, 0.0, 0.0)

    let segment (a : V3d) (b : V3d) (interior : V3d) =
        { startPoint = a
          endPoint   = b
          points     = IndexList.ofList [ interior ] }

    { Annotation.initial with
        key         = Guid.NewGuid()
        text        = "test, annotation"          // contains the CSV separator on purpose
        surfaceName = "surface A"
        geometry    = Geometry.Polyline
        semantic    = Semantic.Horizon1
        points      = IndexList.ofList [ p0; p1; p2 ]
        segments    = IndexList.ofList [ segment p0 p1 (V3d(5.0, 3.0, 0.0))
                                         segment p1 p2 (V3d(15.0, 3.0, 0.0)) ]
        results     = Some { AnnotationResults.initial with length = 20.0; wayLength = 23.323807579381203 } }

/// length of one test segment: two legs of a 5-3 right triangle
let private expectedSegmentLength =
    2.0 * sqrt (5.0 * 5.0 + 3.0 * 3.0)

let private csvSettings granularity fields pointFields =
    { AnnotationExportSettings.initial with
        format           = ExportFormat.Csv
        granularity      = granularity
        coordinates      = CoordinateMode.Cartesian
        annotationFields = fields
        pointFields      = pointFields }

let private records settings annotation =
    AnnotationExport.buildRecords settings HashMap.empty Planet.None V3d.OOI [ annotation ]

let private cell (column : string) (record : ExportRecord) =
    record.fields |> List.tryFind (fst >> (=) column) |> Option.map snd

let private number (column : string) (record : ExportRecord) =
    match cell column record with
    | Some (VNum v) -> Some v
    | _ -> None

let tests () =
    testList "annotation export" [

        test "per-annotation granularity yields exactly one record with the centre coordinate" {
            let annotation = testAnnotation ()
            let settings = csvSettings ExportGranularity.PerAnnotation [ AnnotationField.Text ] []
            let rows = records settings annotation

            Expect.hasLength rows 1 "one row per annotation"
            match rows with
            | [ row ] ->
                // bounding box of the polyline is x in [0,20], y in [0,3]
                Expect.equal (number "x" row) (Some 10.0) "centre x"
                Expect.equal (number "y" row) (Some 1.5) "centre y"
                Expect.equal (number "z" row) (Some 0.0) "centre z"
            | _ -> failtest "expected exactly one row"
        }

        test "per-point granularity yields one record per sampled point" {
            let annotation = testAnnotation ()
            let settings = csvSettings ExportGranularity.PerPoint [] [ PointField.Index; PointField.Cartesian ]
            let rows = records settings annotation

            // two segments, each start + interior + end, joints duplicated
            Expect.hasLength rows 6 "one row per sampled point"
            Expect.equal (rows |> List.map (cell "pointIndex"))
                         ([ 0 .. 5 ] |> List.map (VInt >> Some))
                         "point indices run consecutively"
        }

        test "control points only when sampled points are switched off" {
            let annotation = testAnnotation ()
            let settings =
                { csvSettings ExportGranularity.PerPoint [] [ PointField.Index ] with
                    useSampledPoints = false }

            Expect.hasLength (records settings annotation) 3 "one row per control point"
        }

        test "segment lengths sum to the total way length" {
            let annotation = testAnnotation ()
            let settings =
                csvSettings ExportGranularity.PerPoint []
                    [ PointField.SegmentIndex; PointField.SegmentLength; PointField.CumulativeDistance ]
            let rows = records settings annotation

            let perSegment =
                rows
                |> List.choose (fun row ->
                    match cell "segmentIndex" row, number "segmentLength" row with
                    | Some (VInt index), Some length -> Some (index, length)
                    | _ -> None)
                |> List.distinctBy fst

            Expect.hasLength perSegment 2 "two distinct segments"
            for (_, length) in perSegment do
                Expect.floatClose Accuracy.high length expectedSegmentLength "segment length"

            let total = perSegment |> List.sumBy snd
            let lastCumulative =
                rows |> List.tryLast |> Option.bind (number "distance")

            Expect.floatClose Accuracy.high total (2.0 * expectedSegmentLength) "segments sum to the way length"
            match lastCumulative with
            | Some cumulative ->
                Expect.floatClose Accuracy.high cumulative total "cumulative distance matches the summed segments"
            | None -> failtest "no cumulative distance in the last row"
        }

        test "step length is the distance to the previous point" {
            let annotation = testAnnotation ()
            let settings = csvSettings ExportGranularity.PerPoint [] [ PointField.StepLength ]
            let rows = records settings annotation

            match rows |> List.map (number "stepLength") with
            | Some first :: rest ->
                Expect.equal first 0.0 "the first point has no predecessor"
                // 0-based leg of the 5-3 triangle, then back down, then the
                // zero-length hop across the duplicated segment joint
                let expected = sqrt (5.0 * 5.0 + 3.0 * 3.0)
                let nonZero = rest |> List.choose id |> List.filter (fun v -> v > 1e-9)
                Expect.hasLength nonZero 4 "four non-degenerate steps"
                for step in nonZero do
                    Expect.floatClose Accuracy.high step expected "step length"
            | _ -> failtest "expected step lengths on every row"
        }

        test "schema drives the CSV header and column order" {
            let settings =
                csvSettings ExportGranularity.PerPoint
                    [ AnnotationField.Text; AnnotationField.WayLength ]
                    [ PointField.Index; PointField.Cartesian; PointField.CumulativeDistance ]

            Expect.equal
                (AnnotationExport.schemaOf settings)
                [ "text"; "wayLength"; "pointIndex"; "x"; "y"; "z"; "distance" ]
                "schema follows the settings, in declaration order"
        }

        test "geographic exports name the body per feature" {
            // a collection-level property is not an attribute in GIS tools, so
            // the body has to be a field of its own
            let settings =
                { csvSettings ExportGranularity.PerAnnotation [ AnnotationField.Key ] [] with
                    coordinates = CoordinateMode.Geographic }

            Expect.equal
                (AnnotationExport.schemaOf settings)
                [ "key"; "lat"; "lon"; "alt"; "body" ]
                "body accompanies the geographic coordinates"

            match records settings (testAnnotation ()) with
            | [ row ] -> Expect.equal (cell "body" row) (Some (VText "None")) "body names the reference body"
            | _ -> failtest "expected exactly one row"
        }

        test "the group path keeps nesting that groupName loses" {
            let annotation = testAnnotation ()
            let groupPath = HashMap.ofList [ annotation.key, [ "Outcrop A"; "Bedding" ] ]
            let settings =
                csvSettings ExportGranularity.PerAnnotation
                    [ AnnotationField.GroupName; AnnotationField.GroupPath ] []

            match AnnotationExport.buildRecords settings groupPath Planet.None V3d.OOI [ annotation ] with
            | [ row ] ->
                Expect.equal (cell "groupPath" row) (Some (VText "Outcrop A/Bedding")) "full path"
                Expect.equal (cell "groupName" row) (Some (VText "Bedding")) "innermost group only"
            | _ -> failtest "expected exactly one row"
        }

        test "colorHex is GIS-usable while color stays exact" {
            let annotation = { testAnnotation () with color = { c = C4b(18uy, 52uy, 86uy, 255uy) } }
            let settings =
                csvSettings ExportGranularity.PerAnnotation
                    [ AnnotationField.Color; AnnotationField.ColorHex ] []

            match records settings annotation with
            | [ row ] ->
                Expect.equal (cell "colorHex" row) (Some (VText "#123456")) "hex form for symbol styling"
                match cell "color" row with
                | Some (VText raw) ->
                    // must survive C4b.Parse so a later reimport is exact
                    Expect.equal (C4b.Parse raw) annotation.color.c "color round-trips through C4b.Parse"
                | _ -> failtest "no color cell"
            | _ -> failtest "expected exactly one row"
        }

        test "the annotation key is exported even when unticked" {
            // it is the only stable handle for matching a feature back to its
            // annotation after a GIS round trip
            let model =
                { AnnotationExportModel.initial with
                    annotationFields = FSharp.Data.Adaptive.HashSet.ofList [ AnnotationField.Text ] }

            let settings = AnnotationExportModel.toSettings model
            Expect.contains settings.annotationFields AnnotationField.Key "key is forced into the export"

            let cleared = AnnotationExportApp.update model (SetAllAnnotationFields false)
            Expect.contains
                (AnnotationExportModel.toSettings cleared).annotationFields
                AnnotationField.Key
                "even after deselecting everything"
        }

        test "CSV is culture-invariant and quotes cells containing the separator" {
            let annotation = testAnnotation ()
            let settings =
                csvSettings ExportGranularity.PerAnnotation
                    [ AnnotationField.Text; AnnotationField.WayLength ] []

            let path = IO.Path.Combine(IO.Path.GetTempPath(), IO.Path.GetRandomFileName() + ".csv")
            let previous = Thread.CurrentThread.CurrentCulture
            try
                // decimal-comma locale: the writer this replaced produced "23,32"
                // here, silently corrupting a comma-separated file
                Thread.CurrentThread.CurrentCulture <- CultureInfo.GetCultureInfo "de-AT"
                ExportWriters.writeCsv path (AnnotationExport.schemaOf settings) (records settings annotation)

                let lines = IO.File.ReadAllLines path
                Expect.hasLength lines 2 "header plus one row"
                // per-annotation records carry the centre coordinate as well
                Expect.equal lines.[0] "text,wayLength,x,y,z" "header"
                Expect.stringContains lines.[1] "\"test, annotation\"" "text cell is quoted"
                Expect.stringContains lines.[1] "23.3238" "decimal point, not comma"
            finally
                Thread.CurrentThread.CurrentCulture <- previous
                if IO.File.Exists path then IO.File.Delete path
        }

        test "missing measurements become empty cells rather than NaN" {
            // Annotation.initial has no results, so every measurement is unavailable
            let settings =
                csvSettings ExportGranularity.PerAnnotation [ AnnotationField.Area; AnnotationField.DipAngle ] []

            match records settings Annotation.initial with
            | [ row ] ->
                Expect.equal (cell "area" row) (Some VMissing) "area is missing"
                Expect.equal (cell "dipAngle" row) (Some VMissing) "dip angle is missing"
                Expect.equal (ExportValue.toCsvString VMissing) "" "missing renders as an empty cell"
            | _ -> failtest "expected exactly one row"
        }

        test "longitude conventions and notation are independent" {
            let convert convention signed =
                AnnotationExport.normalizeLongitude convention signed 77.0

            Expect.equal (convert LongitudeConvention.Native false) 77.0 "native passes through"
            Expect.equal (convert LongitudeConvention.Flipped false) 283.0 "flipped mirrors"
            Expect.equal (convert LongitudeConvention.Shifted false) 257.0 "shifted moves the prime meridian"
            Expect.equal (convert LongitudeConvention.FlippedShifted false) 103.0 "mirrored and shifted"

            // the notation is a separate choice and never changes the location
            Expect.equal (convert LongitudeConvention.Native true) 77.0 "already inside the signed range"
            Expect.equal (convert LongitudeConvention.Flipped true) -77.0 "283 written as -77"
            Expect.equal (convert LongitudeConvention.Shifted true) -103.0 "257 written as -103"
        }

        test "longitude output always lands in a valid range" {
            for convention in AnnotationExportSettings.allLongitudeConventions do
                for raw in [ -190.0; -180.0; -0.5; 0.0; 77.0; 180.0; 359.5; 360.0; 540.0 ] do
                    let unsigned = AnnotationExport.normalizeLongitude convention false raw
                    Expect.isTrue
                        (unsigned >= 0.0 && unsigned < 360.0)
                        (sprintf "%A of %f gave %f, outside [0,360)" convention raw unsigned)

                    let signed = AnnotationExport.normalizeLongitude convention true raw
                    Expect.isTrue
                        (signed > -180.0 && signed <= 180.0)
                        (sprintf "%A of %f gave %f, outside (-180,180]" convention raw signed)
        }

        test "presets change the settings and stay overridable" {
            let profile =
                AnnotationExportSettings.initial
                |> AnnotationExportSettings.applyPreset ExportPreset.Profile

            Expect.equal profile.format ExportFormat.Csv "profile writes CSV"
            Expect.equal profile.granularity ExportGranularity.PerPoint "profile is per point"
            Expect.equal profile.scope ExportScope.Selected "profile exports the selection"

            let qgis =
                AnnotationExportSettings.initial
                |> AnnotationExportSettings.applyPreset ExportPreset.QgisFeatures

            Expect.equal qgis.format ExportFormat.GeoJson "QGIS writes GeoJSON"
            Expect.equal qgis.coordinates CoordinateMode.Geographic "QGIS is geographic"
            Expect.equal qgis.longitude LongitudeConvention.Shifted "QGIS needs the shifted prime meridian"
            Expect.contains qgis.annotationFields AnnotationField.ColorHex "QGIS gets a styleable colour"
            Expect.contains qgis.annotationFields AnnotationField.GroupPath "QGIS gets the group tree"
        }
    ]
