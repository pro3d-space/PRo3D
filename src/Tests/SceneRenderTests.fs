/// PRo3D.SceneRender and `pro3d-tool render-bookmarks` (#871): job files, bookmark
/// selection, and the Dimorphos test scene rendered headlessly from bookmarks saved in it,
/// including the fixed level of detail. The rendering tests need a GL context and the
/// test data (PRO3D_TEST_DATA); they skip otherwise.
module PRo3D.Tests.SceneRenderTests

open System
open System.IO

open Expecto

open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Viewer
open PRo3D.SceneRender

open PRo3D.Tests

let private bookmark (name : string) =
    Bookmark.init name (CameraView.lookAt V3d.OOO V3d.IOO V3d.OOI) V3d.OOO NavigationMode.FreeFly

let private names (bs : list<Bookmark>) = bs |> List.map (fun b -> b.name)

/// Mean absolute difference per channel, in [0, 255].
let private meanDifference (a : PixImage<byte>) (b : PixImage<byte>) =
    let ma, mb = a.GetMatrix<C4b>(), b.GetMatrix<C4b>()
    let mutable sum = 0L
    ma.ForeachIndex(fun i ->
        let x, y = ma.[i], mb.[i]
        sum <- sum + int64 (abs (int x.R - int y.R) + abs (int x.G - int y.G) + abs (int x.B - int y.B)))
    float sum / float (3L * ma.SX * ma.SY)

/// Fraction of all pixels that differ noticeably.
let private changedFraction (a : PixImage<byte>) (b : PixImage<byte>) =
    let ma, mb = a.GetMatrix<C4b>(), b.GetMatrix<C4b>()
    let mutable changed = 0L
    ma.ForeachIndex(fun i ->
        let x, y = ma.[i], mb.[i]
        if max (abs (int x.R - int y.R)) (max (abs (int x.G - int y.G)) (abs (int x.B - int y.B))) > 16 then
            changed <- changed + 1L)
    float changed / float (ma.SX * ma.SY)

/// a job folder that is a rooted path on every OS (C:\jobs is a relative path on Linux)
let private jobDir = Path.Combine(Path.GetTempPath(), "jobs")

let private parse (json : string) = JobFile.parse jobDir json

