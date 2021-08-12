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
open Aardvark.SceneGraph.Opc
open Aardvark.UI
open Aardvark.VRVis
open Aardvark.VRVis.Opc
open Aardvark.GeoSpatial.Opc
open OpcViewer.Base
open Aardvark.Rendering

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.SimulatedViews
open RemoteControlModel
open PRo3D.Viewer

open Aardium

open Chiron

open Suave
open Suave.WebPart
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Json

open FSharp.Data.Adaptive

open System.Reflection
open System.Runtime.InteropServices



[<DataContract>]
type Calc =
   { 
      [<field: DataMember(Name = "a")>]
      a : int;
      [<field: DataMember(Name = "b")>]
      b : int;
   }
 

 [<DataContract>]
type Result =
   { 
      [<field: DataMember(Name = "result")>]
      result : string;
   }

type EmbeddedRessource = EmbeddedRessource

let viewerVersion       = "4.0.0-prerelease3"
let catchDomainErrors   = false

open System.IO
open System.Runtime.InteropServices

let rec allFiles dirs =
    if Seq.isEmpty dirs then Seq.empty else
        seq { yield! dirs |> Seq.collect Directory.EnumerateFiles
              yield! dirs |> Seq.collect Directory.EnumerateDirectories |> allFiles }


let getFreePort() =
    let l = System.Net.Sockets.TcpListener(Net.IPAddress.Loopback, 0)
    l.Start()
    let ep = l.LocalEndpoint |> unbox<Net.IPEndPoint>
    l.Stop()
    ep.Port
   
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

    //let asdfasdf= typeof<Aardvark.SceneGraph.IO.Loader.Animation>.IsAutoLayout;
    //Aardvark.Base.IntrospectionProperties.BundleEntryPoint <- executeablePath

    // does not work for self-containted publishes'
    //let selfPath = System.Environment.GetCommandLineArgs().[0]
    let workingDirectory =  executeablePath |> Path.GetDirectoryName
    if Directory.Exists workingDirectory then
        Log.line "setting current directory to: %s" workingDirectory
        System.Environment.CurrentDirectory <- workingDirectory
    else  
        Log.warn "execute"
    Config.besideExecuteable <- workingDirectory
    PRo3D.Minerva.Config.besideExecuteable <- workingDirectory
    

    let startupArgs = (CommandLine.parseArguments argv)
    System.Threading.ThreadPool.SetMinThreads(12, 12) |> ignore
    

    Log.line "path: %s, current dir: %s" executeablePath System.Environment.CurrentDirectory
    Config.colorPaletteStore <- Path.combine [appData; "favoriteColors.js"]
    Log.line "Color palette favorite colors are stored here: %s" Config.colorPaletteStore

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
            Aardium.initPath p
        | _ -> 
            Log.warn "system aardium"; 
            Aardium.init()

    
    Aardvark.Init()
    let mutable cooTrafoInitialized = false
    let disposables = List<IDisposable>()
    try
        CooTransformation.initCooTrafo appData
        cooTrafoInitialized <- true

        //use app = new VulkanApplication()
        Glfw.Config.hideCocoaMenuBar <- true
        use app = new OpenGlApplication()
        let runtime = app.Runtime    

        Aardvark.Rendering.GL.RuntimeConfig.SupressSparseBuffers <- true
        //app.ShaderCachePath <- None

        PRo3D.Core.Drawing.DrawingApp.usePackedAnnotationRendering <- true
        Sg.hackRunner <- runtime.CreateLoadRunner 1 |> Some

        Serialization.init()
    
        Serialization.registry.RegisterFactory (fun _ -> KdTrees.level0KdTreePickler)
        Serialization.registry.RegisterFactory (fun _ -> Init.incorePickler)
    
        Log.line "PRo3D Viewer - Version: %s; powered by Aardvark" viewerVersion
    
        let signature =
            runtime.CreateFramebufferSignature [
                DefaultSemantic.Colors, { format = RenderbufferFormat.Rgba8; samples = 1 }
                DefaultSemantic.Depth,  { format = RenderbufferFormat.Depth24Stencil8; samples = 1 }
            ]

        use sendQueue = new BlockingCollection<string>()    
        
        let ws (webSocket : WebSocket) (context: HttpContext) =
            socket {
                let mutable loop = true
            
                while loop do
                    let str = sendQueue.Take()
                    Log.warn "taking item from bc"
                    let s = ByteSegment(System.Text.Encoding.UTF8.GetBytes str)
                
                    do! webSocket.send Text s true
            }        

        Sg.useAsyncLoading <- (argv |> Array.contains "-sync" |> not)
        let startEmpty = (argv |> Array.contains "-empty")

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
            | None when Minerva.Config.ShowMinervaErrors ->
                Log.warn "need dump file ... dump=\"[dumpfilepath]\" -> using defaultPath '.\MinervaData\dump.csv'"
                @".\MinervaData\dump.csv"
            | _ -> 
                @".\MinervaData\dump.csv"

        let cacheFile =
            match argsKv |> HashMap.tryFind "cache" with
            | Some file -> file
            | None when Minerva.Config.ShowMinervaErrors ->
                Log.warn "need cache file ... cache=\"[cachefilepath]\" -> using defaultPath '.\MinervaData\dump.cache'"
                @".\MinervaData\dump.cache"
            | _ -> 
                @".\MinervaData\dump.cache"

        //let access =
        //    match argsKv |> HashMap.tryFind "access" with
        //    | Some file -> file
        //    | None -> failwith "need minerva access ... access=\"minervaaccount:pw\" "

        Log.startTimed "[Viewer] reading json scene"
        //let loadedScnx : Scene = 
        //    @"E:\PRo3D\Scenes SCN\20191210_ViewPlanner.pro3d"
        //    |> Chiron.readFromFile 
        //    |> Json.parse 
        //    |> Json.deserialize

        //Log.line "[Viewer] scene: %A" loadedScnx

        let mainApp = 
            ViewerApp.start runtime signature startEmpty messagingMailbox sendQueue dumpFile cacheFile

        let s = { MailboxState.empty with update = mainApp.update Guid.Empty }
        MailboxAction.InitMailboxState s |> messagingMailbox.Post
    
    
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


        let setCORSHeaders =
            Suave.Writers.setHeader  "Access-Control-Allow-Origin" "*"
            >=> Suave.Writers.setHeader "Access-Control-Allow-Headers" "content-type"
    
        let allow_cors : WebPart =
            choose [
                OPTIONS >=>
                    fun context ->
                        context |> (
                            setCORSHeaders
                            >=> OK "CORS approved" )
            ]

        let port = getFreePort()
        let uri = sprintf "http://localhost:%d" port

        let suaveServer = 
            WebPart.startServerLocalhost port [
                allow_cors
                MutableApp.toWebPart' runtime false mainApp
                path "/websocket" >=> handShake ws
                Reflection.assemblyWebPart typeof<EmbeddedRessource>.Assembly
               // Reflection.assemblyWebPart typeof<CorrelationDrawing.CorrelationPanelResources>.Assembly //(System.Reflection.Assembly.LoadFrom "PRo3D.CorrelationPanels.dll")
               // prefix "/instrument" >=> MutableApp.toWebPart runtime instrumentApp

                path "/crash.txt" >=> Suave.Writers.setMimeType "text/plain" >=> request (fun r -> 
                    Files.sendFile logFilePath false
                )

                path "/minilog.txt" >=> Suave.Writers.setMimeType "text/plain" >=> request (fun r -> 
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
                    OK miniLog
                    //Files.sendFile "Aardvark.log" false
                )
                // should all be handled via embedded resources
                //Suave.Files.browse (IO.Directory.GetCurrentDirectory())
                //Suave.Files.browseHome        
            ] 

        disposables.Add(suaveServer)

        Log.line "serving at: %s" uri

        
        //WebPart.startServer 4322 [
        //    MutableApp.toWebPart' runtime false instrumentApp        
        //    Suave.Files.browseHome
        //]

        // screenshot app

        if startupArgs.remoteApp then
            let send msg =
                match msg with
                  | RemoteAction.SetCameraView cv ->
                      mainApp.update Guid.Empty (ViewerAction.SetCamera cv |> Seq.singleton)
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
                      mainApp.update Guid.Empty (ViewerAction.SetCameraAndFrustum2 (v.view,frustum) |> Seq.singleton)

            let remoteApp = 
                App.start (PRo3D.RemoteControlApp.app uri send)

            let takeScreenshot (shot:Shot) =   
                let act = CaptureShot shot |> Seq.singleton
                remoteApp.update Guid.Empty act
                { result = shot.folder }

            let takePlatformShot (shot:PlatformShot) =   
                let act = CapturePlatform shot |> Seq.singleton
                remoteApp.update Guid.Empty act
                { result = shot.folder }

            let remotePort = 12346
            let d = WebPart.startServerLocalhost 12346 [ 
                MutableApp.toWebPart runtime (remoteApp)
                POST >=> path "/shots" >=> mapJson takeScreenshot
                POST >=> path "/platformshots" >=> mapJson takePlatformShot
                Suave.Files.browseHome
            ] 
            disposables.Add(d)
            Log.line "Remote app started at port: %d" remotePort
        else   
            Log.warn "no remote app started"
    
        let titlestr = "PRo3D Viewer - " + viewerVersion + " - VRVis Zentrum für Virtual Reality und Visualisierung Forschungs-GmbH"

        // do not change this line. full url with url needs to be printed for mac deployment!
        Log.line "full url: %s" uri

        System.Threading.Thread.Sleep(100)

        if startupArgs.serverMode then  
            Log.line "running server mode. Press Key to close >"
            Console.Read() |> ignore
        else
            Aardium.run {
                url uri   //"http://localhost:4321/?page=main"
                width 1280
                height 800
                debug true
                title titlestr


                windowoptions {|  minWidth = 180; minHeight = 180; title = "PRo3D.Viewer";|}
                hideDock true
                autoclose true
            }

    finally
        if cooTrafoInitialized then
            CooTransformation.deInitCooTrafo ()
        
        for d in disposables do
            d.Dispose()
    0
 
