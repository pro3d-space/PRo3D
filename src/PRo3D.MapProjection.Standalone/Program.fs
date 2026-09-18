/// PRo3D.MapProjection.Standalone.exe: the map projection panel (#772) as a standalone app.
///
///   PRo3D.MapProjection.Standalone.exe --opc <dir> [--opc <dir> ...] [--annotations <file> ...]
///                            [--frame DIMORPHOS_SHM] [--planet Dimorphos] [--port 4330] [--server]
///
/// `--opc` takes an OPC directory (its hierarchies are found below it) or a single
/// hierarchy. `--annotations` takes a PRo3D annotation file or an SBMT structure file (points,
/// ellipses, circles), read in `--frame`. `--server` serves without opening a window and runs until stdin closes --
/// the mode the Playwright spec drives. PRo3D embeds the same `MapProjectionApp.view` as the
/// `mapprojection` page.
/// Lives in the PRo3D.MapProjection namespace although it builds into
/// PRo3D.MapProjection.Standalone.exe, so it sees the library modules directly.
module PRo3D.MapProjection.Program

open System
open System.IO

open Aardvark.Base
open Aardvark.Application.Slim
open Aardvark.UI
open Aardvark.UI.Giraffe
open FSharp.Data.Adaptive
open Aardvark.GeoSpatial.Opc.Load   // IRuntime.CreateLoadRunner

open Aardium

open PRo3D.Base

let private argAfter (argv : string[]) (name : string) =
    argv |> Array.tryFindIndex ((=) name) |> Option.bind (fun i -> Array.tryItem (i + 1) argv)

[<EntryPoint; STAThread>]
let main argv =
    let opcs = [ for i in 0 .. argv.Length - 2 do if argv.[i] = "--opc" then yield argv.[i + 1] ]
    let annotationFiles = [ for i in 0 .. argv.Length - 2 do if argv.[i] = "--annotations" then yield argv.[i + 1] ]
    let frame = argAfter argv "--frame" |> Option.defaultValue "DIMORPHOS_SHM"
    let planet =
        argAfter argv "--planet"
        |> Option.bind (fun s -> match Enum.TryParse<Planet>(s, true) with | true, p -> Some p | _ -> None)
        |> Option.defaultValue Planet.Dimorphos
    let port =
        argAfter argv "--port"
        |> Option.bind (fun s -> match Int32.TryParse s with | true, p -> Some p | _ -> None)
        |> Option.defaultValue 4330
    let server = argv |> Array.contains "--server"

    Aardvark.Init()
    if not server then Aardium.Init()

    use app = new OpenGlApplication()
    // the same runtime set-up PRo3D.Viewer's Program.fs does for OPC surfaces
    Aardvark.Rendering.GL.RuntimeConfig.SuppressSparseBuffers <- true
    PRo3D.Core.Surface.Sg.hackRunner <- app.Runtime.CreateLoadRunner 1 |> Some

    let surfaces =
        opcs
        |> List.choose (fun dir ->
            let hierarchies =
                if Directory.Exists(Path.Combine(dir, "Patches")) then [| dir |]
                elif Directory.Exists dir then MapSg.hierarchiesOf dir
                else [||]
            if hierarchies.Length = 0 then
                Log.warn "[map] no OPC hierarchy at %s" dir
                None
            else
                Some ({ hierarchies = hierarchies; placement = AVal.constant Trafo3d.Identity; visible = AVal.constant true } : MapSg.MapSurface))
    Log.line "[map] %s, %d surface(s)" (string planet) surfaces.Length

    let annotations =
        annotationFiles
        |> List.collect (fun file ->
            try
                let loaded = MapAnnotations.load frame file
                Log.line "[map] %d annotation(s) from %s" loaded.Length file
                loaded
            with e ->
                Log.warn "[map] cannot read annotations from %s: %s" file e.Message
                [])

    let inputs =
        {
            planet      = AVal.constant planet
            surfaces    = ASet.ofList surfaces
            annotations = { MapAnnotations.none with annotations = MapAnnotations.ofList annotations }
        }
    use instance = MapProjectionApp.app inputs |> App.start

    Server.startLocalhost port instance.CancellationToken [
        MutableApp.toWebPart app.Runtime instance
        WebPart.ofType<Primitives.EmbeddedResources>
    ] |> ignore

    let mapUrl = sprintf "http://localhost:%d/" port
    printfn "MAP_PROJECTION_URL:%s" mapUrl

    if server then
        // runs until stdin closes, like PRo3D.Viewer --server
        Console.Read() |> ignore
    else
        Aardium.run {
            url mapUrl
            width 1280
            height 760
            title "PRo3D Map Projection"
        }
    0
