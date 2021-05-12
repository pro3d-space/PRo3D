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

[<ModelType>]
type SnapshotApp<'model,'aModel, 'msg> =
  {
    /// the app that is used to create the scenegraph that should be rendered; snapshot updates will be applied to this app
    mutableApp           : MutableApp<'model, 'msg>
    /// the adaptive model associated with the mutable app
    adaptiveModel        : 'aModel
    sceneGraph           : 'aModel -> IRuntime -> ISg<'msg>
    snapshotAnimation    : SnapshotAnimation
    /// animation actions are applied before rendering images
    getAnimationActions  : SnapshotAnimation -> seq<'msg>
    /// snapshot actions are applied before rendering each corresponding image
    getSnapshotActions   : Snapshot -> Frustum -> string -> seq<'msg> //snashot -> frustum -> pathname -> actions
    runtime              : IRuntime
    /// used to render only a range of images in a SnapshotAnimation
    renderRange          : option<RenderRange>
    /// the folder where rendered images will be saved
    outputFolder         : string
    renderMask           : bool
    renderDepth          : bool
    verbose              : bool
  }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SnapshotApp =
    let executeAnimation (app : SnapshotApp<'model,'aModel, 'msg>) =
        
        let resolution = V3i (app.snapshotAnimation.resolution.X, app.snapshotAnimation.resolution.Y, 1)
        let near, far =
            match app.snapshotAnimation.nearplane, app.snapshotAnimation.farplane with
            | Some n, Some f -> n, f
            | None, Some f   -> SnapshotAnimation.defaultNearplane, f
            | Some n, None   -> n, SnapshotAnimation.defaultFarplane
            | None, None     ->  SnapshotAnimation.defaultNearplane, SnapshotAnimation.defaultFarplane

        let foV = 
            match app.snapshotAnimation.fieldOfView with
            | Some fov -> fov
            | None -> 30.0
        let frustum =
          Frustum.perspective foV near far 
                              (float(resolution.X)/float(resolution.Y))


        let depth = app.runtime.CreateTexture (resolution, TextureDimension.Texture2D, TextureFormat.Depth24Stencil8, 1, 1);
        let col   = app.runtime.CreateTexture (resolution, TextureDimension.Texture2D, TextureFormat.Rgba8, 1, 1);

        let signature = 
            app.runtime.CreateFramebufferSignature [
                DefaultSemantic.Colors, { format = RenderbufferFormat.Rgba8; samples = 1 }
                DefaultSemantic.Depth,  { format = RenderbufferFormat.Depth24Stencil8; samples = 1 }
            ]

        let fbo = 
            app.runtime.CreateFramebuffer(
                signature, 
                Map.ofList [
                    DefaultSemantic.Colors, col.GetOutputView()
                    DefaultSemantic.Depth, depth.GetOutputView()
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

        let sg = app.sceneGraph app.adaptiveModel app.runtime

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
            let actions = (app.getSnapshotActions snapshot frustum fullPathName)
            app.mutableApp.updateSync (Guid.NewGuid ()) actions

            //TODO rno should be obsolete with snc rendering
            //taskclear.Run (null, fbo) |> ignore
            //task.Run (null, fbo) |> ignore
            //System.Threading.Thread.Sleep(1000)
            //TODO end

            renderAndSave (sprintf "%s.png" fullPathName) app.verbose parameters

    let readAnimation (snapshotPath : string)
                      (snapshotType : SnapshotType) =   
        match snapshotType with
        | SnapshotType.Camera -> //backwards compatibility
            SnapshotAnimation.readLegacyFile snapshotPath
        | SnapshotType.CameraAndSurface ->
            SnapshotAnimation.read snapshotPath
        | _ -> None 

    let transformAllSurfaces (surfacesModel : SurfaceModel) (surfaceUpdates : list<SnapshotSurfaceUpdate>) =
        let surfaceGuids = HashMap.keys surfacesModel.surfaces.flat 
        //let nrSurfaces = HashSet.count surfaceGuids
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
                            //Log.line "Transforming surface %s with %s" surf.name (trafo.ToString ())
                            {surf with preTransform = trafo}
                        | None -> surf
                    let surf =
                        match upd.translation with
                        | Some tr -> 
                          //Log.line "Transforming surface %s with %s" surf.name (tr.ToString ())
                          let translation = {surf.transformation.translation with value = tr}
                          {surf with transformation = {surf.transformation with translation = translation}}
                        | None -> surf
                    surf
                | None -> surf

            SurfaceModel.updateSingleSurface updatedSurf surfacesModel
        // apply surface tranformation to each surface mentioned in the snapshot
        


        let model = 
            SnapshotUtils.applyToModel surfaces surfacesModel transformSurf       

        //let debug = model.surfaces.flat 
        //              |> HashMap.toList
        //              |> List.map(fun (_,v) -> v |> Leaf.toSurface)
        //              |> List.map (fun s -> sprintf "%s %s" s.name (s.preTransform.ToString ()))
        //Log.line "%s" (debug.ToString ())

        model

    let menuItems placeAction generateJsonAction useObjectPlacements = //TODO rno importBookmarkAction =
        let menuContentList =
            alist  {
                let! useObjectPlacements = useObjectPlacements 
                if useObjectPlacements then
                    yield 
                        div [ clazz "ui item"; onMouseClick (fun _ -> placeAction)][
                            text "Place Objects"
                        ]
              
                yield div [ clazz "ui item";onMouseClick (fun _ -> generateJsonAction)][
                  text "Generate Snapshot File"
                ]
            }

        div [ clazz "ui dropdown item"] [
            text "Snapshots"
            i [clazz "dropdown icon"][] 
            Incremental.div (AttributeMap.ofList [ clazz "menu"]) menuContentList      
        ]