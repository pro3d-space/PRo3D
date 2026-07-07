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
open Aardvark.Rendering
open Aardvark.Application.Slim
open Aardvark.SceneGraph.Opc
open Aardvark.UI
open Aardvark.UI.Giraffe
open Aardvark.VRVis
open Aardvark.VRVis.Opc
open Aardvark.GeoSpatial.Opc
open OpcViewer.Base

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.SimulatedViews
open RemoteControlModel
open PRo3D.Viewer

open Aardium

open Chiron

open FSharp.Data.Adaptive

open Aardvark.GeoSpatial.Opc.Load


type EmbeddedRessource = EmbeddedRessource

let viewerVersion       = "4.25.0-prerelease7-Snapshots"
let catchDomainErrors   = false

open System.IO

let rec allFiles dirs =
    if Seq.isEmpty dirs then Seq.empty else
        seq { yield! dirs |> Seq.collect Directory.EnumerateFiles
              yield! dirs |> Seq.collect Directory.EnumerateDirectories |> allFiles }

let startApplication (startupArgs : CLStartupArgs) =
    System.Threading.ThreadPool.SetMinThreads(12, 12) |> ignore
      
    // ensure appdata is here
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create) |> printfn "ApplicationData: %s"
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create) |> printfn "LocalApplicationData: %s"


    let appData = Path.combine [Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); "Pro3D"]
    Config.configPath <- appData

    if not (Directory.Exists appData) then Directory.CreateDirectory(appData) |> ignore
      
    let logFilePath = Path.Combine(appData, "PRo3D.log")
    Aardvark.Base.Report.LogFileName <- logFilePath
    Log.line "Running with AppData: %s" appData

    let crashDumpFile = "Aardvark.log"

    Aardium.init()      
  
    Aardvark.Init()

    Aardvark.Data.PixImageFreeImage.Init()

    let mutable cooTrafoInitialized = false
    let disposables = List<IDisposable>()


    try 
        CooTransformation.initCooTrafo None appData
        cooTrafoInitialized <- true

        use app = new OpenGlApplication()
        let runtime = app.Runtime         

        Aardvark.Rendering.GL.RuntimeConfig.SuppressSparseBuffers <- true
        PRo3D.Core.Drawing.DrawingApp.usePackedAnnotationRendering <- false

        Sg.hackRunner <- runtime.CreateLoadRunner 1 |> Some

        Serialization.init()
  
        Serialization.registry.RegisterFactory (fun _ -> KdTrees.level0KdTreePickler)
        Serialization.registry.RegisterFactory (fun _ -> Init.incorePickler)
  
        Log.line "PRo3D Viewer - Version: %s; powered by Aardvark" viewerVersion
  
        let signature =
            runtime.CreateFramebufferSignature [
                DefaultSemantic.Colors, TextureFormat.Rgba8
                DefaultSemantic.DepthStencil, TextureFormat.Depth24Stencil8
            ]

        use sendQueue = new BlockingCollection<string>()    
      
        let ws (cancellationToken : CancellationToken) (webSocket : IWebSocket) (context: 'HttpContext) : Tasks.Task =
            task {
                let mutable loop = true
          
                while loop && not cancellationToken.IsCancellationRequested do
                    let str = sendQueue.Take()
                    Log.warn "taking item from bc"
                    do! webSocket.SendText(str, cancellationToken)
            }        


        // main app
        let cts = new CancellationTokenSource()
        let messagingMailbox = MailboxProcessor.Start(Viewer.initMessageLoop cts, cts.Token)    
 

        let dumpFile =
                @".\MinervaData\dump.csv"

        let cacheFile =
                @".\MinervaData\dump.cache"

        //Log.startTimed "[Viewer] reading json scene"


        let port = Server.getFreeTcpPort Net.IPAddress.Loopback
        let uri = sprintf "http://localhost:%d" port

        let mainApp =
            SimulatedViews.PRo3DUtils.start 
                runtime signature startupArgs.startEmpty messagingMailbox 
                sendQueue dumpFile cacheFile uri 8 "" viewerVersion 
                { StartupArgs.initArgs with showExplorationPoint = startupArgs.showExplorationPoint; showReferenceSystem = startupArgs.showReferenceSystem }

        disposables.Add mainApp

        let s = 
            {MailboxState.empty with update = 
                                        (fun a -> 
                                            let a = Seq.map ViewerMessage a
                                            mainApp.Update(Guid.Empty, a))
            }
        MailboxAction.InitMailboxState s |> messagingMailbox.Post
  
     
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

        let suaveServer = 
            Server.startLocalhost port mainApp.CancellationToken [
                allow_cors
                MutableApp.toWebPart' runtime false mainApp
                http.route "/websocket" >=> http.handShake (ws mainApp.CancellationToken)
                WebPart.ofType<EmbeddedRessource>
                WebPart.ofType<Aardvark.UI.Primitives.EmbeddedResources>
                // Reflection.assemblyWebPart typeof<CorrelationDrawing.CorrelationPanelResources>.Assembly //(System.Reflection.Assembly.LoadFrom "PRo3D.CorrelationPanels.dll")
                // prefix "/instrument" >=> MutableApp.toWebPart runtime instrumentApp

                http.route "/crash.txt" >=> http.mimeType "text/plain" >=> http.sendFile "Aardvark.log"

                http.route "/minilog.txt" >=> http.mimeType "text/plain" >=> http.request (fun r -> 
                    use s = File.Open("Aardvark.log", FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
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

        disposables.Add(suaveServer)

        Log.line "serving at: %s" uri

        let titlestr = "PRo3D Viewer - " + viewerVersion + " - VRVis Zentrum für Virtual Reality und Visualisierung Forschungs-GmbH"


        Sg.useAsyncLoading <- false // need this for rendering without gui!
        SnapshotGenerator.animate runtime mainApp.MutableModel mainApp startupArgs |> ignore
        try            
            match startupArgs.exitOnFinish with
            | true ->
                ()
            | false -> 
                Log.line ""
                Log.line "Execution finished."
                Log.line "Your images have been saved to %s" (Path.GetFullPath startupArgs.outFolder)
                Log.line "Press any key to exit."
                System.Console.ReadLine () |> ignore
        with 
        | e -> 
            Log.line "%s" e.Message
    finally
        if cooTrafoInitialized then
            CooTransformation.deInitCooTrafo ()
        for d in disposables do
            d.Dispose()     

[<EntryPoint;STAThread>]
let main argv = 
    // ensure appdata is here
    
    // check if there are command line arguments, and if they are valid
    Sg.useAsyncLoading <- false 
    let startupArgs = (SimulatedViews.CommandLine.parseArguments argv)
    match startupArgs.hasValidAnimationArgs with
    | true ->
        startApplication startupArgs
    | false ->
        Log.line "Press any key to exit."
        System.Console.ReadLine () |> ignore
    0

 
