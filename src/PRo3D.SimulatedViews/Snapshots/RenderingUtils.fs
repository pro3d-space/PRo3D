namespace PRo3D.SimulatedViews


open Aardvark.Base

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

type RenderRange =
    {
      fromFrame : int
      frameCount : int
    }
    with static member fromOptions from count =
            match from, count with
            | Some from, Some count -> Some {fromFrame = from; frameCount = count}
            | _,_ -> None

module Rendering =
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
        let colorImage = r.runtime.Download(r.colorTexture) |> Some
        (colorImage, depthImage)

    let tryRender (r : RenderParameters) =
        let result = 
            try 
                render r
            with 
            | e ->
                Log.error "%s" e.Message
    //                Environment.Exit(int ExitCode.REQUEST_RESTART)
                None, None
        result

    type SnapshotFilenames =
        {
            baseName : string
            depth      : string
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
            }
        | _, [path;ending] ->
            {
                baseName = sprintf "%s.%s" path ending
                depth = sprintf "%s_depth.%s" path ending
            }
        | _,_ -> 
            {
                baseName = filename
                depth = sprintf "%s_depth.png" filename
            }

    let renderAndSave (filename : string)
                      (verbose  : bool) 
                      (p        : RenderParameters) =
        Aardvark.GeoSpatial.Opc.PatchLod.useAsyncLoading <- true
        match verbose with
        | true -> Report.Verbosity <- 3
        | false -> Report.Verbosity <- -1
        let (col, depth) = tryRender p
        Report.Verbosity <- 3
        match depth, String.contains "mask" filename with
        | Some depth, false ->
            let names = genSnapshotFilenames filename
            try 
                depth.SaveAsImage(names.depth) 
                depth.TryDispose () |> ignore
                Log.line "[SNAPSHOT] Saved %s" names.depth
            with e ->
                Log.error "[SNAPSHOT] Could not save image %s" names.baseName
                Log.error "%s" e.Message
        | _,_ -> ()
        try
            match col with
            | Some col ->
                col.SaveAsImage(filename) 
                col.TryDispose () |> ignore
                Log.line "[SNAPSHOT] %s" filename
            | None -> 
                Log.error "[SNAPSHOT] Could not render image %s" filename
                //Environment.Exit(int ExitCode.REQUEST_RESTART)
        with e ->
            Log.error "[SNAPSHOT] Could not save image %s" filename
            Log.error "%s" e.Message