namespace PRo3D.Core

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open FSharp.Data.Adaptive

/// Fullscreen "shadows and highlights" pass over the offscreen surfaces (OffscreenSurfaces).
///
/// Sits between `OffscreenSurfaces.render` and `OffscreenSurfaces.blit`: it takes the surfaces'
/// colour and depth textures and renders a new colour texture of the same size, format and
/// sample count, which the blit then copies into the view in place of the original. The depth
/// is not touched - the blit still copies the original depth, so everything drawn afterwards is
/// depth-tested against the surfaces exactly as without the pass.
///
/// **The effect itself is a placeholder (identity).** `Shader.effect` is the place for the real
/// one; nothing else needs to change for it. See docs/ShadowHighlight.md.
module ShadowHighlight =

    module Shader =
        open FShade

        /// the surfaces' colour: multisampled, premultiplied alpha (see OffscreenSurfaces.render)
        let private color =
            sampler2dMS {
                texture uniform?ShadowHighlightColor
                filter Filter.MinMagPoint
            }

        /// the surfaces' depth (multisampled, [0,1] window depth, 1 = no surface in the last
        /// priority group). Unused by the placeholder, bound for the real effect.
        let private depth =
            sampler2dMS {
                texture uniform?ShadowHighlightDepth
                filter Filter.MinMagPoint
            }

        type Vertex =
            {
                [<FragCoord>] fc  : V4f
                [<SampleId>]  sid : int
            }

        /// Per sample (reading gl_SampleID runs the fragment stage per sample): the output has
        /// the input's sample count, so sample `sid` of a texel maps to sample `sid` of the same
        /// texel and the antialiased edges survive unchanged.
        ///
        /// Input and output colour are premultiplied - un-premultiply (c.XYZ / c.W where
        /// c.W > 0) before a tonal adjustment and premultiply again after, and keep alpha, or
        /// the blit's premultiplied blend breaks where no surface is (alpha 0).
        ///
        /// PLACEHOLDER: identity. Also mind the macOS OpenGL 4.1 rule for whatever replaces it
        /// (ai/RENDERING.md, Native & Platform Notes): pass anything the CPU knows (sample
        /// count, size, ...) as a uniform instead of querying it in GLSL.
        let effect (v : Vertex) =
            fragment {
                let p = V2i v.fc.XY
                // TODO (shadows & highlights): put the fullscreen effect HERE, in place of this
                // identity. Available per sample:
                //   colour: color.Read(p, v.sid)   - premultiplied RGBA of the surfaces
                //   depth : depth.Read(p, v.sid).X - window depth [0,1], 1 = no surface
                // Extra parameters: add `uniform?Name` reads here and `Sg.uniform "Name" ...`
                // on the pass in `apply` below. An effect that needs blurred/neighbourhood
                // input (local averages) can add passes in `apply` - the output must stay a
                // multisampled Rgba8 texture with `samples` samples at `size`.
                // Try it: OpcViewer <opc-dir> --shadow-highlight  (H toggles; docs/ShadowHighlight.md)
                return color.Read(p, v.sid)
            }

    /// The shadow/highlight pass over `color` (with `depth`), both as made by
    /// `OffscreenSurfaces.render` with `samples` samples at `size`: a colour texture of the
    /// same size, format and sample count.
    ///
    /// Like the surfaces' own targets (OffscreenSurfaces.renderTargets) the pass is compiled
    /// on first acquire and disposed on last release, so it only exists while a view uses it.
    let apply
        (runtime : IRuntime)
        (samples : int)
        (size    : aval<V2i>)
        (color   : aval<ITexture>)
        (depth   : aval<ITexture>) : aval<ITexture> =
        let signature () =
            runtime.CreateFramebufferSignature([ DefaultSemantic.Colors, TextureFormat.Rgba8 ], samples)
        // never an empty target, and the same size the inputs are made at (OffscreenSurfaces.render)
        let size = size |> AVal.map (fun s -> V2i(max 1 s.X, max 1 s.Y))
        // TODO (shadows & highlights): parameters of the effect go onto this pass
        // (Sg.uniform ...); further passes (e.g. a blurred luminance) are built like this one
        let pass =
            Sg.fullScreenQuad
            |> Sg.texture "ShadowHighlightColor" color
            |> Sg.texture "ShadowHighlightDepth" depth
            |> Sg.shader {
                do! DefaultSurfaces.trafo
                do! Shader.effect
            }
            |> Sg.viewTrafo' Trafo3d.Identity
            |> Sg.projTrafo' Trafo3d.Identity
            |> Sg.depthTest (AVal.constant DepthTest.None)
        OffscreenSurfaces.renderTargets runtime signature pass
            (RenderTask.renderToColorWithClear size (clear { color C4f.Zero }))
            (fun c -> c.Acquire())
            (fun c -> c.Release())
            (fun c t rt -> c.GetValue(t, rt) :> ITexture)
        :> aval<ITexture>

    /// `OffscreenSurfaces.blit` with the shadow/highlight pass in between while `enabled`.
    /// Switched off, it is exactly the plain blit and the pass's target is released.
    let blit
        (enabled       : aval<bool>)
        (runtime       : IRuntime)
        (sourceSamples : int)
        (targetSamples : int)
        (size          : aval<V2i>)
        (color         : aval<ITexture>)
        (depth         : aval<ITexture>) : ISg =
        let plain  = OffscreenSurfaces.blit sourceSamples targetSamples color depth
        let shaded =
            let processed = apply runtime sourceSamples size color depth
            OffscreenSurfaces.blit sourceSamples targetSamples processed depth
        enabled
        |> AVal.map (fun on -> if on then shaded else plain)
        |> Sg.dynamic