let private jobTests =
    testList "job file and bookmark selection" [

        test "a pattern selects by name, wildcard or GUID, in scene order and each once" {
            let a, b, c = bookmark "crater rim", bookmark "crater floor", bookmark "overview"
            let all = [ a; b; c ]
            Expect.equal (Bookmarks.select [ "*" ] all |> Result.map names) (Ok (names all)) "* is every bookmark"
            Expect.equal (Bookmarks.select [ "overview"; "crater*" ] all |> Result.map names)
                         (Ok [ "crater rim"; "crater floor"; "overview" ]) "scene order, not pattern order"
            Expect.equal (Bookmarks.select [ "crater ?im"; "crater rim" ] all |> Result.map names)
                         (Ok [ "crater rim" ]) "a bookmark two patterns select is rendered once"
            Expect.equal (Bookmarks.select [ string c.key ] all |> Result.map names) (Ok [ "overview" ]) "by GUID"
            Expect.equal (Bookmarks.select [ "Overview" ] all |> Result.isError) true "names are case-sensitive"
        }

        test "a pattern that selects nothing fails and lists what there is" {
            match Bookmarks.select [ "overview"; "typo" ] [ bookmark "overview" ] with
            | Ok _ -> failtest "a typo must not silently render fewer images"
            | Result.Error e ->
                Expect.stringContains e "'typo'" "names the pattern"
                Expect.stringContains e "overview" "lists the bookmarks there are"
        }

        test "file names are file-system safe and disambiguated" {
            let a, b, c = bookmark "rim / west", bookmark "Rim / West", bookmark "floor"
            match Bookmarks.fileNames [ a; b; c ] |> List.map snd with
            | [ fa; fb; fc ] ->
                Expect.equal fc "floor" "a unique name is kept"
                Expect.isTrue (fa.StartsWith "rim___west_" && fb.StartsWith "Rim___West_") "colliding names get the GUID's first block"
                Expect.notEqual (fa.ToLowerInvariant()) (fb.ToLowerInvariant()) "distinct on case-insensitive file systems"
            | files -> failtestf "expected three names, got %A" files
        }

        test "a job file's top level becomes one pass, with defaults" {
            match parse """{ "scene": "s/x.pro3d", "lodLevel": 2 }""" |> Result.bind JobFile.toJob with
            | Result.Error e -> failtest e
            | Ok job ->
                Expect.equal job.scene (Path.GetFullPath(Path.Combine(jobDir, "s", "x.pro3d"))) "relative to the job file"
                Expect.equal job.out (Path.GetFullPath(Path.Combine(jobDir, "s", "x-renders"))) "beside the scene"
                match job.passes with
                | [ p ] ->
                    Expect.equal p.bookmarks [ "*" ] "every bookmark"
                    Expect.equal p.settings { size = V2i(1920, 1080); lodLevel = Some 2 } "default size"
                    Expect.equal p.suffix "_lod2" "a fixed level is in the file name"
                | ps -> failtestf "expected one pass, got %d" ps.Length
        }

        test "passes inherit the top level, and the command line overrides it" {
            let file =
                parse """{
                    "scene": "x.pro3d", "bookmarks": "rim", "width": 640, "height": 480,
                    // comments and trailing commas are fine
                    "passes": [ { "lodLevel": 0 }, { "lodLevel": 3, "width": 320 }, { "suffix": "_viewer" }, ]
                }"""
            let cli = { JobFile.empty with height = Some 200; bookmarks = Some [ "*" ] }
            match file |> Result.map (JobFile.merge cli) |> Result.bind JobFile.toJob with
            | Result.Error e -> failtest e
            | Ok job ->
                let summary = job.passes |> List.map (fun p -> p.bookmarks, p.settings.size, p.settings.lodLevel, p.suffix)
                Expect.equal summary [
                    [ "*" ], V2i(640, 200), Some 0, "_lod0"
                    [ "*" ], V2i(320, 200), Some 3, "_lod3"
                    [ "*" ], V2i(640, 200), None,   "_viewer"
                ] "command line over file top level, pass over top level"
        }

        test "bad job files fail with the field named" {
            let error json =
                match parse json |> Result.bind JobFile.toJob with
                | Ok _ -> failtestf "accepted %s" json
                | Result.Error e -> e
            Expect.stringContains (error """{ "scene": "x", "lodlevel": 2 }""") "'lodlevel'" "a misspelt key"
            Expect.stringContains (error """{ "scene": "x", "width": "wide" }""") "'width' must be an integer" "a wrong type"
            Expect.stringContains (error """{ "scene": "x", "width": 1.5 }""") "'width' must be an integer" "a fraction"
            Expect.stringContains (error """{ "scene": "x", "lodLevel": -1 }""") "LoD level" "a negative level"
            Expect.stringContains (error """{ "scene": "x", "passes": [ { "hight": 1 } ] }""") "passes[0]" "where in the passes"
            Expect.stringContains (error """{ "bookmarks": "*" }""") "no scene" "no scene"
            Expect.stringContains (error """[ 1 ]""") "JSON object" "not an object"
            Expect.stringContains (error """{ "scene": """) "not valid JSON" "broken JSON"
        }

        test "two passes writing the same files are refused before rendering" {
            let job : RenderJob =
                { scene = "x"; out = Path.Combine(Path.GetTempPath(), "out")
                  passes = [ for _ in 1 .. 2 -> { bookmarks = [ "*" ]; settings = RenderSettings.defaults; suffix = "" } ] }
            match RenderJob.plan [ bookmark "a" ] job with
            | Ok _ -> failtest "the second pass would overwrite the first"
            | Result.Error e -> Expect.stringContains e "suffix" "says how to fix it"
        }
    ]

/// The Dimorphos projection scene with two bookmarks added, saved to a temporary file.
/// Built through the viewer's update function, the way a user prepares the bookmarks.
let private sceneWithBookmarks (template : string) (opc : string) =
    let dir = Path.Combine(Path.GetTempPath(), "pro3d-tests", "scene-render")
    let scene = TestUtils.Scenes.fromTemplate template opc (Path.Combine(dir, "bookmarks.pro3d"))
    let freshModel, update = Render.makeViewer ()
    let loaded = update (freshModel ()) (ViewerAction.LoadScene scene)
    match loaded.scene.surfacesModel.sgSurfaces |> HashMap.toSeq |> Seq.tryHead with
    | None -> failtest "the scene's OPC did not load"
    | Some (_, surface) ->
        let bb = surface.globalBB
        let c, up = bb.Center, bb.Center.Normalized
        let north = up.Cross(V3d.OOI).Cross(up).Normalized
        let size = bb.Size.Length
        let views =
            [
                "Top view",  CameraView.lookAt (c + up * 1.5 * size) c north
                "Side view", CameraView.lookAt (c + north * 1.5 * size) c up
            ]
        let bookmarks =
            views |> List.fold (fun groups (name, view) ->
                GroupsApp.addLeafToActiveGroup (Leaf.Bookmarks (Bookmark.init name view c NavigationMode.FreeFly)) false groups
            ) loaded.scene.bookmarks
        let saved = update { loaded with scene = { loaded.scene with bookmarks = bookmarks } } (ViewerAction.SaveAs scene)
        Expect.equal saved.scene.scenePath (Some scene) "the scene was saved"
        scene

