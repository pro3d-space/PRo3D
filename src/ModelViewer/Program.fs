open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Data.GLTF
open Aardvark.Application
open FSharp.Data.Adaptive
open System.IO

open System.Reflection

open Example.GLTF

[<EntryPoint>]
let main args =

    let emptyScene =
        let mutable materials = Map.empty
        let mutable geometries = Map.empty
        let mutable nodes = []
        {
            Materials = materials
            Meshes = geometries
            ImageData = Map.empty
            RootNode = { Name = None; Trafo = None; Meshes = []; Children = nodes }
        }

    let getFileNamesWithExtension folderPath fileExtension =
        Directory.GetFiles(folderPath, $"*.{fileExtension}")
        |> Array.map Path.GetFullPath

    let roverFolderPath = @"..\..\..\resources\m2020-urdf-models\rover\meshes"
    let roverFileExtension = "gltf" 

    let roverFiles = getFileNamesWithExtension roverFolderPath roverFileExtension

    let roverModel = roverFiles |> Array.map(fun path -> match GLTF.tryLoad path with | Some scene -> scene | None -> emptyScene)
    let models = clist roverModel

    Aardvark.Init()

    let app = new Aardvark.Application.Slim.OpenGlApplication(DebugLevel.None)
    let win = app.CreateGameWindow(4)

    let view =
        CameraView.lookAt (V3d(10, 10, 10)) V3d.Zero V3d.OOI
        |> DefaultCameraController.control win.Mouse win.Keyboard win.Time
        |> AVal.map CameraView.viewTrafo

    let proj =
        win.Sizes
        |> AVal.map (fun s -> Frustum.perspective 45 0.1 100.0 (float s.X / float s.Y))
        |> AVal.map Frustum.projTrafo

    let enableTask =
        RenderTask.custom (fun _ ->
            OpenTK.Graphics.OpenGL4.GL.Enable(OpenTK.Graphics.OpenGL4.EnableCap.TextureCubeMapSeamless)
        )



    win.DropFiles.Add (fun paths ->
        let ms = paths |> Array.choose GLTF.tryLoad
        if ms.Length > 0 then
            transact (fun () ->
                models.UpdateTo models |> ignore
            )
    )

    let sw = System.Diagnostics.Stopwatch.StartNew()
    let rot =
        win.Time |> AVal.map (fun _ -> Trafo3d.RotationZ(0.4 * sw.Elapsed.TotalSeconds))
    let centerTrafo1 =
        models |> AList.toAVal |> AVal.map (fun models ->
            let bb = models |> Seq.map (fun m -> m.BoundingBox) |> Box3d
            Trafo3d.Translation(-bb.Center) *
            Trafo3d.Scale(5.0 / bb.Size.NormMax)
        )


    let rotateRoverTrafo = 
        models |> AList.toAVal |> AVal.map (fun models ->
            let bb = models |> Seq.map (fun m -> m.BoundingBox) |> Box3d
            Trafo3d.RotationX(1.57)
        )

    let renderTask =
        Sg.ofList [
            models
            |> AList.toAVal
            |> AVal.map (fun scenes -> SceneSg.toSimpleSg win.Runtime scenes)
            |> Sg.dynamic
            |> Sg.trafo centerTrafo1
            |> Sg.trafo rotateRoverTrafo
            |> Sg.trafo' (Trafo3d.RotationX Constant.PiHalf)

        ]
        //|> Sg.uniform' "LightLocation" (V3d(10,20,30))
        |> Sg.shader {
            do! Shader.trafo
            do! Shader.shade
        }
        //|> Sg.trafo rot
        |> Sg.viewTrafo ((rot, view) ||> AVal.map2 (*))
        |> Sg.projTrafo proj
        |> Sg.compile win.Runtime win.FramebufferSignature
    win.RenderTask <- RenderTask.ofList [enableTask; renderTask]
    win.Run()

    0