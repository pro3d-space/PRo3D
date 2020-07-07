namespace OrientationCube

open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.Base.Rendering
open Aardvark.SceneGraph
open Aardvark.Rendering
open Aardvark.UI
open Aardvark.UI.Trafos
open Aardvark.UI.Primitives

module Shader =
    
    open FShade
    
    let aspectTrafo (v : Effects.Vertex) =
        vertex {
            let vps = uniform.ViewportSize
            let aspect = (float vps.X) / (float vps.Y)
            let tx = 0.75
            let ty = 0.75
            return {v with pos = V4d(v.pos.X / aspect + tx, v.pos.Y + ty, v.pos.Z, v.pos.W)}
        }
    
    let samplerAniso =
        sampler2d {
            texture uniform.DiffuseColorTexture
            filter Filter.Anisotropic
        }

    let anisoTexShader (v : Effects.Vertex) =
        fragment {
            let c = samplerAniso.Sample v.tc
            return {v with c = c}
        }
        
module Sg =
    let loadModel (filename : string) =
        Aardvark.SceneGraph.IO.Loader.Assimp.load filename
        |> Sg.adapter
        |> Sg.noEvents
    
    let orthoOrientation (camView : IMod<CameraView>) (model : ISg<'msg>)  =
        //camView |> Mod.map (fun x -> x.)
        let viewTrafo =
            camView
            |> Mod.map ( fun cv ->
                let view = CameraView.look V3d.OOO (V3d.OIO * -1.0) Mars.Terrain.up
                view.ViewTrafo
            )
        
        let orthoTrafo =
            let d = 5.0
            let t = V3d((-d+1.0), -d+1.0, 0.0)
            let min = V3d(-d, -d, -d*2.0)
            let max = V3d(d, d, d*2.0)
            let fr = Frustum.ortho (Box3d(min, max))
            Mod.constant (Frustum.orthoTrafo fr)
        
        model
        |> Sg.trafo (Mod.constant (Trafo3d.Scale(1.0,1.0,-1.0)))
        |> Sg.trafo (Mod.constant (Trafo3d.RotationXInDegrees(90.0)))
        |> Sg.trafo ( camView |> Mod.map ( fun v ->  Trafo3d.RotateInto(V3d.OOI, v.Sky) ) )
        |> Sg.viewTrafo viewTrafo
        |> Sg.projTrafo orthoTrafo
        |> Sg.shader {
            do! DefaultSurfaces.trafo
            do! Shader.aspectTrafo
            do! Shader.anisoTexShader
        }
        |> Sg.pass (RenderPass.after "cube" RenderPassOrder.Arbitrary RenderPass.main)
    
//    let insideOrientation (camView : IMod<CameraView>) (frustum : IMod<Frustum>) (model : ISg<'msg>) =
//        let viewTrafo =
//            camView
//            |> Mod.map ( fun cv ->
//                let view = CameraView.look V3d.OOO cv.Forward V3d.OOI
//                view.ViewTrafo
//            )
//        
//        let perspTrafo = frustum |> Mod.map ( fun f -> Frustum.projTrafo f)
//        
//        model
//        |> Sg.trafo (Mod.constant (Trafo3d.Scale(100.0, 100.0, 100.0)))
//        |> Sg.viewTrafo viewTrafo
//        |> Sg.projTrafo perspTrafo
//        |> Sg.shader {
//            do! DefaultSurfaces.trafo
//            do! DefaultSurfaces.diffuseTexture
//        }
//        |> Sg.pass (RenderPass.after "cube" RenderPassOrder.Arbitrary RenderPass.main)
    
