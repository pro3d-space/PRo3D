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

/// An annotation with no `segments` — what a `Projection.Linear` annotation
/// looks like, where the picked points are the whole geometry.
let private unsegmentedAnnotation points =
    { Annotation.initial with
        key      = Guid.NewGuid()
        geometry = Geometry.Polyline
        points   = IndexList.ofList points
        segments = IndexList.empty }

let private records settings annotation =
    AnnotationExport.buildRecords settings None HashMap.empty Planet.None V3d.OOI [ annotation ]

let private recordsOn planet settings annotation =
    AnnotationExport.buildRecords settings None HashMap.empty planet V3d.OOI [ annotation ]

/// A stand-in for the viewer's surface sampler: two layers on every point, so
/// the surface columns can be pinned without an OPC dataset.
let private stubProperties (p : V3d) =
    [ AnnotationExport.surfaceColumnName "Gravity", VNum p.X
      AnnotationExport.surfaceColumnName "Albedo",  VNums [| 0.25; 0.5; 0.75 |] ]

let private stubSampler : SurfacePropertySampler =
    fun p -> { lonLatRadius = None; properties = stubProperties p }

/// A sampler that also carries per-vertex coordinates, as an OPC shipping a
/// LonLatRad layer would. Channels are (longitude, latitude, radius).
let private stubSamplerWithCoordinates (lonLatRadius : V3d) : SurfacePropertySampler =
    fun p -> { lonLatRadius = Some lonLatRadius; properties = stubProperties p }

