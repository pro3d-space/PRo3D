open Aardvark.Base
open Aardvark.Application.Slim
open Aardvark.UI
open Aardium
open Suave

[<EntryPoint>]
let main argv =
    Aardvark.Init()
    Aardium.init()

    use app = new OpenGlApplication()
    let instance = PRo3D.GeometryLab.App.app |> App.start

    WebPart.startServerLocalhost 4322 [
        MutableApp.toWebPart' app.Runtime false instance
        Suave.Files.browseHome
    ] |> ignore

    Aardium.run {
        url "http://localhost:4322/"
        width 1100
        height 850
        debug true
    }
    0
