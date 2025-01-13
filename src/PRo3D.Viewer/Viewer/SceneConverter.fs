namespace PRo3D

open System
open System.Diagnostics

open System.Net.Http

// helper module for spawning processes
module private Process = 

    open Aardvark.Base

    let runProc filename args startDir = 
        let timer = Stopwatch.StartNew()
        let procStartInfo = 
            ProcessStartInfo(
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                FileName = filename,
                Arguments = args
            )
        match startDir with | Some d -> procStartInfo.WorkingDirectory <- d | _ -> ()

        let outputHandler (_sender:obj) (args:DataReceivedEventArgs) = 
            Log.line "[%s] %s" filename args.Data

        let errorHandler (_sender:obj) (args:DataReceivedEventArgs) = 
            if args.Data <> null then Log.error "[%s] %s" filename args.Data

        let p = new Process(StartInfo = procStartInfo)
        p.OutputDataReceived.AddHandler(DataReceivedEventHandler outputHandler)
        p.ErrorDataReceived.AddHandler(DataReceivedEventHandler errorHandler)
        let started = 
            try
                p.Start()
            with | ex ->
                ex.Data.Add("filename", filename)
                reraise()
        if not started then
            failwithf "Failed to start process %s" filename
        printfn "Started %s with pid %i" p.ProcessName p.Id
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        p.WaitForExit()
        timer.Stop()
        printfn "Finished %s after %A milliseconds" filename timer.ElapsedMilliseconds
        if p.ExitCode = 0 then Choice1Of2 ()
        else Choice2Of2 p.ExitCode




