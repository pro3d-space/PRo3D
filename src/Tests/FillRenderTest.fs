/// Annotation fills drawn onto the terrain (#841), on the GPU: the deferred path the views use
/// (offscreen surfaces + one-draw DeferredFills), and the stencil fallback (Snapshots) in the
/// benchmark.
///
/// The old fill was a flat lid between the outline points: terrain rising inside the outline
/// covered it, which is the z-fighting users reported. These render a terrain with a bump,
/// then fills through the real PackedRendering code, and read back pixels. Self-skip without
/// a GL context.
module FillRenderTest

open System
open System.Diagnostics
open Expecto
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

let private size = V2i(256, 256)

/// 3 m bump at the origin, flat (< 1 cm) beyond r = 5
let private height (x : float) (y : float) = 3.0 * exp (-(x * x + y * y) / 4.0)

let private terrainColor = C4b(128uy, 128uy, 128uy, 255uy)

let private terrain =
    let n = 96
    let extent = 15.0
    let at i j =
        let x = -extent + 2.0 * extent * float i / float n
        let y = -extent + 2.0 * extent * float j / float n
        V3f(x, y, height x y)
    let positions =
        [| for j in 0 .. n - 1 do
            for i in 0 .. n - 1 do
                let a, b, c, d = at i j, at (i + 1) j, at (i + 1) (j + 1), at i (j + 1)
                yield! [| a; b; c; a; c; d |] |]
    Sg.draw IndexedGeometryMode.TriangleList
    |> Sg.vertexAttribute DefaultSemantic.Positions (AVal.constant positions)
    |> Sg.vertexAttribute DefaultSemantic.Colors (AVal.constant (Array.create positions.Length terrainColor))
    |> Sg.shader {
        do! DefaultSurfaces.trafo
        do! DefaultSurfaces.vertexColor
    }

/// A filled square of half side `half` around `centre` (x, y), its corners on the terrain.
let private square (cx : float) (cy : float) (half : float) (color : C4b) (alpha : float) =
    let corners = [ V2d(-1, -1); V2d(1, -1); V2d(1, 1); V2d(-1, 1) ]
    let a =
        Annotation.make Projection.Linear None Geometry.Polygon None
            ({ c = color } : Aardvark.UI.ColorInput) Annotation.Initial.thickness "test-surface"
    { a with
        points    =
            corners
            |> List.map (fun c -> let x, y = cx + half * c.X, cy + half * c.Y in V3d(x, y, height x y))
            |> IndexList.ofList
        showFill  = true
        fillColor = { c = color }
        fillAlpha = { a.fillAlpha with value = alpha } }

/// The red, opaque 10 x 10 square around the bump.
let private filledSquare () = square 0.0 0.0 5.0 C4b.Red 1.0

let private frustumFor (far : float) = Frustum.perspective 60.0 0.1 far 1.0

/// Fill geometry of `annos` under `viewTrafo`.
let private fillGeometry (viewTrafo : aval<Trafo3d>) (reach : FillReach) (annos : list<Annotation>) =
    let ordered =
        annos
        |> List.map (fun a -> a.key, AdaptiveAnnotation a)
        |> ASet.ofList
        |> PackedRendering.orderedAnnotations
    PackedRendering.fills ColorByCategory.disabled (AVal.constant Planet.None) (AVal.constant reach)
        (AVal.constant 0.0) ordered (viewTrafo |> AVal.map (fun t -> t.Forward))

/// What the viewer renders, in its order: the surfaces (offscreen, then copied back with
/// their depth), then the fills against the surfaces' depth texture.
let private deferredScene runtime (signature : IFramebufferSignature) (viewTrafo : aval<Trafo3d>) (projTrafo : aval<Trafo3d>) (fills : Option<PackedRendering.FillGeometry>) =
    let withCamera sg = sg |> Sg.viewTrafo viewTrafo |> Sg.projTrafo projTrafo
    let color, depth = OffscreenSurfaces.render runtime signature.Samples C4f.Black (AVal.constant size) (withCamera terrain)
    [
        OffscreenSurfaces.blit color depth
        match fills with
        | Some f -> PackedRendering.DeferredFills.render runtime f depth (AVal.constant size) viewTrafo projTrafo
        | None -> ()
    ]

