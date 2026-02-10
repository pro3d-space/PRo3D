namespace PRo3D.Core

[<AutoOpen>]
module Shader =

    open Aardvark.Base
    open Aardvark.Rendering
    open Aardvark.Rendering.Effects
    
    open FShade

    let LoDColor  (v : Vertex) =
        fragment {
            if uniform?LodVisEnabled then
                let c : V4f = uniform?LoDColor
                let gamma = 1.0f
                let grayscale = 0.2126f * v.c.X ** gamma + 0.7152f * v.c.Y ** gamma  + 0.0722f * v.c.Z ** gamma 
                return grayscale * c 
            else return v.c
        }


