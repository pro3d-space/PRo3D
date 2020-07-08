namespace PRo3D

open Aardvark.Service

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Base.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Rendering.Text
open System.Collections.Concurrent
open System.Runtime.Serialization
open PRo3D
open PRo3D.Surfaces
open PRo3D.Viewer
open PRo3D.OrientationCube

module NoGuiViewer =

    type RenderParameters =
        {
              runtime           : IRuntime
              outputDescription : OutputDescription
              colorTexture      : IBackendTexture
              task              : IRenderTask
              clearTask         : IRenderTask
              size              : option<V2i>
              depthTexture      : option<IBackendTexture>
        }

    let render (r : RenderParameters) = 
        r.clearTask.Run(null, r.outputDescription) |> ignore
        r.task.Run(null, r.outputDescription) |> ignore
        let depthImage = 
            match r.size, r.depthTexture with
              | Some size, Some depthTexture ->
                let mat = Matrix<float32>(int64 size.X, int64 size.Y)
                r.runtime.DownloadDepth(depthTexture,0,0,mat)
                // there might be a better way to do this
                let max = Array.max mat.Data
                let min = Array.min mat.Data
                let scaleFactor = (max - min)
                let inline scale x =
                    (x - min) / scaleFactor
                let mat = mat.Map scale
                let mat = mat.ToByteColor ()
                Some (PixImage<byte>(mat))
              | _,_ -> None
        let colorImage = r.runtime.Download(r.colorTexture)
        (colorImage, depthImage)
        
    type SnapshotFilenames =
        {
            baseName : string
            depth      : string
            mask       : string
        }
    
    let mutable filecounter = 0
    let genSnapshotFilenames (filename : string) : SnapshotFilenames =
        let parts = String.split '.' filename
           
        match String.length filename, parts with
        | 0, _ -> 
            filecounter <- filecounter + 1
            {
                baseName = sprintf "no_filename_%2i.png" filecounter
                depth = sprintf "no_filename_%2i_depth.png" filecounter
                mask = sprintf "no_filename_%2i_mask.png" filecounter
            }
        | _, [path;ending] ->
            {
                baseName = sprintf "%s.%s" path ending
                depth = sprintf "%s_depth.%s" path ending
                mask  = sprintf "%s_mask.%s" path ending
            }
        | _,_ -> 
            {
                baseName = filename
                depth = sprintf "%s_depth.png" filename
                mask = sprintf "%s_mask.png" filename
            }

    let renderAndSave (filename : string)
                      (verbose  : bool) 
                      (p        : RenderParameters) =
        match verbose with
        | true -> Report.Verbosity <- 3
        | false -> Report.Verbosity <- -1
        let (col, depth) = render p
        match depth with
        | Some depth ->
            let names = genSnapshotFilenames filename
            Report.Verbosity <- 3
            try 
                depth.SaveAsImage(names.depth)
                depth.TryDispose () |> ignore
                Log.line "%s" names.depth
            with e ->
                Log.error "[SNAPSHOT] Could not save image %s" names.baseName
                Log.error "%s" e.Message
        | _ -> ()
        Report.Verbosity <- 3
        try
            col.SaveAsImage(filename) 
            col.TryDispose () |> ignore
            Log.line "[SNAPSHOT] %s" filename
        with e ->
            Log.error "[SNAPSHOT] Could not save image %s" filename
            Log.error "%s" e.Message

    

    let executeAnimation (mApp        : MutableApp<Model, ViewerAction>)
                         (mModel      : MModel) 
                         (renderDepth : bool)
                         (verbose     : bool)
                         (outPath     : string)
                         (runtime     : IRuntime)
                         (animation   : SnapshotAnimation) =
        let resolution = animation.resolution
        let frustum =
            Frustum.perspective animation.fieldOfView 1.0 1000000.0 
                                (float(resolution.X)/float(resolution.Y))
        let depth = runtime.CreateTexture(resolution, TextureFormat.Depth24Stencil8, 1, 1);
        let col = runtime.CreateTexture(resolution, TextureFormat.Rgba8, 1, 1);
        let signature = 
            runtime.CreateFramebufferSignature [
                DefaultSemantic.Colors, { format = RenderbufferFormat.Rgba8; samples = 1 }
                DefaultSemantic.Depth,  { format = RenderbufferFormat.Depth24Stencil8; samples = 1 }
            ]

        let fbo = 
            runtime.CreateFramebuffer(
                signature, 
                Map.ofList [
                    DefaultSemantic.Colors, col.GetOutputView()
                    DefaultSemantic.Depth, depth.GetOutputView()
                ]
            ) |> OutputDescription.ofFramebuffer
        let positions = animation.snapshots
        //    match animation.snapshots.IsEmpty with
        //    | true -> animation.snapshots
        //    | false -> [animation.snapshots.Head]@animation.snapshots
        
        let sg = ViewerUtils.getSurfacesSgWithCamera mModel
        let taskclear = runtime.CompileClear(signature,AVal.constant C4f.Black,AVal.constant 1.0)
        let task = runtime.CompileRender(signature, sg)
        let (size, depth) = 
            match renderDepth with 
            | true -> Some resolution, Some depth
            | false -> None, None

        for i in 0 .. (positions.Length-1) do
            let snapshot = positions.[i]
            let actions = snapshot.toActions frustum
            mApp.updateSync (Guid.NewGuid ()) actions
            let fullPathName = Path.combine [outPath;snapshot.filename]
            let parameters : RenderParameters =
                {
                    runtime             = runtime
                    size                = size
                    outputDescription   = fbo
                    colorTexture        = col
                    depthTexture        = depth
                    task                = task
                    clearTask           = taskclear
                }
            renderAndSave (sprintf "%s.png" fullPathName) verbose parameters
           
    let animate   (runtime      : IRuntime) 
                  (mModel       : MModel)
                  (mApp         : MutableApp<Model, ViewerAction>) 
                  (startupArgs  : StartupArgs) =
        let args = startupArgs
        match args.snapshotPath, args.snapshotType with
        | Some spath, Some stype ->   
            let hasLoadedOpc = 
                match args.opcPaths with
                | Some opcs ->
                    mApp.updateSync Guid.Empty (ViewerAction.ImportSurface opcs |> Seq.singleton)
                    true

                | None -> false
            
            let hasLoadedAny = 
                match args.objPaths with
                | Some objs ->
                    for x in objs do
                        mApp.updateSync Guid.Empty  (x |> List.singleton 
                                                       |> ViewerAction.ImportObject 
                                                       |> Seq.singleton)
                    true
                | None -> 
                    hasLoadedOpc
            match hasLoadedAny with
            | true ->
                let data = 
                    match stype with
                    | SnapshotType.Camera -> //backwards compatibility
                        SnapshotAnimation.readLegacyFile spath
                    | SnapshotType.CameraAndSurface ->
                        SnapshotAnimation.read spath
                    | _ -> None
                match data with
                    | Some data ->
                        executeAnimation mApp mModel args.renderDepth startupArgs.verbose startupArgs.outFolder runtime data
                    | None -> 
                        Log.line "[SNAPSHOT] Could not load data."
            | false -> 
                Log.line "[SNAPSHOT] No valid paths to surfaces found."
                ()
        | _,_ -> ()   