/// Offscreen target; renders `sgs` one after the other, as the viewer's render commands do.
type private Target(runtime : IRuntime, signature : IFramebufferSignature, sgs : list<ISg>) =
    let tasks       = sgs |> List.map (fun sg -> runtime.CompileRender(signature, sg))
    let clear       = runtime.CompileClear(signature, clear { color C4f.Black; depth 1.0; stencil 0 })
    let color       = runtime.CreateTexture2D(size, TextureFormat.Rgba8, 1, signature.Samples)
    let depth       = runtime.CreateRenderbuffer(size, TextureFormat.Depth24Stencil8, signature.Samples)
    let resolved    = runtime.CreateTexture2D(size, TextureFormat.Rgba8)
    let fbo =
        runtime.CreateFramebuffer(signature, [
            DefaultSemantic.Colors,       color.GetOutputView()
            DefaultSemantic.DepthStencil, depth :> IFramebufferOutput
        ])

    member x.Frame() =
        clear.Run(RenderToken.Empty, fbo)
        for t in tasks do t.Run(RenderToken.Empty, fbo)

    /// Forces queued work to complete, and yields the image.
    member x.Image() =
        runtime.ResolveMultisamples(color.GetOutputView(), resolved)
        runtime.Download(resolved).ToPixImage<byte>()

    interface IDisposable with
        member x.Dispose() =
            fbo.Dispose(); resolved.Dispose(); depth.Dispose(); color.Dispose()
            clear.Dispose(); for t in tasks do t.Dispose()

/// Renders `annos` (none: terrain only) through the deferred path and returns the image.
let private renderAnnos runtime (signature : IFramebufferSignature) (camera : CameraView) (far : float) (reach : FillReach) (annos : list<Annotation>) =
    let viewTrafo = AVal.constant (CameraView.viewTrafo camera)
    let projTrafo = AVal.constant (Frustum.projTrafo (frustumFor far))
    let fills = if List.isEmpty annos then None else Some (fillGeometry viewTrafo reach annos)
    use target = new Target(runtime, signature, deferredScene runtime signature viewTrafo projTrafo fills)
    target.Frame()
    target.Image()

let private renderWith (far : float) runtime signature (camera : CameraView) (reach : FillReach) =
    let image = renderAnnos runtime signature camera far reach [ filledSquare () ]
    /// pixel column of a world point (rows are left alone: the samples share the centre row,
    /// which keeps the test independent of the download's row order)
    let column (p : V3d) =
        let ndc = (CameraView.viewTrafo camera * Frustum.projTrafo (frustumFor far)).Forward.TransformPosProj p
        int (round ((ndc.X * 0.5 + 0.5) * float size.X))
    image, column

let private render runtime signature camera reach = renderWith 200.0 runtime signature camera reach

/// Pixels of the red fill colour: green (channel 1 in RGBA and BGRA alike) near 0 where the
/// grey terrain has 128 and the black background has 0 in every channel.
let private filledPixels (image : PixImage<byte>) =
    let g = image.GetChannel(1L)
    let a = image.GetChannel(0L)
    let b = image.GetChannel(2L)
    let mutable n = 0
    for y in 0L .. g.Size.Y - 1L do
        for x in 0L .. g.Size.X - 1L do
            if g.[x, y] < 40uy && (max a.[x, y] b.[x, y]) > 200uy then n <- n + 1
    n

/// Green is channel 1 in both RGBA and BGRA: 0 for the red fill, 128 for the grey terrain.
let private green (image : PixImage<byte>) (x : int) (y : int) =
    image.GetChannel(1L).[int64 x, int64 y]

let private samePixel (a : PixImage<byte>) (b : PixImage<byte>) (x : int64) (y : int64) =
    let mutable same = true
    for c in 0L .. 2L do
        if abs (int (a.GetChannel(c).[x, y]) - int (b.GetChannel(c).[x, y])) > 8 then same <- false
    same

let private withGL (f : IRuntime -> IFramebufferSignature -> unit) =
    match PRo3D.Tests.Render.context.Value with
    | None -> skiptest "no OpenGL runtime in this environment"
    | Some (runtime, signature) -> f runtime signature

