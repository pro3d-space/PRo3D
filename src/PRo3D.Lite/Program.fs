open System

open System.Threading
open System.Threading.Tasks

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.Application.Slim
open Aardvark.UI
open Aardvark.UI.Giraffe

open Aardium
open PRo3D.Lite
open PRo3D.Base

type Self = Self

[<EntryPoint; STAThread>]
let main argv = 
    Aardvark.Init()
    Aardium.Init()
    
    let appData = Path.combine [Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); "Pro3D"]
    CooTransformation.initCooTrafo None appData

    let useVulkan = false

    let runtime, disposable =
        if useVulkan then
            let app = new Aardvark.Rendering.Vulkan.HeadlessVulkanApplication()
            app.Runtime :> IRuntime, app :> IDisposable
        else
            let app = new OpenGlApplication()
            (app :> IApplication).Runtime.ShaderCachePath <- None
            app.Runtime :> IRuntime, app :> IDisposable
    use __ = disposable
    
    let mutable mapp : MutableApp<_,_,_> = Unchecked.defaultof<_>
    let emit msg = mapp.Update(Guid.Empty, [msg])
    let app = App.app runtime emit

    use instance =
        app |> App.start

    mapp <- instance

    Server.startLocalhost 4321 instance.CancellationToken [
        MutableApp.toWebPart runtime instance
        WebPart.ofType<Primitives.EmbeddedResources>
        WebPart.ofType<Self>
    ] |> ignore

    Aardium.run {
        url "http://localhost:4321/"
        width 1024
        height 768
#if DEBUG
        debug true
        log (fun msg -> Report.Line(2, $"[Aardium] {msg}"))
#endif
    }

    0 