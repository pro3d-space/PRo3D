namespace PRo3D.Shading


open Aardvark.Base
open Aardvark.Base.Rendering
open Aardvark.Base.Rendering.Effects
open FShade

module Shader =

    type UniformScope with
        member x.PointSize          : float = uniform?PointSize
        member x.LightDirection     : V3d = uniform?LightDirection
        member x.Ambient            : float = uniform?Ambient
        member x.AmbientShadow      : float = uniform?AmbientShadow
        member x.LightViewProj      : M44d = uniform?LightViewProj

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

    let improvedDiffuseTexture (v : Effects.Vertex) =
        fragment {
            let texColor = diffuseSampler.Sample(v.tc,-1.0)
            return texColor
        }

    let lighting (v : Effects.Vertex) =
        fragment {
            let n = v.n |> Vec.normalize // viewspace normal
            let l = uniform.LightLocation //debug direction //TODO rename
            let lv = Vec.normalize (uniform.ViewTrafo * V4d(l,0.0)).XYZ // light direction in view space
            let texColor = diffuseSampler.Sample(v.tc,-1.0) 
            let ambient = uniform.Ambient
            let diffuse = texColor.XYZ * (max 0.0 (Vec.dot n -lv))
            return V4d(diffuse * (1.0 - ambient) + V3d(ambient), 1.0)
        }

    type ShadowVertex =
        {
            [<Position>]                pos     : V4d            
            [<WorldPosition>]           wp      : V4d
            [<TexCoord>]                tc      : V2d
            [<Color>]                   c       : V4d
            [<Normal>]                  n       : V3d
            [<SourceVertexIndex>]       sv      : int
            [<Semantic("ShadowProj")>]  pProj   : V4d

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
            let depth = shadowSampler.Sample(v.tc, -1.0)
            let c = if depth < 1.0 then 1.0 else 0.0
            return V4d(c, c, c, 1.0)
        }

    let dispatchOPCShader (v : ShadowVertex) = 
        fragment {
            let drawShadows : bool = uniform?drawShadows
            if  drawShadows then
                let p = v.pProj.XYZ / v.pProj.W
                let tc = V3d(0.5, 0.5,0.5) + V3d(0.5, 0.5, 0.5) * p.XYZ
                let shadow =
                    if tc.X < 0.0 || tc.X > 1.0 || tc.Y < 0.0 || tc.Y > 1.0 then 1.0
                    else
                    let lightDepth = min tc.Z 1.0
                    (shadowSampler.Sample(tc.XY, lightDepth - 0.000017))
                let ambient = uniform.AmbientShadow
                let d = ambient + shadow * (1.0 - ambient) //TODO proper lighting if needed

                let texColor = diffuseSampler.Sample(v.tc,-1.0)
                return V4d(texColor.XYZ * d, 1.0)
            else 
                return diffuseSampler.Sample(v.tc,-1.0)
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
                    let lv = Vec.normalize (uniform.ViewTrafo * V4d(l,0.0)).XYZ // light direction in view space
                    let texColor = diffuseSampler.Sample(v.tc,-1.0) 
                    let ambient = uniform.Ambient
                    let diffuse = texColor.XYZ * (max 0.0 (Vec.dot n -lv))
                    return V4d(diffuse * (1.0 - ambient) + V3d(ambient), 1.0)
                else 
                    return diffuseSampler.Sample(v.tc,-1.0)
        }   