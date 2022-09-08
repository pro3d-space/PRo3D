namespace PRo3D.SimulatedViews

open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive
open Aardvark.UI.Animation
open System
open PRo3D.Base
open PRo3D.Core.SequencedBookmarks
open MBrace.FsPickler.Json   

open Aardvark.UI
open Chiron

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Snapshot =

    let writeToFile (snapshot : SurfaceSnapshot) (path : string) =
        snapshot
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile (Path.combine [path; "snapshot.json"])

    let readSCPlacement (filepath : string) =
        try
            let json =
                filepath
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (scPlacement : ObjectPlacementParameters) =
                json |> Json.deserialize
            scPlacement
        with e ->
            Log.error "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            Log.line "%s" e.StackTrace 
            raise e

    let writeSCPlacementToFile (scPlacement : ObjectPlacementParameters) 
                               (filepath : string) =
        scPlacement
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile filepath

    let toSnapshotCamera (camView : CameraView) : SnapshotCamera =
        {
            location = camView.Location
            forward  = camView.Forward
            up       = camView.Up
        }

    let importSnapshotCamera (filepath : string) : SnapshotCamera =
        try
            let json =
                filepath
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (cam : SnapshotCamera) =
                json |> Json.deserialize
            cam
        with e ->
            Log.error "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            Log.line "%s" e.StackTrace 
            raise e

    let cleanName name =
        let name = String.replace "#" "" name
//        let name = String.replace " " "_" name
        name

    let fromTimeSteps (steps : list<AnimationTimeStep>) =
        let toSnapshot step =
            match step.content with
            | AnimationTimeStepContent.Bookmark bm ->
                {
                    filename       = step.filename
                    transformation = bm |> BookmarkTransformation.Bookmark
                } 
            | AnimationTimeStepContent.Camera cam ->
                {
                    filename       = step.filename
                    transformation = SnapshotCamera.fromCamera cam 
                                     |> BookmarkTransformation.Camera
                } 
        steps
            |> List.map toSnapshot
