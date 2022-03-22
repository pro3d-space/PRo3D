module Server

open System
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Saturn
open Microsoft.AspNetCore.Builder

open Shared

open Giraffe

open PRo3D.Base
open System.Threading
open System.Threading.Tasks

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.Application.Slim
open Aardvark.UI

open Aardium
open PRo3D.Lite
open PRo3D.Base

open Aardvark.Service.Giraffe
open Aardvark.UI.Giraffe
open Microsoft.AspNetCore

module Storage =
    let todos = ResizeArray()

    let addTodo (todo: Todo) =
        if Todo.isValid todo.Description then
            todos.Add todo
            Ok()
        else
            Result.Error "Invalid todo"

    do
        addTodo (Todo.create "Create new SAFE project")
        |> ignore

        addTodo (Todo.create "Write your app") |> ignore
        addTodo (Todo.create "Ship it !!!") |> ignore

let todosApi (instance : MutableApp<Model, Message>) =
    {
        getTodos = fun () -> async { return Storage.todos |> List.ofSeq }
        addTodo =
            fun todo -> 
                async {
                    return
                        match Storage.addTodo todo with
                        | Ok () -> todo
                        | Result.Error e -> failwith e 
                }
        centerScene = fun () ->
            async {
                instance.update Guid.Empty (Seq.singleton Message.CenterScene)
                return ()
            }
        
    }



[<EntryPoint>]
let main _ =

    Aardvark.Init()

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

    let renderApp = MutableApp.toWebPart runtime instance

    let webApp =
        Remoting.createApi ()
        |> Remoting.withRouteBuilder Route.builder
        |> Remoting.fromValue (todosApi instance)
        |> Remoting.buildHttpHandler

    let app =
        choose [
            subRoute "/render" renderApp 
            webApp
        ]

    let app =
        application {
            url "http://*:8085"
            use_router app
            memory_cache
            use_static "public"
            use_gzip
            app_config (fun ab -> ab.UseWebSockets().UseMiddleware<WebSockets.WebSocketMiddleware>())
        }

    run app
    0