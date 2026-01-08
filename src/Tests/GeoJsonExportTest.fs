namespace GeoJsonRework

open Thoth.Json.Net

module GeoJson =
    open Aardvark.Base
    open PRo3D.Core
    open PRo3D.Base.Annotation
    open FSharp.Data.Adaptive

    let coordinate (p : V3d) =
        Encode.list [ Encode.float p.X; Encode.float p.Y; Encode.float p.Z ]

    let featureProperties (annotation: Annotation) =
        match annotation.geometry with
        | Geometry.AxisEllipse ->
            Encode.object [
                "ellipse", Encode.bool true
            ]
        | _ -> 
            Encode.object [
            ]

    let featureGeometry (annotation: Annotation) =
        match annotation.geometry with
        | Geometry.Point ->
            let p =
                annotation.points
                |> Seq.head
            Encode.object [
                "type", Encode.string "Point"
                "coordinates", coordinate p
                "properties", Encode.object []
            ]
        | Geometry.Line
        | Geometry.Polyline
        | Geometry.DnS
        | Geometry.TT ->
            let coordinates =
                annotation.points
                |> Seq.map coordinate
                |> Seq.toList
            Encode.object [
                "type", Encode.string "LineString"
                "coordinates", Encode.list coordinates
                "properties", Encode.object []
            ]
        | Geometry.Polygon ->
            let coordinates =
                annotation.points
                |> Seq.map coordinate
                |> Seq.toList
            Encode.object [
                "type", Encode.string "Polygon"
                "coordinates", Encode.list [ Encode.list coordinates ]
                "properties", Encode.object []
            ]
        | Geometry.AxisEllipse ->
            let coordinates =
                annotation.points
                |> Seq.map coordinate
                |> Seq.toList
            let coordinatesClosed =
                match coordinates with
                | [] -> []
                | h :: _ -> coordinates @ [h]
            Encode.object [
                "type", Encode.string "Polygon"
                "coordinates", Encode.list [ Encode.list coordinatesClosed ]
                "properties", Encode.object []
            ]
        | _ ->
            Encode.object [
                "type", Encode.string "unknown"
                "coordinates", Encode.list []
            ]


    let feature (annotation : Annotation) =
        Encode.object [
            "type", Encode.string "Feature"
            "geometry", featureGeometry annotation
            "properties", featureProperties annotation
        ]

    let globalProperties annotations =
        Encode.object [
            "referenceFrame", Encode.string "IAU_MARS" // TODO
        ]

    let featureCollection (annotations : Annotations) (encodeCartesian : bool)=
        let flattedAnnotations = annotations.annotations.flat |> HashMap.toList |> List.map snd |> List.map Leaf.toAnnotation
        match encodeCartesian with
        | true ->
            Encode.object [
                "type", Encode.string "FeatureCollection"
                "features", Encode.list (flattedAnnotations |> List.map (fun ann -> feature ann))
                "properties", globalProperties annotations

            ]
        | false ->
            Encode.object [
                "type", Encode.string "FeatureCollection"
                "features", Encode.list (flattedAnnotations |> List.map (fun ann -> feature ann))
            ]

    let encoder (annotations : Annotations) : JsonValue =
        featureCollection annotations true

module GeoJsonExportTest =

    open Aardvark.Base
    open PRo3D.Core
    open PRo3D.Base.Annotation
    open FSharp.Data.Adaptive

    module GeoJsonImportExport =  

        // requirements
        // - geojson conformant export
        // - should be compatible with qgis and arcgis
        // - additional properties: isSelected, ellipse
        // - support for cartesian and geographic coordinates
        //    - by default export in geographic coordinates (lon, lat, alt)
        //    - flag while export, whether cartesian coordiantes shall be included (in properties field)
        // - if cartesian coordinates are included, the reference frame must be included as well (in properties field top level meta data)
        // - support for different annotation types (point, line, polygon, ellipse, ...)

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

        (*
    
        earlier: Annotations -> GeometryCollection -> Chiron Serilizer 
        then: Annoations -> string (via json lib)

        read in: string -> Annoations (low prio)

        *)

        let test (a : Annotation) = 
            ()

        type CoordinateConfiguration =
            | CartesianOnly of referenceFrame : string
            | GeographicOnly
            | Both of referenceFrame : string

        type LatLonAlt = V3d
        type Cartesian = V3d
    

        let toJsonString (referenceFrame : string) 
                         (configuration : CoordinateConfiguration) 
                         (toLatLonAlt : Cartesian -> LatLonAlt)
                         (a : Annotations) : string = 
            failwith ""


        let annotations = 
            { Annotation.initial with geometry = Geometry.Point; points = IndexList.ofList [V3d(1.0,10.0,10.0)] }


module Tests =

    open System
    open System.IO

    open System.Text.Json.Nodes

    open Expecto

    open Aardvark.Base

    open PRo3D.Extensions
    open PRo3D.Extensions.FSharp

    open PRo3D.Core

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

    let xyz2LatLonAlt (body : string) (x : float) (y : float) (z : float) =
        let mutable lat, lon, alt = 0.0, 0.0, 0.0
        let res = CooTransformation.Xyz2LatLonAlt(body, x, y, z, &lat, &lon, &alt)
        if res <> 0 then
            None
        else
            Some(V3d(lat, lon, alt))


    let tests () =
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
                let latlon = xyz2LatLonAlt "MARS" pos.X pos.Y pos.Z
                Expect.isSome latlon "could not get lat lon"
            }

            test "SerializeIncome" {
                let annotation = readJson("annotation_3.ann")
                let parsedAnnotation = GeoJson.encoder annotation
                Expect.isTrue parsedAnnotation.HasValues "..."            }
        ]