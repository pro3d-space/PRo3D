open System

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.Application.Slim
open Aardvark.UI

open Suave
open Suave.WebPart
open Aardium
open PRo3D.Lite

open PRo3D.Base

type Self = Self


[<EntryPoint; STAThread>]
let main argv = 
    Aardvark.Init()
    Aardium.init()
    
    let appData = Path.combine [Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); "Pro3D"]
    CooTransformation.initCooTrafo appData

    // media apps require a runtime, which serves as renderer for your render controls.
    // you can use OpenGL or VulkanApplication.
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

    WebPart.startServerLocalhost 4321 [ 
        Reflection.assemblyWebPart typeof<Self>.Assembly
        Reflection.assemblyWebPart typeof<Aardvark.UI.Primitives.EmbeddedResources>.Assembly
        MutableApp.toWebPart' runtime false instance
        Suave.Files.browseHome
    ] |> ignore
    

    Aardium.run {
        url "http://localhost:4321/"
        width 1024
        height 768
        debug true
    }
    0 