let tests () =
    testList "fill render" [

        test "the fill covers terrain rising inside the outline, and nothing outside" {
            withGL (fun runtime signature ->
                // straight down onto the bump: its top is the centre pixel
                let camera = CameraView.lookAt (V3d(0.0, 0.0, 30.0)) (V3d(0.0, 0.0, 3.0)) V3d.OIO
                let image, column = render runtime signature camera (FillReach.Metres 5.0)
                let cy = size.Y / 2

                Expect.isLessThan (green image (size.X / 2) cy) 40uy
                    "the bump top, 3 m above every outline point, must be filled - the old flat lid sat under it"
                Expect.isLessThan (green image (column (V3d(4.0, 0.0, height 4.0 0.0))) cy) 40uy
                    "terrain just inside the outline must be filled"
                Expect.isGreaterThan (green image (column (V3d(8.0, 0.0, 0.0))) cy) 100uy
                    "terrain outside the outline must stay unfilled")
        }

        test "a reach shorter than the bump leaves its top unfilled" {
            withGL (fun runtime signature ->
                let camera = CameraView.lookAt (V3d(0.0, 0.0, 30.0)) (V3d(0.0, 0.0, 3.0)) V3d.OIO
                let image, _ = render runtime signature camera (FillReach.Metres 1.0)
                Expect.isGreaterThan (green image (size.X / 2) (size.Y / 2)) 100uy
                    "a 1 m reach must not colour terrain 3 m above the outline - the control does what it says")
        }

        test "the fill stays right with the camera inside the prism" {
            withGL (fun runtime signature ->
                // 1.5 m above the bump top, well inside the +-5 m prism: the near plane cuts
                // the prism's top cap away, which is what z-fail is for
                let camera = CameraView.lookAt (V3d(0.0, 0.0, 4.5)) (V3d(0.0, 0.0, 3.0)) V3d.OIO
                let image, _ = render runtime signature camera (FillReach.Metres 5.0)
                Expect.isLessThan (green image (size.X / 2) (size.Y / 2)) 40uy
                    "the terrain below a camera inside the prism must still be filled")
        }

        test "a far plane cutting the prism neither drops the fill nor paints empty background" {
            withGL (fun runtime signature ->
                // low and grazing across the square: with far = 16.5 the far wall (y = 5) and
                // the bottom cap lie beyond the far plane while most terrain is still in front
                let camera = CameraView.lookAt (V3d(0.0, -12.0, 1.5)) (V3d(0.0, 4.0, 0.0)) V3d.OOI
                let reference, _ = renderWith 200.0 runtime signature camera (FillReach.Metres 5.0)
                let cut, _       = renderWith 16.5 runtime signature camera (FillReach.Metres 5.0)
                let r = filledPixels reference
                let c = filledPixels cut
                printfn "[fill] grazing view: %d filled pixels with far 200, %d with far 16.5" r c
                Expect.isGreaterThan r 1000 "the reference view shows the fill"
                // fewer is right - terrain beyond the far plane is not drawn, so neither is its fill
                Expect.isLessThanOrEqual c r
                    "a clamped back face must not paint the empty background beyond the far plane"
                Expect.isGreaterThan (float c) (0.8 * float r)
                    "prism faces beyond the far plane must still count")
        }

        test "a near prism covering another fill on screen does not take its pixels" {
            withGL (fun runtime signature ->
                // low across two fills: the near red prism's top and walls cover the far blue
                // fill on screen. Painted from one shared mark, the far fill's terrain took the
                // colour of whichever prism face reached it first - the user's straight cut.
                let camera = CameraView.lookAt (V3d(0.0, -16.0, 6.0)) (V3d(0.0, 7.0, 0.0)) V3d.OOI
                let near = square 0.0 -4.0 2.5 C4b.Red 1.0
                let far  = square 0.0  7.0 3.0 C4b.Blue 1.0
                let reach = FillReach.Metres 6.0
                let none = renderAnnos runtime signature camera 200.0 reach []
                let a    = renderAnnos runtime signature camera 200.0 reach [ near ]
                let b    = renderAnnos runtime signature camera 200.0 reach [ far ]
                let ab   = renderAnnos runtime signature camera 200.0 reach [ near; far ]

                let mutable tintedA, wrongA, tintedB, wrongB = 0, 0, 0, 0
                for y in 0L .. int64 size.Y - 1L do
                    for x in 0L .. int64 size.X - 1L do
                        let inA = not (samePixel a none x y)
                        let inB = not (samePixel b none x y)
                        if inA && not inB then
                            tintedA <- tintedA + 1
                            if not (samePixel ab a x y) then wrongA <- wrongA + 1
                        if inB && not inA then
                            tintedB <- tintedB + 1
                            if not (samePixel ab b x y) then wrongB <- wrongB + 1
                printfn "[fill] screen overlap: near %d px (%d wrong), far %d px (%d wrong)" tintedA wrongA tintedB wrongB
                Expect.isGreaterThan tintedA 500 "the near fill is in view"
                Expect.isGreaterThan tintedB 200 "the far fill is in view"
                Expect.isLessThan (float wrongA) (0.005 * float tintedA) "the near fill keeps its own colour"
                Expect.isLessThan (float wrongB) (0.005 * float tintedB) "the far fill keeps its own colour")
        }

        test "surfaces of an earlier priority group survive the offscreen copy" {
            withGL (fun runtime signature ->
                // the viewer clears depth before each priority group, so outside the last
                // group's surfaces the offscreen depth is 1 - the terrain (group 0) must still be
                // copied there, not discarded as "no surface" (review of #841)
                let camera = CameraView.lookAt (V3d(0.0, 0.0, 30.0)) (V3d(0.0, 0.0, 0.0)) V3d.OIO
                let viewTrafo = AVal.constant (CameraView.viewTrafo camera)
                let projTrafo = AVal.constant (Frustum.projTrafo (frustumFor 200.0))
                let patch =
                    let p = [| V3f(-2.0f, -2.0f, 4.0f); V3f(2.0f, -2.0f, 4.0f); V3f(2.0f, 2.0f, 4.0f); V3f(-2.0f, -2.0f, 4.0f); V3f(2.0f, 2.0f, 4.0f); V3f(-2.0f, 2.0f, 4.0f) |]
                    Sg.draw IndexedGeometryMode.TriangleList
                    |> Sg.vertexAttribute DefaultSemantic.Positions (AVal.constant p)
                    |> Sg.vertexAttribute DefaultSemantic.Colors (AVal.constant (Array.create p.Length C4b.Blue))
                    |> Sg.shader {
                        do! DefaultSurfaces.trafo
                        do! DefaultSurfaces.vertexColor
                    }
                let groups =
                    OffscreenSurfaces.inPriorityGroups viewTrafo projTrafo (AList.ofList [ terrain; patch ])
                let color, depth = OffscreenSurfaces.render runtime signature.Samples C4f.Black (AVal.constant size) groups
                use target = new Target(runtime, signature, [ OffscreenSurfaces.blit color depth ])
                target.Frame()
                let image = target.Image()
                // terrain only: a point well outside the patch, on the centre row
                let ndc = (CameraView.viewTrafo camera * Frustum.projTrafo (frustumFor 200.0)).Forward.TransformPosProj (V3d(8.0, 0.0, 0.0))
                let x = int (round ((ndc.X * 0.5 + 0.5) * float size.X))
                Expect.isGreaterThan (green image x (size.Y / 2)) 100uy
                    "terrain of group 0 outside the group-1 patch must be copied (it has depth 1 after the last clear)"
                Expect.isLessThan (green image (size.X / 2) (size.Y / 2)) 40uy "the group-1 patch is drawn at the centre")
        }

        test "fills overlapping on the terrain blend" {
            withGL (fun runtime signature ->
                let camera = CameraView.lookAt (V3d(0.0, 0.0, 30.0)) (V3d(0.0, 0.0, 0.0)) V3d.OIO
                let left  = square -2.0 0.0 3.0 C4b.Red 0.5
                let right = square  2.0 0.0 3.0 C4b.Blue 0.5
                let reach = FillReach.Metres 5.0
                let a  = renderAnnos runtime signature camera 200.0 reach [ left ]
                let b  = renderAnnos runtime signature camera 200.0 reach [ right ]
                let ab = renderAnnos runtime signature camera 200.0 reach [ left; right ]
                // the origin, in both squares, is the centre pixel
                let cx, cy = int64 (size.X / 2), int64 (size.Y / 2)
                Expect.isFalse (samePixel ab a cx cy) "the overlap is not just the first fill"
                Expect.isFalse (samePixel ab b cx cy) "the overlap is not just the second fill")
        }

        test "benchmark: 512 and 10240 fills, deferred one-draw fills against the stencil fallback" {
            withGL (fun runtime signature ->
                // grids of small squares around the bump; the camera moves every frame, as in
                // navigation, so the surfaces and the fill accumulation really re-render
                for (cols, rows, spacing, half) in [ (32, 16, 0.8, 0.3); (128, 80, 0.22, 0.08) ] do
                    printfn "[fill-bench] --- %d fills ---" (cols * rows)
                    let annos =
                        [ for j in 0 .. rows - 1 do
                            for i in 0 .. cols - 1 do
                                let x = -0.5 * spacing * float (cols - 1) + spacing * float i
                                let y = -0.5 * spacing * float (rows - 1) + spacing * float j
                                yield square x y half C4b.Red 0.6 ]

                    let cameraAt (k : int) =
                        let a = 0.002 * float (k % 2)
                        CameraView.lookAt (V3d(sin a * 10.0, -10.0, 25.0)) V3d.Zero V3d.OOI
                    let frameIndex = cval 0
                    let viewTrafo = frameIndex |> AVal.map (fun k -> CameraView.viewTrafo (cameraAt k))
                    let projTrafo = AVal.constant (Frustum.projTrafo (frustumFor 200.0))
                    let withCamera sg = sg |> Sg.viewTrafo viewTrafo |> Sg.projTrafo projTrafo
                    let geometry = fillGeometry viewTrafo (FillReach.Metres 2.0) annos

                    let stencilSg (fills : ISg) = [ withCamera terrain; withCamera fills ]

                    let measure (name : string) (sgs : list<ISg>) =
                        use target = new Target(runtime, signature, sgs)
                        let frame () =
                            transact (fun () -> frameIndex.Value <- frameIndex.Value + 1)
                            target.Frame()
                        // warm up: compilation, uploads, first-frame preparation
                        for _ in 1 .. 20 do frame ()
                        target.Image() |> ignore
                        let frames = 100
                        let best = Array.zeroCreate 5
                        for r in 0 .. best.Length - 1 do
                            use q = runtime.CreateTimeQuery()
                            let qs = [ q :> IQuery ]
                            qs.Reset()
                            let sw = Stopwatch.StartNew()
                            qs.Begin()
                            for _ in 1 .. frames do frame ()
                            qs.End()
                            target.Image() |> ignore
                            sw.Stop()
                            best.[r] <- sw.Elapsed.TotalMilliseconds / float frames, q.GetResult(true).TotalMilliseconds / float frames
                        let wall = best |> Array.map fst |> Array.min
                        let gpu  = best |> Array.map snd |> Array.min
                        printfn "[fill-bench] %-30s wall %7.3f ms/frame, gpu %7.3f ms/frame" name wall gpu
                        wall

                    let direct   = measure "terrain, direct" [ withCamera terrain ]
                    let deferred = measure "terrain, offscreen + blit" (deferredScene runtime signature viewTrafo projTrafo None)
                    let b2       = measure "fills: deferred one draw (B2)" (deferredScene runtime signature viewTrafo projTrafo (Some geometry))
                    let batched  = measure "fills: stencil fallback" (stencilSg (PackedRendering.packedFillRender geometry))
                    printfn "[fill-bench] fills cost: B2 %.3f ms (over offscreen terrain), stencil fallback %.3f ms (over direct terrain); offscreen+blit itself %.3f ms"
                        (b2 - deferred) (batched - direct) (deferred - direct)
                    Expect.isGreaterThan b2 0.0 "measured")
        }
    ]
