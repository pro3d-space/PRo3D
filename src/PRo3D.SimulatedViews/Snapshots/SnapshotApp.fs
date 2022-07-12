namespace PRo3D.SimulatedViews

open System
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Rendering.Text
open System.Collections.Concurrent
open System.Runtime.Serialization
open PRo3D
open Adaptify
open FSharp.Data.Adaptive
open PRo3D.Core.Surface
open PRo3D.Core
open PRo3D.SimulatedViews.Rendering
open Aardvark.UI



type NearFarRecalculation =
  | Both
  | FarPlane
  | NoRecalculation

[<ModelType>]
type SnapshotApp<'model,'aModel, 'msg> =
  {
    /// the app that is used to create the scenegraph that should be rendered; snapshot updates will be applied to this app
    mutableApp           : MutableApp<'model, 'msg>
    /// the adaptive model associated with the mutable app
    adaptiveModel        : 'aModel
    // the sg (including camera) to be rendered 
    sg                   : ISg
    snapshotAnimation    : SnapshotAnimation
    /// animation actions are applied before rendering images
    getAnimationActions  : SnapshotAnimation -> seq<'msg>
    /// snapshot actions are applied before rendering each corresponding image
    getSnapshotActions   : Snapshot -> NearFarRecalculation -> Frustum -> string -> seq<'msg> //snashot -> frustum -> pathname -> actions
    runtime              : IRuntime
    /// used to render only a range of images in a SnapshotAnimation
    renderRange          : option<RenderRange>
    /// the folder where rendered images will be saved
    outputFolder         : string
    // render an additional image where OBJs are drawn as one-coloured blobs 
    renderMask           : bool // originally for Mars-DL project; not in use
    // render an additional image with depth information 
    renderDepth          : bool // originally for Mars-DL project; not in use
    verbose              : bool
  }



