/// Window layouts (docs/WindowLayouts.md): layout validation, the layout file format,
/// the AppData library, the sidecar beside scenes, and scene compatibility with
/// PRo3D <= 6.2, which requires a `dockConfig` pickle in every scene.
module PRo3D.Tests.WindowLayoutTests

open System
open System.IO
open System.Threading
open System.Collections.Concurrent

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI.Primitives.Golden
open FSharp.Data.Adaptive

open Chiron
open Expecto

open PRo3D
open PRo3D.Base
open PRo3D.Viewer

module private Fixture =

    let element (id : string) =
        { Id = id; Title = id; Closable = true; Header = Some Header.Top; Buttons = None
          MinSize = None; Size = Size.Weight 1; KeepAlive = true }

    let stack (ids : list<string>) =
        Layout.Stack { Header = Header.Top; Buttons = None; Size = Size.Weight 1; Content = ids |> List.map element }

    let row (content : list<Layout>) =
        Layout.RowOrColumn { IsRow = true; Size = Size.Weight 1; Content = content }

    let column (content : list<Layout>) =
        Layout.RowOrColumn { IsRow = false; Size = Size.Weight 1; Content = content }

    let window (root : Layout) = { Root = Some root; PopoutWindows = [] }

    /// A fresh directory as PRO3D_LAYOUT_DIR for the duration of `f`.
    let withLayoutDir (f : string -> 'a) =
        let dir = Path.Combine(Path.GetTempPath(), "pro3d-layout-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory dir |> ignore
        let previous = Environment.GetEnvironmentVariable "PRO3D_LAYOUT_DIR"
        Environment.SetEnvironmentVariable("PRO3D_LAYOUT_DIR", dir)
        try f dir
        finally
            Environment.SetEnvironmentVariable("PRO3D_LAYOUT_DIR", previous)
            try Directory.Delete(dir, true) with _ -> ()

    let ok (r : Result<'a, string>) (what : string) =
        match r with
        | Result.Ok v -> v
        | Result.Error e -> failtestf "%s failed: %s" what e

    let isError (r : Result<'a, string>) (what : string) =
        match r with
        | Result.Ok _ -> failtestf "%s should have failed" what
        | Result.Error _ -> ()

/// The viewer as the UI drives it, without a GL runtime.
module private Head =

    let make () =
        let cts       = new CancellationTokenSource()
        let mailbox   = MailboxProcessor.Start(Viewer.initMessageLoop cts, cts.Token)
        let sendQueue = new BlockingCollection<string>()
        let model =
            Viewer.initial mailbox StartupArgs.initArgs "" 1 "." ViewerLenses._animator "tests"
        let update (m : Model) (msg : ViewerAction) =
            ViewerApp.updateViewer
                (Unchecked.defaultof<IRuntime>) (Unchecked.defaultof<IFramebufferSignature>)
                sendQueue mailbox m msg
        model, update

    let layout (a : LayoutAction) = ViewerAction.LayoutMessage a

    let feedbackCount (m : Model) = m.scene.feedbackThreads.store |> HashMap.count

    let setVersion (m : Model) = m.layout.golden.SetLayout |> Option.map snd |> Option.defaultValue 0

/// What PRo3D <= 6.2 does with a scene's `dockConfig`: a required string field holding an
/// FsPickler JSON pickle of `DockConfig`. If this fails, old versions cannot open the scene.
module private OldReader =

    let dockConfig (sceneJson : string) : Result<Aardvark.UI.Primitives.DockConfig, string> =
        try
            let read : Json<string> = json { return! Json.read "dockConfig" }
            match read (Json.parse sceneJson) with
            | JsonResult.Value pickle, _ ->
                let pickler = MBrace.FsPickler.Json.FsPickler.CreateJsonSerializer(indent = true)
                Result.Ok (pickler.UnPickleOfString<Aardvark.UI.Primitives.DockConfig> pickle)
            | JsonResult.Error e, _ -> Result.Error e
        with e ->
            Result.Error e.Message

let private serializeScene (scene : Scene) =
    scene |> Json.serialize |> Json.formatWith JsonFormattingOptions.Pretty

let private sanitizeTests =
    testList "sanitize" [

        test "built-in layouts keep their arrangement and have an unclosable main view" {
            for d in DashboardModes.all do
                let s = LayoutOps.sanitize d.layout
                Expect.equal (LayoutOps.sanitize s) s (sprintf "%s: sanitize is idempotent" d.name)
                Expect.contains (LayoutOps.panelIds s) LayoutPanels.render (sprintf "%s: has the main view" d.name)
                let rec elements (l : Layout) =
                    match l with
                    | Layout.Element e -> [e]
                    | Layout.Stack s -> s.Content
                    | Layout.RowOrColumn rc -> rc.Content |> List.collect elements
                let all = s.Root |> Option.map elements |> Option.defaultValue []
                for e in all do
                    Expect.equal e.Closable (e.Id <> LayoutPanels.render) (sprintf "%s: closability of %s" d.name e.Id)
        }

        test "unknown and duplicate panels are dropped, empty containers disappear" {
            let l = Fixture.window (Fixture.row [ Fixture.stack ["render"; "surfaces"]; Fixture.stack ["minerva"; "surfaces"]; Fixture.stack ["corr_svg"] ])
            let s = LayoutOps.sanitize l
            Expect.equal (LayoutOps.panelIds s) ["render"; "surfaces"] "only the first surfaces remains"
            Expect.equal (LayoutOps.shape s) "r[s[render;surfaces;]]" "empty stacks removed"
        }

        test "a layout without the main view gets one, an empty one is the main view" {
            let noRender = LayoutOps.sanitize (Fixture.window (Fixture.column [ Fixture.stack ["surfaces"] ]))
            Expect.equal (LayoutOps.shape noRender) "r[s[render;]c[s[surfaces;]]]" "render added beside"
            let empty = LayoutOps.sanitize { Root = None; PopoutWindows = [] }
            Expect.equal (LayoutOps.shape empty) "s[render;]" "render only"
            let unknownOnly = LayoutOps.sanitize (Fixture.window (Fixture.stack ["nope"]))
            Expect.equal (LayoutOps.shape unknownOnly) "s[render;]" "render only"
        }

        test "titles, headers and sizes are normalized" {
            let e = { Fixture.element "surfaces" with Title = "evil"; Header = None; Size = Size.Percentage 5000 }
            let s = LayoutOps.sanitize (Fixture.window (Fixture.row [ Fixture.stack ["render"]; Layout.Stack { Header = Header.Top; Buttons = None; Size = Size.Weight -3; Content = [e] } ]))
            match s.Root with
            | Some (Layout.RowOrColumn { Content = [_; Layout.Stack st] }) ->
                Expect.equal st.Size (Size.Weight 1) "weight clamped"
                match st.Content with
                | [e] ->
                    Expect.equal e.Title "Surfaces" "title from the registry"
                    Expect.equal e.Header (Some Header.Top) "header restored"
                    Expect.equal e.Size (Size.Percentage 100) "percentage clamped"
                | _ -> failtest "one element expected"
            | other -> failtestf "unexpected root %A" other
        }

        test "popouts are sanitized and positions can be stripped" {
            let popout = { Root = Fixture.stack ["gis"; "render"]; Position = Some (V2i(4000, -900)); Size = Some (V2i(800, 600)) }
            let l = { Root = Some (Fixture.stack ["surfaces"]); PopoutWindows = [popout; { popout with Root = Fixture.stack ["nope"] }] }
            let s = LayoutOps.sanitize l
            Expect.equal (List.length s.PopoutWindows) 1 "empty popout dropped"
            Expect.equal (LayoutOps.panelIds s) ["surfaces"; "gis"; "render"] "render found in the popout"
            let stripped = LayoutOps.withoutPopoutPositions s
            Expect.equal (stripped.PopoutWindows |> List.map (fun p -> p.Position, p.Size)) [None, Some (V2i(800, 600))] "size kept, position gone"
            Expect.equal (LayoutOps.shape stripped) (LayoutOps.shape s) "same shape"
        }

        testProperty "sanitize is idempotent and always contains the main view exactly once" <| fun (picks : list<list<int>>) (rows : bool) ->
            let ids = (LayoutPanels.all |> List.map (fun p -> p.id)) @ ["unknown"; "minerva"]
            let pick (i : int) = ids.[abs (i % List.length ids)]
            let stacks = picks |> List.truncate 6 |> List.map (List.truncate 6 >> List.map pick >> Fixture.stack)
            let root = if rows then Fixture.row stacks else Fixture.column stacks
            let s = LayoutOps.sanitize (Fixture.window root)
            let ids = LayoutOps.panelIds s
            LayoutOps.sanitize s = s
            && (ids |> List.filter ((=) LayoutPanels.render) |> List.length) = 1
            && List.length ids = (ids |> List.distinct |> List.length)
    ]

let private panelTests =
    testList "reopen panels" [

        test "closed panels are the known ones not in the layout" {
            let l = LayoutOps.sanitize DashboardModes.renderOnly.layout
            Expect.equal (LayoutOps.closedPanels l |> List.length) (List.length LayoutPanels.all - 1) "everything but render"
            Expect.isEmpty (LayoutOps.closedPanels (LayoutOps.sanitize (Fixture.window (Fixture.stack (LayoutPanels.all |> List.map (fun p -> p.id)))))) "nothing closed"
        }

        test "a panel is added to the stack with most tabs that does not hold the main view" {
            let l = LayoutOps.sanitize (Fixture.window (Fixture.row [ Fixture.stack ["render"; "bookmarks"; "config"]; Fixture.column [ Fixture.stack ["surfaces"]; Fixture.stack ["annotations"; "scalebars"] ] ]))
            let added = LayoutOps.addPanel "gis" l
            Expect.equal (LayoutOps.shape added) "r[s[render;bookmarks;config;]c[s[surfaces;]s[annotations;scalebars;gis;]]]" "added to the larger side stack"
        }

        test "next to a lone main view the panel gets its own stack" {
            let added = LayoutOps.addPanel "gis" (LayoutOps.sanitize DashboardModes.renderOnly.layout)
            Expect.equal (LayoutOps.shape added) "r[s[render;]s[gis;]]" "new stack beside"
        }

        test "unknown or present panels leave the layout alone" {
            let l = LayoutOps.sanitize DashboardModes.m2020.layout
            Expect.equal (LayoutOps.addPanel "nope" l) l "unknown"
            Expect.equal (LayoutOps.addPanel "surfaces" l) l "already there"
        }
    ]

let private fileTests =
    testList "layout file" [

        test "a layout round-trips through the file format" {
            let popout = { Root = Fixture.stack ["gis"]; Position = Some (V2i(10, 20)); Size = Some (V2i(640, 480)) }
            let layout = LayoutOps.sanitize { (LayoutOps.sanitize DashboardModes.m2020.layout) with PopoutWindows = [popout] }
            let text = LayoutFile.serialize { name = "mine"; layout = layout }
            let parsed = Fixture.ok (LayoutFile.tryParse text) "parse"
            Expect.equal parsed.name "mine" "name"
            Expect.equal parsed.layout layout "layout"
        }

        test "parsing never throws" {
            let valid = LayoutFile.serialize { name = "x"; layout = LayoutOps.sanitize DashboardModes.gis.layout }
            let inputs = [
                ""; " "; "null"; "[]"; "{}"; "42"; "\"text\""; "{\"format\":\"pro3d-layout\"}"
                "{\"format\":\"pro3d-layout\",\"version\":1,\"layout\":[]}"
                "{\"format\":\"pro3d-layout\",\"version\":1,\"layout\":{\"root\":{\"type\":\"banana\"}}}"
                "{\"format\":\"pro3d-layout\",\"version\":1,\"layout\":{\"root\":{\"type\":\"stack\",\"content\":[{\"type\":\"row\"}]}}}"
                "{\"format\":\"pro3d-layout\",\"version\":1,\"layout\":{\"root\":{\"type\":\"component\"}}}"
                "{\"format\":\"pro3d-layout\",\"version\":\"1\",\"layout\":{}}"
                "{\"format\":\"pro3d-layout\",\"version\":99999999999,\"layout\":{}}"
            ]
            // every prefix of a valid file, as a crash mid-write would leave it
            let prefixes = [ for i in 0 .. 7 .. valid.Length - 1 -> valid.Substring(0, i) ]
            for input in inputs @ prefixes do
                match LayoutFile.tryParse input with
                | Result.Ok f -> Expect.contains (LayoutOps.panelIds f.layout) LayoutPanels.render "a parsed layout is sanitized"
                | Result.Error _ -> ()
        }

        test "a file from a newer PRo3D is refused, not misread" {
            let text = (LayoutFile.serialize { name = "x"; layout = LayoutOps.sanitize DashboardModes.gis.layout }).Replace("\"version\": 1", "\"version\": 2")
            Fixture.isError (LayoutFile.tryParse text) "newer version"
        }

        test "the browser's serialized layout is accepted and sanitized" {
            let browser = GoldenLayout.Json.serialize LayoutConfig.Default (Fixture.window (Fixture.row [ Fixture.stack ["render"]; Fixture.stack ["minerva"; "bookmarks"] ]))
            let l = Fixture.ok (LayoutFile.tryParseGolden browser) "browser layout"
            Expect.equal (LayoutOps.shape l) "r[s[render;]s[bookmarks;]]" "sanitized"
            Fixture.isError (LayoutFile.tryParseGolden "{\"root\":{\"type\":\"what\"}}") "garbage"
        }

        test "a failed write leaves neither an exception nor a temp file" {
            Fixture.withLayoutDir (fun dir ->
                let target = Path.Combine(dir, "blocked.json")
                Directory.CreateDirectory target |> ignore
                Fixture.isError (LayoutFile.tryWriteAtomic target "{}") "write onto a directory"
                Expect.isFalse (File.Exists (target + ".tmp")) "temp file cleaned up"
            )
        }
    ]

let private libraryTests =
    testList "library" [

        test "save, list, rename and delete" {
            Fixture.withLayoutDir (fun dir ->
                let layout = LayoutOps.sanitize DashboardModes.gis.layout
                Fixture.ok (LayoutLibrary.trySave dir { name = "Zeta"; layout = layout }) "save Zeta"
                Fixture.ok (LayoutLibrary.trySave dir { name = "alpha: a/b"; layout = layout }) "save with invalid file name chars"
                Expect.equal (LayoutLibrary.list dir |> List.map (fun f -> f.name)) ["alpha: a/b"; "Zeta"] "sorted display names"

                Fixture.ok (LayoutLibrary.tryRename dir "Zeta" "Beta") "rename"
                Fixture.isError (LayoutLibrary.tryRename dir "Beta" "alpha: a/b") "rename onto an existing name"
                Fixture.isError (LayoutLibrary.trySave dir { name = "   "; layout = layout }) "empty name"
                Expect.equal (LayoutLibrary.list dir |> List.map (fun f -> f.name)) ["alpha: a/b"; "Beta"] "renamed"

                Fixture.ok (LayoutLibrary.tryDelete dir "Beta") "delete"
                Expect.equal (LayoutLibrary.list dir |> List.map (fun f -> f.name)) ["alpha: a/b"] "deleted"
            )
        }

        test "an unreadable library entry is listed, and opening it tells the user why it fails" {
            Fixture.withLayoutDir (fun dir ->
                Directory.CreateDirectory(LayoutLibrary.libraryDir dir) |> ignore
                File.WriteAllText(Path.Combine(LayoutLibrary.libraryDir dir, "broken.json"), "{ not json")
                Fixture.ok (LayoutLibrary.trySave dir { name = "fine"; layout = LayoutOps.sanitize DashboardModes.core.layout }) "save"
                Expect.equal (LayoutLibrary.list dir |> List.map (fun e -> e.name)) ["broken"; "fine"] "both listed"

                let m = LayoutApp.initial dir
                Expect.equal m.library ["broken"; "fine"] "in the menu"
                let m', feedback = LayoutApp.update dir (LayoutAction.ApplyLibrary "broken") m
                Expect.equal m'.current m.current "layout unchanged"
                match feedback with
                | [text] -> Expect.stringContains text "cannot be read" "feedback"
                | other -> failtestf "expected one feedback message, got %A" other

                Fixture.isError (LayoutLibrary.tryRename dir "broken" "renamed") "a broken entry cannot be renamed"
                Fixture.ok (LayoutLibrary.tryDelete dir "broken") "but deleted"
                Expect.equal (LayoutLibrary.list dir |> List.map (fun e -> e.name)) ["fine"] "gone"
            )
        }

        test "a corrupt current layout falls back to the default, is kept aside and reported" {
            Fixture.withLayoutDir (fun dir ->
                File.WriteAllText(LayoutLibrary.currentPath dir, "{\"format\":\"pro3d-layout\",\"version\":1,\"layo")
                let m = LayoutApp.initial dir
                Expect.equal m.activeName DashboardModes.defaultDashboard.name "default layout"
                Expect.isTrue (File.Exists (LayoutLibrary.currentPath dir + ".corrupt")) "kept aside"
                Expect.isFalse (File.Exists (LayoutLibrary.currentPath dir)) "moved away"
                match m.dialog with
                | LayoutDialog.Notice (message, LayoutDialog.None) -> Expect.stringContains message "could not be read" "the user is told at start"
                | other -> failtestf "expected a notice, got %A" other
                Expect.equal (LayoutApp.initial dir).dialog LayoutDialog.None "and only once"

                // a scene layout question opened meanwhile waits behind the notice
                let scene = Path.Combine(dir, "s.pro3d")
                Fixture.ok (SceneLayoutSidecar.tryWrite scene (LayoutOps.sanitize DashboardModes.core.layout)) "sidecar"
                let m, _ = LayoutApp.sceneOpened dir scene m
                let m, _ = LayoutApp.update dir LayoutAction.CloseDialog m
                match m.dialog with
                | LayoutDialog.SceneLayout _ -> ()
                | other -> failtestf "expected the scene layout question after the notice, got %A" other
            )
        }

        test "the default layout is M2020 with the GIS view" {
            Fixture.withLayoutDir (fun dir ->
                let m = LayoutApp.initial dir
                Expect.equal m.activeName "M2020" "M2020"
                Expect.contains (LayoutOps.panelIds m.current) "gis" "GIS view included"
                Expect.equal m.dialog LayoutDialog.None "nothing to report"
            )
        }

        test "the current layout is persisted and restored" {
            Fixture.withLayoutDir (fun dir ->
                let m = LayoutApp.initial dir
                let m, _ = LayoutApp.update dir (LayoutAction.ApplyDashboard DashboardModes.gis.name) m
                // written in the background, after bursts settle
                let deadline = DateTime.Now.AddSeconds 10.0
                while not (File.Exists (LayoutLibrary.currentPath dir)) && DateTime.Now < deadline do
                    Thread.Sleep 50
                let restored = LayoutApp.initial dir
                Expect.equal restored.activeName DashboardModes.gis.name "name restored"
                Expect.equal (LayoutOps.shape restored.current) (LayoutOps.shape m.current) "layout restored"
                Expect.equal restored.golden.DefaultLayout restored.current "boots into it"
            )
        }
    ]

let private appTests =
    testList "layout app" [

        test "garbage from the browser leaves the model alone" {
            Fixture.withLayoutDir (fun dir ->
                let m = LayoutApp.initial dir
                let m', feedback = LayoutApp.update dir (LayoutAction.Changed "{\"root\":") m
                Expect.equal m' m "unchanged"
                Expect.isEmpty feedback "no feedback"
            )
        }

        test "a layout change from the browser becomes the boot layout without being pushed back" {
            Fixture.withLayoutDir (fun dir ->
                let m = LayoutApp.initial dir
                let json = GoldenLayout.Json.serialize LayoutConfig.Default (LayoutOps.sanitize DashboardModes.renderOnly.layout)
                let m', _ = LayoutApp.update dir (LayoutAction.Changed json) m
                Expect.equal (LayoutOps.shape m'.current) "s[render;]" "current"
                Expect.equal m'.golden.DefaultLayout m'.current "reload boots into it"
                Expect.equal m'.golden.SetLayout m.golden.SetLayout "nothing pending: nothing sent"

                // after an applied layout, the pending message is replayed to clients that
                // connect later; once the browser shows the pushed arrangement, it must carry
                // the browser's layout, under the same version
                let serialized (l : WindowLayout) = GoldenLayout.Json.serialize LayoutConfig.Default l
                let core = LayoutOps.sanitize DashboardModes.core.layout
                let applied, _ = LayoutApp.update dir (LayoutAction.ApplyDashboard DashboardModes.core.name) m
                let pushed = applied.golden.SetLayout

                // an event from before the push arrives late: the push must not be replaced
                let stale, _ = LayoutApp.update dir (LayoutAction.Changed json) applied
                Expect.equal stale.golden.SetLayout pushed "a stale event leaves the push alone"
                Expect.isFalse stale.pushConfirmed "not confirmed"

                let shown, _ = LayoutApp.update dir (LayoutAction.Changed (serialized core)) stale
                Expect.isTrue shown.pushConfirmed "the browser shows the push"
                let changed, _ = LayoutApp.update dir (LayoutAction.Changed json) shown
                match pushed, changed.golden.SetLayout with
                | Some (_, v0), Some (layout, v1) ->
                    Expect.equal v1 v0 "connected clients do not get it again"
                    Expect.equal layout changed.current "a reloaded page gets the current layout"
                    Expect.equal (LayoutOps.shape layout) "s[render;]" "the user's change"
                | other -> failtestf "unexpected SetLayout %A" other
            )
        }

        test "every applied layout is pushed with a new version" {
            Fixture.withLayoutDir (fun dir ->
                let m = LayoutApp.initial dir
                let m1, _ = LayoutApp.update dir (LayoutAction.ApplyDashboard DashboardModes.gis.name) m
                let m2, _ = LayoutApp.update dir (LayoutAction.ApplyDashboard DashboardModes.gis.name) m1
                let m3, _ = LayoutApp.update dir (LayoutAction.ReopenPanel "comparison") m2
                let version (m : LayoutModel) = m.golden.SetLayout |> Option.map snd |> Option.defaultValue 0
                Expect.equal [version m1; version m2; version m3] [1; 2; 3] "versions"
                Expect.contains (LayoutOps.panelIds m3.current) "comparison" "panel reopened"
            )
        }

        test "save as, load and rename through the dialogs" {
            Fixture.withLayoutDir (fun dir ->
                let run (m : LayoutModel) (actions : list<LayoutAction>) =
                    actions |> List.fold (fun m a -> LayoutApp.update dir a m |> fst) m
                let m = LayoutApp.initial dir
                let m = run m [ LayoutAction.ApplyDashboard DashboardModes.provenance.name
                                LayoutAction.OpenDialog LayoutDialog.SaveAs
                                LayoutAction.SetNameInput "  My Layout "
                                LayoutAction.SaveCurrentAs ]
                Expect.equal m.library ["My Layout"] "saved"
                Expect.equal m.dialog LayoutDialog.None "closed"
                Expect.equal m.activeName "My Layout" "active"

                let m = run m [ LayoutAction.ApplyDashboard DashboardModes.gis.name; LayoutAction.ApplyLibrary "My Layout" ]
                Expect.equal (LayoutOps.shape m.current) (LayoutOps.shape (LayoutOps.sanitize DashboardModes.provenance.layout)) "loaded"

                let m = run m [ LayoutAction.OpenDialog LayoutDialog.Manage
                                LayoutAction.OpenDialog (LayoutDialog.Rename "My Layout")
                                LayoutAction.SetNameInput "Renamed"
                                LayoutAction.Rename ]
                Expect.equal m.library ["Renamed"] "renamed"
                Expect.equal m.activeName "Renamed" "active follows the rename"

                let m, feedback = LayoutApp.update dir (LayoutAction.ApplyLibrary "gone") m
                Expect.isNonEmpty feedback "missing layout reported"
                Expect.equal m.activeName "Renamed" "nothing applied"
            )
        }
    ]

let private sidecarTests =
    testList "scene sidecar" [

        test "the sidecar drops popout positions and reads back" {
            Fixture.withLayoutDir (fun dir ->
                let scene = Path.Combine(dir, "a.pro3d")
                let popout = { Root = Fixture.stack ["gis"]; Position = Some (V2i(10, 20)); Size = Some (V2i(640, 480)) }
                let layout = LayoutOps.sanitize { (LayoutOps.sanitize DashboardModes.m2020.layout) with PopoutWindows = [popout] }
                Fixture.ok (SceneLayoutSidecar.tryWrite scene layout) "write"
                Expect.equal (SceneLayoutSidecar.path scene) (scene + ".layout") "file name"
                match Fixture.ok (SceneLayoutSidecar.tryRead scene) "read" with
                | Some f ->
                    Expect.equal f.name "a" "scene name"
                    Expect.equal f.layout (LayoutOps.withoutPopoutPositions layout) "layout without positions"
                | None -> failtest "sidecar missing"
                Expect.equal (Fixture.ok (SceneLayoutSidecar.tryRead (Path.Combine(dir, "none.pro3d"))) "missing") None "no sidecar"
            )
        }

        test "opening a scene offers its layout only when the arrangement differs" {
            Fixture.withLayoutDir (fun dir ->
                let scene = Path.Combine(dir, "shared.pro3d")
                let m = LayoutApp.initial dir

                Fixture.ok (SceneLayoutSidecar.tryWrite scene m.current) "write"
                let same, _ = LayoutApp.sceneOpened dir scene m
                Expect.equal same.dialog LayoutDialog.None "same arrangement: nothing to ask"

                let other = LayoutOps.sanitize DashboardModes.core.layout
                Fixture.ok (SceneLayoutSidecar.tryWrite scene other) "write"
                let offered, _ = LayoutApp.sceneOpened dir scene m
                match offered.dialog with
                | LayoutDialog.SceneLayout p ->
                    Expect.equal p.sceneName "shared" "name"
                    Expect.isFalse p.alreadyInLibrary "not in the library"
                    Expect.isFalse p.importIntoLibrary "import is opt-in"
                    Expect.isTrue p.apply "apply preselected"
                | other -> failtestf "expected the scene layout dialog, got %A" other

                File.WriteAllText(SceneLayoutSidecar.path scene, "{ broken")
                let broken, feedback = LayoutApp.sceneOpened dir scene m
                Expect.equal broken.dialog LayoutDialog.None "no dialog for a broken sidecar"
                Expect.isNonEmpty feedback "but the user is told"
            )
        }

        test "confirming imports under a free name and applies" {
            Fixture.withLayoutDir (fun dir ->
                let scene = Path.Combine(dir, "shared.pro3d")
                let other = LayoutOps.sanitize DashboardModes.core.layout
                Fixture.ok (SceneLayoutSidecar.tryWrite scene other) "write"
                Fixture.ok (LayoutLibrary.trySave dir { name = "shared"; layout = LayoutOps.sanitize DashboardModes.provenance.layout }) "name taken"

                let m = LayoutApp.initial dir
                let m, _ = LayoutApp.sceneOpened dir scene m
                let m = [ LayoutAction.SetImportSceneLayout true; LayoutAction.ConfirmSceneLayout ] |> List.fold (fun m a -> LayoutApp.update dir a m |> fst) m
                Expect.equal m.library ["shared"; "shared (2)"] "imported under a free name"
                Expect.equal (LayoutOps.shape m.current) (LayoutOps.shape other) "applied"
                Expect.equal m.activeName "shared (2)" "active"
                Expect.equal m.dialog LayoutDialog.None "closed"

                // now in the library: opening the scene again does not offer an import
                let m2 = LayoutApp.initial dir
                let m2, _ = LayoutApp.sceneOpened dir scene { m2 with current = LayoutOps.sanitize DashboardModes.renderOnly.layout }
                match m2.dialog with
                | LayoutDialog.SceneLayout p -> Expect.isTrue p.alreadyInLibrary "recognized"
                | other -> failtestf "expected the dialog, got %A" other
            )
        }

        test "ignoring changes nothing" {
            Fixture.withLayoutDir (fun dir ->
                let scene = Path.Combine(dir, "s.pro3d")
                Fixture.ok (SceneLayoutSidecar.tryWrite scene (LayoutOps.sanitize DashboardModes.core.layout)) "write"
                let m = LayoutApp.initial dir
                let offered, _ = LayoutApp.sceneOpened dir scene m
                let m', _ = LayoutApp.update dir LayoutAction.CloseDialog offered
                Expect.equal m'.current m.current "layout unchanged"
                Expect.isEmpty m'.library "nothing imported"
            )
        }
    ]

let private sceneTests =
    testList "scene compatibility" [

        test "a new scene carries a dockConfig that PRo3D 6.2 can read" {
            Fixture.withLayoutDir (fun _ ->
                let m, _ = Head.make ()
                let text = serializeScene m.scene
                match OldReader.dockConfig text with
                | Result.Ok cfg -> Expect.stringContains (sprintf "%A" cfg.content) "render" "the default layout"
                | Result.Error e -> failtestf "PRo3D 6.2 could not read the scene: %s" e
            )
        }

        test "the dockConfig of a loaded scene is written back unchanged" {
            Fixture.withLayoutDir (fun _ ->
                let m, _ = Head.make ()
                let custom = LegacyDockConfig.pickled().Replace("0.7", "0.6123")
                let text = serializeScene { m.scene with legacyDockConfig = Some custom }
                let loaded : Scene = text |> Json.parse |> Json.deserialize
                Expect.equal loaded.legacyDockConfig (Some custom) "read verbatim"
                let again = serializeScene loaded
                Expect.equal (again |> Json.parse |> Json.deserialize : Scene).legacyDockConfig (Some custom) "written verbatim"
                Expect.isOk (OldReader.dockConfig again) "still readable by 6.2"
            )
        }

        test "a scene without dockConfig loads and gains the default" {
            Fixture.withLayoutDir (fun _ ->
                let m, _ = Head.make ()
                let withoutField =
                    match serializeScene m.scene |> Json.parse with
                    | Json.Object props -> Json.Object (Map.remove "dockConfig" props) |> Json.format
                    | other -> failtestf "scene is no object: %A" other
                let loaded : Scene = withoutField |> Json.parse |> Json.deserialize
                Expect.isNone loaded.legacyDockConfig "nothing read"
                Expect.isOk (OldReader.dockConfig (serializeScene loaded)) "default written"
            )
        }

        test "a scene saved by PRo3D 6.2 keeps its customised dockConfig" {
            let fixture = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "tools", "profileDrawing", "test.pro3d")
            if not (File.Exists fixture) then skiptest "tools/profileDrawing/test.pro3d not found"
            Fixture.withLayoutDir (fun _ ->
                let text = File.ReadAllText fixture
                let original = match OldReader.dockConfig text with | Result.Ok c -> c | Result.Error e -> failtestf "fixture: %s" e
                let loaded : Scene = text |> Json.parse |> Json.deserialize
                match OldReader.dockConfig (serializeScene loaded) with
                | Result.Ok roundTripped -> Expect.equal roundTripped original "same layout for 6.2"
                | Result.Error e -> failtestf "6.2 cannot read the re-saved scene: %s" e
            )
        }
    ]

let private viewerTests =
    testList "viewer" [

        test "saving a scene writes the layout beside it" {
            Fixture.withLayoutDir (fun dir ->
                let m, update = Head.make ()
                let path = Path.Combine(dir, "scenes", "saved.pro3d")
                Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
                let saved = update m (ViewerAction.SaveAs path)
                Expect.isTrue (File.Exists path) "scene written"
                match Fixture.ok (SceneLayoutSidecar.tryRead path) "sidecar" with
                | Some f -> Expect.equal f.layout (LayoutOps.withoutPopoutPositions saved.layout.current) "current layout"
                | None -> failtest "no sidecar"
            )
        }

        test "a sidecar that cannot be written does not fail the save" {
            Fixture.withLayoutDir (fun dir ->
                let m, update = Head.make ()
                let path = Path.Combine(dir, "blocked.pro3d")
                // a directory where the sidecar should go
                Directory.CreateDirectory(SceneLayoutSidecar.path path) |> ignore
                let before = Head.feedbackCount m
                let saved = update m (ViewerAction.SaveAs path)
                Expect.isTrue (File.Exists path) "scene written anyway"
                Expect.equal saved.scene.scenePath (Some path) "scene path set"
                Expect.isOk (OldReader.dockConfig (File.ReadAllText path)) "a complete scene"
                Expect.isGreaterThan (Head.feedbackCount saved) before "user told"
            )
        }

        test "a new scene keeps the layout and its browser channel version" {
            Fixture.withLayoutDir (fun _ ->
                let m, update = Head.make ()
                let m = update m (Head.layout (LayoutAction.ApplyDashboard DashboardModes.gis.name))
                let v = Head.setVersion m
                let fresh = update m ViewerAction.NewScene
                Expect.equal fresh.layout m.layout "layout kept"
                let next = update fresh (Head.layout (LayoutAction.ApplyDashboard DashboardModes.core.name))
                Expect.equal (Head.setVersion next) (v + 1) "the next layout is not swallowed by a version reset"
            )
        }
    ]

let tests () =
    testList "Window layouts" [
        sanitizeTests
        panelTests
        fileTests
        libraryTests
        appTests
        sidecarTests
        sceneTests
        viewerTests
    ]
