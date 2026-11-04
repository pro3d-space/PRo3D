namespace PRo3D.SceneRender

open System
open System.Collections.Concurrent
open System.IO
open System.Text.RegularExpressions
open System.Threading

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.SceneGraph   // runtime.CompileRender for an ISg; before Aardvark.UI so its Sg wins
open Aardvark.UI
open Aardvark.GeoSpatial.Opc.Load   // runtime.CreateLoadRunner

open OpcViewer.Base
open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Viewer

/// How to render one image.
type RenderSettings =
    {
        /// Output size in pixels. The scene's field of view is kept, the aspect follows
        /// the size -- what the viewer shows in a window of that size.
        size     : V2i
        /// `Some n` refines every OPC hierarchy to exactly level n (0 = root patch),
        /// replacing the viewer's distance-based level of detail (see LodOverride).
        lodLevel : Option<int>
    }

module RenderSettings =

    let defaults = { size = V2i(1920, 1080); lodLevel = None }

/// Plain (camera) bookmarks of a scene: listing and selecting them.
module Bookmarks =

    /// The scene's bookmarks in the order the bookmark panel shows them: depth-first
    /// through the groups, each group's own bookmarks before its subgroups.
    let ofGroups (groups : GroupsModel) : list<Bookmark> =
        let rec walk (node : Node) : list<Bookmark> =
            let own =
                node.leaves
                |> IndexList.toList
                |> List.choose (fun id ->
                    match HashMap.tryFind id groups.flat with
                    | Some (Leaf.Bookmarks b) -> Some b
                    | _ -> None)
            let nested = node.subNodes |> IndexList.toList |> List.collect walk
            own @ nested
        walk groups.rootGroup

    /// `*` and `?` match any run of characters and any one character; everything else
    /// is literal. Case-sensitive, like the names in the bookmark panel.
    let private wildcard (pattern : string) : Regex =
        let body = Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".")
        Regex("^" + body + "$")

    /// The bookmarks `patterns` select, in scene order and each once. A pattern is a
    /// bookmark name (with `*` / `?` wildcards; `*` alone is every bookmark) or a
    /// bookmark GUID. A pattern that selects nothing is an error: a typo must not
    /// silently render fewer images.
    let select (patterns : list<string>) (bookmarks : list<Bookmark>) : Result<list<Bookmark>, string> =
        let matches (pattern : string) (b : Bookmark) =
            match Guid.TryParse pattern with
            | true, id -> b.key = id
            | false, _ -> (wildcard pattern).IsMatch b.name
        let unmatched =
            patterns |> List.filter (fun p -> bookmarks |> List.exists (matches p) |> not)
        match patterns, unmatched with
        | [], _ -> Result.Error "no bookmarks requested"
        | _, [] -> Ok (bookmarks |> List.filter (fun b -> patterns |> List.exists (fun p -> matches p b)))
        | _, missing ->
            let available =
                match bookmarks with
                | [] -> "the scene has no bookmarks"
                | bs -> bs |> List.map (fun b -> sprintf "  %s  (%O)" b.name b.key) |> String.concat Environment.NewLine |> sprintf "available:%s%s" Environment.NewLine
            Result.Error (sprintf "no bookmark matches %s%s%s"
                            (missing |> List.map (sprintf "'%s'") |> String.concat ", ") Environment.NewLine available)

    /// A file name for each bookmark: its name with characters a file system rejects
    /// replaced, and the GUID's first block appended where two would collide.
    let fileNames (bookmarks : list<Bookmark>) : list<Bookmark * string> =
        let invalid = Path.GetInvalidFileNameChars() |> Set.ofArray
        let clean (name : string) =
            let s = name |> String.map (fun c -> if invalid.Contains c || Char.IsWhiteSpace c then '_' else c)
            if String.IsNullOrWhiteSpace s then "bookmark" else s
        let counts = bookmarks |> List.countBy (fun b -> (clean b.name).ToLowerInvariant()) |> Map.ofList
        bookmarks
        |> List.map (fun b ->
            let c = clean b.name
            match Map.tryFind (c.ToLowerInvariant()) counts with
            | Some n when n > 1 -> b, sprintf "%s_%s" c (b.key.ToString("N").Substring(0, 8))
            | _ -> b, c)

