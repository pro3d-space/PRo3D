/// PRo3D Lite: the minimal PRo3D.
///
///   PRo3D.Lite.exe [--opc <dir> ...] [--scene <file.pro3d>] [--port 4340] [--server]
///
/// `--opc` imports OPC directories (or folders of them) at start, `--scene` opens a scene.
/// `--server` serves without opening a window and runs until stdin closes - the mode the
/// Playwright spec drives; it prints `LITE_URL:<url>` once the server is up.
module PRo3D.Lite.Program

open System
open System.Collections.Concurrent
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application.Slim
open Aardvark.UI
open Aardvark.UI.Giraffe
open Aardvark.UI.Primitives.Golden
open FSharp.Data.Adaptive

open Aardium

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Composition

type Resources = Resources

let private argAfter (argv : string[]) (name : string) =
    argv |> Array.tryFindIndex ((=) name) |> Option.bind (fun i -> Array.tryItem (i + 1) argv)

let private argsAfter (argv : string[]) (name : string) =
    [ for i in 0 .. argv.Length - 2 do if argv.[i] = name then yield argv.[i + 1] ]

[<EntryPoint; STAThread>]
let main argv =
    let opcs   = argsAfter argv "--opc"
    let scene  = argAfter argv "--scene"
    let server = argv |> Array.contains "--server"
    let port =
        argAfter argv "--port"
        |> Option.bind (fun s -> match Int32.TryParse s with | true, p -> Some p | _ -> None)
        |> Option.defaultValue 4340

    let appData = Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ApplicationData, "Pro3D")
    Directory.CreateDirectory appData |> ignore

    // Before Aardvark.Init: it registers the scene-graph rules ([<Rule>]) of the assemblies
    // loaded at that moment, and PRo3D.Core's (the OPC footprint/attribute semantics) are
    // needed to render a surface. Touching PRo3D.Core's config loads it - as PRo3D.Viewer does.
    PRo3D.Config.configPath <- appData
    PRo3D.Config.besideExecuteable <- AppContext.BaseDirectory

    Aardvark.Init()
    if not server then Aardium.Init()
    // planetary up vectors and measurements (altitude, bearing, ...) need the SPICE frames
    CooTransformation.initCooTrafo None appData

    use app = new OpenGlApplication()
    let runtime = app.Runtime :> IRuntime
    ProcessInit.initRuntime runtime true
    ProcessInit.initSerialization ()

    let signature =
        runtime.CreateFramebufferSignature([
            DefaultSemantic.Colors, TextureFormat.Rgba8
            DefaultSemantic.DepthStencil, TextureFormat.Depth24Stencil8
        ], samples = 4)

    // DrawingApp reports to this queue; Lite has no websocket listener, so it is drained here
    let sendQueue = new BlockingCollection<string>()
    let drain = Threading.Thread((fun () -> for _ in sendQueue.GetConsumingEnumerable() do ()), IsBackground = true)
    drain.Start()

    let update = LiteApp.update runtime signature sendQueue

    let initial =
        let m = LiteApp.initial (UserPreferences.load ())
        let m = match scene with Some path -> LiteApp.openScene runtime signature path m | None -> m
        if List.isEmpty opcs then m else update m (ImportOpcs opcs)

    let liteApp : App<LiteModel, AdaptiveLiteModel, LiteAction> =
        {
            unpersist = Unpersist.instance
            threads   = LiteApp.threads
            initial   = initial
            update    = update
            view      = LiteGui.view runtime
        }

    use instance = App.start liteApp
    instance.DocumentTitle <- "PRo3D Lite"

    let http = HttpBackend.Instance
    Server.startLocalhost port instance.CancellationToken [
        Aardvark.UI.Primitives.Resources.toWebPart http
        MutableApp.toWebPart' runtime false instance
        WebPart.ofType<Resources>
        GoldenLayout.toWebPart http
    ] |> ignore

    let liteUrl = sprintf "http://localhost:%d/" port
    printfn "LITE_URL:%s" liteUrl

    if server then
        // runs until stdin closes, like PRo3D.Viewer --server
        Console.Read() |> ignore
    else
        Aardium.run {
            url liteUrl
            width 1400
            height 860
            title "PRo3D Lite"
            dynamicTitle true
        }
    0
