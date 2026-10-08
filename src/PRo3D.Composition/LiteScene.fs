namespace PRo3D.Composition

open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Core

open Chiron

/// The core of a `.pro3d` scene - camera, surfaces, view config and reference system - read
/// and written with the same codecs and keys the full scene uses. A host that only knows this
/// core (PRo3D.Lite) keeps every other key of the file it opened (`extras`) and writes them back
/// untouched, so opening and saving a full scene loses nothing, and full PRo3D opens a scene the
/// lite host created from scratch (see `Scene.read3`).
type SceneCore =
    {
        cameraView      : CameraView
        navigationMode  : NavigationMode
        exploreCenter   : V3d
        surfaceModel    : SurfaceModel
        config          : ViewConfigModel
        referenceSystem : ReferenceSystem
    }

module LiteScene =

    /// The scene version written for a scene created from scratch (`Scene.current`).
    let currentVersion = 3

    let private readCore : Json<SceneCore> =
        json {
            let! cameraView      = Json.readWith Ext.fromJson<CameraView,Ext> "cameraView"
            let! navigationMode  = Json.read "navigationMode"
            let! exploreCenter   = Json.read "exploreCenter"
            let! surfaceModel    = Json.read "surfaceModel"
            let! config          = Json.read "config"
            let! referenceSystem = Json.read "referenceSystem"
            return {
                cameraView      = cameraView
                navigationMode  = navigationMode |> enum<NavigationMode>
                exploreCenter   = exploreCenter |> V3d.Parse
                surfaceModel    = surfaceModel
                config          = config
                referenceSystem = referenceSystem
            }
        }

    let private writeCore (scenePath : Option<string>) (s : SceneCore) : Json<unit> =
        json {
            do! Json.writeWith Ext.toJson<CameraView,Ext> "cameraView" s.cameraView
            do! Json.write "navigationMode" (s.navigationMode |> int)
            do! Json.write "exploreCenter"  (s.exploreCenter.ToString())
            do! Json.write "surfaceModel"    s.surfaceModel
            do! Json.write "config"          s.config
            do! Json.write "scenePath"       scenePath
            do! Json.write "referenceSystem" s.referenceSystem
        }

    /// Parses a scene document. On success also returns the whole document, to be handed back
    /// to `toJson` as the extras when saving.
    let ofJson (text : string) : Result<SceneCore * Json, string> =
        try
            let doc = Json.parse text
            match doc with
            | Json.Object _ ->
                match readCore doc with
                | JsonResult.Value core, _ -> Ok (core, doc)
                | JsonResult.Error e, _ -> Result.Error e
            | _ -> Result.Error "a scene file must hold a JSON object"
        with e ->
            Result.Error e.Message

    /// The scene document: the extras (if any) with the core keys replaced. A scene created
    /// from scratch gets the current version; an opened one keeps its own.
    let toJson (extras : Option<Json>) (scenePath : Option<string>) (core : SceneCore) : Json =
        let baseMap =
            match extras with
            | Some (Json.Object m) -> m
            | _ -> Map.ofList [ "version", Json.Number (decimal currentVersion) ]

        match writeCore scenePath core (Json.Object Map.empty) with
        | JsonResult.Value (), Json.Object coreMap ->
            coreMap |> Map.fold (fun acc k v -> Map.add k v acc) baseMap |> Json.Object
        | _ ->
            // writing records into an object always yields an object
            Json.Object baseMap

    let tryLoad (path : string) : Result<SceneCore * Json, string> =
        try
            File.ReadAllText path |> ofJson
        with e ->
            Result.Error e.Message

    let save (path : string) (extras : Option<Json>) (core : SceneCore) : Result<unit, string> =
        try
            toJson extras (Some path) core
            |> Json.formatWith JsonFormattingOptions.Pretty
            |> fun text -> File.WriteAllText(path, text)
            Ok ()
        with e ->
            Log.error "[LiteScene] couldn't save %s: %s" path e.Message
            Result.Error e.Message
