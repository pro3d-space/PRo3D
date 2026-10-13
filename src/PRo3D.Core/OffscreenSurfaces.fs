namespace PRo3D.Core

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open FSharp.Data.Adaptive

/// Surfaces rendered into textures, then copied into the view (#841).
///
/// The view keeps working exactly as before - the copy restores colour *and* depth, per
/// sample, so everything drawn afterwards (annotations, traverses, overlays) is depth-tested
/// against the surfaces as if they had been drawn directly. In addition the surfaces' depth
/// now exists as a texture, which is what the annotation fills test against
/// (PackedRendering.DeferredFills), and what any other screen-space technique can build on.
///
/// The targets follow the size they are given; the viewer hands in the render control's size.
/// A server-side screenshot renders the same scene at another resolution and so currently
/// gets surface textures of the control's size, scaled - the copy maps through ViewportSize,
/// so it stays aligned, just not sharper.
module OffscreenSurfaces =

    /// The surfaces' depth as seen by one view, with what produced it: anything comparing
    /// against it has to render with the same trafos.
    type SurfaceDepth =
        {
            /// multisampled depth texture
            depth     : aval<ITexture>
            size      : aval<V2i>
            viewTrafo : aval<Trafo3d>
            projTrafo : aval<Trafo3d>
        }

    module Shader =
        open FShade

        let private color =
            sampler2dMS {
                texture uniform?OffscreenColor
                filter Filter.MinMagPoint
            }

        let private depth =
            sampler2dMS {
                texture uniform?OffscreenDepth
                filter Filter.MinMagPoint
            }

        type BlitVertex =
            {
                [<Position>]  pos : V4f
                [<FragCoord>] fc  : V4f
                [<SampleId>]  sid : int
            }

        type BlitFragment =
            {
                [<Color>] c : V4f
                [<Depth>] d : float32
            }

        /// Per sample (reading gl_SampleID makes the fragment stage run per sample), so the
        /// copy is exact: same colour, same depth, edges keep their antialiasing. Samples the
        /// surfaces did not cover are left to the view's own clear.
        let blit (v : BlitVertex) =
            fragment {
                let vp : V2i = uniform?ViewportSize
                let p = V2i (v.fc.XY * V2f depth.Size / V2f vp)
                let d = depth.Read(p, v.sid).X
                if d >= 1.0f then discard()
                return { c = color.Read(p, v.sid); d = d }
            }

    /// Colour and depth of the surfaces in `sg`, rendered at `size` with `samples` samples -
    /// the sample count of the target they are copied into (the copy is per sample) - over
    /// `background`, the target's own clear colour, so translucent surfaces blend as before.
    /// `sg` brings its own view and projection.
    let render (runtime : IRuntime) (samples : int) (background : C4f) (size : aval<V2i>) (sg : ISg) =
        let signature =
            runtime.CreateFramebufferSignature(
                [
                    DefaultSemantic.Colors,       TextureFormat.Rgba8
                    DefaultSemantic.DepthStencil, TextureFormat.Depth24Stencil8
                ],
                samples)
        let color, depth =
            runtime.CompileRender(signature, sg)
            |> RenderTask.renderToColorAndDepthWithClear size (clear { color background; depth 1.0; stencil 0 })
        AdaptiveResource.cast<ITexture> color, AdaptiveResource.cast<ITexture> depth

    /// `groups` drawn one after the other, the depth cleared before each - the viewer's surface
    /// priority groups - under the given trafos.
    let inPriorityGroups (viewTrafo : aval<Trafo3d>) (projTrafo : aval<Trafo3d>) (groups : alist<ISg>) : ISg =
        RenderCommand.Ordered(
            alist {
                for sg in groups do
                    yield RenderCommand.Clear(clear { depth 1.0 })
                    yield RenderCommand.Render sg
            })
        |> Sg.execute
        |> Sg.viewTrafo viewTrafo
        |> Sg.projTrafo projTrafo

    /// Fullscreen copy of `color` and `depth` into the current target, writing depth.
    let blit (color : aval<ITexture>) (depth : aval<ITexture>) : ISg =
        Sg.fullScreenQuad
        |> Sg.texture "OffscreenColor" color
        |> Sg.texture "OffscreenDepth" depth
        |> Sg.shader {
            do! DefaultSurfaces.trafo
            do! Shader.blit
        }
        |> Sg.viewTrafo' Trafo3d.Identity
        |> Sg.projTrafo' Trafo3d.Identity
        // Always, not None: with the test disabled GL writes no depth either
        |> Sg.depthTest (AVal.constant DepthTest.Always)
        |> Sg.blendMode (AVal.constant BlendMode.None)

    /// A scene graph that keeps its scene events (picking) but renders nothing - for the
    /// surfaces, whose pixels now come from the offscreen copy while clicks on them must still
    /// reach their handlers.
    type PickOnlyApplicator<'msg>(child : Aardvark.UI.ISg<'msg>) =
        inherit Aardvark.UI.Sg.AbstractApplicator<'msg>(child)

    [<Rule>]
    type PickOnlySem() =
        member x.RenderObjects(a : PickOnlyApplicator<'msg>, scope : Ag.Scope) : aset<IRenderObject> =
            ASet.empty

    let pickOnly (sg : Aardvark.UI.ISg<'msg>) : Aardvark.UI.ISg<'msg> =
        PickOnlyApplicator<'msg>(sg) :> Aardvark.UI.ISg<'msg>
