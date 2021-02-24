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

let viewerVersion       = "3.1.0-prerelease1"
let catchDomainErrors   = false

open System.IO

let rec allFiles dirs =
    if Seq.isEmpty dirs then Seq.empty else
        seq { yield! dirs |> Seq.collect Directory.EnumerateFiles
              yield! dirs |> Seq.collect Directory.EnumerateDirectories |> allFiles }
   
[<EntryPoint;STAThread>]
let main argv = 
    // check if there are command line arguments, and if they are valid
    let startupArgs = (CommandLine.parseArguments argv)
    System.Threading.ThreadPool.SetMinThreads(12, 12) |> ignore

    Aardvark.Init()
    Aardium.init()        

    use app = new OpenGlApplication()
    let runtime = app.Runtime    

    Aardvark.Rendering.GL.RuntimeConfig.SupressSparseBuffers <- true
    app.ShaderCachePath <- None

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

    PatchLod.useAsyncLoading <- true //(argv |> Array.contains "-sync" |> not)
    let startEmpty = (argv |> Array.contains "-empty")

    // main app
    let cts = new CancellationTokenSource()
    let messagingMailbox = MailboxProcessor.Start(Viewer.initMessageLoop cts, cts.Token)    
    
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
        | None -> 
            Log.warn "need dump file ... dump=\"[dumpfilepath]\" -> using defaultPath '.\MinervaData\dump.csv'"
            @".\MinervaData\dump.csv"

    let cacheFile =
        match argsKv |> HashMap.tryFind "cache" with
        | Some file -> file
        | None -> 
            Log.warn "need cache file ... cache=\"[cachefilepath]\" -> using defaultPath '.\MinervaData\dump.cache'"
            @".\MinervaData\dump.cache"

    Log.startTimed "[Viewer] reading json scene"

    let (mModel, mainApp) = 
        ViewerApp.start runtime signature messagingMailbox sendQueue startupArgs dumpFile cacheFile

    let s = { MailboxState.empty with update = mainApp.update Guid.Empty }
    MailboxAction.InitMailboxState s |> messagingMailbox.Post
    
    CooTransformation.initCooTrafo () // should be a function not a value
       
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
    
    match startupArgs.hasValidAnimationArgs with
    | true ->
        PatchLod.useAsyncLoading <- true // need this for rendering without gui!
        SnapshotGenerator.animate runtime mModel mainApp startupArgs |> ignore
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
    | false ->
        Aardium.run {
            url "http://localhost:54322/"   //"http://localhost:4321/?page=main"
            width 1280
            height 800
            debug true
            title titlestr
        }

    CooTransformation.deInitCooTrafo ()
    0
 
