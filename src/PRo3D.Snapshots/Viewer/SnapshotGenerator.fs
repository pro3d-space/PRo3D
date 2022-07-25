namespace PRo3D

open Aardvark.Service

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Rendering.Text
open System.Collections.Concurrent
open System.Runtime.Serialization
open PRo3D
open PRo3D.Core.Surface
open PRo3D.Viewer
open PRo3D.OrientationCube
open PRo3D.SimulatedViews
open Adaptify

module SnapshotGenerator =
    let loadData (args  : PRo3D.SimulatedViews.CLStartupArgs) 
                 (mApp  : MutableApp<Model, ViewerAction>) =
        match args.snapshotPath, args.snapshotType with
        | Some spath, Some stype ->   
            let hasLaodedScene = 
                match args.scenePath with
                | Some sp ->
                    mApp.updateSync Guid.Empty (ViewerAction.LoadScene sp |> Seq.singleton)
                    true
                | None -> false

            let hasLoadedOpc = 
                match args.opcPaths with
                | Some opcs ->
                    mApp.updateSync Guid.Empty (ViewerAction.ImportDiscoveredSurfaces opcs |> Seq.singleton)
                    true
                | None -> false
            let hasLoadedAny = 
                match args.objPaths with
                | Some objs ->
                    for x in objs do
                        mApp.updateSync Guid.Empty  (x |> List.singleton 
                                                       |> ViewerAction.ImportObject 
                                                       |> Seq.singleton)
                    true
                | None -> 
                    hasLoadedOpc || hasLaodedScene
            hasLoadedAny
        | None , _ -> 
            Log.warn "[CLI] No snapshot file path was specified."
            false
        | _, None -> 
            Log.warn "[CLI] The snapshot file type was not specified."
            false

    let readAnimation (startupArgs  : CLStartupArgs) = 
        match startupArgs.snapshotPath, startupArgs.snapshotType with
        | Some spath, Some stype ->   
                match stype with
                | SnapshotType.Camera -> //backwards compatibility
                    SnapshotAnimation.readLegacyFile spath
                | SnapshotType.CameraAndSurface ->
                    SnapshotAnimation.read spath
        | _ -> None

    let getSnapshotActions (this : Snapshot) recalcNearFar frustum filename =
        let actions = 
            [
                ViewerAction.SetCameraAndFrustum2 (this.view,frustum);
                //ViewerAction.SetMaskObjs this.renderMask
            ]
        //let sunAction = // originally for Mars-DL project; not in use
        //    match this.lightDirection with
        //    | Some p -> [Viewer.ConfigPropertiesMessage 
        //                  (ConfigProperties.Action.ShadingMessage 
        //                    (Shading.ShadingAction.SetLightDirectionV3d p))
        //                ]
        //    | None -> []        
        let surfAction =
            match this.surfaceUpdates with
            | Some s ->
                match s.IsEmptyOrNull () with
                | false ->
                    [ViewerAction.TransformAllSurfaces s
                    ]
                | true -> []
            | None -> []
        //let PlacementAction = // originally for Mars-DL project; not in use
        //    match this.placementParameters with
        //    | Some sc ->
        //        match sc.IsEmptyOrNull () with
        //        | false ->
        //            [ViewerAction.UpdatePlacementParameters (sc, filename)] 
        //        | true -> []
        //    | None -> []
        
        let recalcNearFarAction =
            match recalcNearFar with
            | NearFarRecalculation.Both -> [ViewerAction.RecalculateNearFarPlane]
            | NearFarRecalculation.FarPlane -> [ViewerAction.RecalculateFarPlane]
            | NearFarRecalculation.NoRecalculation -> []

        // ADD ACTIONS FOR NEW SNAPSHOT MEMBERS HERE

        actions@surfAction@recalcNearFarAction |> List.toSeq    

    let getAnimationActions (anim : SnapshotAnimation) =       
        Seq.singleton (ViewerAction.SetRenderViewportSize anim.resolution)
    //    let lightActions = // originally for Mars-DL project; not in use
    //        match anim.lightLocation with
    //        | Some loc -> 
    //            [
    //                (Viewer.ConfigPropertiesMessage 
    //                (ConfigProperties.Action.ShadingMessage 
    //                  (Shading.ShadingAction.SetLightPositionV3d loc)))
    //                (Viewer.ConfigPropertiesMessage 
    //                (ConfigProperties.Action.ShadingMessage 
    //                  (Shading.ShadingAction.SetUseLighting true)))                    
    //            ]
    //        | None -> []
    //    lightActions |> List.toSeq
      
           
    let animate   (runtime      : IRuntime) 
                  (mModel       : AdaptiveModel)
                  (mApp         : MutableApp<Model, ViewerAction>) 
                  (args         : CLStartupArgs) =

        let hasLoadedAny = loadData args mApp
        match hasLoadedAny with
        | true ->
            let data = readAnimation args
            match data with
            | Some data ->
                let foV = 
                    match data.fieldOfView with
                    | Some fov -> fov
                    | None -> SnapshotApp.defaultFoV
                let frustum,_,_,_ = SnapshotApp.calculateFrustumRecalcNearFar data
                let sg = SnapshotSg.viewRenderView runtime (System.Guid.NewGuid().ToString()) 
                                                   (AVal.constant data.resolution) mModel 
                let snapshotApp : SnapshotApp<Model, AdaptiveModel, ViewerAction> = 
                    {
                        mutableApp          = mApp
                        adaptiveModel       = mModel
                        sg                  = sg
                        snapshotAnimation   = data
                        getAnimationActions = getAnimationActions
                        getSnapshotActions  = getSnapshotActions
                        runtime             = runtime
                        renderRange         = RenderRange.fromOptions args.frameId args.frameCount
                        outputFolder        = args.outFolder
                        renderMask          = args.renderMask
                        renderDepth         = args.renderDepth
                        verbose             = args.verbose
                    }
                SnapshotApp.executeAnimation snapshotApp //mApp mModel args.renderDepth startupArgs.verbose startupArgs.outFolder runtime data
            | None -> 
                Log.error "[SNAPSHOT] Could not load data."
        | false -> 
            Log.error "[SNAPSHOT] No valid paths to surfaces found."
            ()