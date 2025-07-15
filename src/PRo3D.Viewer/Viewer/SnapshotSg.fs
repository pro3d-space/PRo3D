namespace PRo3D

open Aardvark.Service

open System
open System.Collections.Concurrent
open System.IO
open System.Diagnostics

open Adaptify.FSharp.Core

open Aardvark.Base
open Aardvark.Base.Geometry
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Rendering.Text
open Aardvark.UI
open Aardvark.UI.Operators
open Aardvark.UI.Primitives
open Aardvark.UI.Trafos
open Aardvark.UI.Animation
open Aardvark.Application

open Aardvark.Data.Opc
open Aardvark.SceneGraph.SgPrimitives.Sg
open Aardvark.VRVis

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Navigation2
open PRo3D.Bookmarkings

open PRo3D.Core.Surface
open PRo3D.Viewer

open PRo3D.SimulatedViews
//open PRo3D.Minerva
//open PRo3D.Linking
 
open Aether
open Aether.Operators

open PRo3D.Core.Surface


open Adaptify.FSharp.Core
open OpcViewer.Base.Shader
open ViewerUtils
open PRo3D.ViewerApp

///// TO TEST NEW RENDERTASKS IN VIEWER
// ADD IN PROGRAM.FS
//GL.RuntimeConfig.UseNewRenderTask <- true
//
//let sg = SnapshotSg.createSceneGraph m.scene.surfacesModel.sgGrouped overlayed depthTested true m
//            |> Sg.noEvents
//FreeFlyController.controlledControl // WORKS! NEED TO ADD ORBIT CONTROLLER
//            m.navigation.camera (fun msg ->
//                                    msg |> Navigation.FreeFlyAction
//                                        |> ViewerAction.NavigationMessage)
//            frustum 
//            (renderControlAttributes id m) sg

///// END DEBUG

// testing batch processing: 
// PRo3D.Snapshots.exe --scn "C:\Users\rnowak\Desktop\Pro3D\TestScenes\prio.pro3d" --asnap "C:\Users\rnowak\Desktop\Pro3D\PRo3D\bin\Debug\net5.0\images\batchRendering.json" --out "C:\Users\rnowak\Desktop\Pro3D\PRo3D\bin\Debug\net5.0\images" --exitOnFinish --verbose

/// PRo3D Sg for batch rendering (snapshots)
module SnapshotSg =

    let isViewPlanVisible (m:AdaptiveModel) =
        adaptive {
            let! id = m.scene.viewPlans.selectedViewPlan
            match id with
            | Some v -> 
                let! vp = m.scene.viewPlans.viewPlans |> AMap.tryFind v
                match vp with
                | Some selVp -> return! selVp.isVisible
                | None -> return false
            | None -> return false
        }

    /// creaste simple sg for debugging purposes
    let createDebugSg (m:AdaptiveModel) =
        let camera = AVal.map2 (fun v f -> Camera.create v f) m.navigation.camera.view m.frustum 
        let frustum = AVal.map2 (fun o f -> o |> Option.defaultValue f) m.overlayFrustum m.frustum // use overlay frustum if Some()
        let sg =
            Sg.box' C4b.White Box3d.Unit 
                // here we use fshade to construct a shader: https://github.com/aardvark-platform/aardvark.docs/wiki/FShadeOverview
                |> Sg.effect [
                        DefaultSurfaces.trafo                 |> toEffect
                        DefaultSurfaces.constantColor C4f.Red |> toEffect
                        DefaultSurfaces.simpleLighting        |> toEffect
                    ]
                // extract our viewTrafo from the dynamic cameraView and attach it to the scene graphs viewTrafo 
                |> Sg.camera camera
                // compute a projection trafo, given the frustum contained in frustum
                |> Sg.projTrafo (frustum |> AVal.map Frustum.projTrafo    )
                |> Sg.trafo (m.scene.exploreCenter  |> AVal.map Trafo3d.Translation)
        sg

    /// create scengegraph using Rendering.RenderCommands
    let createSceneGraph (sgGrouped:alist<amap<Guid,AdaptiveSgSurface>>) 
                         overlayed depthTested (runtime : IRuntime) (allowFootprint : bool) 
                         (allowDepthview : bool) 
                         (calcDepth : bool) id cam (m:AdaptiveModel)  =
        let view = m.navigation.camera.view
        let grouped = ViewerUtils.createGroupedSgs sgGrouped view allowFootprint allowDepthview m      


        let commands = 
            
            alist {
                for sg in grouped do
                    if (not calcDepth) then yield RenderCommand.ClearDepth(1.0) 
                    let sg = sg :> ISg
                    yield RenderCommand.Ordered [sg]
                    
                yield RenderCommand.Ordered [depthTested :> ISg]
                if (not calcDepth) then 
                    yield RenderCommand.ClearDepth(1.0) 
                    yield RenderCommand.Ordered [(overlayed :> ISg)] 
            } |> RenderCommand.Ordered

        Sg.execute commands

    let viewRenderView (runtime : IRuntime) (id : string) 
                       (viewportSize : aval<V2i>) (calcDepth : bool) (m: AdaptiveModel) = 
        let frustum = AVal.map2 (fun o f -> o |> Option.defaultValue f) 
                                m.overlayFrustum m.frustum // use overlay frustum if Some()
        let observer = Gis.GisApp.getObserverSystemAdaptive m.scene.gisApp

        let overlayed = ViewerApp.createOverlaySg m

        let depthTested = 
            ViewerApp.getDepthTested frustum m.navigation.camera.view observer id runtime m //annotations + scaleBars

        let camera = AVal.map2 (fun v f -> Camera.create v f) m.navigation.camera.view m.frustum 
        let sg = createSceneGraph m.scene.surfacesModel.sgGrouped 
                                  overlayed depthTested runtime true false calcDepth id camera m
        sg
            |> Sg.noEvents
            |> Sg.camera camera
            |> Sg.uniform "ViewportSize" viewportSize


        
     
