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
                  (lightDirection : option<V3d>) (nameSuffix : list<string>) =
        let zipped = Seq.zip camViews [0 .. (Seq.length camViews - 1)] 
                    |> Seq.zip nameSuffix

        seq {
            for (s, (v, i)) in zipped do
               let name = sprintf "%06i_%s" i s
               let name = String.replace "#" "" name

               yield {
                        filename        = name
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

