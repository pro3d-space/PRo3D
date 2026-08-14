open System

open Aardvark.Base
open Aardvark.Application.Slim
open Aardvark.UI
open Aardvark.UI.Giraffe

open Aardium

[<EntryPoint; STAThread>]
let main _argv =
    Aardvark.Init()
    Aardium.Init()

    use app = new OpenGlApplication()
    use instance = PRo3D.GeometryLab.App.app |> App.start

    Server.startLocalhost 4322 instance.CancellationToken [
        MutableApp.toWebPart app.Runtime instance
        WebPart.ofType<Primitives.EmbeddedResources>
    ] |> ignore

    Aardium.run {
        url "http://localhost:4322/"
        width 1100
        height 850
        debug true
    }
    0
