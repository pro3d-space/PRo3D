namespace GeoJsonRework

open FSharp.Data.Adaptive

open Aardvark.Base

open PRo3D.Core
open PRo3D.Base.Annotation
open PRo3D.Base

module Tests =

    open System
    open System.IO

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


    let tests () =

        let isSelected = fun _ -> false

        testSequenced <| testList "init" [

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

            test "SerializeIncome Both" {
                let annotations = readJson("annotation_1.ann")
                let groundTruth = File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "Annotations", "annotation_1_xyz_latlon.json"))
                let flattedAnnotations = annotations.annotations.flat |> HashMap.toList |> List.map snd |> List.map Leaf.toAnnotation
                let parsedAnnotation = GeoJsonQGIS.encoder (GeoJsonQGIS.CoordinateConfiguration.Both "Mars") isSelected flattedAnnotations
                Expect.equal parsedAnnotation groundTruth "could not serialize annotations"
            }

            test "SerializeIncome Cartesian" {
                let annotations = readJson("annotation_1.ann")
                let groundTruth = File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "Annotations", "annotation_1_xyz.json"))
                let flattedAnnotations = annotations.annotations.flat |> HashMap.toList |> List.map snd |> List.map Leaf.toAnnotation
                let parsedAnnotation = GeoJsonQGIS.encoder (GeoJsonQGIS.CoordinateConfiguration.CartesianOnly) isSelected flattedAnnotations
                Expect.equal parsedAnnotation groundTruth "could not serialize annotations"
            }

            test "SerializeIncome Geographic" {
                let annotations = readJson("annotation_1.ann")
                let groundTruth = File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "Annotations", "annotation_1_latlon.json"))
                let flattedAnnotations = annotations.annotations.flat |> HashMap.toList |> List.map snd |> List.map Leaf.toAnnotation
                let parsedAnnotation = GeoJsonQGIS.encoder (GeoJsonQGIS.CoordinateConfiguration.GeographicOnly "Mars") isSelected flattedAnnotations
                Expect.equal parsedAnnotation groundTruth "could not serialize annotations"
            }
        ]