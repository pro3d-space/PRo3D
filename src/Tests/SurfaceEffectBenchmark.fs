/// Frame time of the #719 surface-effect variants, measured through the viewer's own
/// scene graph (SurfaceEffectHarness.surfacesSg -> ViewerUtils.createGroupedSgs).
///
/// PR #762 states it is "not measured on macOS", and that #719's 13.9x came from a
/// standalone OpcViewer benchmark harness rather than the viewer. This closes that gap:
/// same process, same patches, same camera, same render objects -- only the variant
/// differs.
///
/// The A/B is the switch test's trick. `triangleFilter 1e9` turns the triangle filter on
/// with a maximum no triangle can exceed, so the geometry-stage variant is composed and
/// filters nothing. Lean and geometry-stage therefore draw the *same pixels* from the
/// *same geometry*; any difference is the stage itself.
///
/// NOTE this is the maximum-emission case, and is not "what the filter costs in use":
/// with a real threshold the filter emits fewer triangles and #719 measured it *faster*
/// than composed-but-disabled (61.40 vs 64.12 ms).
///
/// This is a TOOL, not a test. It runs from `Tests.dll --bench`, entirely on the main
/// thread and outside Expecto, for two reasons: a frame-time threshold on a developer
/// machine is a flaky test and does not belong in a correctness suite; and on macOS GLFW
/// drives an NSApplication run loop, so a GL context created on an Expecto worker thread
/// never returns (observed: a 50-minute deadlock, not a slow run).
///
/// Every stage prints before it starts, flushed, so a stall is always locatable.
module PRo3D.Tests.SurfaceEffectBenchmark

open System
open System.IO
open System.Diagnostics

open Expecto

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Viewer

open PRo3D.Tests

/// What one configuration measured.
type Measured =
    {
        /// GPU time per frame, from an ITimeQuery spanning the batch. Immune to the
        /// CPU-side submission cost and to the readback that ends the batch.
        gpuMs        : float[]
        /// Wall-clock per frame over the batch including its single sync: what a user
        /// actually waits for, at the cost of including CPU submission.
        wallMs       : float[]
        drawCalls    : int
        effDrawCalls : int
        instructions : int
        image        : PixImage<byte>
    }

let private median (xs : float[]) =
    let s = Array.sort xs
    if s.Length % 2 = 1 then s.[s.Length / 2] else (s.[s.Length / 2 - 1] + s.[s.Length / 2]) / 2.0

/// Cheap content fingerprint, to decide when the LoD tree has stopped refining.
let private fingerprint (img : PixImage<byte>) =
    let data = img.Volume.Data
    let mutable h = 17UL
    let mutable i = 0
    while i < data.Length do
        h <- h * 1099511628211UL ^^^ uint64 data.[i]
        i <- i + 7
    h

