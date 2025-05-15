namespace PRo3D.SimulatedViews

open FSharp.Data.Adaptive
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph.``Sg RuntimeCommand Extensions``
open Aardvark.UI 
open Aardvark.Data
open System
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

type T = float32


module Rendering =
    let renderCommandsToSceneGraph (renderCommands : alist<Aardvark.SceneGraph.RenderCommand>) =
        Sg.execute (Aardvark.SceneGraph.RenderCommand.Ordered renderCommands)

    //let toT (v : int64) : T = float32 v
    let toT (v : float) : T = float32 v

    Aardvark.Init()

    let render (r : RenderParameters) (projMat : Trafo3d) = 
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
                let dist = Array.distinct mat.Data
                let av = Array.average mat.Data
                let scaleFactor = (max - min)
                Log.line "[SNAPSHOT] Min: %f, Max: %f, scale: %f" min max scaleFactor
                let inline scale x =
                    (x - min) / scaleFactor


                // test for real depth values
                //let mutable depthMat = Matrix<float>(int64 size.X, int64 size.Y)
                let pi = PixImage<'t>(V2i(int64 size.X, int64 size.Y), 1)

                let mutable depthMat = pi.GetMatrix<'t>()
                depthMat.SetByCoord(fun (l : V2l) -> 
                    let depth = mat.[l.X, l.Y] 
                    let ndcX = (2.0 * (float)l.X) / ((float)size.X) - 1.0
                    let ndcY = (2.0 * (float)l.Y) / ((float)size.Y) - 1.0
                    let ndcZ = 2.0 * (float)depth - 1.0

                    let ndcVec = V3d(ndcX, ndcY, ndcZ)
                        
                    toT (projMat.InvTransformPosProj(ndcVec).Z)
                ) |> ignore

                
                //for x in 0 .. (size.X - 1) do
                //    // normalized device coordinates
                //    let ndcX = (2.0 * (float)x) / ((float)size.X) - 1.0
                //    for y in 0.. (size.Y - 1) do
                //        let depth = mat.[x, y] 
                //        let ndcY = (2.0 * (float)y) / ((float)size.Y) - 1.0
                //        let ndcZ = 2.0 * (float)depth - 1.0

                //        let ndcVec = V3d(ndcX, ndcY, ndcZ)
                        
                //        let posViewSpace = projMat.InvTransformPosProj(ndcVec)

                //        depthMat.SetValue(posViewSpace.Z,x,y)

                let mat = mat.Map scale //no scaling"!!
                //let matD = mat.ToDoubleColor() //depth.tiff
                let matB = mat.ToByteColor () //depth.png

                //let mmax = Array.max depthMat.Data
                //let mmin = Array.min depthMat.Data
                //let mdist = Array.distinct depthMat.Data
                //let mav = Array.average depthMat.Data

                Some (PixImage<byte>(matB)), Some pi
                //Some (PixImage<byte>(matB)), Some (PixImage<float>(depthMat))
              | _,_ -> None, None
        let colorImage = r.runtime.Download(r.colorTexture) |> Some
        (colorImage, depthImageByte, deptImageFloat)

    let tryRender (r : RenderParameters) (projMat : Trafo3d) =
        let result = 
            try 
                render r projMat
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
            exrDepth : string
        }

    let mutable filecounter = 0
    let genSnapshotFilenames (filename : string) : SnapshotFilenames =
        let parts = String.split "." filename |> Array.toList
        let name = filename |> System.IO.Path.GetFileNameWithoutExtension
        let path = filename |> System.IO.Path.GetFullPath
        let test = System.IO.Path.ChangeExtension(filename, (sprintf "tiff"))
        let exr = System.IO.Path.ChangeExtension(filename, (sprintf "exr"))
       
        match String.length filename, parts with
        | 0, _ -> 
            filecounter <- filecounter + 1
            {
                baseName = sprintf "no_filename_%2i.png" filecounter
                depth = sprintf "no_filename_%2i_depth.png" filecounter
                tifDepth = sprintf "no_filename_%2i_depth.tiff" filecounter
                exrDepth = sprintf "no_filename_%2i_depth.exr" filecounter
            }
        | _, [path;ending] ->
            {
                baseName = sprintf "%s.%s" path ending
                depth = sprintf "%s_depth.%s" path ending
                tifDepth = sprintf "%s_depth.tiff" path
                exrDepth = sprintf "no_filename_%s_depth.exr" path
            }
        | _,_ -> 
            {
                baseName = filename
                depth = sprintf "%s_depth.png" filename
                tifDepth = sprintf "%s" test
                exrDepth = sprintf "%s" exr

            }

    let renderAndSave (filename : string)
                      (verbose  : bool) 
                      (p        : RenderParameters) 
                      (projMatInv  : Trafo3d) =
        match verbose with
        | true -> Report.Verbosity <- 3
        | false -> Report.Verbosity <- -1
        let (col, depthByte, depthFloat) = tryRender p projMatInv
        let names = genSnapshotFilenames filename
        Report.Verbosity <- 3
        match depthByte, String.contains "mask" filename with
        | Some depthB, false ->
            try 
                //depthB.Save(names.depth)
                depthB.TryDispose () |> ignore
                //Log.line "[SNAPSHOT] Saved %s" names.depth
            with e ->
                Log.error "[SNAPSHOT] Could not save image %s" names.baseName
                Log.error "%s" e.Message
        | _,_ -> ()

        match depthFloat, String.contains "mask" filename with
        | Some depthF, false ->
            try 
                //let test = PixImageDevil.SaveAsImageDevil(depthF, names.tifDepth, PixFileFormat.Tiff, PixSaveOptions.Default, 90 )
                //let saveParams = PixSaveParams(PixFileFormat.Exr)
                //let loader = PixImageFreeImage.Loader//PixImageDevil.Loader
                
                //loader.SaveToFile(names.exrDepth, depthF, saveParams) |> ignore
                PixImageFreeImage.Loader.SaveToFile(names.exrDepth, depthF, PixSaveParams(PixFileFormat.Exr)) |> ignore
                depthF.TryDispose () |> ignore
                Log.line "[SNAPSHOT] Saved %s" names.exrDepth


                let loadedPi = PixImage.Load(names.exrDepth, PixImageFreeImage.Loader) |> unbox<PixImage<T>>
                let loadedMat = loadedPi.GetMatrix<T>()
                let data = loadedMat.Data.Map(fun v -> Math.Abs(v))//.IntoArray()
                let max = Array.max data
                let min = Array.min data
                let max = toT 12000.0

                let scaleFactor = (max - min)
                Log.line "[SNAPSHOT] Min: %f, Max: %f, scale: %f" min max scaleFactor
                let inline scale x =
                    (x - min) / scaleFactor

                let pi = PixImage<'t>(loadedMat.Dim, 1)
                let depthMat = pi.GetMatrix<'t>()
                depthMat.SetByCoord(fun (l : V2l) -> 
                    let v = loadedMat.[l]
                    scale (Math.Abs(v))
                )

                let saveParams = PixSaveParams(PixFileFormat.Jpeg)
                let loader = PixImageDevil.Loader
                let img = pi
                loader.SaveToFile(names.depth, img, saveParams) |> ignore
                pi.TryDispose () |> ignore
                Log.line "[SNAPSHOT] %s" names.depth
            with e ->
                Log.error "[SNAPSHOT] Could not save image %s" names.exrDepth
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

 