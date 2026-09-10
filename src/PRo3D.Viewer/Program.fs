open System 

//open System.Windows.Forms
open System.Collections.Concurrent
open System.Diagnostics
open System.Threading
open System.Xml
open System.Text
open System.Runtime.Serialization
open System.Collections.Generic

open Aardvark.Base
open Aardvark.Application.Slim
open OpcViewer.Base
open Aardvark.Rendering

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open RemoteControlModel
open PRo3D.Viewer

open Aardium

open Chiron

open Aardvark.UI
open Aardvark.UI.Giraffe

open FSharp.Data.Adaptive

open System.Reflection
open System.Runtime.InteropServices

open Aardvark.GeoSpatial.Opc.Load

type EmbeddedRessource = EmbeddedRessource

[<DataContract>]
type Result =
   { 
      [<field: DataMember(Name = "result")>]
      result : string;
   }

let viewerVersion       = "5.4.0"

let catchDomainErrors   = false

open System.IO
open System.Runtime.InteropServices

let rec allFiles dirs =
    if Seq.isEmpty dirs then Seq.empty else
        seq { yield! dirs |> Seq.collect Directory.EnumerateFiles
              yield! dirs |> Seq.collect Directory.EnumerateDirectories |> allFiles }
   
[<EntryPoint;STAThread>]
let main argv = 
    // ensure appdata is here
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create) |> printfn "ApplicationData: %s"
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create) |> printfn "LocalApplicationData: %s"
    
    let appData = Path.combine [Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); "Pro3D"]
    Config.configPath <- appData

    if not (Directory.Exists appData) then Directory.CreateDirectory(appData) |> ignore

    let logFilePath = Path.Combine(appData, "PRo3D.log")
    Aardvark.Base.Report.LogFileName <- logFilePath
    Log.line "Running with AppData: %s" appData    

    // use this one to get path to self-contained exe (not temp expanded dll)
    let executeablePath = 
        if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then 
            System.Environment.GetCommandLineArgs().[0]
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then
            System.Environment.GetCommandLineArgs().[0]
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            Process.GetCurrentProcess().MainModule.FileName
        else 
            Log.warn "could not detect os platform.. assuming linux"
            System.Environment.GetCommandLineArgs().[0]
    
    printf "ExecuteablePath: %s" executeablePath

    // does not work for self-containted publishes'
    //let selfPath = System.Environment.GetCommandLineArgs().[0]
    let workingDirectory =  executeablePath |> Path.GetDirectoryName
    if Directory.Exists workingDirectory then
        Log.line "setting current directory to: %s" workingDirectory
        System.Environment.CurrentDirectory <- workingDirectory
    else  
        Log.warn "execute"
    Config.besideExecuteable <- workingDirectory
    //PRo3D.Minerva.Config.besideExecuteable <- workingDirectory
    

    let startupArgs = CommandLine.parseArguments argv

    // --noMapping --samples 8 --backgroundColor red
    Config.backgroundColor <- startupArgs.backgroundColor
    Config.useMapping <- startupArgs.useMapping

    // no more limitedShaderCapabilities split: the projection shaders use
    // bounded uniform arrays instead of storage buffers, so the same effect
    // runs on macOS (GL 4.1)

    System.Threading.ThreadPool.SetMinThreads(12, 12) |> ignore
    
    Log.line "path: %s, current dir: %s" executeablePath System.Environment.CurrentDirectory

    let os = 
        if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
            OSPlatform.OSX
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then
            OSPlatform.Linux
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            OSPlatform.Windows
        else 
            Log.warn "could not detect os platform.. assuming linux"
            OSPlatform.Linux

    if not startupArgs.serverMode then
        let aardiumPath = 
            try
                let ass = workingDirectory
                if os = OSPlatform.Windows then
                    let exe = Path.Combine(ass, "tools", "Aardium.exe")
                    Log.line "exists? %s" exe
                    if File.Exists exe then
                        Some (Path.Combine(ass, "tools"))
                    else
                        None
                elif os = OSPlatform.OSX then
                    let app = Path.Combine(ass, "tools", "Aardium.app")
                    Log.line "exists? %A" app
                    if Directory.Exists app || File.Exists app then
                        Some (Path.Combine(ass, "tools"))
                    else None
                else None
            with _ ->
                None
        match aardiumPath with
        | Some p when true -> 
            Log.line "init aardium at: %s" p
            Aardium.Init p
        | _ -> 
            Log.warn "system aardium"; 
            Aardium.Init()

    Config.previewIntersections <- startupArgs.allowPreviewIntersections

    Aardvark.Init()
    let mutable cooTrafoInitialized = false
    let disposables = List<IDisposable>()
    try
        //let p = Path.GetFullPath(startupArgs.defaultSpiceKernelPath)
        //Log.line "full spice "
        CooTransformation.initCooTrafo startupArgs.defaultSpiceKernelPath appData
        cooTrafoInitialized <- true
        //CooTransformation.getRelState viewerBody supportBody observer time referenceFrame
        let r = PRo3D.SPICE.CooTransformation.getRelState "HERA" "SUN" "MARS" (DateTime.Parse("2025-03-12T11:26:13.011Z")) "HERA_AFC-1"
        //use app = new VulkanApplication()
        //Glfw.Config.hideCocoaMenuBar <- true
        use app = new OpenGlApplication()
        let runtime = app.Runtime    

        match startupArgs.data_samples with
        | None -> 
            if runtime.Context.Driver.renderer.Contains("Intel(R) Iris(R) Xe Graphics") then
                Log.warn "intel iris workaround active - multisampling must be disabled, see:  https://github.com/pro3d-space/PRo3D/issues/116"
                Config.data_samples <- "1"
                Config.disableMultisampling <- true
            else 
                Config.data_samples <- "4"
        | Some v -> 
            if runtime.Context.Driver.renderer.Contains("Intel(R) Iris(R) Xe Graphics") then
                Log.warn "you specified number of samples %s, this is not recommended on intel iris graphics and might lead to problems, see: https://github.com/pro3d-space/PRo3D/issues/116" v
            Config.data_samples <- v

        
        Log.line "render control config: %A" (Config.data_samples, Config.backgroundColor, Config.useMapping)
    

        Aardvark.Rendering.GL.RuntimeConfig.SuppressSparseBuffers <- true
        //app.ShaderCachePath <- None

        PRo3D.Core.Drawing.DrawingApp.usePackedAnnotationRendering <- true
        Sg.hackRunner <- runtime.CreateLoadRunner 1 |> Some

        Serialization.init()
    
        Serialization.registry.RegisterFactory (fun _ -> KdTrees.level0KdTreePickler)
        Serialization.registry.RegisterFactory (fun _ -> Init.incorePickler)
    
        Log.line "PRo3D Viewer - Version: %s; powered by Aardvark" viewerVersion
        let titlestr = 
                match startupArgs.port with
                | Some p -> "PRo3D Viewer - " + viewerVersion + " - VRVis Zentrum für Virtual Reality und Visualisierung Forschungs-GmbH - listening: http://localhost:" + p
                | None -> "PRo3D Viewer - " + viewerVersion + " - VRVis Zentrum für Virtual Reality und Visualisierung Forschungs-GmbH"

        Config.title <- titlestr
    
        let signature =
            runtime.CreateFramebufferSignature([
                DefaultSemantic.Colors, TextureFormat.Rgba8
                DefaultSemantic.DepthStencil, TextureFormat.Depth24Stencil8
            ], samples = ViewerApp.dataSamples)

        use sendQueue = new BlockingCollection<string>()

        let ws (cancellationToken : CancellationToken) (webSocket : IWebSocket) (_: 'HttpContext) : Tasks.Task =
            task {
                let mutable loop = true
            
                while loop && not cancellationToken.IsCancellationRequested do
                    let str = sendQueue.Take()
                    Log.warn "taking item from bc"
                    do! webSocket.SendText(str, cancellationToken)
            }

        Sg.useAsyncLoading <- (argv |> Array.contains "-sync" |> not)
        //let startEmpty = (argv |> Array.contains "-empty")
        let startEmpty = not (argv |> Array.contains "-loadRecent")
        Log.line "[StartupArgs] -empty currently default, use -loadRecent instead. startEmpty = %b" startEmpty

        UI.enabletoolTips <- (argv |> Array.contains "-notooltips" |> not)

        // main app
        //use form = new Form(Width = 1280, Height = 800)
        let cts = new CancellationTokenSource()
        let messagingMailbox = MailboxProcessor.Start(Viewer.initMessageLoop cts, cts.Token)    

       // let minervaMailbox = MailboxProcessor.Start(PRo3D.Minerva.App.messagingMailbox cts, cts.Token)
    
        let argsKv = 
            argv 
            |> Array.filter(fun x -> x.Contains "=")
            |> Array.map(fun x -> 
                  let kv = x.Split [|'='|]
                  kv.[0],kv.[1])
            |> HashMap.ofArray

        let dumpFile =
            match argsKv |> HashMap.tryFind "dump" with
            | Some file -> file
            | _ -> "dump.csv"
            //| None when Minerva.Config.ShowMinervaErrors ->
            //    Log.warn "need dump file ... dump=\"[dumpfilepath]\" -> using defaultPath '.\MinervaData\dump.csv'"
            //    @".\MinervaData\dump.csv"
            //| _ -> 
            //    @".\MinervaData\dump.csv"

        let cacheFile =
            match argsKv |> HashMap.tryFind "cache" with
            | Some file -> file
            | _ -> "dump.cache"
            //| None when Minerva.Config.ShowMinervaErrors ->
            //    Log.warn "need cache file ... cache=\"[cachefilepath]\" -> using defaultPath '.\MinervaData\dump.cache'"
            //    @".\MinervaData\dump.cache"
            //| _ -> 
            //    @".\MinervaData\dump.cache"

        //let access =
        //    match argsKv |> HashMap.tryFind "access" with
        //    | Some file -> file
        //    | None -> failwith "need minerva access ... access=\"minervaaccount:pw\" "
        
        let port = 
            match startupArgs.port with
            | None -> Server.getFreeTcpPort Net.IPAddress.Loopback
            | Some port -> 
                match Int32.TryParse port with
                | (true, v) -> v
                | _ -> 
                    Log.warn "could not parse int from port %s" port
                    Server.getFreeTcpPort Net.IPAddress.Loopback

        let renderingUrl = sprintf "http://localhost:%d" port

        let startupLoad =
            match startupArgs.loadScene with
            | Some scene -> 
                Log.line "[Viewer] loading scene %s" scene
                ViewerApp.ViewerStartupLoad.LoadScene scene
            | None when startEmpty -> 
                Log.line "[Viewer] starting empty"
                ViewerApp.ViewerStartupLoad.Empty
            | _ -> 
                Log.line "[Viewer] loading last scene"
                ViewerApp.ViewerStartupLoad.LoadLastScene

        use mainApp =
            ViewerApp.start 
                runtime 
                signature 
                startupLoad
                messagingMailbox 
                sendQueue 
                dumpFile 
                cacheFile 
                renderingUrl 
                ViewerApp.dataSamples
                startupArgs.enableProvenanceTracking
                appData
                viewerVersion

        let adaptiveModel = mainApp.MutableModel
        mainApp.DocumentTitle <- titlestr

        { MailboxState.empty with 
            update = (fun a -> 
                let a = Seq.map ViewerMessage a
                mainApp.Update(Guid.Empty, a)
            ) 
        }
        |> InitMailboxState
        |> messagingMailbox.Post
    
        //let domainError (sender:obj) (args:UnhandledExceptionEventArgs) =
        //    let e = args.ExceptionObject :?> Exception;
        //    Log.error "%A" e
        //    MessageBox.Show(e.Message, "Sorry for the inconvenience", MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore
        //    ()
    
        let domainError (sender:obj) (args:UnhandledExceptionEventArgs) =
            let e = args.ExceptionObject :?> Exception;
            Log.error "%A" e
            // TODO -> Media Message-Box (implement)
            //MessageBox.Show(e.Message, "Sorry for the inconvenience", MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore
            ()
    
        if catchDomainErrors then
            AppDomain.CurrentDomain.UnhandledException.AddHandler(UnhandledExceptionEventHandler(domainError))

        let http = HttpBackend.Instance
        let (>=>) x y = http.compose x y
    
        let allow_cors : WebPart =
            http.method HttpMethod.Options
            >=> http.header "Access-Control-Allow-Origin" "*"
            >=> http.header "Access-Control-Allow-Headers" "content-type"
            >=> http.ok "CORS approved"

        if startupArgs.enableProvenanceTracking && not startupArgs.enableRemoteApi then
            failwith "provenance tracking requires remote api to be enabled "

        let remoteApi =
            match startupArgs.enableRemoteApi with
            | true -> 
                Log.line "attaching remote API"
                let applyMessage msg = mainApp.UpdateSync(Guid.Empty, [msg])

                //let storage = ProvenanceModel.localDirectory "./provenanceData"
                let storage = ProvenanceModel.nopStorage()

                let api = RemoteApi.Api(applyMessage, adaptiveModel.provenanceModel, adaptiveModel, storage)
                RemoteApi.Http.webPart mainApp.CancellationToken storage api
            | _ ->
                http.choose []

        let server = 
            let startServer = 
                if startupArgs.enableRemoteApi then
                    fun port -> Server.start $"http://{Net.IPAddress.Any}:{port}" mainApp.CancellationToken false
                else
                    fun port -> Server.startLocalhost port mainApp.CancellationToken

            startServer port [
                if startupArgs.disableCors then allow_cors
                MutableApp.toWebPart' runtime false mainApp
                http.route "/websocket" >=> http.handShake (ws mainApp.CancellationToken)
                http.subRoute "/api" remoteApi
                WebPart.ofType<EmbeddedRessource>
                WebPart.ofType<Primitives.EmbeddedResources>
               // Reflection.assemblyWebPart typeof<CorrelationDrawing.CorrelationPanelResources>.Assembly //(System.Reflection.Assembly.LoadFrom "PRo3D.CorrelationPanels.dll")
               // prefix "/instrument" >=> MutableApp.toWebPart runtime instrumentApp

                http.route "/crash.txt" >=> http.mimeType "text/plain" >=> http.sendFile logFilePath

                http.route "/minilog.txt" >=> http.mimeType "text/plain" >=> http.request (fun r ->
                    use s = File.Open(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                    use r = new StreamReader(s)
                    let log = 
                        seq {
                            while not r.EndOfStream do 
                                let line = r.ReadLine() 
                                if line.Contains "GetPluginAssemblyPaths" || line.Contains "[cache hit ]" then 
                                    () 
                                else 
                                    yield line
                        } |> Seq.toArray
                    let head = Array.take (min log.Length 300) log
                    let trail = log.[max 0 (log.Length - 20) .. max 0 (log.Length - 1)]
                    let newline = """%0D%0A"""
                    let miniLog = sprintf "%s%s..truncated..%s%s" (String.concat "%0D%0A" head) newline (String.concat "%0D%0A" trail) newline
                    http.ok miniLog
                    //Files.sendFile "Aardvark.log" false
                )
                // should all be handled via embedded resources
                //Suave.Files.browse (IO.Directory.GetCurrentDirectory())
                //Suave.Files.browseHome        
            ]

        //WebPart.startServer 4322 [
        //    MutableApp.toWebPart' runtime false instrumentApp        
        //    Suave.Files.browseHome
        //]

        // screenshot app

        if startupArgs.remoteApp then
            let send msg =
                match msg with
                  | RemoteAction.SetCameraView cv ->
                      mainApp.Update(Guid.Empty, ViewerMessage (ViewerAction.SetCamera cv) |> Seq.singleton)
                  | RemoteAction.SetView v ->                                
                      Log.line "Setting View %A" v
                      let frustum = Frustum.perspective v.fovH v.near v.far (float v.resolution.X / float v.resolution.Y)
                      let frustum = { 
                          frustum with 
                              left   = frustum.left   - v.principalPoint.X
                              right  = frustum.right  - v.principalPoint.X
                              top    = frustum.top    - v.principalPoint.Y
                              bottom = frustum.bottom - v.principalPoint.Y
                          }
                      let cameraAction = ViewerMessage (ViewerAction.SetCameraAndFrustum2 (v.view,frustum))
                      mainApp.Update(Guid.Empty, cameraAction |> Seq.singleton)

            let remoteApp = 
                App.start (PRo3D.RemoteControlApp.app renderingUrl send)

            let takeScreenshot (shot:Shot) =   
                let act = CaptureShot shot |> Seq.singleton
                remoteApp.Update(Guid.Empty, act)
                { result = shot.folder }

            let takePlatformShot (shot:PlatformShot) =   
                let act = CapturePlatform shot |> Seq.singleton
                remoteApp.Update(Guid.Empty, act)
                { result = shot.folder }

            let remotePort = 12346 
            Server.startLocalhost 12346 remoteApp.CancellationToken [
                MutableApp.toWebPart runtime remoteApp
                http.method HttpMethod.Post >=> http.route "/shots" >=> http.mapJson takeScreenshot
                http.method HttpMethod.Post >=> http.route "/platformshots" >=> http.mapJson takePlatformShot
            ] |> ignore
            disposables.Add(remoteApp)
            Log.line "Remote app started at port: %d" remotePort
        else   
            Log.warn "no remote app started"

        if startupArgs.serverMode then
            Log.line "running server mode"

            let lockObj = obj()

            // Communicate media errors to the electron process to display a report dialog
            let sendErrorToElectron (message: string) =
                if notNull message then
                    lock lockObj (fun _ ->
                        printfn "ELECTRON_ERROR_START"
                        for line in String.getLines message do printfn "ELECTRON_ERROR:%s" line
                        printfn "ELECTRON_ERROR_END"
                    )

            mainApp.ApplicationError.Add (fun error ->
                let message =
                    match error.Source with
                    | ApplicationErrorSource.Update msg ->
                        $"Update for message '{msg}' failed: {error.Exception}"

                    | ApplicationErrorSource.EventHandler (_, name, sender, args) ->
                        $"Event handler '{name}' for {sender} faulted (args: {args}): {error.Exception}"

                    | ApplicationErrorSource.ChannelUpdate (_, elementId, channelName) ->
                        $"Failed to get '{channelName}' messages for {elementId}: {error.Exception}"

                sendErrorToElectron message
            )

            mainApp.InternalError.Add (fun error ->
                let message =
                    match error.Source with
                    | InternalErrorSource.Rendering _ ->
                        $"Failed to execute render task: {error.Exception}"

                    | InternalErrorSource.MessageParsing (_, data) ->
                        $"Failed to parse message '{data}': {error.Exception}"

                    | InternalErrorSource.Connection _ ->
                        null

                sendErrorToElectron message
            )

            printfn "ELECTRON_URL:%s" renderingUrl // Do not change these lines, the Electron process listens for these strings
            printfn "ELECTRON_GPU:%s" app.Context.Driver.renderer
            printfn "ELECTRON_LOG_FILE:%s" logFilePath
            Console.Read() |> ignore
        else
            Aardium.run {
                url renderingUrl   //"http://localhost:4321/?page=main"
                width 1280
                height 800
#if DEBUG
                debug true
                log (fun msg -> Report.Line(2, $"[Aardium] {msg}"))
#endif
                title titlestr
            }

    finally
        if cooTrafoInitialized then
            CooTransformation.deInitCooTrafo ()
        
        for d in disposables do
            d.Dispose()
    0
 
