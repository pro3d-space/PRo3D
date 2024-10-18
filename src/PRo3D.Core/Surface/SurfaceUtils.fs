namespace PRo3D

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.UI

open Aardvark.UI
open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open Adaptify.FSharp.Core

module SurfaceUtils =

    [<Obsolete("toModSurface might throw and is not safe to use. see https://github.com/pro3d-space/PRo3D/issues/277")>]
    let toModSurface (leaf : AdaptiveLeafCase) = 
         adaptive {
            let c = leaf
            match c with 
                | AdaptiveSurfaces s -> return s
                | _ -> return c |> sprintf "wrong type %A; expected AdaptiveSurfaces" |> failwith
            }
         
    [<Obsolete("lookup might throw and is not safe to use. see https://github.com/pro3d-space/PRo3D/issues/277")>]
    let lookUp (guid : Guid) (table:amap<Guid, AdaptiveLeafCase>) =
        let entry = 
            adaptive { 
                match! table |> AMap.tryFind guid with
                | None -> 
                    // we can only throw here..... see https://github.com/pro3d-space/PRo3D/issues/277
                    return failwithf "surface with guid %A not found" guid
                | Some e -> 
                    return e
            }

        entry |> AVal.bind(fun x -> x |> toModSurface)

    let toAvalSurfaces (surfaces : amap<Guid, AdaptiveLeafCase>) =
        surfaces |> AMap.map (fun guid surface -> toModSurface surface)

    let getSurfaceName (guid : System.Guid) (surfaces : AdaptiveSurfaceModel) =
        surfaces.surfaces.flat
            |> AMap.find guid
            |> AVal.bind (fun x -> (toModSurface x) 
                                      |> AVal.bind (fun x -> x.name))

    let toName (surface : aval<AdaptiveSurface>) =
        surface |> AVal.bind (fun s -> s.name)

    let internal surfaceHasName (name : aval<option<string>>) (otherName : aval<string>) = 
        adaptive {
            let! name = name
            let! otherName = otherName
            match name with
            | Some name -> return name = otherName
            | None -> return false
        }

    /// returns None if no surface with the given name was found
    /// if multiple surfaces with the given name exist, a warning is logged and one of them is returned
    let findASurfaceIdByName (surfaceModel : AdaptiveSurfaceModel) (name : aval<option<string>>) =
        let surfaces = toAvalSurfaces surfaceModel.surfaces.flat
        let filtered = surfaces |> AMap.filterA (fun k v -> surfaceHasName name (toName v))
        let surfaceId =
            adaptive {
                let! count = AMap.count filtered
                if count = 0 then
                    let first = (filtered |> AMap.keys |> AList.ofASet |> AList.tryFirst)    
                    return! first
                else if count > 1 then
                    Log.warn "[Comparison] Found multiple surfaces with the same name."
                    let first = (filtered |> AMap.keys |> AList.ofASet |> AList.tryFirst)    
                    return! first
                else return None
            }
        surfaceId

    let internal getSingleValue (aMap : amap<'k,'v>) =
        aMap |> AMap.toASet 
             |> AList.ofASet 
             |> AList.tryFirst 
             |> AVal.map (fun x -> x |> Option.map (fun x -> snd x))

    let internal bindAdaptiveOption (value : aval<option<aval<'a>>>) =
        adaptive {
            let! value = value
            match value with
            | Some value -> 
                let! value = value
                return Some value
            | None -> return None
        }

    let internal mapAdaptiveOption (mapping : 'a -> 'b) (value : aval<option<aval<'a>>>)  =
        adaptive {
            let! value = value
            match value with
            | Some value -> 
                let! value = value
                return Some (mapping value)
            | None -> return None
        }

    /// returns None if no surface with the given name was found
    /// if multiple surfaces with the given name exist, a warning is logged and one of them is returned
    let findASurfaceSgByName (surfaceModel : AdaptiveSurfaceModel) (name : aval<option<string>>) =
        let surfaces = toAvalSurfaces surfaceModel.surfaces.flat
        let filtered = surfaces |> AMap.filterA (fun k v -> surfaceHasName name (toName v))
        let surfaceId =
            adaptive {
                let! count = AMap.count filtered
                if count = 0 then
                    let first = getSingleValue filtered
                                  |> mapAdaptiveOption (fun x -> x.guid |> AVal.bind (fun x -> AMap.find x surfaceModel.sgSurfaces))
                                  |> bindAdaptiveOption
                    return! first
                else if count > 1 then
                    Log.warn "[Comparison] Found multiple surfaces with the same name."
                    let first = getSingleValue filtered
                                  |> mapAdaptiveOption (fun x -> x.guid |> AVal.bind (fun x -> AMap.find x surfaceModel.sgSurfaces))
                                  |> bindAdaptiveOption
                    return! first
                else return None
            }
        surfaceId
        

            