[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SnapshotApp =
    let defaultFoV = 30.0
    let mutable verbose = false

    let calculateFrustumRecalcNearFar (snapshotAnimation : SnapshotAnimation)  = 
        let resolution = V3i (snapshotAnimation.resolution.X, snapshotAnimation.resolution.Y, 1)
        let recalcOption, near, far =
            match snapshotAnimation.nearplane, snapshotAnimation.farplane with
            | Some n, Some f -> NearFarRecalculation.NoRecalculation, n, f
            | None, Some f   -> NearFarRecalculation.NoRecalculation, SnapshotAnimation.defaultNearplane, f
            | Some n, None   -> NearFarRecalculation.FarPlane, n, SnapshotAnimation.defaultFarplane
            | None, None     -> NearFarRecalculation.Both, SnapshotAnimation.defaultNearplane, SnapshotAnimation.defaultFarplane

        let foV = 
            match snapshotAnimation.fieldOfView with
            | Some fov -> fov
            | None -> defaultFoV
        let frustum =
          Frustum.perspective foV near far 
                              (float(resolution.X)/float(resolution.Y))
        frustum, recalcOption, near, far

    let executeAnimation (app : SnapshotApp<'model,'aModel, 'msg>) =
        verbose <- app.verbose
        let frustum, recalcOption, near, far = calculateFrustumRecalcNearFar app.snapshotAnimation
        let resolution = V3i (app.snapshotAnimation.resolution.X, app.snapshotAnimation.resolution.Y, 1)
        if verbose then
            Log.line "calculated near plane as %f and far plane as %f" near far

        let col   = app.runtime.CreateTexture (resolution, TextureDimension.Texture2D, TextureFormat.Rgba8, 1, 8);
        let depth = app.runtime.CreateTexture (resolution, TextureDimension.Texture2D, TextureFormat.Depth24Stencil8, 1, 8);

        let signature = 
             app.runtime.CreateFramebufferSignature ([
                 DefaultSemantic.Colors, TextureFormat.Rgba8
                 DefaultSemantic.DepthStencil, TextureFormat.Depth24Stencil8
             ], 8)

        let fbo = 
            app.runtime.CreateFramebuffer(
                signature, 
                Map.ofList [
                    DefaultSemantic.Colors, col.GetOutputView()
                    DefaultSemantic.DepthStencil, depth.GetOutputView()
                ]
            ) |> OutputDescription.ofFramebuffer
        app.mutableApp.updateSync (Guid.NewGuid ()) (app.getAnimationActions app.snapshotAnimation)

        let snapshots =
            let id, count =
                match app.renderRange with
                | Some r ->
                  r.fromFrame, r.frameCount
                | None -> 0, app.snapshotAnimation.snapshots.Length
            app.snapshotAnimation.snapshots
                |> List.indexed
                |> List.filter (fun (i, x) -> i >= id && i < id + count)
                |> List.map snd            
        let snapshots =
            match app.renderMask, app.snapshotAnimation.renderMask with
            | true, _ | _, Some true ->
                seq {
                    for s in snapshots do
                        yield s
                        yield {s with renderMask = Some true
                                      placementParameters = None
                                      filename = sprintf "%s_mask" s.filename
                                      surfaceUpdates = None
                              }
                    } |> Seq.toList
            | false, _ -> snapshots

        let sg = app.sg //app.sceneGraph app.runtime app.adaptiveModel

        let taskclear = app.runtime.CompileClear(signature,AVal.constant C4f.Black,AVal.constant 1.0)
        let task = app.runtime.CompileRender(signature, sg)
        
        let (size, depth) = 
            match app.renderDepth with 
            | true -> Some app.snapshotAnimation.resolution, Some depth
            | false -> None, None

        let parameters : RenderParameters =
            {
                runtime             = app.runtime
                size                = size
                outputDescription   = fbo
                colorTexture        = col
                depthTexture        = depth
                task                = task
                clearTask           = taskclear
            }
        
        let maxInd = snapshots.Length - 1
        for i in 0..maxInd do
            let snapshot = snapshots.[i]
            let fullPathName = Path.combine [app.outputFolder;snapshot.filename]
            let actions = (app.getSnapshotActions snapshot recalcOption frustum fullPathName)
            if app.verbose then Log.line "[Snapshots] Updating parameters for next frame."
            app.mutableApp.updateSync (Guid.NewGuid ()) actions 

            renderAndSave (sprintf "%s.png" fullPathName) app.verbose parameters

    let readAnimation (snapshotPath : string)
                      (snapshotType : SnapshotType) =   
        match snapshotType with
        | SnapshotType.Camera -> //backwards compatibility
            SnapshotAnimation.readLegacyFile snapshotPath
        | SnapshotType.CameraAndSurface ->
            SnapshotAnimation.read snapshotPath

    let transformAllSurfaces (surfacesModel : SurfaceModel) (surfaceUpdates : list<SnapshotSurfaceUpdate>) =
        let surfaces = surfacesModel.surfaces.flat 
                                |> HashMap.toList
                                |> List.map(fun (_,v) -> v |> Leaf.toSurface)

        let hasName (surf : Surface) (name : string) =
            let surfName = 
                let n = surf.opcPaths.FirstOrDefault ""
                match n.Length with
                | len when len > 0 -> n
                | _ -> surf.importPath
            String.contains (String.toLowerInvariant name) (String.toLowerInvariant surfName)

        let transformSurf surfacesModel surf =
            let surfaceUpdate  = 
                surfaceUpdates
                    |> List.filter (fun s -> hasName surf s.surfname)
                    |> List.tryHead

            let updatedSurf =
                match surfaceUpdate with
                | Some upd ->
                    let surf =
                        match upd.visible with
                        | Some v -> {surf with isVisible    = v}
                        | None -> surf
                    let surf =
                        match upd.trafo with
                        | Some trafo ->
                            if verbose then 
                                Log.line "Transforming surface %s with %s" surf.name (trafo.ToString ())
                            {surf with preTransform = trafo}
                        | None -> surf
                    let surf =
                        match upd.translation with
                        | Some tr -> 
                          if verbose then 
                            Log.line "Transforming surface %s with %s" surf.name (tr.ToString ())
                          let translation = {surf.transformation.translation with value = tr}
                          {surf with transformation = {surf.transformation with translation = translation}}
                        | None -> surf
                    surf
                | None -> surf

            // apply surface tranformation to each surface mentioned in the snapshot
            SurfaceModel.updateSingleSurface updatedSurf surfacesModel
        
        
        let model = 
            SnapshotUtils.applyToModel surfaces surfacesModel transformSurf       

        model

    let menuItems placeAction generateJsonAction useObjectPlacements =
        let menuContentList =
            alist  {
                let! useObjectPlacements = useObjectPlacements 
                if useObjectPlacements then
                    yield 
                        div [ clazz "ui item"; onMouseClick (fun _ -> placeAction)] [
                            text "Place Objects"
                        ]
              
                yield div [ clazz "ui item";onMouseClick (fun _ -> generateJsonAction)] [
                  text "Generate Snapshot File"
                ]
            }

        div [ clazz "ui dropdown item"] [
            text "Snapshots"
            i [clazz "dropdown icon"] [] 
            Incremental.div (AttributeMap.ofList [ clazz "menu"]) menuContentList      
        ]