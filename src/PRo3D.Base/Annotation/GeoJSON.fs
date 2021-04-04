namespace PRo3D.Base.Annotation



open System
open Microsoft.FSharp.Reflection
open Newtonsoft.Json
open Newtonsoft.Json.Converters

open Aardvark.Base

open Chiron
//open NUnit.Framework
open System.Text.RegularExpressions
open System.IO

      
module GeoJSON =
    //type V2d = {
    //    X:float
    //    Y:float
    //}
    
    //type V3d = {
    //    X:float
    //    Y:float
    //    Z:float
    //}
    
    type Coordinate =
    | TwoDim of V2d
    | ThreeDim of V3d
    
    type Ext = Ext
    
    type Ext with
        static member V3dToArray(v:V3d) =
            [v.X;v.Y;v.Z]
    
        static member V3dFromArray(fl: List<float>) =            
            if fl.Length = 3 then
                V3d(fl.[0], fl.[1], fl.[2])
            else
                V3d.NaN
        
        static member V2dToArray(v:V2d) =
            [v.X;v.Y]
    
        static member V2dFromArray(fl: List<float>) =
            if fl.Length = 2 then
                V2d(fl.[0], fl.[1])
            else
                V2d.NaN
                
                
        static member CoordinateFromArray(fl: List<float>) =
            if fl.Length = 2 then
                fl |> Ext.V2dFromArray |> Coordinate.TwoDim                
            elif fl.Length = 3 then
                fl |> Ext.V3dFromArray |> Coordinate.ThreeDim                
            else
                V3d.NaN |> Coordinate.ThreeDim
    
        static member CoordinateToArray(c:Coordinate) =
            match c with
            | TwoDim v -> Ext.V2dToArray(v)
            | ThreeDim v -> Ext.V3dToArray(v)
    
    
        static member readCoordinate name = 
            json {
                let! x = Json.read name
                return x |> Ext.CoordinateFromArray
            }
        static member readCoordinateL name = 
            json {
                let! x = Json.read name
                return x |> List.map(fun x -> x |> Ext.CoordinateFromArray)
            }
        static member readCoordinateLL name = 
            json {
                let! x = Json.read name
                return x |> List.map(fun x -> x |> List.map(fun x -> x |> Ext.CoordinateFromArray))
            }
        static member readCoordinateLLL name = 
            json {
                let! x = Json.read name
                return x |> List.map(fun x -> x |> List.map(fun x -> x |> List.map(fun x -> x |> Ext.CoordinateFromArray)))
            }
            
    
        static member ToGeoJson (x:Coordinate) =
            json{
                do! Json.write "coordinates" (x |> Ext.CoordinateToArray)
            }
        static member ToGeoJson (x:List<Coordinate>) =
            json{
                do! Json.write "coordinates"  (x |> List.map(fun x -> x |> Ext.CoordinateToArray))
            }
        static member ToGeoJson (x:List<List<Coordinate>>) =
            json{
                do! Json.write "coordinates"  (x |> List.map(fun x -> x |> List.map(fun x -> x |> Ext.CoordinateToArray)))
            }
        static member ToGeoJson (x:List<List<List<Coordinate>>>) =
            json{
                do! Json.write "coordinates"  (x |> List.map(fun x -> x |> List.map(fun x -> x|> List.map(fun x -> x |> Ext.CoordinateToArray))))
            }
    
    type GeoJsonGeometry =
    | Point                 of coordinates : Coordinate
    | MultiPoint            of coordinates : List<Coordinate>
    | LineString            of coordinates : List<Coordinate>
    | MultiLineString       of coordinates : List<List<Coordinate>>
    | Polygon               of coordinates : List<List<Coordinate>>
    | MultiPolygon          of coordinates : List<List<List<Coordinate>>>
    | GeometryCollection    of geometries :  List<GeoJsonGeometry>
    
        with 
        
        static member ToJson (x: GeoJsonGeometry) = 
            json {
                match x with
                | Point(c) -> 
                    do! Ext.ToGeoJson c
                    do! Json.write "type" "Point"
                | MultiPoint(c) -> 
                    do! Ext.ToGeoJson  c
                    do! Json.write "type" "MultiPoint"
                | LineString(c) ->
                    do! Ext.ToGeoJson  c
                    do! Json.write "type" "LineString"
                | MultiLineString(c) -> 
                    do! Ext.ToGeoJson  c
                    do! Json.write "type" "MultiLineString"
                | Polygon(c) ->
                    do! Ext.ToGeoJson  c
                    do! Json.write "type" "Polygon"
                | MultiPolygon(c) ->
                    do! Ext.ToGeoJson  c
                    do! Json.write "type" "MultiPolygon"
                | GeometryCollection(c) ->
                    do! Json.write "geometries" c
                    do! Json.write "type" "GeometryCollection"
            }
        
        static member FromJson (_: GeoJsonGeometry) = 
            json {
                let! (x: string) = Json.read "type"
                match x with
                | "Point" -> 
                    let! y = Ext.readCoordinate "coordinates"
                    return Point(y)
                | "MultiPoint" -> 
                    let! y = Ext.readCoordinateL "coordinates"
                    return MultiPoint(y)
                | "LineString" -> 
                    let! y = Ext.readCoordinateL "coordinates"
                    return LineString(y)
                | "MultiLineString" -> 
                    let! y = Ext.readCoordinateLL "coordinates"
                    return MultiLineString(y)
                | "Polygon" -> 
                    let! y = Ext.readCoordinateLL "coordinates"
                    return Polygon(y)
                | "MultiPolygon" -> 
                    let! y = Ext.readCoordinateLLL "coordinates"
                    return MultiPolygon(y)
                | "GeometryCollection" -> 
                    let! y = Json.read "geometries"
                    return GeometryCollection(y)
                | _ ->
                    return Point(V3d.NaN |> ThreeDim)
            }
        
    type GeoJsonFeature = {
        geometry:GeoJsonGeometry
        bbox: Option<List<float>>
    }
    with    
        static member ToJson (gf: GeoJsonFeature) =
            json{
                do! Json.write "geometry" gf.geometry
                match gf.bbox with 
                | Some(b) -> do! Json.write "bbox" b
                | None -> ()
                do! Json.write "type" "Feature"
            }
        
        static member FromJson (_: GeoJsonFeature) =
            json{
                let! g = Json.read "geometry"
                let! (b:Option<List<float>>) = Json.tryRead "bbox"
                return {geometry = g;bbox =b}
            }
                    
    type GeoJsonFeatureCollection = {
        features : List<GeoJsonFeature>
        bbox     : Option<List<float>>
        // properties: Option<properties>
    }
    with            
        static member ToJson (x: GeoJsonFeatureCollection) =
            json{
                do! Json.write "features" x.features
                match x.bbox with 
                | Some(b) -> do! Json.write "bbox" b
                | None -> ()
                do! Json.write "type" "FeatureCollection"
            }
            
        static member FromJson (_: GeoJsonFeatureCollection) =
            json{
                let! g = Json.read "features"
                let! (b:Option<List<float>>) = Json.tryRead "bbox"
                return {features = g;bbox =b}
            }
        

        
