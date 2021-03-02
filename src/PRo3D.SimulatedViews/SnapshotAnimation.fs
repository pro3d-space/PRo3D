namespace PRo3D.SimulatedViews

open Aardvark.Base
open Aardvark.UI.Animation
open System
open PRo3D.Base
open MBrace.FsPickler.Json   

open Aardvark.UI
open Chiron

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SnapshotAnimation = 

    ///////////////////// WRITE TESTDATA
    let writeTestAnimation () =
        SnapshotAnimation.TestData
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile "animationTest.json"
        SnapshotAnimation.TestData
    //////////////////////    

    let writeToDir (animation : SnapshotAnimation) (dirpath : string) =
        animation
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile (Path.combine [dirpath; "snapshots.json"])

    let writeToFile (animation : SnapshotAnimation) (filepath : string) =
        animation
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile filepath

    let readArnoldAnimation path =
        try
            let foo =
                path
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (bar : ArnoldAnimation) =
                foo
                |> Json.deserialize
            bar
        with e ->
            Log.error "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            Log.line "%s" e.StackTrace 
            raise e

    let readLegacyFile path =
        let aa = readArnoldAnimation path
        Some (aa.toSnapshotAnimation ())

    let read path = 
        try
            let foo =
                path
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (bar : SnapshotAnimation) =
                foo
                |> Json.deserialize
            Some bar
        with e ->
            Log.error "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            Log.line "%s" e.StackTrace 
            raise e

    let generate (snapshots : seq<Snapshot>) (foV : float) (renderMask : bool) =
        {
            fieldOfView = Some foV //Some 30.0
            resolution  = V2i(4096)
            nearplane   = Some 0.001
            farplane    = Some 100.0
            lightLocation = None
            snapshots   = snapshots |> Seq.toList
            renderMask  = Some renderMask
        }

    let readTestAnimation () =
        try
            let foo =
                @"./animationTest.json"
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (bar : SnapshotAnimation) =
                foo
                |> Json.deserialize
            Some bar
        with e ->
            Log.line "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            None

    let updateSnapshotCam (location : V3d) (forward : V3d) (up : V3d) = 
        CameraView.look location forward.Normalized up.Normalized

    let updateArnoldSnapshotCam (extr:ArnoldSnapshot)  = 
        CameraView.look extr.location extr.forward.Normalized extr.up.Normalized

    let updateSnapshotFrustum (frust:Frustum) (fieldOfView : double) (resolution : V2i)  =
        let resh  = float(resolution.X)
        let resv  = float(resolution.Y)

        Frustum.perspective fieldOfView frust.near frust.far (resh/resv)

    let updateArnoldSnapshotFrustum (frust:Frustum) (dataAnimation:ArnoldAnimation)  =
        updateSnapshotFrustum frust dataAnimation.fieldOfView dataAnimation.resolution
        
    let loadData (path : string) =
        try 
             let (arnoldanims : ArnoldAnimation) =
                    path 
                        |> Serialization.readFromFile
                        |> Json.parse 
                        |> Json.deserialize
             arnoldanims |> Some    
        with e ->        
            Log.error "[ViewerIO] couldn't load %A" e
            None
        
    
    let snapshot (fpPath:string) (filename:string) (width : int) (height : int) =
        let pngName =
            if filename.IsEmpty() then
                let now = DateTime.Now
                System.String.Format(
                        "{0:0000}{1:00}{2:00}_{3:00}{4:00}{5:00}{6:00}",
                        now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Millisecond)
            else filename
        try Utilities.takeScreenshotFromAllViews "http://localhost:54321" width height pngName fpPath ".jpg" with e -> printfn "error: %A" e
       
    
    let startSnapshots (scenepath : string) (filename:string) (width : int) (height : int) = 
        let fpPath = FootPrint.getFootprintsPath scenepath
        snapshot fpPath filename width height 

    let animateAndScreenshot (scenePath:string) (ex: Extrinsics) (duration : RelativeTime) (name : string) (width : int) (height : int) = 
      let fpPath = FootPrint.getFootprintsPath scenePath
      {
        (CameraAnimations.initial name) with 
          sample = fun (localTime, globalTime) (state : CameraView) -> // given the state and t since start of the animation, compute a state and the cameraview
            if localTime < duration then 
                
              let now = DateTime.Now
              let pngName = System.String.Format(
                                        "{0:0000}{1:00}{2:00}_{3:00}{4:00}{5:00}_{6}",
                                        now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Millisecond)
               
               
              //try Utilities.takeScreenshot "http://localhost:54321" width height pngName fpPath with e -> printfn "error: %A" e

              let rot      = Rot3d.RotateInto(state.Forward, ex.camLookAt) * localTime / duration
              let forward' = (Rot3d rot).Transform(state.Forward)

              let uprot     = Rot3d.RotateInto(state.Up, ex.camUp) * localTime / duration
              let up'       = (Rot3d uprot).Transform(state.Up)
              
              let vec       = ex.position - state.Location
              let velocity  = vec.Length / duration                  
              let dir       = vec.Normalized
              let location' = state.Location + dir * velocity * localTime

              let view = 
                state 
                  |> CameraView.withForward forward'
                  |> CameraView.withUp up'
                  |> CameraView.withLocation location'
      
              Some (state,view)
            else None
      }