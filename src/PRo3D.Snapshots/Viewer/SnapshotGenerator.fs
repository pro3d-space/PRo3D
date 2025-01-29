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
open PRo3D.Base
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
                    if args.verbose then
                        Log.line "Loading %s" sp
                    mApp.updateSync Guid.Empty (ViewerAction.LoadScene sp |> ViewerMessage |> Seq.singleton)
                    true
                | None -> false

            let hasLoadedOpc = 
                match args.opcPaths with
                | Some opcs ->
                    mApp.updateSync Guid.Empty (ViewerAction.DiscoverAndImportOpcs opcs |> ViewerMessage |>  Seq.singleton)
                    if args.verbose then
                        Log.line "Loading %s" (opcs |> List.reduce (fun a b -> sprintf "%s %s" a b))

                    true
                | None -> false
            let hasLoadedAny = 
                match args.objPaths with
                | Some objs ->
                    // TODO @RebeccaNowak what loader should be used here?
                    for x in objs do
                        if args.verbose then
                            Log.line "Loading %s" x
                        mApp.updateSync Guid.Empty  (x |> List.singleton 
                                                       |> (curry ViewerAction.ImportObject MeshLoaderType.Wavefront)
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
            | BookmarkTransformation.Configuration config ->
                let actions = 
                    [
                        ViewerAction.SetCamera config.camera.view
                        ViewerAction.FrustumMessage (FrustumProperties.Action.SetFrustum config.frustum)
                        ViewerAction.SetFrustum config.frustum

                    ]
                actions
                |> List.map ViewerMessage
                |> List.toSeq    
            | BookmarkTransformation.Bookmark bookmark ->
                Log.line "[SnapshotGenerator] Getting bookmark actions for %s" filename
                let actions = 
                    let sceneStateAction = 
                        match bookmark.sceneState with
                        | Some state ->
                            [ViewerAction.SetSceneState state]
                        | None -> []

                    let frustumAction =
                        match bookmark.frustumParameters, bookmark.sceneState with
                        | Some fp, None -> 
                            [ViewerAction.SetFrustum fp.perspective]
                        | Some fp, Some state ->
                            let aspectRatio = (float fp.resolution.X) / (float fp.resolution.Y)
                            let frustum = 
                                FrustumUtils.calculateFrustum' 
                                    state.stateConfig.frustumModel.focal.value
                                    fp.nearplane
                                    fp.farplane
                                    aspectRatio
                            [ViewerAction.SetFrustum frustum]
                        | None, Some state ->
                            [ViewerAction.SetFrustum state.stateConfig.frustumModel.frustum]
                        | None, None ->
                            []

                    let writeMetadataAction =
                        match bookmark.metadata with
                        | Some metadata ->
                            let dir = System.IO.Path.GetDirectoryName filename
                            let filename = System.IO.Path.GetFileNameWithoutExtension filename
                            let filename = sprintf "%s.json" filename
                            let path = System.IO.Path.Combine (dir, filename)
                            [ViewerAction.WriteBookmarkMetadata (path, bookmark)]
                        | None -> []

                    sceneStateAction@
                    frustumAction@
                    writeMetadataAction@
                    [
                        ViewerAction.SetCamera bookmark.cameraView
                    ]
                actions
                |> List.map ViewerMessage
                |> List.toSeq    

    let getAnimationActions (anim : SnapshotAnimation) =       
        match anim with
        | SnapshotAnimation.CameraAnimation a ->
            seq {
                yield ViewerAction.SetRenderViewportSize a.resolution |> ViewerMessage
                yield ViewerAction.SetFrustum a.Frustum |> ViewerMessage
            }

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
            | Some (SnapshotAnimation.PanoramaCollection data) ->
                Log.warn "[SnapshotGenerator] Panoramas are not yet implemented!"
            | None -> 
                Log.error "[SNAPSHOT] Could not load data."
        | false -> 
            Log.error "[SNAPSHOT] No valid paths to surfaces found."
            ()