namespace PRo3D.SimulatedViews

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI.Animation
open System
open PRo3D.Base
open MBrace.FsPickler.Json   

open Aardvark.UI
open Chiron

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Snapshot =

    let writeToFile (snapshot : Snapshot) (path : string) =
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


    let fromViews (camViews : seq<CameraView>) (sp : option<List<ObjectPlacementParameters>>) 
                  (lightDirection : option<V3d>) =
        let foo = Seq.zip camViews [0 .. (Seq.length camViews - 1)]
        seq {
            for (v, i) in foo do
               yield {
                        filename        = sprintf "%06i" i
                        camera          = v |> toSnapshotCamera
                        sunPosition     = None
                        lightDirection  = lightDirection
                        surfaceUpdates  = None
                        placementParameters    = sp
                        renderMask      = None         
                      }
               //yield {
               //         filename        = sprintf "%06i_mask" i
               //         camera          = v |> toSnapshotCamera
               //         sunPosition     = None
               //         lightDirection  = Some lightDirection
               //         surfaceUpdates  = None
               //         shattercones    = None
               //         renderMask      = Some true     
               //      }
            }

