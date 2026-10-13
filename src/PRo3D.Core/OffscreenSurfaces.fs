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
        /// copy is exact: same colour, same depth, edges keep their antialiasing.
        ///
        /// Everything is copied, depth 1 included: the depth is cleared before every surface
        /// priority group, so it only holds the last group - surfaces of earlier groups have
        /// colour but depth 1 there, exactly as when they were drawn into the view directly.
        /// Discarding depth-1 samples would drop them.
        let blit (v : BlitVertex) =
            fragment {
                let vp : V2i = uniform?ViewportSize
                let p = V2i (v.fc.XY * V2f depth.Size / V2f vp)
                return { c = color.Read(p, v.sid); d = depth.Read(p, v.sid).X }
            }

    /// `sg` rendered offscreen, its task compiled only while the result is acquired and
    /// disposed when it is released. The render control acquires its scene's resources per
    /// client and releases them when the client goes (aardvark.media compiles and disposes
    /// the scene task per client), so an offscreen pass built in the view function lives
    /// exactly as long as a client shows it. Compiled eagerly in the view function instead,
    /// the task - and all the surfaces' GPU data in it - outlived every page that used it.
    ///
    /// `signature` creates the framebuffer signature (with the task, disposed with it);
    /// `outputs` makes the render-to-texture resources of the compiled task; `acquire`,
    /// `release` and `get` handle them as a whole.
    ///
    /// Compute creates the task if it does not exist yet. Every consumer goes through a
    /// render task's resource manager, which acquires first; a bare GetValue without Acquire
    /// would compile a task nothing ever disposes.
    let renderTargets
        (runtime   : IRuntime)
        (signature : unit -> IFramebufferSignature)
        (sg        : ISg)
        (outputs   : IRenderTask -> 'o)
        (acquire   : 'o -> unit)
        (release   : 'o -> unit)
        (get       : 'o -> AdaptiveToken -> RenderToken -> 'a) : IAdaptiveResource<'a> =
        let current : ref<Option<IFramebufferSignature * IRenderTask * 'o>> = ref None
        let ensure () =
            match current.Value with
            | Some (_, _, o) -> o
            | None ->
                let s = signature ()
                let task = runtime.CompileRender(s, sg)
                let o = outputs task
                acquire o
                current.Value <- Some (s, task, o)
                o
        { new AdaptiveResource<'a>() with
            override x.Create() = ensure () |> ignore
            override x.Destroy() =
                match current.Value with
                | Some (s, task, o) ->
                    release o
                    task.Dispose()
                    s.Dispose()
                | None -> ()
                current.Value <- None
            override x.Compute(token, renderToken) =
                get (ensure ()) token renderToken
        } :> IAdaptiveResource<'a>

    /// A part of `parent`, holding `parent` for as long as it is held itself.
    let project (f : 'a -> 'b) (parent : IAdaptiveResource<'a>) : IAdaptiveResource<'b> =
        { new AdaptiveResource<'b>() with
            override x.Create() = parent.Acquire()
            override x.Destroy() = parent.Release()
            override x.Compute(token, renderToken) = f (parent.GetValue(token, renderToken))
        } :> IAdaptiveResource<'b>

    /// Colour and depth of the surfaces in `sg`, rendered at `size` with `samples` samples -
    /// the sample count of the target they are copied into (the copy is per sample) - over
    /// `background`, the target's own clear colour, so translucent surfaces blend as before.
    /// `sg` brings its own view and projection.
    let render (runtime : IRuntime) (samples : int) (background : C4f) (size : aval<V2i>) (sg : ISg) =
        let signature () =
            runtime.CreateFramebufferSignature(
                [
                    DefaultSemantic.Colors,       TextureFormat.Rgba8
                    DefaultSemantic.DepthStencil, TextureFormat.Depth24Stencil8
                ],
                samples)
        // never an empty target: a view reports its size only after its first resize event
        let size = size |> AVal.map (fun s -> V2i(max 1 s.X, max 1 s.Y))
        let targets =
            renderTargets runtime signature sg
                (RenderTask.renderToColorAndDepthWithClear size (clear { color background; depth 1.0; stencil 0 }))
                (fun (c, d) -> c.Acquire(); d.Acquire())
                (fun (c, d) -> c.Release(); d.Release())
                (fun (c, d) t rt -> struct(c.GetValue(t, rt) :> ITexture, d.GetValue(t, rt) :> ITexture))
        let color = targets |> project (fun (struct(c, _)) -> c)
        let depth = targets |> project (fun (struct(_, d)) -> d)
        color :> aval<ITexture>, depth :> aval<ITexture>

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
