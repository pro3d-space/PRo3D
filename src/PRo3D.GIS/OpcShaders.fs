namespace PRo3D.Core

[<AutoOpen>]
module Shader =

    open Aardvark.Base
    open Aardvark.Rendering
    open Aardvark.Rendering.Effects
    
    open FShade

    /// Same as OpcViewer.Base.Shader.LoDColor.LoDColor, but with ONE return: the packaged
    /// one has two, and a second return path makes FShade duplicate the whole rest of the
    /// effect in the generated GLSL (#719, FShade#39). This one sits near the START of the
    /// OPC surface stack, so it duplicated almost all of it. Use this copy there.
    let LoDColor  (v : Vertex) =
        fragment {
            let mutable color = v.c
            if uniform?LodVisEnabled then
                let c : V4f = uniform?LoDColor
                let gamma = 1.0f
                let grayscale = 0.2126f * v.c.X ** gamma + 0.7152f * v.c.Y ** gamma  + 0.0722f * v.c.Z ** gamma
                color <- grayscale * c
            return color
        }


