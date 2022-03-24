open System

open System.Threading
open System.Threading.Tasks
open Giraffe

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.Application.Slim
open Aardvark.UI

open Aardium
open PRo3D.Lite
open PRo3D.Base

open Aardvark.UI.Giraffe
open Aardvark.Service.Giraffe

type Self = Self

[<EntryPoint; STAThread>]
let main argv = 
    Aardvark.Init()
    Aardium.init()
    
    let appData = Path.combine [Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); "Pro3D"]
    CooTransformation.initCooTrafo appData

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
    
    let mutable mapp : MutableApp<_,_> = Unchecked.defaultof<_>
    let emit msg = mapp.update Guid.Empty (Seq.singleton msg)
    let app = App.app runtime emit

    let instance = 
        app |> App.start

    mapp <- instance


    let webApp = 
        choose [
            Reflection.assemblyWebPart typeof<Self>.Assembly
            Reflection.assemblyWebPart typeof<Aardvark.UI.Primitives.EmbeddedResources>.Assembly
            MutableApp.toWebPart runtime instance
        ]
    use cts = new CancellationTokenSource()
    let server = Server.startServer "http://localhost:4321" cts.Token webApp 


    Aardium.run {
        url "http://localhost:4321/"
        width 1024
        height 768
        debug true
    }
    cts.Cancel()
    instance.shutdown()
    

    0 