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
                         (m:AdaptiveModel)  =
        let usehighlighting = ~~true //m.scene.config.useSurfaceHighlighting
        let filterTexture = ~~true

        //avoids kdtree intersections for certain interactions
        let surfacePicking = 
            m.interaction 
            |> AVal.map(fun x -> 
                match x with
                | Interactions.PickAnnotation | Interactions.PickLog -> false
                | _ -> true
            )
        let vpVisible = isViewPlanVisible m
        let selected = m.scene.surfacesModel.surfaces.singleSelectLeaf
        let refSystem = m.scene.referenceSystem
        let view = m.navigation.camera.view
        let grouped = 
            sgGrouped |> AList.map(
                fun x -> ( x 
                    |> AMap.map(fun _ surface ->                         
                        viewSingleSurfaceSg 
                            surface 
                            m.scene.surfacesModel.surfaces.flat
                            m.frustum 
                            selected 
                            surfacePicking
                            surface.globalBB
                            refSystem 
                            m.footPrint 
                            vpVisible
                            usehighlighting filterTexture
                            allowFootprint
                            false
                            view
                       )
                    |> AMap.toASet 
                    |> ASet.map snd                     
                )                
            )

        //grouped   
        let last = grouped |> AList.tryLast

        let sgs = 
            // bundles of sgs
            alist {                    
                for set in grouped do       
                    yield alist {
                        let sg = 
                            set 
                            |> Sg.set
                            |> Sg.effect [surfaceEffect]
                            |> Sg.uniform "LoDColor" (AVal.constant C4b.Gray)
                            |> Sg.uniform "LodVisEnabled" m.scene.config.lodColoring //()

                        yield sg :> ISg 
                        let depthTested = 
                            last 
                            |> AVal.map (function 
                                | Some e when System.Object.ReferenceEquals(e,set) -> 
                                    depthTested 
                                | _ -> 
                                    Sg.empty
                            )
                        
                        yield (depthTested |> Sg.dynamic) :> ISg
                    }
            }

        let commands = 
            alist {
                for sgBundle in sgs do
                    yield RenderCommand.ClearDepth(1.0) 
                    yield RenderCommand.Ordered sgBundle
                    
                yield RenderCommand.ClearDepth(1.0) 
                yield RenderCommand.Unordered [(overlayed :> ISg)] 
            } |> RenderCommand.Ordered

        Sg.execute commands



    // duplicate code, see Viewer.fs
    // could be resolved by dividing up code in Viewer.fs
    // duplicated to minimise merge troubles, but should be changed once merge is done // TODO RNO
    let viewRenderView (runtime : IRuntime) (id : string) 
                       (viewportSize : aval<V2i>) (m: AdaptiveModel) = 
        //PRo3D.Core.Drawing.DrawingApp.usePackedAnnotationRendering <- false  // not the problem
        let frustum = AVal.map2 (fun o f -> o |> Option.defaultValue f) 
                                m.overlayFrustum m.frustum // use overlay frustum if Some()
        //let cam     = AVal.map2 Camera.create m.navigation.camera.view frustum

        let annotations, discs = 
            DrawingApp.view 
                m.scene.config 
                mdrawingConfig 
                m.navigation.camera.view 
                frustum
                runtime
                (m.viewPortSizes |> AMap.tryFind id |> AVal.map (Option.defaultValue V2i.II))
                (allowAnnotationPicking m)                 
                m.drawing
            
        let annotationSg = 
            let ds =
                discs
                |> Sg.map DrawingMessage
                |> Sg.fillMode (AVal.constant FillMode.Fill)
                |> Sg.cullMode (AVal.constant CullMode.None)

            let annos = 
                annotations
                |> Sg.map DrawingMessage
                |> Sg.fillMode (AVal.constant FillMode.Fill)
                |> Sg.cullMode (AVal.constant CullMode.None)

            Sg.ofList[ds;annos;]

        let overlayed =
            //let near = m.scene.config.nearPlane.value

            let refSystem =
                Sg.view
                    m.scene.config
                    mrefConfig
                    m.scene.referenceSystem
                    m.navigation.camera.view
                |> Sg.map ReferenceSystemMessage  

            let exploreCenter =
                Navigation.Sg.view m.navigation          
          
            let homePosition =
                Sg.viewHomePosition m.scene.surfacesModel
                                 
            let viewPlans =
                ViewPlanApp.Sg.view 
                    m.scene.config 
                    mrefConfig 
                    m.scene.viewPlans 
                    m.navigation.camera.view
                |> Sg.map ViewPlanMessage           

            //let solText = 
            //    MinervaApp.getSolBillboards m.minervaModel m.navigation.camera.view near |> Sg.map MinervaActions
                
            let traverse = 
                [ 
                    TraverseApp.Sg.viewLines m.scene.traverses
                    TraverseApp.Sg.viewText 
                        m.navigation.camera.view
                        m.scene.config.nearPlane.value 
                        m.scene.traverses
                ]
                |> Sg.ofList
                |> Sg.map TraverseMessage
           
            let heightValidation =
                HeightValidatorApp.view m.heighValidation |> Sg.map HeightValidation            
            
            //let orientationCube = PRo3D.OrientationCube.Sg.view m.navigation.camera.view m.scene.config m.scene.referenceSystem

            let annotationTexts =
                DrawingApp.viewTextLabels 
                    m.scene.config
                    mdrawingConfig
                    m.navigation.camera.view
                    m.drawing            

            let scaleBarTexts = 
                ScaleBarsApp.Sg.viewTextLabels 
                    m.scene.scaleBars 
                    m.navigation.camera.view 
                    m.scene.config
                    mrefConfig
                    m.scene.referenceSystem.planet

            [
                exploreCenter; 
                refSystem; 
                viewPlans; 
                homePosition; 
                //solText; 
                annotationTexts |> Sg.noEvents
                heightValidation
                scaleBarTexts
                traverse
            ] |> Sg.ofList // (correlationLogs |> Sg.map CorrelationPanelMessage); (finishedLogs |> Sg.map CorrelationPanelMessage)] |> Sg.ofList // (*;orientationCube*) //solText

        let heightValidationDiscs =
            HeightValidatorApp.viewDiscs m.heighValidation |> Sg.map HeightValidation

        let scaleBars =
            ScaleBarsApp.Sg.view
                m.scene.scaleBars
                m.navigation.camera.view
                m.scene.config
                mrefConfig
            |> Sg.map ScaleBarsMessage

        let sceneObjects =
            SceneObjectsApp.Sg.view m.scene.sceneObjectsModel m.scene.referenceSystem |> Sg.map SceneObjectsMessage

        let geologicSurfacesSg = 
            GeologicSurfacesApp.Sg.view m.scene.geologicSurfacesModel 
            |> Sg.map GeologicSurfacesMessage 


        let traverses =
            TraverseApp.Sg.view                     
                m.scene.referenceSystem
                m.scene.traverses   
            |> Sg.map TraverseMessage

        let depthTested = 
            [
                //linkingSg; 
                annotationSg; 
                //minervaSg;
                heightValidationDiscs; 
                scaleBars; 
                sceneObjects; 
                geologicSurfacesSg 
                traverses
            ] |> Sg.ofList

        let camera = AVal.map2 (fun v f -> Camera.create v f) m.navigation.camera.view m.frustum 
        let sg = createSceneGraph m.scene.surfacesModel.sgGrouped 
                                  overlayed depthTested runtime true m
        sg
            |> Sg.noEvents
            |> Sg.camera camera
            |> Sg.uniform "ViewportSize" viewportSize


        
        
        //FreeFlyController.controlledControl // WORKS! NEED TO ADD ORBIT CONTROLLER
        //            m.navigation.camera (fun msg ->
        //                                    msg |> Navigation.FreeFlyAction
        //                                        |> ViewerAction.NavigationMessage)
        //            frustum 
        //            (renderControlAttributes id m) sg

        ///// END DEBUG

