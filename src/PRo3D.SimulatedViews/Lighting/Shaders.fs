namespace PRo3D.Shading


open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Rendering.Effects
open FShade

module Shader =

    type UniformScope with
        member x.PointSize          : float32 = uniform?PointSize
        member x.LightDirection     : V3f = uniform?LightDirection
        member x.Ambient            : float32 = uniform?Ambient
        member x.AmbientShadow      : float32 = uniform?AmbientShadow
        member x.LightViewProj      : M44f = uniform?LightViewProj

    let private diffuseSampler =
        sampler2d {
            texture uniform?DiffuseColorTexture
            filter Filter.Anisotropic
            maxAnisotropy 16
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    let private shadowSampler =
        sampler2dShadow {
            texture uniform?ShadowTexture
            filter Filter.MinMagLinear
            addressU WrapMode.Border
            addressV WrapMode.Border
            borderColor C4f.White
            comparison ComparisonFunction.LessOrEqual
        }

    let private depthSampler =
        sampler2d {
            texture uniform?DepthTexture
            filter Filter.Anisotropic
            maxAnisotropy 16
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    let improvedDiffuseTexture (v : Effects.Vertex) =
        fragment {
            let texColor = diffuseSampler.Sample(v.tc,-1.0f)
            return texColor
        }

    let lighting (v : Effects.Vertex) =
        fragment {
            let n = v.n |> Vec.normalize // viewspace normal
            let l = uniform.LightLocation //debug direction //TODO rename
            let lv = Vec.normalize (uniform.ViewTrafo * V4f(l,0.0f)).XYZ // light direction in view space
            let texColor = diffuseSampler.Sample(v.tc,-1.0f) 
            let ambient = uniform.Ambient
            let diffuse = texColor.XYZ * (max 0.0f (Vec.dot n -lv))
            return V4f(diffuse * (1.0f - ambient) + V3f(ambient), 1.0f)
        }

    type ShadowVertex =
        {
            [<Position>]                pos     : V4f            
            [<WorldPosition>]           wp      : V4f
            [<TexCoord>]                tc      : V2f
            [<Color>]                   c       : V4f
            [<Normal>]                  n       : V3f
            [<SourceVertexIndex>]       sv      : int
            [<Semantic("ShadowProj")>]  pProj   : V4f

        }

    let shadowShaderV (v : ShadowVertex) =
        vertex {
            //TODO somewhere LVP is wrongly multiplied with MVP, but where?
            // multiplying with inv works, but is a workaround
            let vp = (uniform.LightViewProj * uniform.ModelViewProjTrafoInv) * v.pos
            return { v with pProj =  vp} 
        }
  

    let showShadowMap (v : Vertex) =
        fragment {
            let depth = shadowSampler.Sample(v.tc, -1.0f)
            let c = if depth < 1.0f then 1.0f else 0.0f
            return V4f(c, c, c, 1.0f)
        }

    let dispatchOPCShader (v : ShadowVertex) = 
        fragment {
            let drawShadows : bool = uniform?drawShadows
            if  drawShadows then
                let p = v.pProj.XYZ / v.pProj.W
                let tc = V3f(0.5f, 0.5f,0.5f) + V3f(0.5f, 0.5f, 0.5f) * p.XYZ
                let shadow =
                    if tc.X < 0.0f || tc.X > 1.0f || tc.Y < 0.0f || tc.Y > 1.0f then 1.0f
                    else
                    let lightDepth = min tc.Z 1.0f
                    (shadowSampler.Sample(tc.XY, lightDepth - 0.000017f))
                let ambient = uniform.AmbientShadow
                let ambientShadow = ambient + shadow * (1.0f - ambient) //TODO proper lighting if needed
                let texColor = diffuseSampler.Sample(v.tc,-1.0f) 
                return V4f(texColor.XYZ * ambientShadow, 1.0f)
            else 
                return diffuseSampler.Sample(v.tc,-1.0f)
        }  

    let mask (v : Vertex) = 
        fragment {
            let useMask : bool = uniform?useMask
            if useMask then
                return uniform?maskColor
            else return v.c
        }

    let dispatchOBJShader (v : ShadowVertex) = 
        fragment {
            let useMask : bool = uniform?useMask
            if useMask then return uniform?maskColor else
                let useLighting : bool = uniform?useLighting
            
                if useLighting then
                    let n = v.n |> Vec.normalize // viewspace normal
                    let l = uniform.LightDirection
                    let lv = Vec.normalize (uniform.ViewTrafo * V4f(l,0.0f)).XYZ // light direction in view space
                    let texColor = diffuseSampler.Sample(v.tc,-1.0f) 
                    let ambient = uniform.Ambient
                    let diffuse = texColor.XYZ * (max 0.0f (Vec.dot n -lv))
                    return V4f(diffuse * (1.0f - ambient) + V3f(ambient), 1.0f)
                else 
                    return diffuseSampler.Sample(v.tc,-1.0f)
        }   