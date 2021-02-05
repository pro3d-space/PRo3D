namespace PRo3D.SimulatedViews

open System
open Aardvark.Base
open Aardvark.Base.Rendering
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

type SnapshotApp<'model,'aModel, 'msg> =
  {
    viewerApp            : MutableApp<'model, 'msg>
    sceneGraph           : ISg<'msg>
    snapshotAnimation    : SnapshotAnimation
    getAnimationActions  : SnapshotAnimation -> seq<'msg>
    getSnapshotActions   : Snapshot -> Frustum -> string -> seq<'msg> //snashot -> frustum -> pathname -> actions
    runtime              : IRuntime
    renderRange          : option<RenderRange>
    outputFolder         : string
    renderMask           : bool
    renderDepth          : bool
    verbose              : bool
  }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SnapshotApp =
    let executeAnimation (app : SnapshotApp<'model,'aModel, 'msg>) =
        
        let resolution = app.snapshotAnimation.resolution
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
        let depth = app.runtime.CreateTexture(resolution, TextureFormat.Depth24Stencil8, 1, 1);
        let col = app.runtime.CreateTexture(resolution, TextureFormat.Rgba8, 1, 1);
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
        // let shadowDepthsignature = ViewerUtils.Shadows.shadowDepthsignature runtime
        app.viewerApp.updateSync (Guid.NewGuid ()) (app.getAnimationActions app.snapshotAnimation)

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
            match app.renderMask with
            | true ->
                seq {
                    for s in snapshots do
                        yield s
                        yield {s with renderMask = Some true
                                      shattercones = None
                                      filename = sprintf "%s_mask" s.filename
                                      surfaceUpdates = None
                              }
                    } |> Seq.toList
            | false -> snapshots
        
        let taskclear = app.runtime.CompileClear(signature,AVal.constant C4f.Black,AVal.constant 1.0)
        let task = app.runtime.CompileRender(signature, app.sceneGraph)
        let (size, depth) = 
            match app.renderDepth with 
            | true -> Some resolution, Some depth
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
            app.viewerApp.updateSync (Guid.NewGuid ()) actions
            renderAndSave (sprintf "%s.png" fullPathName) app.verbose parameters

    /// returns (batchSize, nrOfBatches)
    let calculateBatches (a : SnapshotAnimation) =
        let nrOfSnapshots = a.snapshots.Length
        match nrOfSnapshots with
        | nrs when nrs < 1 -> (-1, 0)
        | nrs when nrs >= 1  ->
            let shatterconeEntry = a.snapshots.Head.shattercones
            let nrOfScs =
                match shatterconeEntry with
                | Some sce -> sce |> List.map (fun a -> a.count)
                                  |> List.reduce (+)
                              
                | None -> 0
            let batchSize = 
                match nrs, nrOfScs with
                | (nrs, nrOfScs) when nrs <= 20 && nrOfScs < 6 -> -1
                | (nrs, nrOfScs) when nrs > 20 && nrOfScs = 1 -> 60
                | (nrs, nrOfScs) when nrs > 20 && nrOfScs = 2 -> 50
                | (nrs, nrOfScs) when nrs > 20 && nrOfScs = 3 -> 30
                | (nrs, nrOfScs) when nrs > 20 && nrOfScs <= 6 -> 20
                | (nrs, nrOfScs) when nrs > 20 && nrOfScs > 6 -> 10
                | _ -> -1
            let nrOfBatches =  ((float nrs) / (float batchSize)) |> ceil |> int
            (batchSize, nrOfBatches)
        | _ -> (-1, 1)

    let tryWriteBatchFile' (snapshotPath : string) 
                           (snapshotType : SnapshotType) 
                           (range        : option<RenderRange>) =
        match snapshotType with
        | SnapshotType.Camera -> //backwards compatibility
            None
        | SnapshotType.CameraAndSurface ->
            let anim = SnapshotAnimation.read snapshotPath
            match range, anim with
            | None, Some a -> 
                let batchSize, nrOfBatches = calculateBatches a
                match batchSize, nrOfBatches with
                | -1, _ -> None
                | _, 0 -> None
                | size, nr ->
                    let batches = 
                        [0..nrOfBatches - 1] 
                            |> List.map (fun i -> sprintf "--frameId %i --frameCount %i --exitOnFinish" (i * size) (size))
                            |> List.reduce (fun a b -> sprintf "%s \n %s" a b)
                    System.IO.File.WriteAllText("cl.tmp", batches)
                    Some "cl.tmp"
            | Some r, Some a -> None 
            | _, _ -> None
        | _ -> None


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
        let nrSurfaces = HashSet.count surfaceGuids
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

        let transformSurf m surf =
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
                            {surf with preTransform = trafo}
                        | None -> surf
                    let surf =
                        match upd.translation with
                        | Some tr -> 
                          let translation = {surf.transformation.translation with value = tr}
                          {surf with transformation = {surf.transformation with translation = translation}}
                        | None -> surf
                    surf
                | None -> surf

            SurfaceModel.updateSingleSurface updatedSurf surfacesModel

        // apply surface tranformation to each surface mentioned in the snapshot
        ShatterconeUtils.applyToModel surfaces surfacesModel transformSurf       

    let menuItems placeAction generateJsonAction = //TODO rno importBookmarkAction =
      div [ clazz "ui dropdown item"] [
        text "Snapshots"
        i [clazz "dropdown icon"][] 
        div [ clazz "menu"] [
          div [ clazz "ui item"; onMouseClick (fun _ -> placeAction)][
              text "Place Shattercones"
          ]
          div [ clazz "ui item";onMouseClick (fun _ -> generateJsonAction)][
                text "Generate Shattercone Placement"
              ]
          ]
          //div [ clazz "ui item";
          //  UI.Dialogs.onChooseFiles  importBookmarkAction;
          //  clientEvent "onclick" ("parent.aardvark.processEvent('__ID__', 'onchoose', parent.aardvark.dialog.showOpenDialog({properties: ['openFile']}));") ][
          //  text "Import Bookmark"
          //]
      ]
      