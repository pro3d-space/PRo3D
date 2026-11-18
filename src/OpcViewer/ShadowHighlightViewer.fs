namespace Aardvark.Opc

open System
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Application.Slim
open Aardvark.Data.Opc
open Aardvark.GeoSpatial.Opc
open Aardvark.GeoSpatial.Opc.Load

open FSharp.Data.Adaptive
open MBrace.FsPickler

open PRo3D.Core

/// Testbed for the fullscreen shadow/highlight pass (PRo3D.Core.ShadowHighlight, see
/// docs/ShadowHighlight.md). The surfaces go through the same path as in PRo3D: rendered
/// offscreen (OffscreenSurfaces.render), optionally through the pass, copied into the window
/// (OffscreenSurfaces.blit).
///
///   OpcViewer <opc-dir> [<opc-dir> ...] [--shadow-highlight] [--samples <n>]
///
/// Keys: H toggles the pass, PageUp/PageDown change the speed, WASD + mouse fly.
module ShadowHighlightViewer =

    type Options =
        {
            opcs            : list<string>
            shadowHighlight : bool
            samples         : int
        }

    let usage =
        String.concat Environment.NewLine [
            "usage: OpcViewer <opc-dir> [<opc-dir> ...] [--shadow-highlight] [--samples <n>]"
            "  <opc-dir>             an OPC, or a folder of OPCs (searched two levels deep)"
            "  --shadow-highlight    start with the shadow/highlight pass on (H toggles it)"
            "  --no-shadow-highlight start with it off (default)"
            "  --samples <n>         multisampling of window and surfaces (default 4)"
        ]

    let private isOpc (dir : string) =
        Directory.Exists(Path.Combine(dir, "patches")) || Directory.Exists(Path.Combine(dir, "Patches"))

    /// `dir` itself if it is an OPC, else the OPCs at most `depth` folders below it.
    let rec private findOpcs (depth : int) (dir : string) : list<string> =
        if isOpc dir then [ dir ]
        elif depth <= 0 || not (Directory.Exists dir) then []
        else Directory.GetDirectories dir |> Array.toList |> List.collect (findOpcs (depth - 1))

    let parseArgs (argv : array<string>) : Result<Options, string> =
        let rec go (o : Options) (args : list<string>) =
            match args with
            | [] ->
                match o.opcs with
                | [] -> Result.Error "no OPC found in the given folder(s)"
                | opcs -> Result.Ok { o with opcs = List.rev opcs }
            | "--shadow-highlight" :: rest    -> go { o with shadowHighlight = true } rest
            | "--no-shadow-highlight" :: rest -> go { o with shadowHighlight = false } rest
            | "--samples" :: n :: rest ->
                match Int32.TryParse n with
                | true, s when s >= 1 -> go { o with samples = s } rest
                | _ -> Result.Error (sprintf "--samples expects a positive number, got '%s'" n)
            | a :: _ when a.StartsWith "-" -> Result.Error (sprintf "unknown option '%s'" a)
            | dir :: rest ->
                match findOpcs 2 dir with
                | [] -> Result.Error (sprintf "no OPC in '%s'" dir)
                | found -> go { o with opcs = List.rev found @ o.opcs } rest
        go { opcs = []; shadowHighlight = false; samples = 4 } (Array.toList argv)

    let run (o : Options) =

        Aardvark.Init()

        use app = new OpenGlApplication()
        let win = app.CreateGameWindow(samples = o.samples)
        let runtime = win.Runtime
        // the OPC patches are prepared for the window's signature, so the offscreen
        // surfaces must use its sample count (OffscreenSurfaces.render)
        let samples = win.FramebufferSignature.Samples

        let runner = runtime.CreateLoadRunner 1
        let serializer = FsPickler.CreateBinarySerializer()

        let loaded =
            o.opcs |> List.map (fun basePath ->
                Log.line "[ShadowHighlight] loading %s" basePath
                let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                let bb =
                    match h.tree with
                    | QTree.Node (p, _) | QTree.Leaf p -> p.info.GlobalBoundingBox
                let sg =
                    Sg.patchLod win.FramebufferSignature runner basePath DefaultMetrics.mars2
                        false false ViewerModality.XYZ PatchLod.CoordinatesMapping.Local true
                        (PatchLod.toRoseTree h.tree)
                bb, sg
            )

        let bb = loaded |> List.fold (fun (acc : Box3d) (b, _) -> acc.Union b) Box3d.Invalid
        let diagonal = bb.Size.Length

        // planetary data: up is away from the body centre; small bodies around the origin: +Z
        let up = if bb.Center.Length > diagonal then bb.Center.Normalized else V3d.OOI
        let speed = AVal.init (diagonal / 20.0)
        let initialView = CameraView.lookAt bb.Max bb.Center up
        let view = initialView |> DefaultCameraController.controlWithSpeed speed win.Mouse win.Keyboard win.Time
        let near, far = diagonal / 10000.0, diagonal * 10.0
        let frustum = win.Sizes |> AVal.map (fun s -> Frustum.perspective 60.0 near far (float s.X / float (max 1 s.Y)))

        let enabled = cval o.shadowHighlight
        let report () = Log.line "[ShadowHighlight] pass %s" (if enabled.Value then "on" else "off")
        report ()

        win.Keyboard.KeyDown(Keys.H).Values.Add(fun _ ->
            transact (fun _ -> enabled.Value <- not enabled.Value)
            report ()
        )
        win.Keyboard.KeyDown(Keys.PageUp).Values.Add(fun _ ->
            transact (fun _ -> speed.Value <- speed.Value * 1.5)
        )
        win.Keyboard.KeyDown(Keys.PageDown).Values.Add(fun _ ->
            transact (fun _ -> speed.Value <- speed.Value / 1.5)
        )

        let viewTrafo = view |> AVal.map CameraView.viewTrafo
        let projTrafo = frustum |> AVal.map Frustum.projTrafo

        let surfaces =
            loaded
            |> List.map snd
            |> Sg.ofList
            |> Sg.effect [
                Aardvark.Opc.Shader.stableTrafo |> toEffect
                DefaultSurfaces.constantColor C4f.White |> toEffect
                DefaultSurfaces.diffuseTexture |> toEffect
            ]

        // as in the viewer (Viewer-Utils offscreenScene): offscreen, through the pass, copied in
        let offscreen = OffscreenSurfaces.inPriorityGroups viewTrafo projTrafo (AList.single surfaces)
        let color, depth = OffscreenSurfaces.render runtime samples win.Sizes offscreen
        let sg = ShadowHighlight.blit enabled runtime samples samples win.Sizes color depth

        win.RenderTask <- runtime.CompileRender(win.FramebufferSignature, sg)
        win.Run()
        0
