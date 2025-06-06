﻿namespace PRo3D.SimulatedViews

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI.Animation
open System
open PRo3D.Base
open Aardvark.Rendering
open MBrace.FsPickler.Json   

open Aardvark.UI
open Chiron
open Aether

open PRo3D.Core
open PRo3D.Core.SequencedBookmarks

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SnapshotAnimation = 

    ///////////////////// WRITE TESTDATA
    let writeTestAnimation () =
        CameraSnapshotAnimation.TestData
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile "animationTest.json"
        CameraSnapshotAnimation.TestData
    //////////////////////    

    let existsOrCreate dirname =
        if not (System.IO.Directory.Exists dirname) then
            System.IO.Directory.CreateDirectory dirname |> ignore
            Log.line "Created directory %s" dirname

    let writeToDir (animation : CameraSnapshotAnimation) (dirpath : string) =
        existsOrCreate dirpath
        animation
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile (Path.combine [dirpath; "snapshots.json"])

    let writeToFile (animation : SnapshotAnimation) (filepath : string) =
        Log.line "[Snapshots] Writing JSON %s" filepath
        existsOrCreate (System.IO.Path.GetDirectoryName filepath)
        animation
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile filepath

    let read path = 
        try
            let json =
                path
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (animation : SnapshotAnimation) =
                json
                |> Json.deserialize
            Some animation
        with e ->
            Log.error "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            Log.line "%s" e.StackTrace 
            raise e

    let generate (snapshots : seq<SurfaceSnapshot>) (foV : option<float>) 
                 (nearplane : option<float>) (farplane : option<float>)
                 (resolution : V2i)
                 (renderMask : option<bool>) =
        {
            fieldOfView = foV //Some 30.0
            resolution  = resolution //V2i(1024)
            nearplane   = nearplane //Some 0.01
            farplane    = farplane //Some 100.0
            lightLocation = None
            snapshots   = snapshots |> Seq.toList
            renderMask  = renderMask
        }

    let lerpFrames (fromValue : float) (toValue : float) (nrOfFrames : int) =
        let delta = 
            [0..nrOfFrames - 1]
                |> List.map (fun frameIndex -> float frameIndex / float nrOfFrames)

        let focals =
            delta
            |> List.map (fun delta -> lerp fromValue toValue delta)
        focals 

    let lerpCamera (fromC : CameraView) (toC : CameraView)
                   (nrOfFrames : int) =
        let slerp (src : Rot3d) (dst : Rot3d) (t : float) =
            Rot.SlerpShortest(src, dst, t)

        let lerp (src : V3d) (dst : V3d) (t : float) =
            lerp src dst t

        let sky = fromC.Sky

        let delta = 
            [0..nrOfFrames - 1]
                |> List.map (fun frameIndex -> float frameIndex / float nrOfFrames)

        let positions =
            delta
            |> List.map (fun delta -> lerp fromC.Location toC.Location delta)

        let orientations =
            delta
            |> List.map (fun delta -> slerp fromC.Orientation toC.Orientation delta)

        List.zip positions orientations
        |> List.map (fun (l, o) -> CameraView.orient l o sky)

    let newBmStep filename sb =
        {
            filename = filename
            content  = AnimationTimeStepContent.Bookmark sb
        }

    let newCamStep filename view =
        {
            filename = filename
            content  = AnimationTimeStepContent.Camera view
        }

    let newConfigStep filename view frustum =
        {
            filename = filename
            content  = AnimationTimeStepContent.Configuration (view, frustum)

        }

    let timeStepsFromBookmarks (bm          : SequencedBookmarks) 
                               (fps         : int)=
        let lerpit ((fromBm : Guid), (toBm : Guid)) =
            let fromBm = BookmarkUtils.tryFind fromBm bm
            let toBm   = BookmarkUtils.tryFind toBm bm
            match fromBm, toBm with
            | Some fromBm, Some toBm ->
                let seconds = toBm.duration.value
                let nrOfFrames = seconds * (float fps)
                let lerpedCamera = lerpCamera fromBm.cameraView toBm.cameraView (nrOfFrames |> round |> int)
                let lerpedFrusta =
                    match fromBm.sceneState, toBm.sceneState with
                    | Some fromState, Some toState ->
                        let lerped = 
                            lerpFrames fromState.stateConfig.frustumModel.focal.value 
                                            toState.stateConfig.frustumModel.focal.value
                                            (nrOfFrames |> round |> int)
                        let lerpedFrustra = 
                            lerped 
                            |> List.map (fun focal ->
                                FrustumUtils.calculateFrustum'
                                    focal
                                    toState.stateConfig.nearPlane
                                    toState.stateConfig.farPlane
                                    (bm.resolutionX.value / bm.resolutionY.value))
                        
                        lerpedFrustra
                    | _ ->
                        []
                toBm, lerpedCamera , lerpedFrusta
            | _,_ -> 
                failwith "[Sequenced Bookmarks] A bookmark that is in order list was not found in the hashmap."

        let toSteps fps (bm : SequencedBookmarkModel, cameras : list<CameraView>, frusta : list<Frustum>) =
            let lastFrustum = List.tryLast frusta    
            let withLastFrustum bm = 
                match lastFrustum with
                | Some lastFrustum ->
                    Optic.set SequencedBookmarkModel._frustum lastFrustum bm
                | None ->
                    bm
            seq {
                match frusta with
                | [] ->
                    for c in cameras do
                        yield newCamStep bm.name c    
                | _ -> 
                    for (c, f) in List.zip cameras frusta do
                        yield newConfigStep bm.name c f
                
                yield newBmStep bm.name (withLastFrustum bm)
                if bm.delay.value > 0.0 then
                    let seconds = bm.delay.value
                    let nrOfFrames = (seconds * fps) |> round |> int
                    for nr in [0..nrOfFrames] do
                        yield newCamStep bm.name bm.cameraView

            } |> List.ofSeq

        let timeStepsNoNumbers = 
            bm.orderList 
            |> List.pairwise
            |> List.map lerpit
            |> List.map (toSteps (float fps))
            |> List.concat

        let firstBm = BookmarkUtils.tryFind bm.orderList.[0] bm
        let firstBm =
            match firstBm with
            | Some firstBm ->
                match firstBm.sceneState with
                | Some state ->
                    let frustum = 
                        FrustumUtils.calculateFrustum'
                            state.stateConfig.frustumModel.focal.value
                            state.stateConfig.nearPlane
                            state.stateConfig.farPlane
                            (bm.resolutionX.value / bm.resolutionY.value)
                    Some (Optic.set SequencedBookmarkModel._frustum frustum firstBm)
                | None ->
                    Some firstBm
            | None ->
                firstBm
        
        let timeStepsNoNumbers = 
            [newBmStep firstBm.Value.name firstBm.Value] @ timeStepsNoNumbers

        let numberedTimeSteps =
            timeStepsNoNumbers
            |> List.indexed
            |> List.map (fun (i,s) -> 
                            let filename = sprintf "%06i_%s" i s.filename
                            {s with filename = filename}
                        )
        numberedTimeSteps

    let currentFrameToAnimation (bm          : SequencedBookmarks)  
                                (cameraView  : CameraView)
                                (frustum     : Frustum)
                                (nearPlane   : float) 
                                (farPlane    : float) =
        Log.line "[Viewer] No frames recorded. Saving current frame."
        let snapshots = 
            [{
                filename        = "CurrentFrame"
                camera          = cameraView |> Snapshot.toSnapshotCamera
                sunPosition     = None
                lightDirection  = None
                surfaceUpdates  = None
                placementParameters = None
                renderMask      = None         
                }]
        let snapshotAnimation =
            generate 
                snapshots
                (frustum |> Frustum.horizontalFieldOfViewInDegrees |> Some)
                (nearPlane |> Some)
                (farPlane |> Some)
                (V2i (bm.resolutionX.value, bm.resolutionY.value))
                None    
        snapshotAnimation |> SnapshotAnimation.CameraAnimation

    let fromBookmarks (bm          : SequencedBookmarks)  
                      (cameraView  : CameraView)
                      (fieldOfView : float)
                      (nearplane   : float) 
                      (farplane    : float) =
        let defaultFrustum () =
            {
                resolution  = V2i(bm.resolutionX.value, bm.resolutionY.value)
                fieldOfView = fieldOfView
                nearplane   = nearplane
                farplane    = farplane
            }

        let frustum = //TODO RNO refactor frustum calculation and setting frustum for btach rendering
            let bookmarks = BookmarkUtils.orderedLoadedBookmarks bm
            let frustumFromState state = 
                FrustumUtils.calculateFrustum' 
                    state.stateConfig.frustumModel.focal.value
                    nearplane
                    farplane
                    (bm.resolutionX.value / bm.resolutionY.value)

            match List.tryHead bookmarks with 
            | Some first ->
                let frustumParas = first.frustumParameters
                match frustumParas, first.sceneState with
                | Some frustumParas, Some state ->
                    frustumFromState state
                | None, Some state ->
                    frustumFromState state
                | Some frustumParas, None ->
                    frustumParas.perspective     
                | None, None ->
                    (defaultFrustum ()).perspective
            | None ->  
                (defaultFrustum ()).perspective

        if bm.orderList.Length > 0 then                  
            let snapshots =
                match bm.fpsSetting with
                | FPSSetting.Full ->
                    Snapshot.fromTimeSteps (timeStepsFromBookmarks bm 60) //TODO RNO hardcoded fps
                | FPSSetting.Half ->
                    Snapshot.fromTimeSteps (timeStepsFromBookmarks bm 30) //TODO RNO hardcoded fps
                | _ -> 
                    Snapshot.fromTimeSteps (timeStepsFromBookmarks bm 60) //TODO RNO hardcoded fps

            let snapshotAnimation : BookmarkSnapshotAnimation =
                {
                    snapshots   = snapshots
                    fieldOfView = Some (frustum 
                                        |> Frustum.horizontalFieldOfViewInDegrees)
                    resolution  = V2i(bm.resolutionX.value, bm.resolutionY.value)
                    nearplane   = nearplane
                    farplane    = farplane
                } 
            snapshotAnimation |> SnapshotAnimation.BookmarkAnimation     
        else 
            currentFrameToAnimation bm cameraView frustum 
                                    nearplane farplane

    let readTestAnimation () =
        try
            let foo =
                @"./animationTest.json"
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (bar : CameraSnapshotAnimation) =
                foo
                |> Json.deserialize
            Some bar
        with e ->
            Log.line "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            None

    let updateSnapshotCam (location : V3d) (forward : V3d) (up : V3d) = 
        CameraView.look location forward.Normalized up.Normalized

    let updateSnapshotFrustum (frust:Frustum) (fieldOfView : double) (resolution : V2i)  =
        let resh  = float(resolution.X)
        let resv  = float(resolution.Y)

        Frustum.perspective fieldOfView frust.near frust.far (resh/resv)

