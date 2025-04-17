namespace PRo3D.Viewer

open Aardvark.Base
open Aardvark.UI.Primitives
open Chiron
open PRo3D.Base.Annotation.GeoJSON
open PRo3D.Base
open PRo3D.Core

module TraverseUtilities =
    type TraverseParseError =
        | PropertyNotFound         of propertyName : string * feature : GeoJsonFeature
        | PropertyHasWrongType     of propertyName : string * feature : GeoJsonFeature * expected : string * got : string * str : string
        | GeometryTypeNotSupported of geometryType : string
    
    let (.|) (point : GeoJsonFeature) (propertyName : string) : Result<Json, TraverseParseError> =
        match point.properties |> Map.tryFind propertyName with
        | None -> PropertyNotFound(propertyName, point) |> error
        | Some v -> v |> Ok

    // the parsing functions are a bit verbose but focus on good error reporting....
    let parseIntProperty (feature : GeoJsonFeature) (propertyName : string) : Result<int, TraverseParseError> =
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
    
    let parseDoubleProperty (feature : GeoJsonFeature) (propertyName : string) : Result<float, TraverseParseError> =
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

    let parseStringProperty (feature : GeoJsonFeature) (propertyName : string) =
        match feature.|propertyName with
        | Result.Ok(Json.String p) -> Result.Ok p
        | Result.Ok(e) -> Result.Error (PropertyHasWrongType(propertyName, feature, "Json.String", e.ToString(), e.ToString()))
        | Result.Error(e) -> Result.Error(e)
    
    let compareNatural (left: AdaptiveTraverse) (right: AdaptiveTraverse) =
        Sorting.compareNatural left.tName right.tName

    let computeSolFlyToParameters
        (sol : Sol) 
        (referenceSystem : ReferenceSystem) 
        (rotation: Trafo3d)
        : V3d * V3d * V3d =

        let north = rotation.Forward.TransformDir referenceSystem.northO
        let up    = rotation.Forward.TransformDir referenceSystem.up.value

        north, up, (sol.location[0] + 2.0 * up)

    let computeSolViewplanParameters
        (sol : Sol)
        (referenceSystem : ReferenceSystem)
        (rotation)
        : (string * Trafo3d * V3d * ReferenceSystem) = 

        //let loc =(sol.location + sol.location.Normalized * 0.5)
        //let locTranslation = Trafo3d.Translation(loc)        

        let name = sprintf "Sol %d" sol.solNumber

        name, rotation, sol.location[0], referenceSystem

