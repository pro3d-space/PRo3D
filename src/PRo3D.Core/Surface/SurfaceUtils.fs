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

    let getSurface (leaf : AdaptiveLeafCase) = 
        match leaf with
        | AdaptiveSurfaces surf -> Some surf
        | _ -> None

    let toAvalSurfaces (surfaces : amap<Guid, AdaptiveLeafCase>) : amap<Guid, AdaptiveSurface> =
        surfaces |> AMap.choose (fun guid surface -> getSurface surface)

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