namespace PRo3D.Base



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

        static member tryReadProperties name = 
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
    
    type Geometry =
    | Point                 of coordinates : Coordinate
    | MultiPoint            of coordinates : List<Coordinate>
    | LineString            of coordinates : List<Coordinate>
    | MultiLineString       of coordinates : List<List<Coordinate>>
    | Polygon               of coordinates : List<List<Coordinate>>
    | MultiPolygon          of coordinates : List<List<List<Coordinate>>>
    | GeometryCollection    of geometries :  List<Geometry>
    
        with 
        
        static member ToJson (x: Geometry) = 
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
        
        static member FromJson (_: Geometry) = 
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
            
    type Feature = {
        geometry   : Geometry
        bbox       : Option<List<float>> 
        properties : Map<string, Json> 
    }
    with    
        static member ToJson (gf: Feature) =
            json{
                do! Json.write "geometry" gf.geometry
                match gf.bbox with 
                | Some b -> do! Json.write "bbox" b
                | None -> ()

                do! Json.write "properties" (Chiron.Object gf.properties)

                do! Json.write "type" "Feature"
            }
        
        static member FromJson (_: Feature) =
            json{
                let! g = Json.read "geometry"
                let! (b:Option<list<float>>) = Json.tryRead "bbox"
                let! properties = Json.tryRead "properties"

                return { geometry = g;bbox = b; properties = Option.defaultValue Map.empty properties }
            }
                    
    type FeatureCollection = {
        features   : List<Feature>
        bbox       : Option<List<float>>
        //properties : Option<List<string * string>>
    }
    with            
        static member ToJson (x: FeatureCollection) =
            json{
                do! Json.write "features" x.features
                match x.bbox with 
                | Some b -> do! Json.write "bbox" b
                | None -> ()
                do! Json.write "type" "FeatureCollection"
            }
            
        static member FromJson (_: FeatureCollection) =
            json{
                let! g = Json.read "features"
                let! (b:Option<List<float>>) = Json.tryRead "bbox"
                return {features = g;bbox =b}
            }
                
    type ParseError =
        | PropertyNotFound         of propertyName : string * feature : Feature
        | PropertyHasWrongType     of propertyName : string * feature : Feature * expected : string * got : string * str : string
        | GeometryTypeNotSupported of geometryType : string

    let (.|) (point : Feature) (propertyName : string) : Result<Json, ParseError> =
        match point.properties |> Map.tryFind propertyName with
        | None -> PropertyNotFound(propertyName, point) |> error
        | Some v -> v |> Ok

    // the parsing functions are a bit verbose but focus on good error reporting....
    let parseIntProperty (feature : Feature) (propertyName : string) : Result<int, ParseError> =
        result {
            let json = feature.|propertyName
            match json with
            | Result.Ok(Json.String p) -> 
                return! 
                    Result.Int.tryParse p 
                    |> Result.mapError (fun _ -> 
                        PropertyHasWrongType(propertyName, feature, "int", "string which could not be parsed to an int.", sprintf "%A" json)
                    )
            | Result.Ok(Json.Number n) -> 
                if ((float n) % 1.0) = 0 then  // here we might have gotten a double, instead of inplicity truncating, we report is an error
                    return int n
                else
                    return! 
                        PropertyHasWrongType(propertyName, feature, "int", "decimal which was not an integer.", sprintf "%A" n)
                        |> error
            | Result.Ok(e) -> 
                return! 
                    error (
                        PropertyHasWrongType(propertyName, feature, "Json.Number", e.ToString(), sprintf "%A" json)
                    )
            | Result.Error e -> 
                return! Result.Error e
        }

    let parseDoubleProperty (feature : Feature) (propertyName : string) : Result<float, ParseError> =
        result {
            let json = feature.|propertyName 
            match json with
            | Result.Ok(Json.String p) -> 
                return! 
                    Result.Double.tryParse p 
                    |> Result.mapError (fun _ -> 
                        PropertyHasWrongType(propertyName, feature, "double", "string which could not be parsed to a double", sprintf "%A" json)
                    )
            | Result.Ok(Json.Number n) -> 
                return double n
            | Result.Ok(e) -> 
                return! 
                    error (
                        PropertyHasWrongType(propertyName, feature, "Json.Number", sprintf "%A" e, sprintf "%A" json)
                    )
            | Result.Error e -> 
                return! Result.Error e
        }

    let parseStringProperty (feature : Feature) (propertyName : string) =
        match feature.|propertyName with
        | Result.Ok(Json.String p) -> Result.Ok p
        | Result.Ok(e) -> Result.Error (PropertyHasWrongType(propertyName, feature, "Json.String", e.ToString(), e.ToString()))
        | Result.Error(e) -> Result.Error(e)