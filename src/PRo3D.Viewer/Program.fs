open System

//open System.Windows.Forms
open System.Collections.Concurrent
open System.Diagnostics
open System.Threading
open System.Xml
open System.Text
open System.Runtime.Serialization

open Aardvark.Base
open Aardvark.Application.Slim
open Aardvark.SceneGraph.Opc
open Aardvark.UI
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

let viewerVersion       = "3.1.3"
let catchDomainErrors   = false

open System.IO

let rec allFiles dirs =
    if Seq.isEmpty dirs then Seq.empty else
        seq { yield! dirs |> Seq.collect Directory.EnumerateFiles
              yield! dirs |> Seq.collect Directory.EnumerateDirectories |> allFiles }
   
[<EntryPoint;STAThread>]
let main argv = 
    //Xilium.CefGlue.ChromiumUtilities.unpackCef()
    //Chromium.init argv        

    let startupArgs = (CommandLine.parseArguments argv)
// check if there are command line arguments, and if they are valid    
    System.Threading.ThreadPool.SetMinThreads(12, 12) |> ignore
    
    Aardvark.Base.Report.Verbosity <- 1000
    

    Aardvark.UnpackNativeDependencies(typeof<JR.InstrumentPlatforms>.Assembly)
    let appData = Path.combine [Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); "Pro3D"]

    Log.line "Running with AppData: %s" appData
    Aardvark.Init()

    CooTransformation.initCooTrafo appData


    Aardium.init()        


    //use app = new VulkanApplication()
    use app = new OpenGlApplication()
    let runtime = app.Runtime    

    Aardvark.Rendering.GL.RuntimeConfig.SupressSparseBuffers <- true
    //app.ShaderCachePath <- None

    Sg.hackRunner <- runtime.CreateLoadRunner 2 |> Some

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

    PatchLod.useAsyncLoading <- (argv |> Array.contains "-sync" |> not)
    let startEmpty = (argv |> Array.contains "-empty")

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
            @".\MinervaData\dump.cache"

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

    WebPart.startServerLocalhost 54322 [
        MutableApp.toWebPart' runtime false mainApp
        path "/websocket" >=> handShake ws
        Reflection.assemblyWebPart typeof<EmbeddedRessource>.Assembly
       // Reflection.assemblyWebPart typeof<CorrelationDrawing.CorrelationPanelResources>.Assembly //(System.Reflection.Assembly.LoadFrom "PRo3D.CorrelationPanels.dll")
       // prefix "/instrument" >=> MutableApp.toWebPart runtime instrumentApp
        Suave.Files.browse (IO.Directory.GetCurrentDirectory())
        Suave.Files.browseHome        
    ] |> ignore

        
    //WebPart.startServer 4322 [
    //    MutableApp.toWebPart' runtime false instrumentApp        
    //    Suave.Files.browseHome
    //]

    // screenshot app
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
        App.start (PRo3D.RemoteControlApp.app "http://localhost:54322" send)

    let takeScreenshot (shot:Shot) =   
        let act = CaptureShot shot |> Seq.singleton
        remoteApp.update Guid.Empty act
        { result = shot.folder }

    let takePlatformShot (shot:PlatformShot) =   
        let act = CapturePlatform shot |> Seq.singleton
        remoteApp.update Guid.Empty act
        { result = shot.folder }

    WebPart.startServerLocalhost 12346 [ 
        MutableApp.toWebPart runtime (remoteApp)
        POST >=> path "/shots" >=> mapJson takeScreenshot
        POST >=> path "/platformshots" >=> mapJson takePlatformShot
        Suave.Files.browseHome
    ] |> ignore

    
    let titlestr = "PRo3D Viewer - " + viewerVersion + " - VRVis Zentrum für Virtual Reality und Visualisierung Forschungs-GmbH"

    
    Aardium.run {
        url "http://localhost:54322/"   //"http://localhost:4321/?page=main"
        width 1280
        height 800
        debug true
        title titlestr
    }

    CooTransformation.deInitCooTrafo ()
    0
 
