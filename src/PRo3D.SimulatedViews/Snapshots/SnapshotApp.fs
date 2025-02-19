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
    getSnapshotActions   : Snapshot -> NearFarRecalculation -> string -> seq<'msg> //snashot -> frustum -> pathname -> actions
   // lenses               : PRo3D.Core.SequencedBookmarks.BookmarkLenses<'model>
    runtime              : IRuntime
    /// used to render only a range of images in a SnapshotAnimation
    renderRange          : option<RenderRange>
    /// the folder where rendered images will be saved
    outputFolder         : string
    // render an additional image where OBJs are drawn as one-coloured blobs 
    renderMask           : bool // originally for Mars-DL project; not in use
    // render an additional image with depth information 
    renderDepth          : bool // byte for *.png, originally for Mars-DL project; not in use
    verbose              : bool
  }



[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SnapshotApp =
    let defaultFoV = 30.0
    let mutable verbose = false

    let calculateFrustumRecalcNearFar (snapshotAnimation : CameraSnapshotAnimation)  = 
        let resolution = V3i (snapshotAnimation.resolution.X, snapshotAnimation.resolution.Y, 1)
        let recalcOption, near, far =
            match snapshotAnimation.nearplane, snapshotAnimation.farplane with
            | Some n, Some f -> 
                NearFarRecalculation.NoRecalculation, n, f
            | None, Some f   -> 
                NearFarRecalculation.NoRecalculation, CameraSnapshotAnimation.defaultNearplane, f
            | Some n, None   -> 
                NearFarRecalculation.FarPlane, n, CameraSnapshotAnimation.defaultFarplane
            | None, None     -> 
                NearFarRecalculation.Both, CameraSnapshotAnimation.defaultNearplane, CameraSnapshotAnimation.defaultFarplane

        let foV = 
            match snapshotAnimation.fieldOfView with
            | Some fov -> fov
            | None -> defaultFoV
        let frustum =
          Frustum.perspective foV near far 
                              (float(resolution.X)/float(resolution.Y))
        frustum, recalcOption, near, far

    let calculateFrustum (snapshotAnimation : BookmarkSnapshotAnimation)  = 
        let resolution = V3i (snapshotAnimation.resolution.X, snapshotAnimation.resolution.Y, 1)

        let foV = 
            match snapshotAnimation.fieldOfView with
            | Some fov -> fov
            | None -> defaultFoV
        let frustum =
          Frustum.perspective foV snapshotAnimation.nearplane snapshotAnimation.farplane
                              (float(resolution.X)/float(resolution.Y))
        frustum

    let calculateFrustumP (snapshotAnimation : PanoramaSnapshotCollection)  = 
        let resolution = V3i (snapshotAnimation.resolution.X, snapshotAnimation.resolution.Y, 1)

        let foV = snapshotAnimation.fieldOfView

        let frustum =
          Frustum.perspective foV snapshotAnimation.nearplane snapshotAnimation.farplane
                              (float(resolution.X)/float(resolution.Y))
        frustum

    let private executeCameraAnimation (a : CameraSnapshotAnimation) 
                                       (app : SnapshotApp<'model,'aModel, 'msg>) =
        let frustum, recalcOption, near, far = calculateFrustumRecalcNearFar a
        let resolution = V3i (a.resolution.X, a.resolution.Y, 1)
        let projMat = (frustum |> Frustum.projTrafo)
       
        if verbose then
            Log.line "calculated near plane as %f and far plane as %f" near far

        let col   = app.runtime.CreateTexture (resolution, TextureDimension.Texture2D, TextureFormat.Rgba8, 1, 1);
        let depth = app.runtime.CreateTexture (resolution, TextureDimension.Texture2D, TextureFormat.DepthComponent32f, 1, 1); //Depth24Stencil8 test laura

        let signature = 
             app.runtime.CreateFramebufferSignature ([
                 DefaultSemantic.Colors, TextureFormat.Rgba8
                 DefaultSemantic.DepthStencil, TextureFormat.DepthComponent32f 
             ], 1)

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
                | None -> 0, a.snapshots.Length
            a.snapshots
                |> List.indexed
                |> List.filter (fun (i, x) -> i >= id && i < id + count)
                |> List.map snd            
        let snapshots =
            match app.renderMask, a.renderMask with
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
            | true -> Some a.resolution, Some depth
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
            let actions = (app.getSnapshotActions (Snapshot.Surface snapshot) recalcOption fullPathName)
            if app.verbose then Log.line "[Snapshots] Updating parameters for next frame."
            app.mutableApp.updateSync (Guid.NewGuid ()) actions 

            renderAndSave (sprintf "%s.png" fullPathName) app.verbose parameters projMat

    let private executeBookmarkAnimation (a : BookmarkSnapshotAnimation) 
                                         (app : SnapshotApp<'model,'aModel, 'msg>) =
        let sg = app.sg 
        let resolution = V3i (a.resolution.X, a.resolution.Y, 1)
        let col   = app.runtime.CreateTexture (resolution, TextureDimension.Texture2D, TextureFormat.Rgba8, 1, 8);
        let depth = app.runtime.CreateTexture (resolution, TextureDimension.Texture2D, TextureFormat.Depth24Stencil8, 1, 8);

        let signature = 
             app.runtime.CreateFramebufferSignature ([
                 DefaultSemantic.Colors, TextureFormat.Rgba8
                 DefaultSemantic.DepthStencil, TextureFormat.Depth24Stencil8
             ], 8)

        let taskclear = app.runtime.CompileClear(signature,AVal.constant C4f.Black,AVal.constant 1.0)
        let task = app.runtime.CompileRender(signature, sg)

        let fbo = 
            app.runtime.CreateFramebuffer(
                signature, 
                Map.ofList [
                    DefaultSemantic.Colors, col.GetOutputView()
                    DefaultSemantic.DepthStencil, depth.GetOutputView()
                ]
            ) |> OutputDescription.ofFramebuffer

        if app.verbose then
            Log.line "%s" (app.getAnimationActions app.snapshotAnimation
                            |> Seq.map string
                            |> Seq.reduce (fun a b -> sprintf "%s %s" a b)
                          )
        app.mutableApp.updateSync (Guid.NewGuid ()) (app.getAnimationActions app.snapshotAnimation)

        let (size, depth) = 
            match app.renderDepth with 
            | true -> Some a.resolution, Some depth
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
        
        let maxInd = a.snapshots.Length - 1
        for i in 0..maxInd do
            let snapshot = a.snapshots.[i]
            let fullPathName = Path.combine [app.outputFolder;snapshot.filename]
            let actions = (app.getSnapshotActions (Snapshot.Bookmark snapshot) NearFarRecalculation.NoRecalculation fullPathName)
            if app.verbose then 
                Log.line "[Snapshots] BookmarkAnimation: Updating parameters for next frame."
                Log.line "%s" (actions
                                |> Seq.map string
                                |> Seq.reduce (fun a b -> sprintf "%s %s" a b))
                          
            app.mutableApp.updateSync (Guid.NewGuid ()) actions 

            renderAndSave (sprintf "%s.png" fullPathName) app.verbose parameters Trafo3d.Identity

    let executeAnimation (app : SnapshotApp<'model,'aModel, 'msg>) =
        verbose <- app.verbose
        match app.snapshotAnimation with
        | SnapshotAnimation.CameraAnimation a -> 
            executeCameraAnimation a app
        | SnapshotAnimation.BookmarkAnimation a ->
            executeBookmarkAnimation a app
        | SnapshotAnimation.PanoramaCollection a ->
            let a = CameraSnapshotAnimation.fromPanoramaCollection a
            executeCameraAnimation a app
            
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