namespace GeoJsonRework

open FSharp.Data.Adaptive

open Aardvark.Base

open PRo3D.Core
open PRo3D.Base.Annotation
open PRo3D.Base

module Tests =

    open System
    open System.IO
    open System.Text.Json

    open Expecto

    open PRo3D.Extensions
    open PRo3D.Extensions.FSharp
    open PRo3D.Core.Drawing

    let init () =
        do Aardvark.Base.Aardvark.UnpackNativeDependencies(typeof<CooTransformation.RelState>.Assembly)
        let appData = Path.combine [Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); "Pro3D"]
        // this tests here should also work with just the default kernel which comes with pro3d.
        PRo3D.Base.CooTransformation.initCooTrafo None appData

    let readJson (fileName: string) =
        let fullPath = Path.Combine(__SOURCE_DIRECTORY__, "Annotations", fileName)
        DrawingUtilities.IO.loadAnnotationsFromFile fullPath

    let latLonAlt2Xyz (body : string) (lat : float) (lon : float) (alt : float) =
        let mutable x, y, z = 0.0, 0.0, 0.0
        let res = CooTransformation.LatLonAlt2Xyz(body, lat, lon, alt, &x, &y, &z)
        if res <> 0 then
            None
        else
            Some(V3d(x, y, z))

    let xyz2LatLonAlt (body : string) (referenceFrame : string) (cartesian : V3d)=
        let mutable lat, lon, alt = 0.0, 0.0, 0.0
        Log.warn "reference frame + body  not yet done, assuming IAU_MARS"
        let res = CooTransformation.Xyz2LatLonAlt(body, cartesian.X,cartesian.Y, cartesian.Z, &lat, &lon, &alt)
        if res <> 0 then
            None
        else
            Some(V3d(lat, lon, alt))


    // Coordinates are computed by the CooTransformation native lib and differ in
    // the last digits across platforms (libm), so an exact string compare of the
    // serialized JSON is not portable. Compare the JSON structurally instead,
    // with a numeric tolerance on values.
    let private numbersClose (a: float) (b: float) =
        abs (a - b) <= 1e-6 + 1e-7 * max (abs a) (abs b)

    let tests () =

        testList "init" [

            do init()

            test "GetCoords" {
                // 	18° 22′ 48″ N, 77° 34′ 48″ E  (18.38°, 77.58°)
                let lat, lon, alt = 18.38, 77.58, 0.0

                let xyz = latLonAlt2Xyz "mars" lat lon alt
                Expect.isSome xyz "could get xyz from latlonalt"
            }

            test "GetLatLon" {
                let pos = V3d(693177.2106401927, -3147511.6703961412, 1070879.1507304527)
                let latlon = xyz2LatLonAlt "MARS" "IAU_MARS" pos
                Expect.isSome latlon "could not get lat lon"
            }

            // The QGIS-specific writer these tests used to cover was replaced by
            // the single parameterised GeoJSON writer (see AnnotationExportTest).
            // What is still worth pinning here is that a real .ann file round-trips
            // through the writer against the native coordinate transforms.

            let exportAnnotations () =
                readJson("annotation_1.ann") |> fun a ->
                    a.annotations.flat |> HashMap.toList |> List.map snd |> List.map Leaf.toAnnotation

            let writeToTemp (settings : AnnotationExportSettings) (annotations : list<Annotation>) =
                let path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json")
                try
                    AnnotationExport.write settings None HashMap.empty Planet.Mars V3d.OOI path annotations
                    File.ReadAllText path
                finally
                    if File.Exists path then File.Delete path

            let geoJsonSettings coordinates =
                { AnnotationExportSettings.initial with
                    format      = ExportFormat.GeoJson
                    granularity = ExportGranularity.PerAnnotation
                    coordinates = coordinates }

            test "GeoJson export is a FeatureCollection with one feature per annotation" {
                let annotations = exportAnnotations ()
                let json = annotations |> writeToTemp (geoJsonSettings CoordinateMode.Cartesian)

                use doc = JsonDocument.Parse json
                Expect.equal (doc.RootElement.GetProperty("type").GetString()) "FeatureCollection" "collection type"
                Expect.equal
                    (doc.RootElement.GetProperty("features").GetArrayLength())
                    annotations.Length
                    "one feature per annotation"
            }

            test "Cartesian GeoJson keeps the annotation positions unchanged" {
                let annotations = exportAnnotations ()
                let json = annotations |> writeToTemp (geoJsonSettings CoordinateMode.Cartesian)

                use doc = JsonDocument.Parse json
                let firstCoordinate =
                    doc.RootElement.GetProperty("features").EnumerateArray()
                    |> Seq.tryHead
                    |> Option.map (fun f ->
                        let coordinates = f.GetProperty("geometry").GetProperty("coordinates")
                        // Point -> [x,y,z]; everything else -> nested arrays
                        let rec firstPosition (e : JsonElement) =
                            match e.EnumerateArray() |> Seq.tryHead with
                            | Some head when head.ValueKind = JsonValueKind.Array -> firstPosition head
                            | _ -> e
                        firstPosition coordinates)

                let expected =
                    annotations
                    |> List.tryHead
                    |> Option.bind (fun a -> a |> Annotation.retrievePoints |> List.tryHead)

                match firstCoordinate, expected with
                | Some actual, Some expected ->
                    Expect.isTrue (numbersClose (actual.[0].GetDouble()) expected.X) "x preserved"
                    Expect.isTrue (numbersClose (actual.[1].GetDouble()) expected.Y) "y preserved"
                    Expect.isTrue (numbersClose (actual.[2].GetDouble()) expected.Z) "z preserved"
                | _ -> failtest "no annotation or no coordinates in the export"
            }

            test "Geographic GeoJson carries the body and plausible lat/lon" {
                let annotations = exportAnnotations ()
                let json = annotations |> writeToTemp (geoJsonSettings CoordinateMode.Geographic)

                use doc = JsonDocument.Parse json
                Expect.equal
                    (doc.RootElement.GetProperty("properties").GetProperty("planet").GetString())
                    "Mars" "body is written as a collection property"

                // GeoJSON positions are [longitude, latitude, altitude]
                let positions =
                    doc.RootElement.GetProperty("features").EnumerateArray()
                    |> Seq.collect (fun f ->
                        let rec positions (e : JsonElement) =
                            match e.EnumerateArray() |> Seq.tryHead with
                            | Some head when head.ValueKind = JsonValueKind.Array ->
                                e.EnumerateArray() |> Seq.collect positions
                            | _ -> Seq.singleton e
                        positions (f.GetProperty("geometry").GetProperty("coordinates")))
                    |> Seq.toList

                Expect.isNonEmpty positions "geographic conversion produced coordinates"
                for p in positions do
                    let lon = p.[0].GetDouble()
                    let lat = p.[1].GetDouble()
                    Expect.isTrue (lat >= -90.0 && lat <= 90.0) (sprintf "latitude in range, was %f" lat)
                    Expect.isTrue (lon >= -180.0 && lon <= 360.0) (sprintf "longitude in range, was %f" lon)
            }
        ]