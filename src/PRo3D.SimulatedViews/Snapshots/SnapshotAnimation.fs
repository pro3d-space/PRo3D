namespace PRo3D.SimulatedViews

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI.Animation
open System
open PRo3D.Base
open Aardvark.Rendering
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
        Log.line "[Snapshots] Writing JSON %s" filepath
        animation
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile filepath

    let readLegacyAnimation path =
        try
            let foo =
                path
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (bar : LegacyAnimation) =
                foo
                |> Json.deserialize
            bar
        with e ->
            Log.error "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            Log.line "%s" e.StackTrace 
            raise e

    let readLegacyFile path =
        let animation = readLegacyAnimation path
        Some (animation.toSnapshotAnimation ())

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

    let generate (snapshots : seq<Snapshot>) (foV : option<float>) 
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

    let updateArnoldSnapshotCam (extr:LegacySnapshot)  = 
        CameraView.look extr.location extr.forward.Normalized extr.up.Normalized

    let updateSnapshotFrustum (frust:Frustum) (fieldOfView : double) (resolution : V2i)  =
        let resh  = float(resolution.X)
        let resv  = float(resolution.Y)

        Frustum.perspective fieldOfView frust.near frust.far (resh/resv)

    let updateArnoldSnapshotFrustum (frust:Frustum) (dataAnimation:LegacyAnimation)  =
        updateSnapshotFrustum frust dataAnimation.fieldOfView dataAnimation.resolution
        
    let loadData (path : string) =
        try 
             let (arnoldanims : LegacyAnimation) =
                    path 
                        |> Serialization.readFromFile
                        |> Json.parse 
                        |> Json.deserialize
             arnoldanims |> Some    
        with e ->        
            Log.error "[ViewerIO] couldn't load %A" e
            None
       