namespace PRo3D

open Aardvark.Base

module Shaders =

    open FShade
    open Aardvark.Rendering

    type CursorVertex = 
        {
            [<Semantic("ViewPos")>]
            viewPos : V3f

            [<Position>]
            pos : V4f

            [<Color>]
            c : V4f
        }

    type UniformScope with
        member x.CursorViewSpace : V4f = uniform?CursorViewSpace
        member x.CursorWorldSizeSquared : V4f = uniform?CursorWorldSizeSquared
        member x.CursorShaderEnabled : bool = uniform?CursorShaderEnabled


    let donutVertex (v : CursorVertex) = 
        vertex {
            let vp = uniform.ModelViewTrafo *  v.pos 
            return 
                { v with 
                    viewPos = vp.XYZ
                }
        }

    let donutFragment (v : CursorVertex) =
        fragment {
            // this is written in mutable style intentionally to reduce code bloat in fshade composition
            let mutable c = v.c

            if uniform.CursorShaderEnabled && uniform.CursorViewSpace.W > 0.0f then
                let d = Vec.lengthSquared (uniform.CursorViewSpace.XYZ - v.viewPos)
                let r = 
                    Fun.Smoothstep(d, uniform.CursorWorldSizeSquared.X, uniform.CursorWorldSizeSquared.Y) - 
                    Fun.Smoothstep(d, uniform.CursorWorldSizeSquared.Z, uniform.CursorWorldSizeSquared.W)
                c <- r * V4f.IIII + v.c * (1.0f - r)

            return c
        }