/// Process-wide setup a headless host needs before it opens a scene. The GL runtime
/// itself is the host's: create it (OpenGlApplication) on the thread that will render,
/// after Aardvark.Init().
module Host =

    let private initialized =
        lazy (
            Serialization.init()
            Serialization.registry.RegisterFactory (fun _ -> KdTrees.level0KdTreePickler)
            Serialization.registry.RegisterFactory (fun _ -> PRo3D.Core.Surface.Init.incorePickler)
            Aardvark.UnpackNativeDependencies(typeof<PRo3D.Extensions.FSharp.CooTransformation.RelState>.Assembly)
            let appData = Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ApplicationData, "Pro3D")
            Directory.CreateDirectory appData |> ignore
            CooTransformation.initCooTrafo None appData
        )

    /// Serialization picklers and the coordinate transformation library. Idempotent.
    let init () = initialized.Force()

    /// Scene graphs load OPC patches through the process's one load runner (see
    /// Sg.loadRunnerFor). Publishes one made on this thread unless a host already did.
    let ensureLoadRunner (runtime : IRuntime) =
        match PRo3D.Core.Surface.Sg.hackRunner with
        | Some _ -> ()
        | None -> PRo3D.Core.Surface.Sg.hackRunner <- Some (runtime.CreateLoadRunner 1)

