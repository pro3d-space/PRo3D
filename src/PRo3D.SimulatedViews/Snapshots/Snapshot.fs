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

           

    /// Creates snapshots based on camera views.
    let fromViews (camViews : seq<CameraView>) (sp : option<List<ObjectPlacementParameters>>) 
                  (lightDirection : option<V3d>) (nameSuffix : list<string>) 
                  (stillFrames : option<HashMap<int, FrameRepetition>>) 
                  (fpsSetting : FPSSetting)
                  =
        let zipped = Seq.zip camViews [0 .. (Seq.length camViews - 1)] 
                    |> Seq.zip nameSuffix
        let mutable addToIndex = 0
        seq {
            for (s, (v, i)) in zipped do
                if fpsSetting = FPSSetting.Full || (fpsSetting = FPSSetting.Half && (i % 2 = 0)) then
                    let name = sprintf "%06i_%s" (i + addToIndex) s
                    let name = cleanName name
                    let frame = {
                        filename        = name
                        camera          = v |> toSnapshotCamera
                        sunPosition     = None
                        lightDirection  = lightDirection
                        surfaceUpdates  = None
                        placementParameters = sp
                        renderMask      = None         
                      }
                    match stillFrames with
                    | None ->  yield frame
                    | Some stillFrames -> 
                         // create identical views with new names
                         if HashMap.containsKey i stillFrames then
                             let frameRepetition = HashMap.find i stillFrames
                             let repetitions =
                                if (fpsSetting = FPSSetting.Half) then frameRepetition.repetitions / 2
                                else frameRepetition.repetitions
                             let untilFrame = i + addToIndex + repetitions
                             for j in i+addToIndex..untilFrame do
                                 let name = sprintf "%06i_%s_stillImage" j s
                                 let name = cleanName name
                                 yield {frame with filename = name}
                             addToIndex <- addToIndex + frameRepetition.repetitions
                         else yield frame
                if (fpsSetting = FPSSetting.Half) && (i % 2 = 1) then
                    addToIndex <- addToIndex - 1

            }

