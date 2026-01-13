namespace GeoJsonRework

open Aardvark.Base
open PRo3D.Core
open PRo3D.Base.Annotation
open FSharp.Data.Adaptive

module GeoJsonExportTest =

    // requirements
    // - additional properties: isSelected, ellipse
    // - support for cartesian and geographic coordinates
    //    - by default export in geographic coordinates (lon, lat, alt)
    //    - flag while export, whether cartesian coordiantes shall be included (in properties field)
    // - if cartesian coordinates are included, the reference frame must be included as well (in properties field top level meta data)
    // read in?
    (*
        {
           "type": "FeatureCollection",
           "features": [{
	           "type": "Feature",
	           "geometry": {
		           "type": "Point",
		           "coordinates": [102.0, 0.5],
                   "properties": {
                        "cartesianCoordinates": [x, y, z]
                    }
	           },
	           "properties": {
		           "referenceFrame": "IAU_MARS" // only if cartesian coordinates are included
	           }
             }]
        }
    
    *)


    type CoordinateConfiguration =
        | CartesianOnly of targetReferenceFrame : string
        | GeographicOnly
        | Both of targetReferenceFrame : string

    type LatLonAlt = V3d
    type Cartesian = V3d
    type ReferenceFrame = string
    type Body = string
    

    let toJsonString (configuration : CoordinateConfiguration) 
                     (toLatLonAlt :   Body -> ReferenceFrame ->  V3d -> Option<LatLonAlt>) // deal with case transformation does not work
                     (a : Annotations) : string = 
        failwith ""

    let annotations = 
        { Annotation.initial with geometry = Geometry.Point; points = IndexList.ofList [V3d(1.0,10.0,10.0)] }


module Tests =

    open System
    open System.IO

    open System.Text.Json.Nodes

    open Expecto

    open PRo3D.Extensions
    open PRo3D.Extensions.FSharp

    open Chiron


    let logDir = Path.Combine(".", "logs")

    do Aardvark.Base.Aardvark.UnpackNativeDependencies(typeof<CooTransformation.RelState>.Assembly)

    let init () =
        let appData = Path.combine [Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); "Pro3D"]
        // this tests here should also work with just the default kernel which comes with pro3d.
        PRo3D.Base.CooTransformation.initCooTrafo None appData

    let readJson (fileName: string) =
        let fullPath = Path.Combine(__SOURCE_DIRECTORY__, "Annotations", fileName)
        let jsonString = File.ReadAllText(fullPath)
        let annotationsJson : string = JsonNode.Parse(jsonString).ToJsonString()
        let (annotations : Annotations) = 
            annotationsJson
            |> Json.parse 
            |> Json.deserialize   
        annotations

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
        let isSelected = 
            match model.annotations.singleSelectLeaf with
            | None -> fun _ -> false
            | Some s -> 
                fun (a : Annotation) -> a.key = s
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

            test "SerializeIncome" {
                let annotations = readJson("annotation_3.ann")
                let flattedAnnotations = annotations.annotations.flat |> HashMap.toList |> List.map snd |> List.map Leaf.toAnnotation
                let parsedAnnotation = GeoJsonQGIS.encoder flattedAnnotations isSelected
                Expect.isNotEmpty parsedAnnotation "could not serialize annotations"          }

            (*
            test "SerializeMarsEllipses" {
                let annotationPath = Path.combine [__SOURCE_DIRECTORY__; "data"; "mola-annotations.pro3d.ann"]
                let annotations = DrawingUtilities.IO.loadAnnotationsFromFile annotationPath
                let t = GeoJsonImportExport.toJsonString (Both "IAU_MARS") xyz2LatLonAlt annotations
                Expect.isNotEmpty t "could not serialize annotations"
            }
            *)
        ]