/// Renders offscreen with no per-frame sleep and no per-frame readback.
///
/// GL queues commands, so timing one `task.Run` measures CPU-side submission, not GPU
/// work. Batches submit every frame and sync once at the end, and a GPU time query spans
/// the batch so the readback is not inside the measured interval at all.
/// (SurfaceEffectHarness.Renderer sleeps 20 ms per frame -- correct for settling, useless
/// for timing.)
///
/// `clearColor` (default black) lets a caller tell background from dark content.
type Bench(runtime : IRuntime, signature : IFramebufferSignature, sg : ISg, size : V2i, ?clearColor : C4f) =
    let background = defaultArg clearColor C4f.Black
    let task     = runtime.CompileRender(signature, sg)
    let clear    = runtime.CompileClear(signature, clear { color background; depth 1.0; stencil 0 })
    let color    = runtime.CreateTexture2D(size, TextureFormat.Rgba8, 1, signature.Samples)
    let depth    = runtime.CreateRenderbuffer(size, TextureFormat.Depth24Stencil8, signature.Samples)
    let resolved = runtime.CreateTexture2D(size, TextureFormat.Rgba8)
    let fbo =
        runtime.CreateFramebuffer(signature, [
            DefaultSemantic.Colors,       color.GetOutputView()
            DefaultSemantic.DepthStencil, depth :> IFramebufferOutput
        ])

    let frame (token : RenderToken) =
        clear.Run(token, fbo)
        task.Run(token, fbo)

    /// Forces queued work to complete, and yields the image.
    member x.Sync() : PixImage<byte> =
        runtime.ResolveMultisamples(color.GetOutputView(), resolved)
        runtime.Download(resolved).ToPixImage<byte>()

    /// Render until two consecutive frames are byte-identical, so patch loading, LoD
    /// refinement and any program swap after a variant switch have converged. A fixed
    /// duration is a guess that is either wasteful or wrong depending on the dataset.
    member x.Settle(maxFrames : int) =
        let mutable last = 0UL
        let mutable stable = 0
        let mutable n = 0
        let mutable img = Unchecked.defaultof<PixImage<byte>>
        while n < maxFrames && stable < 2 do
            frame RenderToken.Empty
            img <- x.Sync()
            let h = fingerprint img
            if n > 0 && h = last then stable <- stable + 1 else stable <- 0
            last <- h
            n <- n + 1
        Log.line "[bench]   settled after %d frame(s)%s" n
            (if stable < 2 then sprintf " (NOT converged, cap %d)" maxFrames else "")
        img

    /// `repeats` batches of `frames`, reported individually: the useful question is
    /// whether a gap between configurations is larger than the spread inside one.
    member x.Measure(frames : int, repeats : int, warmup : int) : Measured =
        // one instrumented frame for the fairness controls
        let mutable statToken = RenderToken.Zero
        frame statToken
        x.Sync() |> ignore

        // Discarded: the first batches after a program swap are dominated by warm-up.
        // Measured directly, the same lean configuration ran 4.383 then 0.979 ms/frame --
        // a 4.5x drift inside one arm, larger than the effect being measured.
        for _ in 1 .. warmup do
            for _ in 1 .. frames do frame RenderToken.Empty
            x.Sync() |> ignore

        let gpu  = Array.zeroCreate repeats
        let wall = Array.zeroCreate repeats
        let sw = Stopwatch()
        for r in 0 .. repeats - 1 do
            use q = runtime.CreateTimeQuery()
            // Begin/End directly rather than through RenderToken.Queries: that property is
            // not assignable from F#, and RenderToken.Use() is only a wrapper around these.
            let qs = [ q :> IQuery ]
            qs.Reset()
            sw.Restart()
            qs.Begin()
            for _ in 1 .. frames do frame RenderToken.Empty
            qs.End()
            x.Sync() |> ignore
            sw.Stop()
            gpu.[r]  <- q.GetResult(true).TotalMilliseconds / float frames
            wall.[r] <- sw.Elapsed.TotalMilliseconds / float frames

        {
            gpuMs = gpu; wallMs = wall
            drawCalls = statToken.DrawCallCount
            effDrawCalls = statToken.EffectiveDrawCallCount
            instructions = statToken.TotalInstructions
            image = x.Sync()
        }

    interface IDisposable with
        member x.Dispose() =
            fbo.Dispose(); resolved.Dispose(); depth.Dispose(); color.Dispose(); clear.Dispose(); task.Dispose()

let report (name : string) (m : Measured) =
    let g, w = median m.gpuMs, median m.wallMs
    Log.line "[bench] %-18s gpu %7.3f ms  wall %7.3f ms/frame (%5.1f fps)  draws %d/%d  instr %d"
        name g w (1000.0 / w) m.effDrawCalls m.drawCalls m.instructions
    Log.line "[bench]   gpu  batches: %s" (m.gpuMs  |> Array.map (sprintf "%.3f") |> String.concat "  ")
    Log.line "[bench]   wall batches: %s" (m.wallMs |> Array.map (sprintf "%.3f") |> String.concat "  ")
    g, w

let private meanDifference (a : PixImage<byte>) (b : PixImage<byte>) =
    let ma, mb = a.GetMatrix<C4b>(), b.GetMatrix<C4b>()
    let mutable sum = 0L
    ma.ForeachIndex(fun i ->
        let x, y = ma.[i], mb.[i]
        sum <- sum + int64 (abs (int x.R - int y.R) + abs (int x.G - int y.G) + abs (int x.B - int y.B)))
    float sum / float (3L * ma.SX * ma.SY)

let private withSurfaces (f : Surface -> Surface) (m : Model) =
    let flat =
        m.scene.surfacesModel.surfaces.flat
        |> HashMap.map (fun _ leaf -> match leaf with Leaf.Surfaces s -> Leaf.Surfaces (f s) | other -> other)
    { m with scene = { m.scene with surfacesModel = { m.scene.surfacesModel with surfaces = { m.scene.surfacesModel.surfaces with flat = flat } } } }

