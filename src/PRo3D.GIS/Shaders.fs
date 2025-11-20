namespace PRo3D.SPICE

open Aardvark.Base
open Aardvark.Rendering

module Shaders = 
    
    open FShade
    open Aardvark.Rendering.Effects


    type UniformScope with  
        member x.SunDirectionWorld : V3f = uniform?SunDirectionWorld
        member x.SunLightEnabled : bool = uniform?SunLightEnabled

    type Vertex = {
        [<Position>]                pos     : V4f
        [<Normal>]                  n       : V3f
        [<BiNormal>]                b       : V3f
        [<Tangent>]                 t       : V3f
        [<Color>]                   c       : V4f
        [<TexCoord>]                tc      : V2f
        [<Semantic("LightDir")>]    vldir    : V3f
    }

    let stableTrafo (v : Vertex) =
        vertex {
            let vp = uniform.ModelViewTrafo * v.pos

            return 
                { v with
                    pos = uniform.ProjTrafo * vp
                    c = v.c
                }
        }

    type PlanetNormals = 
        {
            [<Position>] pos : V4f
            [<Semantic("ViewPos")>] vp: V4f
            [<Semantic("LightDir")>]  vldir    : V3f
            [<Normal>] n : V3f
        }

    let planetLocalLightingViewSpace (v : PlanetNormals) = 
        vertex {
            let vp = uniform.ModelViewTrafo * v.pos
            let planetCenter = uniform.ViewTrafo.TransformPos(V3f.Zero)
            return 
                { v with
                    vp = vp
                    vldir = uniform.ViewTrafo.TransformDir uniform.SunDirectionWorld 
                    n = (vp.XYZ - planetCenter) |> Vec.normalize
                }
        }

    let modelNormalViewSpace (v : Vertex) =
        vertex { 
            return 
                { v with
                    n = uniform.ModelViewTrafoInv.TransposedTransformDir v.n |> Vec.normalize 
                    b = uniform.ModelViewTrafo.TransformDir v.b |> Vec.normalize
                    t = uniform.ModelViewTrafo.TransformDir v.t |> Vec.normalize
                 }
        }

    let solarLighting (v : Vertex) = 
        fragment {
            let n = v.n |> Vec.normalize
            let c = v.vldir |> Vec.normalize

            let ambient = 0.01f 
            let diffuse = Vec.dot c n |> max 0.0f 

            let l = ambient + (1.0f - ambient) * diffuse

            if uniform.SunLightEnabled then
                return V4f(v.c.XYZ * l, v.c.W)
            else
                return v.c
        }


    let private specular =
        sampler2d {
            texture uniform?SpecularColorTexture
            filter Filter.MinMagMipLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    type UniformScope with
        member x.HasSpecularColorTexture : bool = x?HasSpecularColorTexture

    let solarLightingWithSpecular (v : Vertex) = 
        fragment {
            let n = v.n |> Vec.normalize
            let c = v.vldir |> Vec.normalize

            let ambient = 0.2f 
            let diffuse = Vec.dot c n |> clamp 0.0f 1.0f 

            let l = ambient + (1.0f- ambient) * diffuse

            let s = Vec.dot c n 

            let specColor =
                if uniform.HasSpecularColorTexture then 
                    let v = specular.Sample(v.tc).XYZ
                    v.X * V3f.III
                else 
                    V3f.III

            let specularTerm = clamp 0.0f 1.0f (pown s 32)
            let specShininess = specColor * specularTerm

            let c = v.c.XYZ * l //+ specShininess

            return V4f(Fun.Min(c, 1.0f ), v.c.W)
        }
    let viewProjSpaceDepthToColor (v : Vertex) =
        fragment {
            let vp = uniform.ModelViewProjTrafo * v.pos

            let d = vp.Z / vp.W
            return V4f(d, 0.0f , 0.0f , 1.0f )
        }

    type TexturedVertex = {
        [<TexCoord>] tc : V2f
        [<Normal>] n : V3f
        [<Tangent>] t : V3f
    }

    let genAndFlipTextureCoord (v : TexturedVertex) =
        vertex {
            return { v with tc = V2f(v.tc.X + 0.5f, 1.0f - v.tc.Y) }
        }



    let private normalSampler =
        sampler2d {
            texture uniform?NormalMapTexture
            filter Filter.MinMagMipLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    let internal normalMap (v : TexturedVertex) =
        fragment {
            let hasNormalMap : bool = uniform?HasNormalMap
            if hasNormalMap then
                let texColor = normalSampler.Sample(v.tc).XYZ
                let texNormal = (2.0f * texColor - V3f.III) |> Vec.normalize

                // make sure tangent space basis is orthonormal -> perform gram-smith normalization
                let n = v.n.Normalized
                let t = v.t.Normalized
                let t = (t - n * (Vec.dot t n)) |> Vec.normalize
                let b = (Vec.cross n t) |> Vec.normalize // NOTE: v.b might be used here to maintain handedness
                        
                // texture normal from tangent to world space
                let n = 
                    texNormal.X * t +
                    texNormal.Y * b +
                    texNormal.Z * n

                return { v with n = n } 
            else
                return v
        }
            

    type ShadowVertex = {
        [<Position>]                       p   : V4f
        [<Semantic("PosShadowViewProj")>]  viewProjPos : V4f
        [<Color>]                          c  : V4f
    }

    type UniformScope with
        member x.StableModelViewProjTexture : M44f = uniform?StableModelViewProjTexture
        member x.HasShadowMap : bool = uniform?HasShadowMap

    let private shadowSampler =
        sampler2dShadow {
            texture uniform?ShadowMap
            filter Filter.MinMagLinear
            addressU WrapMode.Border
            addressV WrapMode.Border
            borderColor C4f.White
            comparison ComparisonFunction.LessOrEqual
        }

    let transformShadowVertices (v : ShadowVertex) = 
        vertex {
            return 
                { v with
                    viewProjPos = uniform.StableModelViewProjTexture * v.p
                }
        }


    let shadow (v : ShadowVertex) =
        fragment {
            let bias : float32 = uniform?ShadowMapBias
            let p = v.viewProjPos.XYZ / v.viewProjPos.W
            let tc = V3f(0.5f , 0.5f ,0.5f ) + V3f(0.5f , 0.5f , 0.5f ) * p.XYZ
            let d = min 1.0f (max 0.2f (shadowSampler.Sample(tc.XY, tc.Z + bias)))
            return V4f(v.c.XYZ * d, v.c.W)
        }

    let offsets = 
        [|
            V2f(-1.0f , -1.0f )
            V2f(1.0f , -1.0f )
            V2f(-1.0f , 1.0f )
            V2f(1.0f , 1.0f )
        |]

    let shadowPCF (v : ShadowVertex) =
        fragment {
            let bias : float32 = uniform?ShadowMapBias
            let p = v.viewProjPos.XYZ / v.viewProjPos.W
            let tc = V3f(0.5f , 0.5f , 0.5f ) + V3f(0.5f , 0.5f , 0.5f ) * p.XYZ

            let sampleRadius = 1.0f / (float32 (Vec.MaxElement shadowSampler.Size)) 
            let numSamples = 4

            let mutable shadow = 0.0f 
            for i in 0 .. offsets.Length - 1 do
                shadow <- shadow + shadowSampler.Sample(tc.XY + offsets[i] * sampleRadius, tc.Z + bias)

            shadow <- shadow / float32 numSamples

            let d = min 1.0f (max 0.2f shadow)
            return V4f(v.c.XYZ * d, v.c.W)
        }
