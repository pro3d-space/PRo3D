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

open PRo3D.Base
      
module GeoJSON =        
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

        static member tryReadM20Props name = 
            json {
                let! x = Json.tryRead name
                let object = 
                    match x with
                    | Some (x:Map<string, Json>) -> x
                    | None -> Map.empty
                return object
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

    type GeometryProperties = 
        | EllipseProperties of center : V2d * major : V2d * min : V2d
        | NoProperties
         with
            static member ToJson(v : GeometryProperties) =
                match v with
                | NoProperties ->
                    json {
                        return ()
                    }
                | EllipseProperties(center, major, minor) -> 
                    json {
                        do! Json.write "specialGeometry" "ellipse"
                        do! Json.write "center" [| center.X; center.Y |]
                        do! Json.write "major" [| major.X; major.Y |]
                        do! Json.write "minor" [| minor.X; minor.Y |]
                    }

    type GeoJsonGeometryProperties = 
        { geometry : GeometryProperties; selected : bool }
        with
            static member ToJson(v : GeoJsonGeometryProperties) =
                json {
                    do! Json.write "geometryProperties" v.geometry
                    do! Json.write "isSelected" v.selected
                }

    type GeoJsonGeometry =
    | Point                 of coordinates : Coordinate * Option<GeoJsonGeometryProperties>
    | MultiPoint            of coordinates : List<Coordinate> * Option<GeoJsonGeometryProperties>
    | LineString            of coordinates : List<Coordinate> * Option<GeoJsonGeometryProperties>
    | MultiLineString       of coordinates : List<List<Coordinate>> * Option<GeoJsonGeometryProperties>
    | Polygon               of coordinates : List<List<Coordinate>> * Option<GeoJsonGeometryProperties>
    | MultiPolygon          of coordinates : List<List<List<Coordinate>>> * Option<GeoJsonGeometryProperties>
    | GeometryCollection    of geometries :  List<GeoJsonGeometry> * Option<GeoJsonGeometryProperties>
    
        with 

        static member PolygonEmptyProperties (coordinates : List<List<Coordinate>>) = GeoJsonGeometry.Polygon(coordinates, None)
        
        static member ToJson (x: GeoJsonGeometry) = 
            let writeProperties (props : Option<GeoJsonGeometryProperties>) = 
                match props with
                | None -> 
                    json {
                        return ()
                    }
                | Some props ->
                    json {
                        do! Json.write "properties" props
                    }
            json {
                match x with
                | Point(c,p) -> 
                    do! Json.write "type" "Point"
                    do! writeProperties p
                    do! Ext.ToGeoJson c
                | MultiPoint(c,p) -> 
                    do! Json.write "type" "MultiPoint"
                    do! writeProperties p
                    do! Ext.ToGeoJson  c
                | LineString(c,p) ->
                    do! Json.write "type" "LineString"
                    do! writeProperties p
                    do! Ext.ToGeoJson  c
                | MultiLineString(c,p) -> 
                    do! Json.write "type" "MultiLineString"
                    do! writeProperties p
                    do! Ext.ToGeoJson  c
                | Polygon(c, p) ->
                    do! Json.write "type" "Polygon"
                    do! writeProperties p
                    do! Ext.ToGeoJson  c
                | MultiPolygon(c,p) ->
                    do! Json.write "type" "MultiPolygon"
                    do! writeProperties p
                    do! Ext.ToGeoJson  c
                | GeometryCollection(c,p) ->
                    do! Json.write "type" "GeometryCollection"
                    do! writeProperties p
                    do! Json.write "geometries" c
            }
        
        static member FromJson (_: GeoJsonGeometry) = 
            json {
                let! (x: string) = Json.read "type"
                match x with
                | "Point" -> 
                    let! y = Ext.readCoordinate "coordinates"
                    return Point(y,None)
                | "MultiPoint" -> 
                    let! y = Ext.readCoordinateL "coordinates"
                    return MultiPoint(y,None)
                | "LineString" -> 
                    let! y = Ext.readCoordinateL "coordinates"
                    return LineString(y,None)
                | "MultiLineString" -> 
                    let! y = Ext.readCoordinateLL "coordinates"
                    return MultiLineString(y,None)
                | "Polygon" -> 
                    let! y = Ext.readCoordinateLL "coordinates"
                    return Polygon(y,None)
                | "MultiPolygon" -> 
                    let! y = Ext.readCoordinateLLL "coordinates"
                    return MultiPolygon(y,None)
                | "GeometryCollection" -> 
                    let! y = Json.read "geometries"
                    return GeometryCollection(y,None)
                | _ ->
                    return Point(V3d.NaN |> ThreeDim, None)
            }

    type GeoJsonProperties = {
        generatedBy : string
        segmentCount : int
        timestamp : string
        traverseType : string
    }
    with    
        static member ToJson (gp: GeoJsonProperties) =
            json{
                do! Json.write "generatedBy" gp.generatedBy
                do! Json.write "segmentCount" gp.segmentCount
                do! Json.write "timestamp" gp.timestamp
                do! Json.write "type" gp.traverseType
            }
        
        static member FromJson (_: GeoJsonProperties) =
            json{
                let! generatedBy = Json.read "generatedBy"
                let! segmentCount = Json.read "segmentCount"
                let! timestamp = Json.read "timestamp"
                let! traverseType = Json.read "type"

                return {
                    generatedBy = generatedBy;
                    segmentCount = segmentCount; 
                    timestamp = timestamp
                    traverseType = traverseType
                }
            }   
        

    type GeoJsonFeature = {
        geometry   : GeoJsonGeometry
        bbox       : Option<List<float>> 
        properties : Map<string, Json> 
    }
    with    
        static member ToJson (gf: GeoJsonFeature) =
            json{
                do! Json.write "geometry" gf.geometry
                match gf.bbox with 
                | Some b -> do! Json.write "bbox" b
                | None -> ()

                do! Json.write "properties" (Chiron.Object gf.properties)

                do! Json.write "type" "Feature"
            }
        
        static member FromJson (_: GeoJsonFeature) =
            json{
                let! g = Json.read "geometry"
                let! (b:Option<list<float>>) = Json.tryRead "bbox"
                let! properties = Json.tryRead "properties"

                return { geometry = g;bbox = b; properties = Option.defaultValue Map.empty properties }
            }
                    
    type GeoJsonFeatureCollection = {
        features   : List<GeoJsonFeature>
        bbox       : Option<List<float>>
        properties : Option<GeoJsonProperties>
    }
    with            
        static member ToJson (x: GeoJsonFeatureCollection) =
            json{
                do! Json.write "features" x.features
                match x.bbox with 
                | Some b -> do! Json.write "bbox" b
                | None -> ()
                do! Json.write "type" "FeatureCollection"
                do! Json.writeOption "properties" x.properties
            }
            
        static member FromJson (_: GeoJsonFeatureCollection) =
            json{
                let! g = Json.read "features"
                let! (b:Option<List<float>>) = Json.tryRead "bbox"
                let! properties = Json.readOrDefault "properties" None
                return {features = g; bbox = b; properties = properties}
            }

        

