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
                 (mApp  : MutableApp<Model, ViewerAnimationAction>) =
        match args.snapshotPath, args.snapshotType with
        | Some spath, Some stype ->   
            let hasLaodedScene = 
                match args.scenePath with
                | Some sp ->
                    mApp.updateSync Guid.Empty (ViewerAction.LoadScene sp |> ViewerMessage |> Seq.singleton)
                    true
                | None -> false

            let hasLoadedOpc = 
                match args.opcPaths with
                | Some opcs ->
                    mApp.updateSync Guid.Empty (ViewerAction.DiscoverAndImportOpcs opcs |> ViewerMessage |>  Seq.singleton)
                    true
                | None -> false
            let hasLoadedAny = 
                match args.objPaths with
                | Some objs ->
                    for x in objs do
                        mApp.updateSync Guid.Empty  (x |> List.singleton 
                                                       |> ViewerAction.ImportObject 
                                                       |> ViewerMessage
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
                    Some (SnapshotAnimation.CameraAnimation (SnapshotAnimation.readLegacyFile spath))
                | SnapshotType.CameraAndSurface ->
                    SnapshotAnimation.read spath
                | SnapshotType.Bookmark ->
                    SnapshotAnimation.read spath
                
        | _ -> None

    let getSnapshotActions (snapshot : Snapshot) recalcNearFar filename =
        match snapshot with
        | Snapshot.Surface snapshot ->
            let actions = 
                [
                    ViewerAction.SetCamera snapshot.view
                    //ViewerAction.SetCameraAndFrustum2 (snapshot.view,frustum) ;
                ]
            let surfAction =
                match snapshot.surfaceUpdates with
                | Some s ->
                    match s.IsEmptyOrNull () with
                    | false ->
                        [ViewerAction.TransformAllSurfaces s
                        ]
                    | true -> []
                | None -> []
        
            let recalcNearFarAction =
                match recalcNearFar with
                | NearFarRecalculation.Both -> [ViewerAction.RecalculateNearFarPlane]
                | NearFarRecalculation.FarPlane -> [ViewerAction.RecalculateFarPlane]
                | NearFarRecalculation.NoRecalculation -> []

            // ADD ACTIONS FOR NEW SNAPSHOT MEMBERS HERE

            actions@surfAction@recalcNearFarAction 
            |> List.map ViewerMessage
            |> List.toSeq    
        | Snapshot.Bookmark snapshot ->
            match snapshot.transformation with
            | BookmarkTransformation.Camera camera ->
                let actions = 
                    [
                        ViewerAction.SetCamera camera.view
                    ]
                actions
                |> List.map ViewerMessage
                |> List.toSeq    
            | BookmarkTransformation.Bookmark bookmark ->
                let actions = 
                    match bookmark.sceneState with
                    | Some state ->
                        [
                            ViewerAction.SetSceneState state
                            ViewerAction.SetCamera bookmark.cameraView
                        ]
                    | None -> []
                actions
                |> List.map ViewerMessage
                |> List.toSeq    

    let getAnimationActions (anim : SnapshotAnimation) =       
        match anim with
        | SnapshotAnimation.CameraAnimation a ->
            Seq.singleton (ViewerAction.SetRenderViewportSize a.resolution |> ViewerMessage)
        | SnapshotAnimation.BookmarkAnimation a ->
            [
             (ViewerAction.SetFrustum (SnapshotApp.calculateFrustum a)) |> ViewerMessage
             (ViewerAction.SetRenderViewportSize a.resolution |> ViewerMessage) 
            ]
           
    let animate   (runtime      : IRuntime) 
                  (mModel       : AdaptiveModel)
                  (mApp         : MutableApp<Model, ViewerAnimationAction>) 
                  (args         : CLStartupArgs) =

        let hasLoadedAny = loadData args mApp
        match hasLoadedAny with
        | true ->
            let animation = readAnimation args
            match animation with
            | Some (SnapshotAnimation.CameraAnimation data) ->
                let foV = 
                    match data.fieldOfView with
                    | Some fov -> fov
                    | None -> SnapshotApp.defaultFoV
                let frustum,_,_,_ = SnapshotApp.calculateFrustumRecalcNearFar data
                let sg = SnapshotSg.viewRenderView runtime (System.Guid.NewGuid().ToString()) 
                                                   (AVal.constant data.resolution) mModel 
                let snapshotApp  = 
                    {
                        mutableApp          = mApp
                        adaptiveModel       = mModel
                        sg                  = sg
                        snapshotAnimation   = SnapshotAnimation.CameraAnimation data
                        getAnimationActions = getAnimationActions
                        getSnapshotActions  = getSnapshotActions
                        runtime             = runtime
                        renderRange         = RenderRange.fromOptions args.frameId args.frameCount
                        outputFolder        = args.outFolder
                        renderMask          = args.renderMask
                        renderDepth         = args.renderDepth
                        verbose             = args.verbose
                    }
                SnapshotApp.executeAnimation snapshotApp
            | Some (SnapshotAnimation.BookmarkAnimation data) ->
                let sg = SnapshotSg.viewRenderView runtime (System.Guid.NewGuid().ToString()) 
                                                   (AVal.constant data.resolution) mModel 
                let snapshotApp  = 
                    {
                        mutableApp          = mApp
                        adaptiveModel       = mModel
                        sg                  = sg
                        snapshotAnimation   = SnapshotAnimation.BookmarkAnimation data
                        getAnimationActions = getAnimationActions
                        getSnapshotActions  = getSnapshotActions
                        runtime             = runtime
                        renderRange         = RenderRange.fromOptions args.frameId args.frameCount
                        outputFolder        = args.outFolder
                        renderMask          = args.renderMask
                        renderDepth         = args.renderDepth
                        verbose             = args.verbose
                    }
                SnapshotApp.executeAnimation snapshotApp
                
            | None -> 
                Log.error "[SNAPSHOT] Could not load data."

        | false -> 
            Log.error "[SNAPSHOT] No valid paths to surfaces found."
            ()