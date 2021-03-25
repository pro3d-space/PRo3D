namespace PRo3D

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open Adaptify.FSharp.Core

module SurfaceUtils =
    let toModSurface (leaf : AdaptiveLeafCase) = 
         adaptive {
            let c = leaf
            match c with 
                | AdaptiveSurfaces s -> return s
                | _ -> return c |> sprintf "wrong type %A; expected AdaptiveSurfaces" |> failwith
            }
         
    let lookUp guid (surfaces:amap<Guid, AdaptiveLeafCase>) =
        let entry = surfaces |> AMap.find guid
        entry |> AVal.bind(fun x -> x |> toModSurface)

    let toAvalSurfaces (surfaces : amap<Guid, AdaptiveLeafCase>) =
        surfaces |> AMap.map (fun guid surface -> toModSurface surface)