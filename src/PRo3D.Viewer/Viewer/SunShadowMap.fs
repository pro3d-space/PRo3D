namespace PRo3D.Viewer

open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive

/// Sun shadow mapping for the viewer's OPC surfaces (LightingMode.SunShadow).
///
/// The receive path (terrainSunShadow in the OPC effect stack) needs the ShadowMap
/// sampler bound on every surface unconditionally -- FShade rejects an unbound sampler at
/// compile time even when the shadow branch is never taken. This module provides that
/// binding: a 1x1 far-plane dummy while shadows are off, the real sun depth map once the
/// caster pass exists.
module SunShadowMap =

    /// One dummy per process: the runtime is a singleton in both the viewer and
    /// PRo3D.Snapshots, and the texture is immutable.
    let private dummyCache = System.Collections.Concurrent.ConcurrentDictionary<IRuntime, ITexture>()

    /// A 1x1 depth texture cleared to the far plane: every comparison passes, i.e.
    /// "fully lit". Bound whenever no real shadow map is available so the shadow
    /// comparison sampler always has a depth texture behind it.
    let dummyTexture (runtime : IRuntime) : ITexture =
        dummyCache.GetOrAdd(runtime, fun runtime ->
            let signature =
                runtime.CreateFramebufferSignature([
                    DefaultSemantic.Colors, TextureFormat.Rgba8
                    DefaultSemantic.DepthStencil, TextureFormat.DepthComponent32f
                ], 1)
            let color = runtime.CreateTexture(V3i(1, 1, 1), TextureDimension.Texture2D, TextureFormat.Rgba8, 1, 1)
            let depth = runtime.CreateTexture(V3i(1, 1, 1), TextureDimension.Texture2D, TextureFormat.DepthComponent32f, 1, 1)
            let output =
                runtime.CreateFramebuffer(
                    signature,
                    Map.ofList [
                        DefaultSemantic.Colors, color.GetOutputView()
                        DefaultSemantic.DepthStencil, depth.GetOutputView()
                    ]) |> OutputDescription.ofFramebuffer
            let clear = runtime.CompileClear(signature, AVal.constant (C4f(0.0f, 0.0f, 0.0f, 0.0f)), AVal.constant 1.0)
            clear.Run(output)
            clear.Dispose()
            // the colour attachment and signature are only scaffolding for the clear;
            // the depth texture is the product and lives for the whole process
            runtime.DeleteTexture color
            signature.Dispose()
            depth :> ITexture)

    /// The texture the OPC surfaces sample. Currently always the dummy -- the caster
    /// pass (rendering the scene's OPC surfaces from a sun-aligned ortho camera) is the
    /// next step; until it lands, SunShadow shades exactly like SunDirect because the
    /// per-patch HasShadowMap gate stays false (no lightViewProj is fed).
    let texture (runtime : IRuntime) : aval<ITexture> =
        AVal.constant (dummyTexture runtime)