/// The geometry-stage variant, filtering nothing: same pixels as lean, different program.
let private triangleFilter (maxSize : float) =
    withSurfaces (fun s -> { s with filterByTriangleSize = true; triangleSize = { s.triangleSize with value = maxSize } })

/// Geometry stage forced by the DISTANCE filter instead of the size filter, with a range
/// nothing exceeds. Same stage, different arithmetic inside it: separates "the stage exists"
/// from "the stage computes something".
let private distanceFilter (bb : Box3d) (range : float) =
    withSurfaces (fun s ->
        { s with
            filterByDistance = true
            homePosition = Some (CameraView.lookAt bb.Center (bb.Center + V3d.IOO) bb.Center.Normalized)
            filterDistance = { s.filterDistance with value = range } })

/// The cross-section variant, clipping nothing.
///
/// Note the convention, which is the opposite of what the name suggests: Surface.Sg writes
/// `if poly.Contains q2 then -d else d` into InsideOutsideV4, and crossSectionClip
/// discards on `< 0` -- so the polygon marks what is CUT AWAY, and geometry outside it is
/// kept. A polygon containing the terrain therefore discards all of it (observed: a fully
/// black frame that still measured "faster than lean", which is exactly the result the
/// correctness precondition exists to reject).
///
/// So: a small polygon placed far from the terrain. The discard is composed into the
/// shader and its uniforms are live, but no fragment ever satisfies it, which is the
/// cross-section analogue of triangleFilter 1e9.
let private crossSectionCovering (bb : Box3d) (m : Model) =
    let c = bb.Center
    let planet = m.scene.referenceSystem.planet
    let basis = PRo3D.Base.Annotation.CrossSection.buildBasisFromUp (CooTransformation.getUpVector c planet)
    // far enough that no basis the viewer might pick brings it back over the terrain
    let away = c + basis.x * (1000.0 * bb.Size.Length + 1.0e6)
    let r = max 1.0 bb.Size.Length
    let at (k : float) = away + (basis.x * cos k + basis.y * sin k) * r
    { m with
        scene =
            { m.scene with
                crossSectionModel =
                    { m.scene.crossSectionModel with
                        // buildPolygon prepends refPoint: a small triangle, far from the terrain
                        crossSection = Some { geometry = LineOnSurface [| at 2.0944; at 4.1888 |]; refPoint = at 0.0 }
                        clippingEnabled = true } } }

let say fmt = Printf.kprintf (fun t -> printfn "%s" t; Console.Out.Flush()) fmt