/// A `.pro3d` scene opened headlessly, rendered as the viewer renders it (surfaces,
/// annotations, overlays: the snapshot scene graph, SnapshotSg.viewRenderView) from
/// its bookmarks.
///
/// The scene is loaded and driven through the viewer's own update function; a bookmark
/// puts the camera where clicking it in the bookmark panel ends up. OPC patches load
/// synchronously, so every image is complete when Render returns -- no settle time.
///
/// Not thread-safe; render from the thread that owns the GL runtime.
type SceneRenderer(runtime : IRuntime, signature : IFramebufferSignature, scenePath : string) =

    do Host.init ()
    do Host.ensureLoadRunner runtime

    let viewportId = "scene-render"

    let sendQueue = new BlockingCollection<string>()
    let cts       = new CancellationTokenSource()
    let mailbox   = MailboxProcessor.Start(Viewer.initMessageLoop cts, cts.Token)

    let update (m : Model) (msg : ViewerAction) =
        ViewerApp.updateViewer runtime signature sendQueue mailbox m msg

    let loaded =
        let path = Path.GetFullPath scenePath
        if not (File.Exists path) then
            raise (FileNotFoundException(sprintf "no scene at %s" path, path))
        let fresh =
            Viewer.initial mailbox StartupArgs.initArgs "" signature.Samples (Path.GetTempPath())
                           ViewerLenses._animator "scene-render"
        // The patch nodes capture the loading mode when the scene graph is built: load
        // synchronously, like PRo3D.Snapshots, so a single frame is a complete image.
        let previous = PRo3D.Core.Surface.Sg.useAsyncLoading
        PRo3D.Core.Surface.Sg.useAsyncLoading <- false
        let m =
            try update fresh (ViewerAction.LoadScene path)
            finally PRo3D.Core.Surface.Sg.useAsyncLoading <- previous
        // LoadScene logs a failure and hands back the model it was given
        match m.scene.scenePath with
        | Some _ -> m
        | None -> failwithf "could not load scene %s (see the log)" path

    let mutable current = loaded

    let adaptive     = AdaptiveModel.Create loaded
    let viewportSize = cval RenderSettings.defaults.size
    let sg           = SnapshotSg.viewRenderView runtime viewportId viewportSize false adaptive

    let task  = runtime.CompileRender(signature, sg)
    let clear = runtime.CompileClear(signature, clear { color C4f.Black; depth 1.0; stencil 0 })

    /// Offscreen targets of the last size rendered; recreated when the size changes.
    let mutable target : Option<V2i * IBackendTexture * IRenderbuffer * IBackendTexture * IFramebuffer> = None

    let disposeTarget () =
        match target with
        | Some (_, color, depth, resolved, fbo) ->
            fbo.Dispose(); resolved.Dispose(); depth.Dispose(); color.Dispose()
            target <- None
        | None -> ()

    let targetFor (size : V2i) =
        match target with
        | Some (s, color, _, resolved, fbo) when s = size -> color, resolved, fbo
        | _ ->
            disposeTarget ()
            let color    = runtime.CreateTexture2D(size, TextureFormat.Rgba8, 1, signature.Samples)
            let depth    = runtime.CreateRenderbuffer(size, TextureFormat.Depth24Stencil8, signature.Samples)
            let resolved = runtime.CreateTexture2D(size, TextureFormat.Rgba8)
            let fbo =
                runtime.CreateFramebuffer(signature, [
                    DefaultSemantic.Colors,       color.GetOutputView()
                    DefaultSemantic.DepthStencil, depth :> IFramebufferOutput
                ])
            target <- Some (size, color, depth, resolved, fbo)
            color, resolved, fbo

    /// Opens the scene with a signature like PRo3D.Snapshots' (RGBA8, depth-stencil, 8x MSAA).
    new(runtime : IRuntime, scenePath : string) =
        new SceneRenderer(runtime, PRo3D.SimulatedViews.SnapshotFramebuffer.createSignature runtime, scenePath)

    member x.ScenePath = Path.GetFullPath scenePath

    /// The scene's plain bookmarks, in bookmark-panel order.
    member x.Bookmarks : list<Bookmark> = Bookmarks.ofGroups loaded.scene.bookmarks

    /// The viewer model as last rendered.
    member x.Model : Model = current

    /// Renders the scene as seen from `bookmark`.
    member x.Render(bookmark : Bookmark, settings : RenderSettings) : PixImage<byte> =
        if settings.size.X <= 0 || settings.size.Y <= 0 then
            invalidArg (nameof settings) (sprintf "image size must be positive, got %A" settings.size)
        match settings.lodLevel with
        | Some level when level < 0 -> invalidArg (nameof settings) (sprintf "LoD level must be >= 0, got %d" level)
        | _ -> ()

        let m =
            // Where clicking the bookmark ends up. The viewer's UpdateCam does not move the
            // camera itself (Viewer.fs keeps the old navigation) but starts a fly-to
            // animation, which only a window's clock drives: set its end state directly,
            // the navigation Bookmarks.update computes.
            let nav = current.navigation
            let m =
                { current with
                    navigation =
                        { nav with
                            camera         = { nav.camera with view = bookmark.cameraView }
                            exploreCenter  = bookmark.exploreCenter
                            navigationMode = bookmark.navigationMode
                            updatePerFrame = (bookmark.navigationMode = NavigationMode.MapView) } }
            update m (ViewerAction.ResizeMainControl (settings.size, viewportId))
        current <- m

        transact (fun () ->
            adaptive.Update m
            viewportSize.Value <- settings.size
            PRo3D.Core.Surface.LodOverride.fixedLevel.Value <- settings.lodLevel
        )

        let color, resolved, fbo = targetFor settings.size
        clear.Run(RenderToken.Empty, fbo)
        task.Run(RenderToken.Empty, fbo)
        runtime.ResolveMultisamples(color.GetOutputView(), resolved)
        runtime.Download(resolved).ToPixImage<byte>()

    interface IDisposable with
        member x.Dispose() =
            // the override is process-wide; leave the viewer's level of detail behind
            transact (fun () -> PRo3D.Core.Surface.LodOverride.fixedLevel.Value <- None)
            disposeTarget ()
            clear.Dispose()
            task.Dispose()
            cts.Cancel()
            sendQueue.Dispose()
            cts.Dispose()