/// Cartesian position of a lat/lon/alt on Mars, or None when the native
/// coordinate transform is unavailable in this environment.
let private onMars (lat : float, lon : float, alt : float) =
    CooTransformation.tryGetXYZFromLatLonAlt' (V3d(lat, lon, alt)) Planet.Mars

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

        test "sampled surface properties become prefixed columns after the fixed schema" {
            let annotation = testAnnotation ()
            let settings =
                { csvSettings ExportGranularity.PerPoint [] [ PointField.Index ] with
                    sampleSurfaceProperties = true }

            let rows =
                AnnotationExport.buildRecords
                    settings (Some stubSampler) HashMap.empty Planet.None V3d.OOI [ annotation ]

            // the sampler's layers are discovered from the records, so they can
            // only ever land behind everything the settings named
            Expect.equal
                (AnnotationExport.schemaFor settings rows)
                [ "pointIndex"; "surface_Gravity"; "surface_Albedo" ]
                "layer columns follow the fixed schema, in first-seen order"

            match rows with
            | first :: _ ->
                Expect.equal (cell "surface_Gravity" first) (Some (VNum 0.0)) "single channel stays a number"
                Expect.equal (cell "surface_Albedo" first)
                             (Some (VNums [| 0.25; 0.5; 0.75 |]))
                             "multi-channel layer keeps its channels in one cell"
            | [] -> failtest "expected one row per point"

            // and nothing at all without a sampler, whatever the setting says
            let bare = AnnotationExport.buildRecords settings None HashMap.empty Planet.None V3d.OOI [ annotation ]
            Expect.equal (AnnotationExport.schemaFor settings bare) [ "pointIndex" ] "no sampler, no columns"
        }

        test "geographic exports name the body per feature" {
            // a collection-level property is not an attribute in GIS tools, so
            // the body has to be a field of its own
            let settings =
                { csvSettings ExportGranularity.PerAnnotation [ AnnotationField.Key ] [] with
                    coordinates = CoordinateMode.Geographic }

            Expect.equal
                (AnnotationExport.schemaOf settings)
                [ "key"; "lat"; "lon"; "alt"; "body"; "latLonAltSource" ]
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

            match AnnotationExport.buildRecords settings None groupPath Planet.None V3d.OOI [ annotation ] with
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

            let rows = records settings annotation
            let path = IO.Path.Combine(IO.Path.GetTempPath(), IO.Path.GetRandomFileName() + ".csv")
            let previous = Thread.CurrentThread.CurrentCulture
            try
                // decimal-comma locale: the writer this replaced produced "23,32"
                // here, silently corrupting a comma-separated file
                Thread.CurrentThread.CurrentCulture <- CultureInfo.GetCultureInfo "de-AT"
                ExportWriters.writeCsv path (AnnotationExport.schemaFor settings rows) rows

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

        test "ground distance drops the climb that 3D distance includes" {
            // the old profile export flattened every point onto the reference
            // surface before measuring; ground distance reproduces that
            let points =
                [ 10.0, 20.00, 0.0
                  10.0, 20.01, 0.0
                  10.0, 20.02, 500.0 ]
                |> List.map onMars

            if points |> List.exists Option.isNone then
                skiptest "CooTransformation unavailable in this environment"
            else
                let annotation = unsegmentedAnnotation (points |> List.choose id)
                let settings =
                    { csvSettings ExportGranularity.PerPoint []
                        [ PointField.CumulativeDistance; PointField.GroundDistance ] with
                        coordinates = CoordinateMode.Geographic }

                let rows = recordsOn Planet.Mars settings annotation

                match rows |> List.tryLast with
                | Some last ->
                    match number "distance" last, number "groundDistance" last with
                    | Some slanted, Some ground ->
                        Expect.isGreaterThan slanted ground "the slanted path is longer than the horizontal run"
                        Expect.isGreaterThan ground 0.0 "ground distance accumulated"
                    | _ -> failtest "distance or groundDistance missing on the last row"
                | None -> failtest "no rows"

                // both accumulators only ever grow
                let monotonic column =
                    rows
                    |> List.choose (number column)
                    |> List.pairwise
                    |> List.forall (fun (a, b) -> b >= a)

                Expect.isTrue (monotonic "distance") "distance is monotonic"
                Expect.isTrue (monotonic "groundDistance") "groundDistance is monotonic"
        }

        test "ground distance equals 3D distance when nothing climbs" {
            let points =
                [ 10.0, 20.00, 0.0
                  10.0, 20.01, 0.0
                  10.0, 20.02, 0.0 ]
                |> List.map onMars

            if points |> List.exists Option.isNone then
                skiptest "CooTransformation unavailable in this environment"
            else
                let annotation = unsegmentedAnnotation (points |> List.choose id)
                let settings =
                    { csvSettings ExportGranularity.PerPoint []
                        [ PointField.CumulativeDistance; PointField.GroundDistance ] with
                        coordinates = CoordinateMode.Geographic }

                match recordsOn Planet.Mars settings annotation |> List.tryLast with
                | Some last ->
                    match number "distance" last, number "groundDistance" last with
                    | Some slanted, Some ground ->
                        Expect.floatClose Accuracy.medium ground slanted "no height difference, so the two agree"
                    | _ -> failtest "distance or groundDistance missing"
                | None -> failtest "no rows"
        }

        test "a geographic export without a frame is detected, not refused" {
            // Planet.None/JPL/ENU have no lat/lon: the file is still written, but
            // every geographic value in it is empty, which the window warns about
            let geographic =
                { AnnotationExportSettings.initial with
                    format = ExportFormat.GeoJson; coordinates = CoordinateMode.Geographic }

            Expect.isTrue
                (AnnotationExport.geographicWithoutFrame geographic Planet.None)
                "no frame, so the geographic values cannot be produced"
            Expect.isTrue
                (AnnotationExport.geographicWithoutFrame geographic Planet.ENU)
                "ENU is not a geographic frame either"
            Expect.isFalse
                (AnnotationExport.geographicWithoutFrame geographic Planet.Mars)
                "Mars has a frame, so there is nothing to warn about"
            Expect.isFalse
                (AnnotationExport.geographicWithoutFrame
                    { geographic with coordinates = CoordinateMode.Cartesian } Planet.None)
                "a cartesian export never needs a frame"
            Expect.isFalse
                (AnnotationExport.geographicWithoutFrame
                    { geographic with format = ExportFormat.Attitude } Planet.None)
                "attitude planes ignore the coordinate setting entirely"
        }

        test "ground distance is missing, not zero, without a geographic frame" {
            // Planet.None has no lat/lon, so the height cannot be removed
            let settings =
                csvSettings ExportGranularity.PerPoint [] [ PointField.GroundDistance ]

            let rows = records settings (testAnnotation ())
            Expect.isNonEmpty rows "rows produced"
            for row in rows do
                Expect.equal (cell "groundDistance" row) (Some VMissing) "reported as missing"
        }

        test "segmentLength falls back to the hop when there are no segments" {
            // a Projection.Linear annotation has no segments: the stretch between
            // two picked points *is* the hop, so the column must not be blank
            let annotation = unsegmentedAnnotation [ V3d.Zero; V3d(3.0, 4.0, 0.0); V3d(3.0, 4.0, 12.0) ]
            let settings =
                csvSettings ExportGranularity.PerPoint [] [ PointField.StepLength; PointField.SegmentLength ]

            match records settings annotation with
            | first :: rest ->
                Expect.equal (cell "segmentLength" first) (Some VMissing) "the first point has no predecessor"
                Expect.isNonEmpty rest "more than one point"
                for row in rest do
                    match number "stepLength" row, number "segmentLength" row with
                    | Some step, Some segment ->
                        Expect.floatClose Accuracy.high segment step "segment length equals the hop"
                    | _ -> failtestf "blank length column on row %A" row.fields
            | [] -> failtest "no rows"
        }

        test "GeoJSON granularity decides the feature geometry" {
            let geoJson granularity =
                { csvSettings granularity [ AnnotationField.Key ] [ PointField.Index ] with
                    format = ExportFormat.GeoJson }

            let annotation = testAnnotation ()

            // per annotation: one feature carrying the whole polyline
            match records (geoJson ExportGranularity.PerAnnotation) annotation with
            | [ single ] ->
                match single.geometry with
                | Some (GLine positions) -> Expect.hasLength positions 6 "the full polyline"
                | other -> failtestf "expected a LineString, got %A" other
            | rows -> failtestf "expected one feature, got %d" rows.Length

            // per point: one Point feature per vertex, which is what makes
            // per-point values individually styleable in a GIS
            let perPoint = records (geoJson ExportGranularity.PerPoint) annotation
            Expect.hasLength perPoint 6 "one feature per vertex"
            for record in perPoint do
                match record.geometry with
                | Some (GPoint _) -> ()
                | other -> failtestf "expected a Point geometry, got %A" other
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

        test "shifted is what lines a body up with a differently oriented product" {
            // The case this exists for: a texture draped on a shape model whose UV
            // origin sits 180 deg from the body-fixed +X axis that longitude 0
            // follows. Real numbers from a Dimorphos export.
            let raw = -139.0730112041369
            Expect.floatClose Accuracy.medium
                (AnnotationExport.normalizeLongitude LongitudeConvention.Shifted true raw)
                40.9269887958631
                "the shift lands the annotation where the draped texture shows it"

            // and the notation genuinely is a separate axis: without the shift the
            // same point is the same place under either range
            Expect.floatClose Accuracy.medium
                (AnnotationExport.normalizeLongitude LongitudeConvention.Native true raw)
                -139.0730112041369 "signed leaves it as it came"
            Expect.floatClose Accuracy.medium
                (AnnotationExport.normalizeLongitude LongitudeConvention.Native false raw)
                220.9269887958631 "unsigned names the same point 360 higher"
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

        test "every format honours the longitude range setting" {
            // GeoJSON briefly forced the signed range on the grounds that its
            // positions are WGS84 by spec. For planetary bodies that is the wrong
            // call - the range is the user's to pick, per format, like any other.
            match onMars (10.0, 257.0, 0.0) with
            | Some position ->
                let setting format signed =
                    { AnnotationExportSettings.initial with
                        format          = format
                        longitude       = LongitudeConvention.Native
                        signedLongitude = signed }

                let longitudeOf format signed =
                    AnnotationExport.tryToGeographic Planet.Mars (setting format signed) position
                    |> Option.map (fun geographic -> geographic.Y)

                for format in [ ExportFormat.GeoJson; ExportFormat.Csv ] do
                    match longitudeOf format false, longitudeOf format true with
                    | Some unsigned, Some signed ->
                        Expect.floatClose Accuracy.medium unsigned 257.0
                            (sprintf "%A unticked keeps 0...360" format)
                        Expect.floatClose Accuracy.medium signed -103.0
                            (sprintf "%A ticked writes -180...180" format)
                    | _ -> failtest "no geographic conversion"
            | None ->
                // no native coordinate transform in this environment
                ()
        }

        test "lat/lon/alt can come from the per-vertex layer instead of SPICE" {
            let annotation = testAnnotation ()
            let settings =
                { csvSettings ExportGranularity.PerPoint [] [ PointField.Geographic ] with
                    coordinates     = CoordinateMode.Geographic
                    longitude       = LongitudeConvention.Native
                    signedLongitude = false
                    latLonAltSource = LatLonAltSource.AaraFile }

            // channels are (longitude, latitude, radius)
            let sampler = stubSamplerWithCoordinates (V3d(77.0, -14.5, 1823.4))

            match AnnotationExport.buildRecords
                    settings (Some sampler) HashMap.empty Planet.Mars V3d.OOI [ annotation ] with
            | row :: _ ->
                Expect.equal (number "lat" row) (Some -14.5) "latitude is the layer's second channel"
                Expect.equal (number "lon" row) (Some 77.0) "longitude is the layer's first channel"
                // the radius, not a height above the spheroid - which is exactly
                // what the source column exists to disambiguate
                Expect.equal (number "alt" row) (Some 1823.4) "altitude is the layer's radius"
                Expect.equal (cell "latLonAltSource" row) (Some (VText "aara_file")) "provenance recorded"
            | [] -> failtest "expected one row per point"

            // a point the layer does not cover keeps its row, resolved by SPICE
            match AnnotationExport.buildRecords
                    settings (Some stubSampler) HashMap.empty Planet.Mars V3d.OOI [ annotation ] with
            | row :: _ ->
                Expect.equal (cell "latLonAltSource" row) (Some (VText "spice_recpgr"))
                             "falls back to SPICE, and says so"
            | [] -> failtest "expected one row per point"
        }

        test "the longitude settings still apply to a file-sourced longitude" {
            // they are notation transforms of a longitude, not part of deriving
            // one, so they cannot depend on where the longitude came from
            let annotation = testAnnotation ()
            let settings =
                { csvSettings ExportGranularity.PerPoint [] [ PointField.Geographic ] with
                    coordinates     = CoordinateMode.Geographic
                    longitude       = LongitudeConvention.Flipped
                    signedLongitude = true
                    latLonAltSource = LatLonAltSource.AaraFile }

            let sampler = stubSamplerWithCoordinates (V3d(77.0, -14.5, 1823.4))

            match AnnotationExport.buildRecords
                    settings (Some sampler) HashMap.empty Planet.Mars V3d.OOI [ annotation ] with
            | row :: _ -> Expect.equal (number "lon" row) (Some -77.0) "360 - 77 = 283, signed as -77"
            | [] -> failtest "expected one row per point"
        }

        test "a cartesian export carries no source column at all" {
            let settings =
                { csvSettings ExportGranularity.PerPoint [] [ PointField.Cartesian ] with
                    latLonAltSource = LatLonAltSource.AaraFile }

            Expect.equal (AnnotationExport.schemaOf settings) [ "x"; "y"; "z" ]
                         "no geographic columns, so nothing to attribute"
        }

        test "selecting the file source warns, and switching back clears it" {
            let initial = AnnotationExportModel.initial
            Expect.equal initial.latLonAltSource LatLonAltSource.Spice "SPICE by default"
            Expect.isNone initial.warning "nothing to warn about yet"

            let onAara =
                AnnotationExportApp.update initial (SetLatLonAltSource LatLonAltSource.AaraFile)
            Expect.equal onAara.warning (Some AnnotationExportApp.aaraAccuracyWarning)
                         "warns about the less accurate source"

            // the warning is about the selection, not the click, so an unrelated
            // change must not silently drop it
            let stillOnAara = AnnotationExportApp.update onAara (ToggleSampledPoints)
            Expect.equal stillOnAara.warning (Some AnnotationExportApp.aaraAccuracyWarning)
                         "survives an unrelated setting change"

            let backToSpice =
                AnnotationExportApp.update stillOnAara (SetLatLonAltSource LatLonAltSource.Spice)
            Expect.isNone backToSpice.warning "cleared on the way back"
        }

        test "a granularity switch leaves the lat/lon/alt source alone" {
            // both granularities can honour the file source: a per-annotation row
            // samples once, at the bounding-box centre
            let model =
                { AnnotationExportModel.initial with latLonAltSource = LatLonAltSource.AaraFile }

            let perAnnotation = AnnotationExportApp.update model (SetGranularity ExportGranularity.PerAnnotation)
            Expect.equal perAnnotation.latLonAltSource LatLonAltSource.AaraFile "kept for per-annotation"

            let perPoint = AnnotationExportApp.update model (SetGranularity ExportGranularity.PerPoint)
            Expect.equal perPoint.latLonAltSource LatLonAltSource.AaraFile "kept for per-point"
        }

        test "a per-annotation export can take lat/lon/alt from the per-vertex layer" {
            let annotation = testAnnotation ()
            let settings =
                { csvSettings ExportGranularity.PerAnnotation [] [] with
                    coordinates     = CoordinateMode.Geographic
                    longitude       = LongitudeConvention.Native
                    signedLongitude = false
                    latLonAltSource = LatLonAltSource.AaraFile }

            // channels are (longitude, latitude, radius)
            let sampler = stubSamplerWithCoordinates (V3d(77.0, -14.5, 1823.4))

            match AnnotationExport.buildRecords
                    settings (Some sampler) HashMap.empty Planet.Mars V3d.OOI [ annotation ] with
            | [ row ] ->
                Expect.equal (number "lat" row) (Some -14.5) "latitude is the layer's second channel"
                Expect.equal (number "lon" row) (Some 77.0) "longitude is the layer's first channel"
                Expect.equal (cell "latLonAltSource" row) (Some (VText "aara_file")) "provenance recorded"
            | rows -> failtestf "expected exactly one row per annotation, got %d" (List.length rows)

            // the bounding-box centre usually floats above the terrain, so the
            // miss is the common case rather than the exceptional one
            match AnnotationExport.buildRecords
                    settings (Some stubSampler) HashMap.empty Planet.Mars V3d.OOI [ annotation ] with
            | [ row ] ->
                Expect.equal (cell "latLonAltSource" row) (Some (VText "spice_recpgr"))
                             "falls back to SPICE, and says so"
            | rows -> failtestf "expected exactly one row per annotation, got %d" (List.length rows)
        }

        test "GeoJSON geometry and properties agree about a file-sourced point" {
            // the geometry used to be recomputed independently of the columns;
            // with two possible sources that would let them drift apart
            let annotation = testAnnotation ()
            let settings =
                { AnnotationExportSettings.initial with
                    format          = ExportFormat.GeoJson
                    granularity     = ExportGranularity.PerPoint
                    coordinates     = CoordinateMode.Geographic
                    longitude       = LongitudeConvention.Native
                    signedLongitude = false
                    latLonAltSource = LatLonAltSource.AaraFile
                    pointFields     = [ PointField.Geographic ] }

            let sampler = stubSamplerWithCoordinates (V3d(77.0, -14.5, 1823.4))

            match AnnotationExport.buildRecords
                    settings (Some sampler) HashMap.empty Planet.Mars V3d.OOI [ annotation ] with
            | row :: _ ->
                match row.geometry with
                // GeoJSON positions are [longitude, latitude, altitude]
                | Some (GPoint p) ->
                    Expect.equal (Some p.X) (number "lon" row) "geometry longitude matches the property"
                    Expect.equal (Some p.Y) (number "lat" row) "geometry latitude matches the property"
                    Expect.equal (Some p.Z) (number "alt" row) "geometry altitude matches the property"
                | other -> failtestf "expected a Point geometry, got %A" other
            | [] -> failtest "expected one row per point"
        }

        test "GeoJSON does not offer the Both coordinate mode" {
            // a Feature's geometry is written in one coordinate system, so the
            // option would suggest a choice the format cannot express
            let geoJson = AnnotationExportSettings.coordinateModesFor ExportFormat.GeoJson
            Expect.isFalse (geoJson |> List.contains CoordinateMode.Both) "Both is not offered"
            Expect.contains geoJson CoordinateMode.Cartesian "cartesian stays"
            Expect.contains geoJson CoordinateMode.Geographic "geographic stays"

            Expect.contains
                (AnnotationExportSettings.coordinateModesFor ExportFormat.Csv)
                CoordinateMode.Both
                "a CSV just emits both sets of columns, so it keeps the option"
        }

        test "switching to GeoJSON moves Both onto a mode the dropdown offers" {
            let withCoordinates mode =
                { AnnotationExportModel.initial with format = ExportFormat.Csv; coordinates = mode }

            let switched =
                AnnotationExportApp.update (withCoordinates CoordinateMode.Both)
                    (SetFormat ExportFormat.GeoJson)
            // Geographic, not Cartesian: Both already wrote geographic geometry,
            // so only the extra x/y/z properties are lost
            Expect.equal switched.coordinates CoordinateMode.Geographic "coerced away from Both"
            Expect.equal switched.format ExportFormat.GeoJson "the format still changed"

            let kept =
                AnnotationExportApp.update (withCoordinates CoordinateMode.Cartesian)
                    (SetFormat ExportFormat.GeoJson)
            Expect.equal kept.coordinates CoordinateMode.Cartesian "a mode GeoJSON offers is left alone"

            let toCsv =
                AnnotationExportApp.update
                    { AnnotationExportModel.initial with
                        format = ExportFormat.GeoJson; coordinates = CoordinateMode.Geographic }
                    (SetFormat ExportFormat.Csv)
            Expect.equal toCsv.coordinates CoordinateMode.Geographic "widening the choice changes nothing"
        }

        test "presets change the settings and stay overridable" {
            let profile =
                AnnotationExportSettings.initial
                |> AnnotationExportSettings.applyPreset ExportPreset.Profile

            Expect.equal profile.format ExportFormat.Csv "profile writes CSV"
            Expect.equal profile.granularity ExportGranularity.PerPoint "profile is per point"
            Expect.equal profile.scope ExportScope.Selected "profile exports the selection"
            Expect.contains profile.pointFields PointField.GroundDistance
                "profile uses the horizontal run, as the old profile export did"

            let qgis =
                AnnotationExportSettings.initial
                |> AnnotationExportSettings.applyPreset ExportPreset.QgisFeatures

            Expect.equal qgis.format ExportFormat.GeoJson "QGIS writes GeoJSON"
            Expect.equal qgis.coordinates CoordinateMode.Geographic "QGIS is geographic"
            // the body's own convention; a product on a differently oriented
            // frame needs Shifted, but that is a per-export override now
            Expect.equal qgis.longitude LongitudeConvention.Native "QGIS uses the body's own prime meridian"

            // the signed range and the per-vertex source are the defaults
            // everywhere now, so every preset arrives at both — including one that
            // starts from them switched off
            Expect.equal AnnotationExportSettings.initial.latLonAltSource LatLonAltSource.Spice
                         "SPICE is the out-of-the-box default"

            for preset in ExportPreset.all |> List.filter (fun p -> p <> ExportPreset.Custom) do
                let applied =
                    { AnnotationExportSettings.initial with
                        signedLongitude = false
                        latLonAltSource = LatLonAltSource.AaraFile }
                    |> AnnotationExportSettings.applyPreset preset
                Expect.equal applied.latLonAltSource LatLonAltSource.Spice
                             (sprintf "%A resets lat/lon/alt to SPICE" preset)
                Expect.isTrue applied.signedLongitude (sprintf "%A writes -180...180" preset)

            // ... and Custom, which is not a preset, leaves a manual choice alone
            let untouched =
                { AnnotationExportSettings.initial with
                    signedLongitude = false
                    latLonAltSource = LatLonAltSource.AaraFile }
                |> AnnotationExportSettings.applyPreset ExportPreset.Custom

            Expect.isFalse untouched.signedLongitude "selecting Custom changes nothing"
            Expect.equal untouched.latLonAltSource LatLonAltSource.AaraFile "not the source either"

            let continuous =
                AnnotationExportSettings.initial
                |> AnnotationExportSettings.applyPreset ExportPreset.ContinuousGeoJson

            Expect.equal continuous.format ExportFormat.ContinuousGeoJson "arms the live export"
            Expect.isTrue (AnnotationExportSettings.isContinuous continuous.format) "recognised as continuous"
            Expect.isTrue (AnnotationExportSettings.hasFixedSchema continuous.format)
                "collapses the settings, like Attitude planes"
            Expect.contains qgis.annotationFields AnnotationField.ColorHex "QGIS gets a styleable colour"
            Expect.contains qgis.annotationFields AnnotationField.GroupPath "QGIS gets the group tree"
        }
    ]