/// Returns 0 on success, non-zero if a precondition or a correctness check failed.
let run () : int =
    let mutable failures = 0
    let check (ok : bool) (what : string) =
        if ok then say "[bench]   PASS  %s" what
        else
            failures <- failures + 1
            say "[bench]   FAIL  %s" what

    let opcDir =
        match Environment.GetEnvironmentVariable "PRO3D_BENCH_OPC" with
        | null | "" ->
          match SurfaceEffectHarness.testDataDir () with
          | Some dir when Directory.Exists (Path.Combine(dir, Render.surfaceName)) -> Path.Combine(dir, Render.surfaceName)
          | _ -> Render.opcSurfaceDir
        | dir -> dir

    say "[bench] step 1/6: OPC data at %s" opcDir
    if not (Directory.Exists opcDir) then
        say "[bench] ABORT: no OPC test data (set PRO3D_TEST_DATA)"; 2
    else

    say "[bench] step 2/6: GL context (must already exist on the main thread)"
    match Render.context.Value with
    | None -> say "[bench] ABORT: no GL runtime"; 2
    | Some (runtime, signature) ->

    say "[bench]   samples = %d" signature.Samples

    say "[bench] step 3/6: viewer startup + CooTransformation"
    Startup.init ()
    PRo3D.Core.Surface.Sg.useAsyncLoading <- false
    do Aardvark.Base.Aardvark.UnpackNativeDependencies(typeof<PRo3D.Extensions.FSharp.CooTransformation.RelState>.Assembly)
    CooTransformation.initCooTrafo None (Path.combine [ Environment.GetFolderPath Environment.SpecialFolder.ApplicationData; "Pro3D" ])

    say "[bench] step 4/6: import OPC through the real viewer update loop"
    let sw = Stopwatch.StartNew()
    let freshModel, update = Render.makeViewer ()
    let imported = update (freshModel ()) (ViewerAction.ImportSurface [ opcDir ])
    say "[bench]   imported in %.1f s" sw.Elapsed.TotalSeconds

    match imported.scene.surfacesModel.sgSurfaces |> HashMap.toSeq |> Seq.tryHead with
    | None -> say "[bench] ABORT: the OPC import produced no surface"; 2
    | Some (_, surface) ->

    // Framing decides what is measured. The bounding-box default puts the whole dataset on
    // screen from far above, which on a large OPC means a coarse LoD, few triangles and
    // almost no geometry-stage cost -- flattering, and nothing like the viewer's usual
    // near-surface view. PRO3D_BENCH_EYE / PRO3D_BENCH_FWD take the numbers PRo3D's
    // readout prints, so a realistic camera can be pinned per dataset.
    let parseV3 name =
        match Environment.GetEnvironmentVariable (name : string) with
        | null | "" -> None
        | v ->
            let parts = v.Split([| ','; ' ' |], StringSplitOptions.RemoveEmptyEntries)
            if parts.Length <> 3 then None
            else
                let ok, xs = parts |> Array.fold (fun (ok, acc) p ->
                                match Double.TryParse(p, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                                | true, x -> ok, x :: acc
                                | _ -> false, acc) (true, [])
                match ok, List.rev xs with
                | true, [ x; y; z ] -> Some (V3d(x, y, z))
                | _ -> None

    let framed =
        let bb = surface.globalBB
        let view =
            match parseV3 "PRO3D_BENCH_EYE", parseV3 "PRO3D_BENCH_FWD" with
            | Some eye, Some fwd ->
                say "[bench]   camera: eye %.1f %.1f %.1f fwd %.4f %.4f %.4f" eye.X eye.Y eye.Z fwd.X fwd.Y fwd.Z
                CameraView.lookAt eye (eye + fwd.Normalized * 1000.0) eye.Normalized
            | _ ->
                let c, up = bb.Center, bb.Center.Normalized
                let north = up.Cross(V3d.OOI).Cross(up).Normalized
                say "[bench]   camera: bounding box (whole dataset, coarse LoD)"
                CameraView.lookAt (c + up * 1.5 * bb.Size.Length) c north
        { imported with navigation = { imported.navigation with camera = { imported.navigation.camera with view = view } } }

    let envInt name dflt =
        match Environment.GetEnvironmentVariable name with
        | null | "" -> dflt
        | v -> match Int32.TryParse v with | true, x -> x | _ -> dflt
    let frames  = envInt "PRO3D_BENCH_FRAMES" 40
    let repeats = envInt "PRO3D_BENCH_REPEATS" 5
    let cap     = envInt "PRO3D_BENCH_SETTLE_CAP" 400
    let sizes =
        match Environment.GetEnvironmentVariable "PRO3D_BENCH_SIZES" with
        | null | "" -> [ V2i(320, 240); V2i(1024, 768) ]
        | v ->
            v.Split(',') |> Array.toList
            |> List.choose (fun s ->
                match s.Trim().Split('x') with
                | [| w; h |] ->
                    match Int32.TryParse w, Int32.TryParse h with
                    | (true, w), (true, h) -> Some (V2i(w, h))
                    | _ -> None
                | _ -> None)

    let outDir = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "benchmark-output")
    Directory.CreateDirectory outDir |> ignore

    for size in sizes do
        say "[bench] step 5/6: === %dx%d, %d samples, %d frames x %d batches ==="
            size.X size.Y signature.Samples frames repeats

        let am = AdaptiveModel.Create framed
        use bench = new Bench(runtime, signature, SurfaceEffectHarness.surfacesSg runtime am, size)
        let switchTo (label : string) (m : Model) =
            say "[bench]   switching to %s, settling (cap %d)..." label cap
            transact (fun () -> am.Update m)
            bench.Settle cap |> ignore

        // All four pool variants, INTERLEAVED (a, b, c, d, a, b, c, d, ...) rather than in
        // blocks. Any drift over the run then hits every arm equally; in blocks it would
        // land entirely on whichever arm ran last.
        let rounds = envInt "PRO3D_BENCH_ROUNDS" 4
        let warmup = envInt "PRO3D_BENCH_WARMUP" 2
        let bbox = surface.globalBB
        // PRO3D_BENCH_MODE=anatomy: is the geometry stage expensive because it EXISTS, or
        // because of what it computes and emits? Arms vary the work and the emission count
        // while keeping the stage identical. `sizeFilter drops all` deliberately changes the
        // output, so output-changing arms are exempt from the same-pixels check.
        let anatomy = Environment.GetEnvironmentVariable "PRO3D_BENCH_MODE" = "anatomy"
        let arms =
            if anatomy then
                [ "lean(noGS)",        framed,                            true
                  "GS emit all",       triangleFilter 1e9 framed,         true
                  "GS emit none",      triangleFilter 1e-9 framed,        false
                  "GS distance all",   distanceFilter bbox 1e9 framed,    true
                  "GS distance none",  distanceFilter bbox 1e-9 framed,   false
                  "GS both filters",   (triangleFilter 1e9 >> distanceFilter bbox 1e9) framed, true ]
            else
                [ "lean",            framed,                                                true
                  "geometryStage",   triangleFilter 1e9 framed,                             true
                  "crossSection",    crossSectionCovering bbox framed,                      true
                  "both",            (triangleFilter 1e9 >> crossSectionCovering bbox) framed, true ]
        let results = arms |> List.map (fun (n, _, _) -> n, ResizeArray<Measured>()) |> dict

        for r in 1 .. rounds do
            for name, model, _ in arms do
                say "[bench]   round %d/%d: %s" r rounds name
                switchTo name model
                results.[name].Add(bench.Measure(frames, repeats, warmup))

        let wallOf name = results.[name] |> Seq.collect (fun m -> m.wallMs) |> Array.ofSeq
        let gpuOf  name = results.[name] |> Seq.collect (fun m -> m.gpuMs)  |> Array.ofSeq
        let lastOf name = let xs = results.[name] in xs.[xs.Count - 1]
        // the baseline is whichever arm is first, not a hardcoded name: the anatomy set
        // calls it "lean(noGS)"
        let baseName =
            match arms with
            | (n, _, _) :: _ -> n
            | [] -> failwith "no arms"
        let leanWall = median (wallOf baseName)

        say "[bench] --- %dx%d, %d batches per arm ---" size.X size.Y (wallOf baseName).Length
        for name, _, _ in arms do
            let w = wallOf name
            say "[bench]   %-14s wall %7.3f ms  (min %7.3f max %7.3f)  %+7.3f vs lean  %.2fx"
                name (median w) (Array.min w) (Array.max w) (median w - leanWall) (median w / leanWall)
        if Array.forall ((=) 0.0) (gpuOf baseName) then
            say "[bench]   (GPU timer query returns 0 on this driver: wall clock only)"

        let lean1 = lastOf baseName
        let lean2 = results.[baseName].[0]

        // A fast render that draws the wrong thing is worthless, so every arm is written
        // out for inspection, not merely compared numerically.
        let save (n : string) (img : PixImage<byte>) =
            let f = Path.Combine(outDir, sprintf "%dx%d-%s.png" size.X size.Y n)
            img.Save f
            say "[bench]   wrote %s (lit %.3f)" f (SurfaceEffectHarness.litFraction img)
        for name, _, _ in arms do save name (lastOf name).image

        // lean measured first vs last: if these disagree the run drifted and the deltas
        // above are suspect, interleaving notwithstanding
        let leanFirst = median (results.[baseName].[0].wallMs)
        let leanLast  = median (lastOf baseName).wallMs
        say "[bench]   baseline drift across the run: %.3f -> %.3f ms (%+.1f%%)"
            leanFirst leanLast (100.0 * (leanLast - leanFirst) / leanFirst)

        // correctness is a precondition for any of the timing meaning anything
        for name, _, samePixels in arms do
            let m = lastOf name
            if samePixels then
                check (SurfaceEffectHarness.litFraction m.image > 0.02) (sprintf "%s actually draws the OPC" name)
                check (meanDifference lean1.image m.image < 2.0)
                    (sprintf "%s draws the same pixels as lean (so its delta is the stage, not the work)" name)
            else
                say "[bench]   (%s deliberately changes the output; lit %.3f)" name (SurfaceEffectHarness.litFraction m.image)
            check (m.drawCalls = lean1.drawCalls) (sprintf "%s submits the same draw calls as lean" name)
        check (meanDifference lean1.image lean2.image < 2.0) "lean renders the same at the end as at the start"
        // if the arms submit different geometry, no timing result means anything

    say "[bench] step 6/6: done, %d check(s) failed" failures
    if failures > 0 then 1 else 0
