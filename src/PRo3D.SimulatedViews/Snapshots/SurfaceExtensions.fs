namespace PRo3D.Core.Surface

open PRo3D.Core
open Aardvark.Base
open FSharp.Data.Adaptive

module SurfaceUtils = 
    let leafToSurface (leaf : AdaptiveLeafCase) = 
        match leaf with 
        | AdaptiveLeaf.AdaptiveSurfaces s -> s
        | _ -> leaf |> sprintf "wrong type %A; expected MSurfaces" |> failwith
        