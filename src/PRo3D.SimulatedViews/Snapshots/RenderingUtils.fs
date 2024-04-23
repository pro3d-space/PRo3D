namespace PRo3D.SimulatedViews

open FSharp.Data.Adaptive
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph.``Sg RuntimeCommand Extensions``
open Aardvark.UI 
//open PixImageDevil //DevILSharp

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
    let renderCommandsToSceneGraph (renderCommands : alist<Aardvark.SceneGraph.RenderCommand>) =
        Sg.execute (Aardvark.SceneGraph.RenderCommand.Ordered renderCommands)

    let render (r : RenderParameters) = 
        r.clearTask.Run(r.outputDescription) |> ignore
        r.task.Run(r.outputDescription) |> ignore
        let depthImageByte, deptImageFloat = 
            match r.size, r.depthTexture with
              | Some size, Some depthTexture ->
                let mat = Matrix<float32>(int64 size.X, int64 size.Y)
                r.runtime.DownloadDepth(depthTexture,mat)
                // there might be a better way to do this
                let max = Array.max mat.Data
                let min = Array.min mat.Data
                let scaleFactor = (max - min)
                Log.line "[SNAPSHOT] Min: %f, Max: %f, scale: %f" min max scaleFactor
                let inline scale x =
                    (x - min) / scaleFactor

                let mat = mat.Map scale
                let matD = mat.ToDoubleColor()
                let matB = mat.ToByteColor ()
                //let mat = mat.Map scale
                //let mat = mat.ToByteColor ()
                Some (PixImage<byte>(matB)), Some (PixImage<float>(matD))
              | _,_ -> None, None
        let colorImage = r.runtime.Download(r.colorTexture) |> Some
        (colorImage, depthImageByte, deptImageFloat)

    let tryRender (r : RenderParameters) =
        let result = 
            try 
                render r
            with 
            | e ->
                Log.error "%s" e.Message
                None, None, None
        result

    type SnapshotFilenames =
        {
            baseName : string
            depth    : string
            tifDepth : string
        }

    let mutable filecounter = 0
    let genSnapshotFilenames (filename : string) : SnapshotFilenames =
        let parts = String.split "." filename |> Array.toList
        let name = filename |> System.IO.Path.GetFileNameWithoutExtension
        let path = filename |> System.IO.Path.GetFullPath
        let test = System.IO.Path.ChangeExtension(filename, (sprintf "tiff"))
       
        match String.length filename, parts with
        | 0, _ -> 
            filecounter <- filecounter + 1
            {
                baseName = sprintf "no_filename_%2i.png" filecounter
                depth = sprintf "no_filename_%2i_depth.png" filecounter
                tifDepth = sprintf "no_filename_%2i_depth.tiff" filecounter
            }
        | _, [path;ending] ->
            {
                baseName = sprintf "%s.%s" path ending
                depth = sprintf "%s_depth.%s" path ending
                tifDepth = sprintf "%s_depth.tiff" path
            }
        | _,_ -> 
            {
                baseName = filename
                depth = sprintf "%s_depth.png" filename
                tifDepth = sprintf "%s" test

            }

    let renderAndSave (filename : string)
                      (verbose  : bool) 
                      (p        : RenderParameters) =
        match verbose with
        | true -> Report.Verbosity <- 3
        | false -> Report.Verbosity <- -1
        let (col, depthByte, depthFloat) = tryRender p
        let names = genSnapshotFilenames filename
        Report.Verbosity <- 3
        match depthByte, String.contains "mask" filename with
        | Some depthB, false ->
            try 
                depthB.Save(names.depth)
                depthB.TryDispose () |> ignore
                Log.line "[SNAPSHOT] Saved %s" names.depth
            with e ->
                Log.error "[SNAPSHOT] Could not save image %s" names.baseName
                Log.error "%s" e.Message
        | _,_ -> ()

        match depthFloat, String.contains "mask" filename with
        | Some depthF, false ->
            try 
                //let test = PixImageDevil.SaveAsImageDevil(depthF, names.tifDepth, PixFileFormat.Tiff, PixSaveOptions.Default, 90 )
                let saveParams = PixSaveParams(PixFileFormat.Tiff)
                let loader = PixImageDevil.Loader
                loader.SaveToFile(names.tifDepth, depthF, saveParams) |> ignore
                depthF.TryDispose () |> ignore
                Log.line "[SNAPSHOT] Saved %s" names.tifDepth
            with e ->
                Log.error "[SNAPSHOT] Could not save image %s" names.tifDepth
                Log.error "%s" e.Message
        | _,_ -> ()

        try
            match col with
            | Some col ->
                col.Save(filename) 
                //let test = PixImageDevil.SaveAsImageDevil(depthFloat.Value, names.tifDepth, PixFileFormat.Tiff, PixSaveOptions.UseDevil, 90 )
                col.TryDispose () |> ignore
                Log.line "[SNAPSHOT] %s" filename
            | None -> 
                Log.error "[SNAPSHOT] Could not render image %s" filename
                //Environment.Exit(int ExitCode.REQUEST_RESTART)
        with e ->
            Log.error "[SNAPSHOT] Could not save image %s" filename
            Log.error "%s" e.Message

 