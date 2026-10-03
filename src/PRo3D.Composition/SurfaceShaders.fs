namespace PRo3D

open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive

/// The surface shaders both hosts render OPCs with: the view-space vertex (`vp`) the
/// triangle-size and distance filters work on, the CPU-composed stable MVP, and the
/// texture-or-lighting fragment. Moved from ViewerUtils so PRo3D.Lite shares them; F#
/// resolves `Shader.x` across this module and OpcViewer.Base.Shader (TriangleFilter, OPCFilter).
module Shader =

    open FShade

    type Vertex = {
        [<Position>]        pos     : V4f
        [<Color>]           c       : V4f
        [<TexCoord>]        tc      : V2f

        [<Semantic("ViewSpacePos")>]
        vp : V4f

        [<Semantic("FootPrintProj")>]
        tc0     : V4f

        [<Normal>] 
        n : V3f

        [<SourceVertexIndex>]  sourceVertexIndex : int
    }

    let fixAlpha (v : Vertex) =
        fragment {
           return V4f(v.c.X, v.c.Y,v.c.Z, 1.0f)
        }

    type UniformScope with
        // size filter stuff
        member x.MaxTriangleSize : float = x?MaxTriangleSize
        member x.FilterTriangleEnabled : bool = x?FilterTriangleEnabled

        // filter for distance to home position
        member x.FilterByDistance : bool = x?FilterByDistance
        member x.FilterDistance : float32 = x?FilterDistance
        member x.HomePositionViewSpace : V3f = x?HomePositionViewSpace


    // performs all checks in view space
    let triangleSizeFilter (input : Triangle<Vertex>) =
        triangle {
            let p0 = input.P0.vp.XYZ
            let p1 = input.P1.vp.XYZ
            let p2 = input.P2.vp.XYZ

            // TriangleSize
            let maxSize = uniform?MaxTriangleSize

            let a = (p1 - p0)
            let b = (p2 - p1)
            let c = (p0 - p2)

            let alpha = a.Length < maxSize
            let beta  = b.Length < maxSize
            let gamma = c.Length < maxSize

            let filterDistanceActive : bool = uniform.FilterByDistance
            let disabled = not uniform.FilterTriangleEnabled
            let smallTriangle = alpha && beta && gamma
            // if disabled, let all trianlges pass
            let validTriangle = disabled || smallTriangle

            if filterDistanceActive then
                let filterRange : float32 = uniform.FilterDistance
                let homePositionVSp : V3f = uniform.HomePositionViewSpace

                let inRange =
                    (Vec.distance homePositionVSp p0) < filterRange &&
                    (Vec.distance homePositionVSp p1) < filterRange &&
                    (Vec.distance homePositionVSp p2) < filterRange

                if validTriangle && inRange then
                    yield { input.P0 with sourceVertexIndex = 0 } 
                    yield { input.P1 with sourceVertexIndex = 1 } 
                    yield { input.P2 with sourceVertexIndex = 2 } 
            else
                if validTriangle then
                    yield { input.P0 with sourceVertexIndex = 0 } 
                    yield { input.P1 with sourceVertexIndex = 1 } 
                    yield { input.P2 with sourceVertexIndex = 2 } 
        }
     

    let stableTrafo (v : Vertex) =
        vertex {
            let p = uniform.ModelViewProjTrafo * v.pos

            return
                { v with
                    pos = p
                    c = v.c
                    vp = uniform.ModelViewTrafo * v.pos
                }
        }

    type UniformScope with
        member x.HasNormals : bool = x?HasNormals

    let private diffuseSampler =
        sampler2d {
            texture uniform.DiffuseColorTexture
            filter Filter.Anisotropic
            maxAnisotropy 16
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }
   
    let textureOrLightingIfPossible (v : Vertex) =
        fragment {
            if uniform.HasDiffuseColorTexture then
                let texColor = diffuseSampler.Sample(v.tc,-1.0f) // TODO: to why is -1 being used here as lod offset?
                return texColor
            else
                if uniform.HasNormals then 
                    let ambient = 0.2f
                    let lView = V3f.OOO - v.vp.XYZ |> Vec.normalize
                    let nView = uniform.ModelViewTrafo.TransformDir(v.n) |> Vec.normalize
                    let diffuse = Vec.dot nView lView |> abs
                    return V4f(v.c.XYZ * diffuse + ambient * V3f.III, 1.0f)
                else
                    return v.c
        }

    /// The OPC diffuse texture, opaque. OPC patches always bind one (the Viewer's OPC effect
    /// samples it unconditionally too); reads only the texture coordinate, as OPC patches carry
    /// no vertex colours.
    let opcDiffuseTexture (v : Vertex) =
        fragment {
            let texColor = diffuseSampler.Sample(v.tc,-1.0f)
            return V4f(texColor.XYZ, 1.0f)
        }