let private renderTests =
    testList "rendering a scene's bookmarks" [

        test "bookmarks render as the viewer shows them, deterministically, at a fixed LoD on request" {
            let template = TestUtils.Roots.testDataDir None [ "HERA"; "Dimorphos_opc"; "AFC_2027-03-21" ]
            match template, TestUtils.Roots.dimorphosOpc None, Render.context.Value with
            | None, _, _ -> skiptest "no AFC_2027-03-21 scene: set PRO3D_TEST_DATA to a PRo3D.Resources.TestData checkout"
            | _, None, _ -> skiptest "no Dimorphos OPC: set PRO3D_TEST_DATA"
            | _, _, None -> skiptest "no OpenGL runtime in this environment"
            | Some templateDir, Some opc, Some (runtime, signature) ->

            Startup.init ()
            let scene = sceneWithBookmarks (Path.Combine(templateDir, "ProjectionTest.pro3d")) opc

            use renderer = new SceneRenderer(runtime, signature, scene)
            // the panel lists the newest bookmark first (GroupsApp prepends)
            Expect.equal (names renderer.Bookmarks) [ "Side view"; "Top view" ] "the saved bookmarks, in panel order"
            let top, side =
                match renderer.Bookmarks with
                | [ s; t ] -> t, s
                | bs -> failtestf "expected two bookmarks, got %A" (names bs)

            let viewer = { RenderSettings.defaults with size = V2i(640, 480) }
            let topImage = renderer.Render(top, viewer)
            Expect.equal topImage.Size (V2i(640, 480)) "the requested size"
            Expect.isGreaterThan (SurfaceEffectHarness.litFraction topImage) 0.02 "Dimorphos is in view"
            Expect.isLessThan (meanDifference topImage (renderer.Render(top, viewer))) 0.5
                "patches load synchronously: the same bookmark renders the same image twice"
            Expect.isGreaterThan (changedFraction topImage (renderer.Render(side, viewer))) 0.02 "the other bookmark is another view"

            let root   = renderer.Render(top, { viewer with lodLevel = Some 0 })
            let deeper = renderer.Render(top, { viewer with lodLevel = Some 2 })
            Expect.isGreaterThan (SurfaceEffectHarness.litFraction root) 0.02 "the root patch alone draws the body"
            Expect.isGreaterThan (changedFraction root deeper) 0.005 "a fixed level selects other patches"
            // the Dimorphos OPC has three levels (0..2): beyond the deepest, the leaves
            Expect.isLessThan (meanDifference deeper (renderer.Render(top, { viewer with lodLevel = Some 9 }))) 0.5
                "a level deeper than the hierarchy renders its leaves"
            Expect.isLessThan (meanDifference topImage (renderer.Render(top, viewer))) 0.5
                "back without a fixed level, the viewer's level of detail returns"

            // the batch: two passes, the tool's job layer
            let out = Path.Combine(Path.GetDirectoryName scene, "renders")
            if Directory.Exists out then Directory.Delete(out, true)
            let job : RenderJob =
                { scene = scene; out = out
                  passes = [ for lod in [ 0; 1 ] -> { bookmarks = [ "*" ]; settings = { size = V2i(320, 240); lodLevel = Some lod }; suffix = sprintf "_lod%d" lod } ] }
            match RenderJob.run ignore renderer job with
            | Result.Error e -> failtest e
            | Ok images ->
                Expect.equal (images |> List.map (fun i -> Path.GetFileName i.file))
                             [ "Side_view_lod0.png"; "Top_view_lod0.png"; "Side_view_lod1.png"; "Top_view_lod1.png" ] "one PNG per bookmark and pass"
                for i in images do
                    Expect.equal (PixImage.Load(i.file).Size) (V2i(320, 240)) (sprintf "%s has the pass's size" i.file)
                Expect.isTrue (File.Exists(Path.Combine(out, RenderJob.manifestName))) "the manifest is written"
        }
    ]

let tests () =
    testList "scene render (#871)" [ jobTests; renderTests ]
