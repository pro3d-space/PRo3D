namespace Aardvark.Opc

open System
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Application.Slim
open Aardvark.Data.Opc
open Aardvark.GeoSpatial.Opc
open Aardvark.GeoSpatial.Opc.Load

open FSharp.Data.Adaptive
open MBrace.FsPickler

/// Headless / interactive OPC renderer for reproducing surface rendering artefacts.
///
/// This exists because the interesting bugs in the OPC pipeline (precision loss at
/// planetary scale, driver differences between Apple Silicon and the Windows/Linux GL
/// stacks) only show up on real data from a specific camera. Reproducing them by hand
/// through the full viewer is slow and not repeatable; this renders one frame of one
/// dataset from one camera into a PNG.
///
/// The `EffectStack` ladder is the actual diagnostic tool: `Minimal` is barely more than
/// "put the triangles on screen", and each rung adds the next stage of the real viewer's
/// surface effect (`ViewerUtils.surfaceEffect`) in the same order. Rendering the same
/// camera at each rung says which stage introduces an artefact, without touching the
/// viewer. The shaders are the viewer's own — the shared ones were moved to
/// `PRo3D.Base.OpcSurfaceShader` for exactly this reason, so a rung cannot silently
/// diverge from what the viewer runs.
module ScreenshotViewer =

    /// How much of `ViewerUtils.surfaceEffect` to run. Cumulative: each rung is the
    /// previous one plus the next stage, in the viewer's own composition order.
    type EffectStack =
        /// stableTrafo + diffuse texture. If an artefact is already here it is in the
        /// geometry, the LOD tree or the texture path, not in PRo3D's shading.
        | Minimal
        /// + triangleSizeFilter, the view-space geometry shader that drops
        /// over-long triangles.
        | Filter
        /// + generateNormal, a second geometry shader composed onto the first.
        | Normals
        /// + planet-local lighting and the solar lighting fragment stage.
        | Lit
        /// + the colour adaption / radiometry fragment chain.
        | Color

    module EffectStack =
        let parse (s : string) =
            match s.ToLowerInvariant() with
            | "minimal" -> Some Minimal
            | "filter"  -> Some Filter
            | "normals" -> Some Normals
            | "lit"     -> Some Lit
            | "color" | "colour" -> Some Color
            | _ -> None

        let name =
            function
            | Minimal -> "minimal" | Filter -> "filter" | Normals -> "normals"
            | Lit -> "lit" | Color -> "color"

        /// The order the ladder is climbed in.
        let all = [ Minimal; Filter; Normals; Lit; Color ]

    /// Where the camera sits. `BearingPitch` mirrors what PRo3D's on-screen readout
    /// prints (position in body-fixed metres, bearing clockwise from north, pitch above
    /// the local horizon), so a bad frame in the viewer is reproduced by copying three
    /// numbers off the screen rather than by guessing at a look-at pair.
    type Camera =
        | BearingPitch of eye : V3d * bearingDeg : float * pitchDeg : float
        | LookAt of eye : V3d * target : V3d
        /// Framed from the dataset bounding box. Only useful as a smoke test — the
        /// precision artefacts want a grazing, near-surface view.
        | FromBoundingBox

    type Config =
        {
            scene       : OpcScene
            camera      : Camera
            size        : V2i
            outputDir   : string
            /// Base name; the stack name and `.png` are appended.
            name        : string
            /// Empty = climb the whole ladder in one run.
            stacks      : list<EffectStack>
            fillMode    : FillMode
            cullMode    : CullMode
            /// Open a window instead of rendering offscreen.
            interactive : bool
            /// Upper bound on render passes per screenshot while the LOD tree settles.
            maxFrames   : int
            /// Some size = switch the view-space triangle filter on with this
            /// MaxTriangleSize. The viewer defaults it off (SurfaceApp.mk), so it is off
            /// here too unless asked for.
            triangleFilter : Option<float>
            /// Some direction = enable solar lighting from this world-space direction.
            /// Without SPICE the viewer has no sun, so this is off by default -- with it
            /// off the `lit` rung passes the colour through unchanged.
            sun         : Option<V3d>
            /// MSAA sample count. The viewer runs the main control at 4 (Config
            /// data_samples), and there is already one GPU in the tree that needs it
            /// forced to 1, so this is worth varying.
            samples     : int
            /// Log the generated GLSL for each rung. The cheapest way to check what a
            /// shader's types actually became -- notably whether anything reached the
            /// GPU as `double`, which Apple Silicon has no hardware for.
            dumpGlsl    : bool
        }

    // ---------------------------------------------------------------- camera

    /// Body-fixed position + bearing/pitch -> CameraView.
    ///
    /// Up is the planetocentric radial direction rather than the ellipsoid normal. The
    /// two differ by well under a degree on Mars, which is irrelevant for framing a
    /// repro, and using the radial direction keeps this free of any ellipsoid/datum
    /// dependency.
    let cameraFromBearingPitch (eye : V3d) (bearingDeg : float) (pitchDeg : float) =
        let up = eye.Normalized
        let pole = V3d.OOI
        let east =
            let e = Vec.cross pole up
            if e.Length < 1e-9 then V3d.IOO else e.Normalized
        let north = Vec.cross up east |> Vec.normalize
        let b = bearingDeg * Constant.RadiansPerDegree
        let p = pitchDeg * Constant.RadiansPerDegree
        let horizontal = north * cos b + east * sin b
        let forward = (horizontal * cos p + up * sin p) |> Vec.normalize
        CameraView.lookAt eye (eye + forward * 1000.0) up

    let private cameraView (bb : Box3d) (camera : Camera) =
        match camera with
        | BearingPitch (eye, bearing, pitch) -> cameraFromBearingPitch eye bearing pitch
        | LookAt (eye, target) -> CameraView.lookAt eye target eye.Normalized
        | FromBoundingBox -> CameraView.lookAt bb.Max bb.Center bb.Center.Normalized

    // ---------------------------------------------------------------- effects

    /// Uniforms every rung needs bound. FShade throws at CompileRender on an unbound
    /// uniform, and the viewer supplies these from its model, so the harness has to
    /// stand in for it. Values are the viewer's "nothing switched on" defaults: the
    /// point is to isolate the shader stages, not to reproduce a particular UI state.
    let private applyDefaultUniforms (cfg : Config) (sg : ISg) =
        sg
        |> Sg.uniform "LodVisEnabled" (AVal.constant false)
        // triangleSizeFilter. Uploaded as a double, exactly as the viewer does
        // (`surf.triangleSize.value` is an aval<float>), so any CPU-side conversion
        // mismatch reproduces here too.
        |> Sg.uniform "MaxTriangleSize" (AVal.constant (defaultArg cfg.triangleFilter 10.0))
        |> Sg.uniform "FilterTriangleEnabled" (AVal.constant cfg.triangleFilter.IsSome)
        |> Sg.uniform "FilterByDistance" (AVal.constant false)
        |> Sg.uniform "FilterDistance" (AVal.constant 1000.0f)
        |> Sg.uniform "HomePositionViewSpace" (AVal.constant V3f.Zero)
        // image projection / shadow varyings the viewer's vertex stages write
        |> Sg.uniform "ProjectedImageModelViewProj" (AVal.constant M44f.Identity)
        |> Sg.uniform "ProjectedImageModelViewProjValid" (AVal.constant false)
        |> Sg.uniform "StableModelViewProjTexture" (AVal.constant M44f.Identity)
        |> Sg.uniform "NormalFlip" (AVal.constant 0.0f)
        // lighting
        |> Sg.uniform "SunLightEnabled" (AVal.constant cfg.sun.IsSome)
        |> Sg.uniform "SunDirectionWorld"
            (AVal.constant (V3f (defaultArg cfg.sun V3d.OOI |> Vec.normalize)))
        // colour adaption / radiometry, all switched off
        |> Sg.uniform "useContrastS" (AVal.constant false)
        |> Sg.uniform "contrastS" (AVal.constant 0.0f)
        |> Sg.uniform "useBrightnS" (AVal.constant false)
        |> Sg.uniform "brightnessS" (AVal.constant 0.0f)
        |> Sg.uniform "useGammaS" (AVal.constant false)
        |> Sg.uniform "gammaS" (AVal.constant 1.0f)
        |> Sg.uniform "useGrayS" (AVal.constant false)
        |> Sg.uniform "useColorS" (AVal.constant false)
        |> Sg.uniform "colorS" (AVal.constant V3f.III)
        |> Sg.uniform "useRadiometry" (AVal.constant false)
        |> Sg.uniform "abR" (AVal.constant (V3f(0.0f, 255.0f, 0.0f)))
        |> Sg.uniform "abG" (AVal.constant (V3f(0.0f, 255.0f, 0.0f)))
        |> Sg.uniform "abB" (AVal.constant (V3f(0.0f, 255.0f, 0.0f)))

    /// The ladder. Stage order matches `ViewerUtils.surfaceEffect` exactly; a rung is a
    /// prefix of that list, not a re-ordering of it.
    let effect (stack : EffectStack) =
        let vertexLighting =
            [ PRo3D.SPICE.Shaders.planetLocalLightingViewSpace |> toEffect
              PRo3D.Core.ImageProjection.Shaders.stableImageProjectionTrafo |> toEffect
              PRo3D.SPICE.Shaders.transformShadowVertices |> toEffect ]

        let transform = [ PRo3D.Base.OpcSurfaceShader.stableTrafo |> toEffect ]
        let sizeFilter = [ PRo3D.Base.OpcSurfaceShader.triangleSizeFilter |> toEffect ]
        let genNormal = [ PRo3D.Core.ImageProjection.Shaders.generateNormal |> toEffect ]
        let texture = [ PRo3D.Base.OPCFilter.improvedDiffuseTexture |> toEffect ]
        let colorChain =
            [ PRo3D.Base.Shader.mapColorAdaption |> toEffect
              PRo3D.Base.Shader.mapRadiometry |> toEffect ]
        let lighting = [ PRo3D.SPICE.Shaders.solarLighting |> toEffect ]

        let stages =
            match stack with
            | Minimal -> transform @ texture
            | Filter  -> transform @ sizeFilter @ texture
            | Normals ->
                [ PRo3D.Core.ImageProjection.Shaders.stableImageProjectionTrafo |> toEffect ]
                @ transform @ sizeFilter @ genNormal @ texture
            | Lit     -> vertexLighting @ transform @ sizeFilter @ genNormal @ texture @ lighting
            | Color   -> vertexLighting @ transform @ sizeFilter @ genNormal @ texture @ colorChain @ lighting

        FShade.Effect.compose stages

    // ---------------------------------------------------------------- scene graph

    /// Union of the hierarchies' global bounding boxes, so the default camera and the
    /// logged extents describe the data rather than a hand-copied literal.
    let boundingBox (scene : OpcScene) =
        let serializer = FsPickler.CreateBinarySerializer()
        let mutable bb = Box3d.Invalid
        for basePath in scene.patchHierarchies do
            let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
            let root =
                match h.tree with
                | QTree.Node (p, _) -> p
                | QTree.Leaf p -> p
            bb <- bb.ExtendedBy root.info.GlobalBoundingBox
        bb

    // ---------------------------------------------------------------- render

    /// Multisampled targets cannot be downloaded directly, so when `samples > 1` the
    /// render goes to an MSAA target and is resolved into a single-sample texture that
    /// the screenshot is read back from -- the same two-step the window path does
    /// internally. Returned as (signature, downloadTarget, output, resolve).
    let private createTarget (runtime : IRuntime) (size : V2i) (samples : int) =
        let samples = max 1 samples
        let res = V3i(size.X, size.Y, 1)
        let color = runtime.CreateTexture(res, TextureDimension.Texture2D, TextureFormat.Rgba8, 1, samples)
        let depth = runtime.CreateTexture(res, TextureDimension.Texture2D, TextureFormat.DepthComponent32f, 1, samples)
        let signature =
            runtime.CreateFramebufferSignature([
                DefaultSemantic.Colors, TextureFormat.Rgba8
                DefaultSemantic.DepthStencil, TextureFormat.DepthComponent32f
            ], samples)
        let fbo =
            runtime.CreateFramebuffer(
                signature,
                Map.ofList [
                    DefaultSemantic.Colors, color.GetOutputView()
                    DefaultSemantic.DepthStencil, depth.GetOutputView()
                ]) |> OutputDescription.ofFramebuffer
        if samples = 1 then
            signature, color, fbo, ignore
        else
            let resolved = runtime.CreateTexture(res, TextureDimension.Texture2D, TextureFormat.Rgba8, 1, 1)
            let resolve () =
                runtime.Copy(color.[TextureAspect.Color, 0, 0], V3i.Zero,
                             resolved.[TextureAspect.Color, 0, 0], V3i.Zero, V3i(size.X, size.Y, 1))
            signature, resolved, fbo, resolve

    /// Cheap content fingerprint, used to decide when the LOD tree has stopped refining.
    let private fingerprint (img : PixImage) =
        let data = img.ToPixImage<byte>().Volume.Data
        let mutable h = 17UL
        let mutable i = 0
        // every 7th byte: enough to notice a patch swapping in, cheap enough to run
        // once per warm-up frame
        while i < data.Length do
            h <- h * 1099511628211UL ^^^ uint64 data.[i]
            i <- i + 7
        h

    /// Render until the image stops changing, then download it.
    ///
    /// A fixed warm-up count is the usual approach but it is wrong in both directions
    /// here: too few passes and the screenshot shows a coarser LOD than the viewer would,
    /// too many and every run pays for the worst case. The LOD decider needs a rendered
    /// frame to decide against, so this just renders until two consecutive frames agree.
    let private renderStable (runtime : IRuntime) (signature : IFramebufferSignature)
                             (fbo : OutputDescription) (color : IBackendTexture)
                             (resolve : unit -> unit) (maxFrames : int) (sg : ISg) =
        use clear = runtime.CompileClear(signature, AVal.constant C4f.Black, AVal.constant 1.0)
        use task = runtime.CompileRender(signature, sg)
        let mutable last = 0UL
        let mutable stable = 0
        let mutable frame = 0
        let mutable image = Unchecked.defaultof<PixImage>
        while frame < maxFrames && stable < 2 do
            clear.Run(fbo)
            task.Run(fbo)
            resolve ()
            image <- runtime.Download(color)
            let h = fingerprint image
            if frame > 0 && h = last then stable <- stable + 1 else stable <- 0
            last <- h
            frame <- frame + 1
        Log.line "[render] settled after %d frame(s)%s" frame
            (if stable < 2 then sprintf " (NOT converged, hit --max-frames %d)" maxFrames else "")
        image

    let private save (dir : string) (name : string) (image : PixImage) =
        if not (Directory.Exists dir) then Directory.CreateDirectory dir |> ignore
        let path = Path.Combine(dir, name)
        image.Save(path)
        Log.line "[out] %s" path
        path

    // ---------------------------------------------------------------- entry point

    let run (cfg : Config) =

        Aardvark.Init()

        let debugConfig =
            if cfg.dumpGlsl then
                { Aardvark.Rendering.GL.DebugConfig.Normal with PrintShaderCode = true }
            else Aardvark.Rendering.GL.DebugConfig.Normal

        use app = new OpenGlApplication(debug = debugConfig)
        let runtime = app.Runtime
        Log.line "[gl] %s | %s" runtime.Context.Driver.vendor runtime.Context.Driver.renderer

        let bb = boundingBox cfg.scene
        Log.line "[opc] bbox centre %.1f %.1f %.1f  size %.1f %.1f %.1f m"
            bb.Center.X bb.Center.Y bb.Center.Z bb.Size.X bb.Size.Y bb.Size.Z

        let view = cameraView bb cfg.camera
        Log.line "[cam] eye %.2f %.2f %.2f  fwd %.4f %.4f %.4f"
            view.Location.X view.Location.Y view.Location.Z
            view.Forward.X view.Forward.Y view.Forward.Z
        Log.line "[cam] near %.3f far %.1f" cfg.scene.near cfg.scene.far

        let stacks = if List.isEmpty cfg.stacks then EffectStack.all else cfg.stacks

        // `asyncLoading = false`: a screenshot must not race the loader. The interactive
        // path keeps async loading so flying around stays responsive.
        let hierarchySgs signature runner asyncLoading =
            let serializer = FsPickler.CreateBinarySerializer()
            cfg.scene.patchHierarchies
            |> Seq.toList
            |> List.map (fun basePath ->
                let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                let t = PatchLod.toRoseTree h.tree
                Sg.patchLod signature runner basePath cfg.scene.lodDecider
                            cfg.scene.useCompressedTextures false ViewerModality.XYZ
                            PatchLod.CoordinatesMapping.Local asyncLoading t)

        let decorate signature runner asyncLoading (stack : EffectStack)
                     (viewTrafo : aval<Trafo3d>) (projTrafo : aval<Trafo3d>) =
            hierarchySgs signature runner asyncLoading
            |> Sg.ofList
            |> Sg.viewTrafo viewTrafo
            |> Sg.projTrafo projTrafo
            |> Sg.effect [ effect stack ]
            |> applyDefaultUniforms cfg
            |> Sg.fillMode (AVal.constant cfg.fillMode)
            |> Sg.cullMode (AVal.constant cfg.cullMode)

        if cfg.interactive then
            let stack = List.head (stacks @ [ Minimal ])
            Log.line "[stack] %s (interactive)" (EffectStack.name stack)
            let win = app.CreateGameWindow()
            let runner = runtime.CreateLoadRunner 1
            let speed = AVal.init cfg.scene.speed
            let controlled = view |> DefaultCameraController.controlWithSpeed speed win.Mouse win.Keyboard win.Time
            let frustum =
                win.Sizes |> AVal.map (fun s ->
                    Frustum.perspective 60.0 cfg.scene.near cfg.scene.far (float s.X / float s.Y))

            win.Keyboard.KeyDown(Keys.PageUp).Values.Add(fun _ ->
                transact (fun _ -> speed.Value <- speed.Value * 1.5))
            win.Keyboard.KeyDown(Keys.PageDown).Values.Add(fun _ ->
                transact (fun _ -> speed.Value <- speed.Value / 1.5))

            let sg =
                decorate win.FramebufferSignature runner true stack
                    (controlled |> AVal.map CameraView.viewTrafo)
                    (frustum |> AVal.map Frustum.projTrafo)
            win.RenderTask <- runtime.CompileRender(win.FramebufferSignature, sg)
            win.Run()
            0
        else
            let signature, color, fbo, resolve = createTarget runtime cfg.size cfg.samples
            let runner = runtime.CreateLoadRunner 1
            let aspect = float cfg.size.X / float cfg.size.Y
            let frustum = Frustum.perspective 60.0 cfg.scene.near cfg.scene.far aspect
            let viewTrafo = AVal.constant (CameraView.viewTrafo view)
            let projTrafo = AVal.constant (Frustum.projTrafo frustum)

            for stack in stacks do
                Log.line "[stack] %s" (EffectStack.name stack)
                let sg = decorate signature runner false stack viewTrafo projTrafo
                let image = renderStable runtime signature fbo color resolve cfg.maxFrames sg
                save cfg.outputDir (sprintf "%s-%s.png" cfg.name (EffectStack.name stack)) image
                |> ignore
            0
