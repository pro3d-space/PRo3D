/// Annotation fills drawn onto the terrain with stencil volumes (#841), on the GPU.
///
/// The old fill was a flat lid between the outline points: terrain rising inside the outline
/// covered it, which is the z-fighting users reported. These render a terrain with a bump
/// inside a square outline, then the fill through the real PackedRendering passes, and read
/// back pixels. Self-skips without a GL context.
module FillStencilRenderTest

open System
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

/// A red, opaque, filled square of side 10 around the bump, its corners on the terrain.
let private filledSquare () =
    let corners = [ V3d(-5, -5, 0); V3d(5, -5, 0); V3d(5, 5, 0); V3d(-5, 5, 0) ]
    let a =
        Annotation.make Projection.Linear None Geometry.Polygon None
            ({ c = C4b.Red } : Aardvark.UI.ColorInput) Annotation.Initial.thickness "test-surface"
    { a with
        points    = corners |> List.map (fun p -> V3d(p.X, p.Y, height p.X p.Y)) |> IndexList.ofList
        showFill  = true
        fillColor = { c = C4b.Red }
        fillAlpha = { a.fillAlpha with value = 1.0 } }

/// Renders terrain, then the fills - two tasks, as the viewer renders surfaces and the
/// depth-tested annotations in separate render commands - and returns the image.
let private render (runtime : IRuntime) (signature : IFramebufferSignature) (camera : CameraView) (reach : FillReach) =
    let frustum = Frustum.perspective 60.0 0.1 200.0 1.0
    let viewTrafo = AVal.constant (CameraView.viewTrafo camera)
    let projTrafo = AVal.constant (Frustum.projTrafo frustum)

    let anno = filledSquare ()
    let ordered = PackedRendering.orderedAnnotations (ASet.single (anno.key, AdaptiveAnnotation anno))
    let fills =
        PackedRendering.fills ColorByCategory.disabled (AVal.constant Planet.None) (AVal.constant reach)
            (AVal.constant 0.0) ordered (viewTrafo |> AVal.map (fun t -> t.Forward))
        |> PackedRendering.packedFillRender

    let withCamera sg = sg |> Sg.viewTrafo viewTrafo |> Sg.projTrafo projTrafo

    use terrainTask = runtime.CompileRender(signature, withCamera terrain)
    use fillTask    = runtime.CompileRender(signature, withCamera fills)
    use clear       = runtime.CompileClear(signature, clear { color C4f.Black; depth 1.0; stencil 0 })

    use color    = runtime.CreateTexture2D(size, TextureFormat.Rgba8, 1, signature.Samples)
    use depth    = runtime.CreateRenderbuffer(size, TextureFormat.Depth24Stencil8, signature.Samples)
    use resolved = runtime.CreateTexture2D(size, TextureFormat.Rgba8)
    use fbo =
        runtime.CreateFramebuffer(signature, [
            DefaultSemantic.Colors,       color.GetOutputView()
            DefaultSemantic.DepthStencil, depth :> IFramebufferOutput
        ])

    clear.Run(RenderToken.Empty, fbo)
    terrainTask.Run(RenderToken.Empty, fbo)
    fillTask.Run(RenderToken.Empty, fbo)
    runtime.ResolveMultisamples(color.GetOutputView(), resolved)
    let image = runtime.Download(resolved).ToPixImage<byte>()

    /// pixel column of a world point (rows are left alone: the samples share the centre row,
    /// which keeps the test independent of the download's row order)
    let column (p : V3d) =
        let ndc = (CameraView.viewTrafo camera * Frustum.projTrafo frustum).Forward.TransformPosProj p
        int (round ((ndc.X * 0.5 + 0.5) * float size.X))

    image, column

/// Green is channel 1 in both RGBA and BGRA: 0 for the red fill, 128 for the grey terrain.
let private green (image : PixImage<byte>) (x : int) (y : int) =
    image.GetChannel(1L).[int64 x, int64 y]

let tests () =
    testList "fill stencil render" [

        test "the fill covers terrain rising inside the outline, and nothing outside" {
            match PRo3D.Tests.Render.context.Value with
            | None -> skiptest "no OpenGL runtime in this environment"
            | Some (runtime, signature) ->
                // straight down onto the bump: its top is the centre pixel
                let camera = CameraView.lookAt (V3d(0.0, 0.0, 30.0)) (V3d(0.0, 0.0, 3.0)) V3d.OIO
                let image, column = render runtime signature camera (FillReach.Metres 5.0)
                let cy = size.Y / 2

                Expect.isLessThan (green image (size.X / 2) cy) 40uy
                    "the bump top, 3 m above every outline point, must be filled - the old flat lid sat under it"
                Expect.isLessThan (green image (column (V3d(4.0, 0.0, height 4.0 0.0))) cy) 40uy
                    "terrain just inside the outline must be filled"
                Expect.isGreaterThan (green image (column (V3d(8.0, 0.0, 0.0))) cy) 100uy
                    "terrain outside the outline must stay unfilled"
        }

        test "a reach shorter than the bump leaves its top unfilled" {
            match PRo3D.Tests.Render.context.Value with
            | None -> skiptest "no OpenGL runtime in this environment"
            | Some (runtime, signature) ->
                let camera = CameraView.lookAt (V3d(0.0, 0.0, 30.0)) (V3d(0.0, 0.0, 3.0)) V3d.OIO
                let image, _ = render runtime signature camera (FillReach.Metres 1.0)
                Expect.isGreaterThan (green image (size.X / 2) (size.Y / 2)) 100uy
                    "a 1 m reach must not colour terrain 3 m above the outline - the control does what it says"
        }

        test "the fill stays right with the camera inside the prism" {
            match PRo3D.Tests.Render.context.Value with
            | None -> skiptest "no OpenGL runtime in this environment"
            | Some (runtime, signature) ->
                // 1.5 m above the bump top, well inside the +-5 m prism: the near plane cuts
                // the prism's top cap away, which is what z-fail is for
                let camera = CameraView.lookAt (V3d(0.0, 0.0, 4.5)) (V3d(0.0, 0.0, 3.0)) V3d.OIO
                let image, _ = render runtime signature camera (FillReach.Metres 5.0)
                Expect.isLessThan (green image (size.X / 2) (size.Y / 2)) 40uy
                    "the terrain below a camera inside the prism must still be filled"
        }
    ]
