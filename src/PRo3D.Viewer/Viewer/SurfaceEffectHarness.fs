namespace PRo3D

open System
open System.Diagnostics

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.SceneGraph   // runtime.CompileRender for an ISg; before Aardvark.UI so its Sg wins
open Aardvark.UI

open PRo3D.Viewer

/// Renders a viewer model's OPC surfaces offscreen, composed exactly as the viewer does
/// (ViewerUtils.createGroupedSgs, surface-effect variants included) and seen through the
/// model's camera. Load a scene into a model, render, change the model, render again:
/// the regression harness for rendering state such as the surface-effect variants (#719).
module SurfaceEffectHarness =

    /// PRO3D_TEST_DATA: a PRo3D.Resources.TestData checkout (tests-ui uses the same).
    let testDataDir () : Option<string> =
        match Environment.GetEnvironmentVariable "PRO3D_TEST_DATA" with
        | null | "" -> None
        | dir when IO.Directory.Exists dir -> Some dir
        | _ -> None

    /// The model's OPC surfaces as the viewer renders them.
    let surfacesSg (runtime : IRuntime) (m : AdaptiveModel) : Aardvark.SceneGraph.ISg =
        ViewerUtils.createGroupedSgs runtime m.scene.surfacesModel.sgGrouped m.navigation.camera.view false false m
        |> ASet.ofAList
        |> Sg.set
        |> Sg.viewTrafo (m.navigation.camera.view |> AVal.map CameraView.viewTrafo)
        |> Sg.projTrafo (m.frustum |> AVal.map Frustum.projTrafo)
        :> Aardvark.SceneGraph.ISg

    /// Offscreen rendering into `signature` (multisampled like the viewer's). Every Render
    /// keeps drawing for at least `settle`, so patch loading and LoD refinement converge
    /// before the image is read back.
    type Renderer(runtime : IRuntime, signature : IFramebufferSignature, sg : Aardvark.SceneGraph.ISg, size : V2i) =
        let task     = runtime.CompileRender(signature, sg)
        let clear    = runtime.CompileClear(signature, clear { color C4f.Black; depth 1.0; stencil 0 })
        let color    = runtime.CreateTexture2D(size, TextureFormat.Rgba8, 1, signature.Samples)
        let depth    = runtime.CreateRenderbuffer(size, TextureFormat.Depth24Stencil8, signature.Samples)
        let resolved = runtime.CreateTexture2D(size, TextureFormat.Rgba8)
        let fbo =
            runtime.CreateFramebuffer(signature, [
                DefaultSemantic.Colors,       color.GetOutputView()
                DefaultSemantic.DepthStencil, depth :> IFramebufferOutput
            ])

        member x.Render(settle : TimeSpan) : PixImage<byte> =
            let sw = Stopwatch.StartNew()
            let mutable frames = 0
            while sw.Elapsed < settle || frames < 3 do
                clear.Run(RenderToken.Empty, fbo)
                task.Run(RenderToken.Empty, fbo)
                frames <- frames + 1
                Threading.Thread.Sleep 20
            runtime.ResolveMultisamples(color.GetOutputView(), resolved)
            runtime.Download(resolved).ToPixImage<byte>()

        interface IDisposable with
            member x.Dispose() =
                fbo.Dispose(); resolved.Dispose(); depth.Dispose(); color.Dispose(); clear.Dispose(); task.Dispose()

    /// Fraction of pixels that are not (near) black: is there a surface in the image.
    let litFraction (image : PixImage<byte>) : float =
        let m = image.GetMatrix<C4b>()
        let mutable lit = 0L
        m.ForeachIndex(fun i -> let c = m.[i] in if int c.R + int c.G + int c.B > 24 then lit <- lit + 1L)
        float lit / float (m.SX * m.SY)