//[<TestFixture>]
//type TestClass() =
    
//    [<Test>] 
//    member this.PointDeserializeTest() =

//        let geojson = """{"coordinates":[100,0],"type":"Point"}"""
//        let point:GeoJsonGeometry = geojson |> Json.parse |> Json.deserialize

//        Assert.AreEqual(point,GeoJsonGeometry.Point(Coordinate.V2d{X=100.0;Y=0.0}))
    
//    [<Test>] 
//    member this.PointSerializeTest() =
    
//        let point = GeoJsonGeometry.Point(Coordinate.V2d{X=101.0;Y=0.0}) |> Json.serialize |> Json.format
//        let geojson = """{"coordinates":[101,0],"type":"Point"}"""

//        Assert.AreEqual(point,geojson)
    
//    [<Test>] 
//    member this.PolygonDeserializeTest() =

//        let geojson = 
//            """{
//                "type": "Polygon",
//                "coordinates": [
//                    [
//                        [100.0, 0.0],
//                        [101.0, 0.0],
//                        [101.0, 1.0],
//                        [100.0, 1.0],
//                        [100.0, 0.0]
//                    ],
//                    [
//                        [100.8, 0.8],
//                        [100.8, 0.2],
//                        [100.2, 0.2],
//                        [100.2, 0.8],
//                        [100.8, 0.8]
//                    ]
//                ]
//            }"""

//        let polygon = GeoJsonGeometry.Polygon([[Coordinate.V2d{X=100.0;Y=0.0};
//                                                Coordinate.V2d{X=101.0;Y=0.0};
//                                                Coordinate.V2d{X=101.0;Y=1.0};
//                                                Coordinate.V2d{X=100.0;Y=1.0};
//                                                Coordinate.V2d{X=100.0;Y=0.0}];
//                                               [Coordinate.V2d{X=100.8;Y=0.8};
//                                                Coordinate.V2d{X=100.8;Y=0.2};
//                                                Coordinate.V2d{X=100.2;Y=0.2};
//                                                Coordinate.V2d{X=100.2;Y=0.8};
//                                                Coordinate.V2d{X=100.8;Y=0.8}]])

//        let polygon_des:GeoJsonGeometry = geojson |> Json.parse |> Json.deserialize

//        Assert.AreEqual(polygon,polygon_des)
        
//    [<Test>] 
//    member this.PolygonSerializeTest() =

//        let polygon = GeoJsonGeometry.Polygon([[Coordinate.V2d{X=100.0;Y=0.0};
//                                                Coordinate.V2d{X=101.0;Y=0.0};
//                                                Coordinate.V2d{X=101.0;Y=1.0};
//                                                Coordinate.V2d{X=100.0;Y=1.0};
//                                                Coordinate.V2d{X=100.0;Y=0.0}];
//                                                [Coordinate.V2d{X=100.8;Y=0.8};
//                                                Coordinate.V2d{X=100.8;Y=0.2};
//                                                Coordinate.V2d{X=100.2;Y=0.2};
//                                                Coordinate.V2d{X=100.2;Y=0.8};
//                                                Coordinate.V2d{X=100.8;Y=0.8}]])

//        let polygon_ser = polygon |> Json.serialize |> Json.formatWith JsonFormattingOptions.Compact
//        let polygon_des:GeoJsonGeometry = polygon_ser |> Json.parse |> Json.deserialize

//        Assert.AreEqual(polygon,polygon_des)


//    [<Test>] 
//        member this.SentinalDatasetTest() =
            
//            let feature = {
//                    geometry = GeoJsonGeometry.Polygon([[Coordinate.V2d{X=13.662865;Y= 47.845915};
//                                                            Coordinate.V2d{X=15.13047; Y=47.853628};
//                                                            Coordinate.V2d{X=15.128057;Y= 46.865628};
//                                                            Coordinate.V2d{X=13.687589;Y= 46.858176};
//                                                            Coordinate.V2d{X=13.662865;Y= 47.845915}]])
//                    bbox = Some([13.662865; 46.858176; 15.13047; 47.853628])
//                }

            
//            let geojson = System.IO.File.ReadAllText "./sentinal.json"

//            let featurecollection_des:GeoJsonFeatureCollection = geojson |> Json.parse |> Json.deserialize

//            Assert.AreEqual(feature,featurecollection_des.features.[1])


