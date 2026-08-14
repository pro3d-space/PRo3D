namespace PRo3D.ProjectionTestbed

open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open FSharp.Data.Adaptive

/// Headless framebuffer rendering.
///
/// Deliberately standalone rather than reusing PRo3D.SimulatedViews.SnapshotApp: that path
/// is bound to the Suave server, the mutableApp message loop and the snapshot animation
/// model, none of which a one-shot CLI needs.
module Offscreen =

    type Target =
        {
            runtime   : IRuntime
            signature : IFramebufferSignature
            color     : IBackendTexture
            depth     : IBackendTexture
            output    : OutputDescription
            size      : V2i
        }

    let createTarget (runtime : IRuntime) (size : V2i) =
        let res = V3i(size.X, size.Y, 1)
        let color = runtime.CreateTexture(res, TextureDimension.Texture2D, TextureFormat.Rgba8, 1, 1)
        let depth = runtime.CreateTexture(res, TextureDimension.Texture2D, TextureFormat.DepthComponent32f, 1, 1)
        let signature =
            runtime.CreateFramebufferSignature([
                DefaultSemantic.Colors, TextureFormat.Rgba8
                DefaultSemantic.DepthStencil, TextureFormat.DepthComponent32f
            ], 1)
        let output =
            runtime.CreateFramebuffer(
                signature,
                Map.ofList [
                    DefaultSemantic.Colors, color.GetOutputView()
                    DefaultSemantic.DepthStencil, depth.GetOutputView()
                ]) |> OutputDescription.ofFramebuffer
        { runtime = runtime; signature = signature; color = color; depth = depth
          output = output; size = size }

    /// Render and download.
    ///
    /// `warmupFrames` exists because even with synchronous patch loading the LOD tree is
    /// only refined once a frame has been rendered with the final camera -- the decider
    /// needs a view to decide against. One render pass captures whatever the initial
    /// tree was; a few passes let it settle. This is cheap and removes a class of
    /// "screenshot looks lower-res than the window" confusion.
    let render (target : Target) (warmupFrames : int) (sg : ISg) =
        let clear = target.runtime.CompileClear(target.signature, AVal.constant C4f.Black, AVal.constant 1.0)
        let task = target.runtime.CompileRender(target.signature, sg)
        for _ in 1 .. max 1 warmupFrames do
            clear.Run(target.output)
            task.Run(target.output)
        let image = target.runtime.Download(target.color)
        task.Dispose()
        clear.Dispose()
        image

    let save (dir : string) (name : string) (image : PixImage) =
        if not (Directory.Exists dir) then Directory.CreateDirectory dir |> ignore
        let path = Path.Combine(dir, name)
        image.Save(path)
        Log.line "[out] %s" path
        path