module SceneLoading = 

    open System.IO
    open PRo3D.Viewer
    open Aardvark.Base
    open Aardvark.Rendering

    [<RequireQualifiedAccess>]
    type SceneLoadResult = 
        | Error of userMessage : string * exn : Option<exn>
        | Loaded of loadedModel : Model * converted : bool * path : string 

    module Converter =
        open System.Net
        open System.IO.Compression

        let mutable ConverterPath = @".\Converter\"
        let mutable ConverterExe = "PRo3D.Converter.exe"

        let mutable DownloadPath = "http://download.vrvis.at/acquisition/pro3d/49a0d346a7ecd7f8eca596a7d895da7cb38ed8c0.zip"

        let downloadFile_ (url: string) (fileStream: FileStream) (client : HttpClient) = async {
            let! responseStream = client.GetStreamAsync(url) |> Async.AwaitTask
            do! responseStream.CopyToAsync(fileStream) |> Async.AwaitTask
        }

        let downloadConverter () = 
            use httpClient = new HttpClient()
            let temp = Path.GetTempFileName()
            Log.line "Downloading converter from: %s" DownloadPath
            use fileStream = File.Create(DownloadPath)
            downloadFile_ temp fileStream httpClient |> Async.RunSynchronously
            if Directory.Exists ConverterPath then Directory.Delete(ConverterPath, true)
            Directory.CreateDirectory ConverterPath |> ignore
            Log.line "unpacking to: %s" ConverterPath
            ZipFile.ExtractToDirectory(temp, ConverterPath)
            let path = (Path.combine [ConverterPath; ConverterExe])
            if File.Exists path then
                Log.line "downloaded converter: %s" path
            else failwith "downloaded and extracted converter but %s was not found (wrong converter deployment?)"

    // convertes *.scn file to *.pro3d file using the converter tool
    let convertSceneTo (scnFile : string) = 
        let converter = Path.combine [Converter.ConverterPath; Converter.ConverterExe]
        if File.Exists converter then ()
        else 
            let err = sprintf "converter on path: %s not found, trying to download..." converter
            Log.line "%s" err
            Converter.downloadConverter ()
        Log.line "using converter: %s" converter
        Process.runProc converter scnFile None
    
    let private changeExtension (ext:string) (scenePath:string) = 
        Path.ChangeExtension(scenePath, ext)

    type ScenePaths =
        {
            scene               : string
            sceneCore           : string
            annotationsCore     : string
            annotationGroups    : string
            annotationVersioned : string
            annotationDepr      : string
        }
    module ScenePaths =
        let create(scenepath : string) =
            {
                scene               = scenepath
                sceneCore           = scenepath |> changeExtension ".pro3d"
                annotationsCore     = scenepath |> changeExtension ".pro3d.ann"
                annotationGroups    = scenepath |> changeExtension ".ann"
                annotationVersioned = scenepath |> changeExtension ".ann.json"
                annotationDepr      = scenepath |> changeExtension ".ann_old"
            }

    let loadSceneFromJson  (m : Model) (runtime : IRuntime) (signature : IFramebufferSignature) (sceneJson : string) =
        SceneLoader.loadSceneFromJson sceneJson m runtime signature 

    // load using new projects aka .pro3d file
    let loadNewStyleSceneFromFile (m : Model) (runtime : IRuntime) (signature : IFramebufferSignature) (sceneFile : string) =
        match Path.GetExtension sceneFile with 
        | ".pro3d" ->
            try 
                SceneLoader.loadSceneFromFile sceneFile m runtime signature 
                |> Model.stashAndSaveRecent sceneFile
                |> ViewerIO.loadRoverData
                |> ViewerIO.loadAnnotations  
                |> ViewerIO.loadCorrelations
                |> ViewerIO.loadLastFootPrint
                |> SceneLoader.addScaleBarSegments
                |> Choice1Of2 
            with e -> 
                let error = sprintf "[PRo3D] SceneLoading.loadScene failed: %A" e.Message
                Log.warn "%s" error
                Log.line "Stacktrace: %A" e.StackTrace
                Choice2Of2(error, Some e)
        | ".scn" -> Choice2Of2(sprintf "tried to load old scn file %s using new loader." sceneFile, None)
        | _ -> Choice2Of2(sprintf "unknown file format extension of file %s. Should be .pro3d" sceneFile, None)

    // if scene file is an old one (*.scn) the file is upgraded automatically to *.pro3d. The resulting file path
    // is returned as well as info if conversion was necessary.
    let loadSceneFromFile (m : Model) (runtime : IRuntime) (signature : IFramebufferSignature) (sceneFile : string)  = 
        if not (File.Exists sceneFile) then
            SceneLoadResult.Error (sprintf "Scene file %s does not exist" sceneFile, None)
        else
            match Path.GetExtension sceneFile with
                | ".pro3d" -> 
                    match loadNewStyleSceneFromFile m runtime signature sceneFile with
                        | Choice1Of2 m -> SceneLoadResult.Loaded(m, false,sceneFile)
                        | Choice2Of2 (message, exn) -> SceneLoadResult.Error(message,exn)
                | ".scn" -> 
                    let convertedFile = Path.ChangeExtension(sceneFile, "pro3d")
                    if File.Exists convertedFile then
                        match loadNewStyleSceneFromFile m runtime signature convertedFile with
                            | Choice1Of2 m -> SceneLoadResult.Loaded(m, false,sceneFile)
                            | Choice2Of2 (message, exn) -> SceneLoadResult.Error(message,exn)
                    else
                        Log.warn "[PRo3d] old scene. Converting %s" sceneFile
                        match convertSceneTo sceneFile with
                            | Choice1Of2 () -> 
                                match loadNewStyleSceneFromFile m runtime signature convertedFile with
                                    | Choice1Of2 m -> SceneLoadResult.Loaded(m, true, convertedFile)
                                    | Choice2Of2 (message, exn) -> SceneLoadResult.Error(message,exn)
                            | Choice2Of2 err -> 
                               SceneLoadResult.Error(sprintf "could not convert file: %s to %s" sceneFile convertedFile,None) 
                | e -> 
                    SceneLoadResult.Error (sprintf "unknown file format: %s" e, None)