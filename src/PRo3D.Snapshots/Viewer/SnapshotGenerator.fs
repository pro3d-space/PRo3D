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
open PRo3D.Shading

module SnapshotGenerator =
    let loadData (args  : StartupArgs) 
                 (mApp  : MutableApp<Model, ViewerAction>) =
        match args.snapshotPath, args.snapshotType with
        | Some spath, Some stype ->   
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
                    hasLoadedOpc
            hasLoadedAny
        | None , _ -> 
            Log.warn "[CLI] No snapshot file path was specified."
            false
        | _, None -> 
            Log.warn "[CLI] The snapshot file type was not specified."
            false

    let readAnimation (startupArgs  : StartupArgs) = 
        match startupArgs.snapshotPath, startupArgs.snapshotType with
        | Some spath, Some stype ->   
                match stype with
                | SnapshotType.Camera -> //backwards compatibility
                    SnapshotAnimation.readLegacyFile spath
                | SnapshotType.CameraAndSurface ->
                    SnapshotAnimation.read spath
                | _ -> None
        | _ -> None

    let getSnapshotActions (this : Snapshot) frustum filename =
        let actions = 
            [
                ViewerAction.SetCameraAndFrustum2 (this.view,frustum); //X
                //ViewerAction.SetMaskObjs this.renderMask
            ]
        let sunAction =
            match this.lightDirection with
            | Some p -> [Viewer.ConfigPropertiesMessage 
                          (ConfigProperties.Action.ShadingMessage 
                            (Shading.ShadingAction.SetLightDirectionV3d p))
                        ]
            | None -> []        
        let surfAction =
            match this.surfaceUpdates with
            | Some s ->
                match s.IsEmptyOrNull () with
                | false ->
                    [ViewerAction.TransformAllSurfaces s
                    ]
                | true -> []
            | None -> []
        let PlacementAction =
            match this.placementParameters with
            | Some sc ->
                match sc.IsEmptyOrNull () with
                | false ->
                    [ViewerAction.UpdatePlacementParameters (sc, filename)] 
                | true -> []
            | None -> []
        // ADD ACTIONS FOR NEW SNAPSHOT MEMBERS HERE
        actions@sunAction@surfAction@PlacementAction |> List.toSeq    

    let getAnimationActions (anim : SnapshotAnimation) =               
        let setNearplane =
            match anim.nearplane with
            | Some np -> 
                [(ConfigProperties.Action.SetNearPlane (Numeric.SetValue np))
                  |> ViewerAction.ConfigPropertiesMessage]
            | None -> []
        let setFarplane =
            match anim.farplane with
            | Some fp -> 
                [(ConfigProperties.Action.SetFarPlane (Numeric.SetValue fp))
                  |> ViewerAction.ConfigPropertiesMessage]
            | None -> []
        let lightActions = 
            match anim.lightLocation with
            | Some loc -> 
                [
                    (Viewer.ConfigPropertiesMessage 
                    (ConfigProperties.Action.ShadingMessage 
                      (Shading.ShadingAction.SetLightPositionV3d loc)))
                    (Viewer.ConfigPropertiesMessage 
                    (ConfigProperties.Action.ShadingMessage 
                      (Shading.ShadingAction.SetUseLighting true)))                    
                ]
            | None -> []
        setNearplane@setFarplane@lightActions |> List.toSeq
      
           
    let animate   (runtime      : IRuntime) 
                  (mModel       : AdaptiveModel)
                  (mApp         : MutableApp<Model, ViewerAction>) 
                  (args         : StartupArgs) =
        let sg = ViewerUtils.getSurfacesSgWithCamera mModel runtime
        let hasLoadedAny = loadData args mApp
        match hasLoadedAny with
        | true ->
            let data = readAnimation args
            match data with
            | Some data ->
                let snapshotApp : SnapshotApp<Model, AdaptiveModel, ViewerAction> = 
                    {
                        mutableApp = mApp
                        adaptiveModel = mModel
                        sceneGraph = ViewerUtils.getSurfacesSgWithCamera
                        snapshotAnimation = data
                        getAnimationActions = getAnimationActions
                        getSnapshotActions = getSnapshotActions
                        runtime = runtime
                        renderRange = RenderRange.fromOptions args.frameId args.frameCount
                        outputFolder = args.outFolder
                        renderMask = args.renderMask
                        renderDepth = args.renderDepth
                        verbose = args.verbose
                    }
                SnapshotApp.executeAnimation snapshotApp //mApp mModel args.renderDepth startupArgs.verbose startupArgs.outFolder runtime data
            | None -> 
                Log.line "[SNAPSHOT] Could not load data."
        | false -> 
            Log.line "[SNAPSHOT] No valid paths to surfaces found."
            ()