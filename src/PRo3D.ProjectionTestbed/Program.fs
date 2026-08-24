module PRo3D.ProjectionTestbed.Program

open System
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Application.Slim
open FSharp.Data.Adaptive

open Aardvark.GeoSpatial.Opc
open Aardvark.GeoSpatial.Opc.Load

open PRo3D.Core
open PRo3D.ImageMapping
open PRo3D.InstrumentVisualization

/// An OPC directory holds its patch hierarchies as immediate subdirectories -- but not
/// every subdirectory is one. Real data folders sit next to saved scenes and annotation
/// files (the Dimorphos_DRACO1 folder ships a `testdimo.pro3d` alongside the OPC), and
/// handing one of those to PatchHierarchy.load throws "patches dir not found". A
/// hierarchy is identified by containing a `Patches` directory.
let private patchHierarchiesOf (opcPath : string) =
    Seq.delay (fun _ ->
        Directory.GetDirectories opcPath
        |> Array.filter (fun d -> Directory.Exists(Path.Combine(d, "Patches")))
        :> seq<_>)

/// The projection shader chain. Order matters twice over:
///   - stableImageProjectionTrafo must precede stableTrafo, because it stashes the
///     object-space position while [<Position>] still holds it.
///   - generateNormal is a geometry shader and builds the face normal from that stashed
///     position, not from [<Position>], so the front-facing test does not depend on the
///     render camera.
let private applyProjectionShaders (flipNormals : bool) (sg : ISg) =
    sg
    |> Sg.shader {
        do! ImageProjection.Shaders.stableImageProjectionTrafo
        do! ImageProjection.Shaders.generateNormal
        do! ImageProjection.Shaders.applyNormalFlip
        if flipNormals then
            do! ImageProjection.Shaders.flipNormals
        do! PRo3D.SPICE.Shaders.stableTrafo
        do! DefaultSurfaces.constantColor C4f.White
        do! DefaultSurfaces.diffuseTexture
        do! ImageProjection.Shaders.stableImageProjection
    }

/// Same geometry and the same normals as the projection chain, but lit by the sun
/// instead of textured with the instrument image. generateNormal is still required --
/// it is what produces the per-face normal the diffuse term needs.
type ShadeMode =
    | AsComputed
    | Inverted
    | DebugNormal
    | DebugOutward
    | DebugOutwardSign
    | DebugModelTrafo
    | AngleIncidence
    | AngleEmission
    | AnglePhase

let private applyShadingShaders (mode : ShadeMode) (sg : ISg) =
    sg
    |> Sg.shader {
        do! ImageProjection.Shaders.stableImageProjectionTrafo
        do! ImageProjection.Shaders.generateNormal
        do! ImageProjection.Shaders.applyNormalFlip
        do! PRo3D.SPICE.Shaders.stableTrafo
        match mode with
        | AsComputed  -> do! Shading.sunDiffuse
        | Inverted    -> do! Shading.sunDiffuseInverted
        | DebugNormal -> do! Shading.debugNormal
        | DebugOutward -> do! Shading.debugOutward
        | DebugOutwardSign -> do! Shading.debugOutwardSign
        | DebugModelTrafo -> do! Shading.debugModelTrafo
        | AngleIncidence | AngleEmission | AnglePhase -> ()
    }

/// generateNormal now orients outward per triangle, so the angles are a property of the
/// geometry alone -- not of the dataset's winding, and not of where the camera happens to
/// be. That is what lets emission legitimately exceed 90 degrees as a quality signal.
let private applyAngleShaders (mode : ShadeMode) (sg : ISg) =
    sg
    |> Sg.shader {
        do! ImageProjection.Shaders.stableImageProjectionTrafo
        do! ImageProjection.Shaders.generateNormal
        do! ImageProjection.Shaders.applyNormalFlip
        do! PRo3D.SPICE.Shaders.stableTrafo
        match mode with
        | AngleEmission -> do! Shading.angleEmission
        | AnglePhase -> do! Shading.anglePhase
        | _ -> do! Shading.angleIncidence
    }

let private run (s : Scenario) (img : ResolvedImage) (cam : ProjectorCamera) =
    Aardvark.Init()
    use app = new OpenGlApplication()

    let runtime = app.Runtime
    let size =
        if s.width > 0 && s.height > 0 then V2i(s.width, s.height)
        else
            match img.size with
            | Some sz -> sz
            | None -> V2i(1024, 1024)
    Log.line "[render] %dx%d (frustum aspect %.4f)" size.X size.Y (float size.X / float size.Y)

    // Extrinsics check that needs NO reference image, and so is free of the circularity
    // in every metric that compares the projected texture to its own source.
    //
    // The mbi sidecar's sc_quat, applied to the instrument's +Z boresight, reproduces the
    // TRG_POS direction to ~1e-4 rad: the boresight points at the target centre to better
    // than half a pixel. Therefore a correct chain MUST project the target body's centre
    // (the origin of its body-fixed frame) onto the principal point, i.e. NDC (0,0).
    // Any deviation here is our error, measured directly rather than inferred from an
    // image comparison.
    // Cross-check the sidecar's camera position against SPICE, WITHOUT needing a CK.
    //
    // --method spice fails here because getLookAt asks for the state in the INSTRUMENT
    // frame (MILANI_ASPECT_NIR1), which needs Milani attitude. Asking for it in J2000
    // instead needs only SPK, so the position half of the extrinsics can still be checked
    // independently. The attitude half cannot -- but it is already validated internally:
    // sc_quat's +Z boresight reproduces TRG_POS to ~1e-4 rad.
    //
    // Hera's position is fetched too, to quantify why AFC's pointing cannot stand in for
    // ASPECT's: they are different spacecraft, and the separation is the parallax error
    // that substitution would introduce.
    // Validate our SPICE time handling against spiceypy: log the DIDYMOS_FIXED->J2000
    // rotation our native path produces for this epoch. Compared offline to spiceypy's
    // pxform at the correctly-converted UTC vs a 69 s (UTC-as-TDB) error, this shows
    // directly whether our leap-second/UTC->ET conversion is right.
    match PRo3D.SPICE.CooTransformation.getRotationTrafo s.referenceFrame "J2000" img.mbi.obs_date with
    | Some t ->
        let f = t.Forward
        Log.line "[spicecmp] %s->J2000 forward:" s.referenceFrame
        Log.line "[spicecmp]   row0 %.6f %.6f %.6f" f.M00 f.M01 f.M02
        Log.line "[spicecmp]   row1 %.6f %.6f %.6f" f.M10 f.M11 f.M12
        Log.line "[spicecmp]   row2 %.6f %.6f %.6f" f.M20 f.M21 f.M22
    | None -> Log.warn "[spicecmp] rotation unavailable"

    let sidecarCamPos = -img.mbi.targetPos * 1000.0
    match PRo3D.SPICE.CooTransformation.getRelState "MILANI" "SUN" s.body img.mbi.obs_date "J2000" with
    | Some st ->
        let d = st.pos - sidecarCamPos
        Log.line "[check] camera position, sidecar vs SPICE (J2000, metres):"
        Log.line "[check]   sidecar %.1f %.1f %.1f  (|r| %.1f)"
            sidecarCamPos.X sidecarCamPos.Y sidecarCamPos.Z sidecarCamPos.Length
        Log.line "[check]   SPICE   %.1f %.1f %.1f  (|r| %.1f)" st.pos.X st.pos.Y st.pos.Z st.pos.Length
        Log.line "[check]   delta   %.1f m  (%.2f mrad at this range, %.1f px)"
            d.Length (d.Length / sidecarCamPos.Length * 1000.0)
            (d.Length / sidecarCamPos.Length / 0.116937 * 640.0)
    | None ->
        Log.warn "[check] no SPK for MILANI relative to %s -- position cross-check skipped" s.body

    match PRo3D.SPICE.CooTransformation.getRelState "HERA" "SUN" s.body img.mbi.obs_date "J2000" with
    | Some st ->
        let sep = (st.pos - sidecarCamPos).Length
        Log.line "[check] HERA is %.1f m from %s; separation from Milani %.1f m"
            st.pos.Length s.body sep
        Log.line "[check]   -> substituting AFC pointing for ASPECT would introduce that"
        Log.line "[check]      separation as parallax (%.1f px at this scale)"
            (sep / sidecarCamPos.Length / 0.116937 * 640.0)
    | None ->
        Log.line "[check] no SPK for HERA at this epoch"

    // Does the shape model's centre of figure explain the residual silhouette offset?
    // The OPC is placed with its origin at the SPICE body point, so a non-zero centroid
    // is the model being off-centre (incomplete coverage, or COF vs the COM SPICE tracks).
    // Project it into the image and compare against the measured per-body pixel shift.
    match patchHierarchiesOf s.opcPath |> Seq.tryHead |> Option.bind OpcSg.modelCenterOfFigure with
    | Some cof ->
        let ndc = cam.full.Forward.TransformPosProj cof
        let px = ndc.X * 0.5 * float size.X
        let py = ndc.Y * 0.5 * float size.Y
        Log.line "[model] %s area-weighted centre of figure is %.1f m off origin (%.1f %.1f %.1f)"
            s.body cof.Length cof.X cof.Y cof.Z
        Log.line "[model]   projects to %+.1f %+.1f px from principal point" px py
    | None ->
        Log.warn "[model] could not estimate %s centre of figure" s.body

    let ndcCentre = cam.full.Forward.TransformPosProj V3d.Zero
    let offPxX = ndcCentre.X * 0.5 * float size.X
    let offPxY = ndcCentre.Y * 0.5 * float size.Y
    Log.line "[check] body centre -> NDC %+.5f %+.5f  =  %+.2f %+.2f px from principal point"
        ndcCentre.X ndcCentre.Y offPxX offPxY
    // NaN first: every comparison against NaN is false, so a threshold test alone reports
    // a NaN chain as "consistent". This check was written that way and did exactly that.
    if Double.IsNaN offPxX || Double.IsNaN offPxY then
        Log.error "[check] the projection matrix is NaN -- the pointing did not resolve"
    elif abs offPxX > 1.0 || abs offPxY > 1.0 then
        Log.warn "[check] the target centre does NOT land on the principal point."
        Log.warn "[check] sc_quat's boresight points at the target to <0.5 px, so this"
        Log.warn "[check] offset is introduced by our own trafo chain, not by the data."
    else
        Log.line "[check] target centre lands on the principal point -- extrinsics consistent"

    // Screenshot mode must load patches synchronously, otherwise the captured frame shows
    // whatever subset of the LOD tree happened to arrive in time.
    let asyncLoading = (s.mode = Interactive)

    let projectedTexture =
        PRo3D.InstrumentProjection.Visualization.createProjectedTexture
            (AVal.constant (Some (img.path, img.metadata)))
            (AVal.constant { idx = s.channel; name = None })

    // Without this the remap uses the default 0..1 range, but instrument data does not
    // fill it -- this ASPECT band peaks at 0.196 -- so everything renders at a fifth of
    // its brightness and the comparison measures exposure instead of geometry.
    let visualizationRange =
        match img.metadata with
        | _, Some meta when meta.image_statistics.Length > s.channel ->
            let st = meta.image_statistics.[s.channel]
            Log.line "[image] band %d range %.4f .. %.4f" s.channel st.minimum st.maximum
            Range1d(st.minimum, st.maximum)
        | _ ->
            Log.warn "[image] no statistics for band %d; falling back to unit range" s.channel
            Range1d.Unit

    let imageSettings =
        { VisualizationProperties.empty with
            projectionOpacity = AVal.constant 1.0
            visualizationRange = AVal.constant visualizationRange
            instrumentImage = projectedTexture }

    // The sun vector is only needed by the shaded pass; a failure here must not stop the
    // projection comparison, so it degrades to "no shaded pass" rather than to an
    // arbitrary direction that would silently fake the independent evidence.
    let sunDir = Setup.sunDirection s.referenceFrame s.body img.mbi.obs_date
    match sunDir with
    | Ok d -> Log.line "[sun] direction in %s: %.4f %.4f %.4f" s.referenceFrame d.X d.Y d.Z
    | Result.Error e -> Log.warn "[sun] %s -- shaded pass disabled" e

    // Must go through this record, not Sg.uniform': ImageProjectionOpcRendering's
    // projectionUniformMap installs SunDirectionWorld as a PER-PATCH uniform sourced from
    // here, defaulting to V3d.Zero when this is None. Being per-patch it sits deeper in
    // the graph than any Sg.uniform' wrapped around the outside, so it wins -- an outer
    // uniform of the same name is silently ignored.
    let projectedImages : aval<Option<Sg.ProjectedImages>> =
        AVal.constant (
            Some {
                imageProjection = AVal.constant (Some cam.full)
                localImageProjectionTrafos = AVal.constant [||]
                sunDirection = AVal.constant (match sunDir with Ok d -> Some d | Result.Error _ -> None)
                sunLightEnabled = AVal.constant (match sunDir with Ok _ -> true | Result.Error _ -> false)
                lightViewProj = AVal.constant None
            })

    // Convert a requested image-plane correction (pixels, +x right, +y up) into a world
    // translation, by probing the actual projection rather than assuming a FOV or a sign:
    // project the origin and two 1 m camera-basis probes, then solve the 2x2 for the world
    // vector that produces the requested pixel shift. This is robust to the render's Y
    // convention because it uses the same cam.full the render uses.
    let modelOffsetWorld =
        if s.modelOffsetPx = V2d.Zero then V3d.Zero
        else
            let camRight = cam.view.Backward.TransformDir V3d.IOO |> Vec.normalize
            let camUp    = cam.view.Backward.TransformDir V3d.OIO |> Vec.normalize
            let projPx (w : V3d) =
                let n = cam.full.Forward.TransformPosProj w
                V2d(n.X * 0.5 * float size.X, n.Y * 0.5 * float size.Y)
            let p0 = projPx V3d.Zero
            let dR = projPx camRight - p0
            let dU = projPx camUp - p0
            let m = M22d(dR.X, dU.X, dR.Y, dU.Y)
            let ab = m.Inverse * s.modelOffsetPx
            let w = ab.X * camRight + ab.Y * camUp
            Log.line "[offset] %.2f %.2f px -> world %.2f %.2f %.2f m (|%.2f| m, %.2f m/px)"
                s.modelOffsetPx.X s.modelOffsetPx.Y w.X w.Y w.Z w.Length (w.Length / s.modelOffsetPx.Length)
            w

    // None = project the instrument image; Some mode = sun-lit, with mode choosing the
    // face-normal sign.
    let buildSg (shading : Option<ShadeMode>) (signature : IFramebufferSignature) =
        let runner = runtime.CreateLoadRunner 1
        let cfg =
            { OpcSg.defaultConfig signature runner DefaultMetrics.mars2 s.body with
                asyncLoading = asyncLoading }
        // Secondary bodies carry their own body-fixed frame, so each gets a SPICE-derived
        // trafo into the primary's frame. A body whose ephemeris or orientation is
        // missing is skipped loudly rather than silently placed at the origin, which
        // would look like a catastrophic geometry bug.
        let extras =
            s.extraBodies
            |> List.choose (fun (name, frame, path) ->
                if not (Directory.Exists path) then
                    Log.warn "[body] %s: OPC not found at %s -- skipping" name path
                    None
                else
                    match Setup.secondaryBodyTrafo s.referenceFrame s.body name frame
                              img.mbi.obs_date s.spicePositionScale with
                    | Result.Error e ->
                        Log.warn "[body] %s: %s -- skipping" name e
                        None
                    | Ok (trafo, rawPos) ->
                        Log.line "[body] %s placed at %.1f m from %s (raw SPICE |pos| = %.4f)"
                            name (trafo.Forward.TransformPos(V3d.Zero)).Length s.body rawPos.Length
                        let bodyCfg = { cfg with body = name }
                        OpcSg.build bodyCfg projectedImages imageSettings (patchHierarchiesOf path)
                        |> Sg.ofList
                        |> Sg.trafo (AVal.constant trafo)
                        |> Some)

        (OpcSg.build cfg projectedImages imageSettings (patchHierarchiesOf s.opcPath) |> Sg.ofList)
        :: extras
        |> Sg.ofList
        // Rigid registration correction. Applied to the geometry, so it moves the shaded
        // silhouette and topography together (the projected-texture pass would slide the
        // texture instead, but that comparison is circular anyway).
        |> Sg.trafo (AVal.constant (Trafo3d.Translation modelOffsetWorld))
        |> (match shading with
            | Some ((AngleIncidence | AngleEmission | AnglePhase) as mode) -> applyAngleShaders mode
            | Some mode -> applyShadingShaders mode
            | None -> applyProjectionShaders s.flipNormals)
        |> Sg.texture "ProjectedTexture" projectedTexture
        |> Sg.uniform' "ProjectedImageModelViewProjValid" true
        |> Sg.uniform' "LodVisEnabled" false
        // The OPC shaders inherit FootprintVP / secondary-texture attributes whether or
        // not this tool uses them; without these the Ag lookup throws at CompileRender.
        |> PRo3D.Core.Surface.Sg.applyFootprint (AVal.constant M44d.Identity)
        // Cross-section clipping (added in releases/6.0.0) is another OPC-surface Ag
        // attribute; without it CompileRender throws "could not get inh CrossSectionData".
        |> PRo3D.Core.SgExtensions.Sg.applyCrossSection (AVal.constant None)
        |> Aardvark.GeoSpatial.Opc.SecondaryTexture.Sg.applySecondaryTextureId
            (AVal.constant (Some { texture = TextureReference.LegacyId 0
                                   channel = ChannelReference.NoChannelSelection }))

    match s.mode with
    | Interactive ->
        use win = app.CreateGameWindow(8)
        let sg =
            buildSg (if s.shaded then Some AsComputed else None) win.FramebufferSignature
            |> Sg.viewTrafo (AVal.constant cam.view)
            |> Sg.projTrafo (AVal.constant cam.proj)
        win.RenderTask <- runtime.CompileRender(win.FramebufferSignature, sg)
        win.Run()
        0

    | Screenshot ->
        let target = Offscreen.createTarget runtime size
        let withCamera sg =
            sg
            |> Sg.viewTrafo (AVal.constant cam.view)
            |> Sg.projTrafo (AVal.constant cam.proj)

        let rendered = Offscreen.render target 4 (buildSg None target.signature |> withCamera)
        let renderPath = Offscreen.save s.outputDir "render.png" rendered
        let renderedGray = Compare.ofPixImage rendered

        // The independent check. Rendered from the same camera but lit by the sun with no
        // instrument image involved, so unlike the projected-texture comparison this one
        // cannot be satisfied by arbitrary geometry. Both normal signs are rendered
        // because which one is outward-facing is not safe to assume -- see Shading.fs.
        let shadedVariants =
            match sunDir with
            | Result.Error _ -> []
            | Ok _ ->
                Log.line "[angles] colormap: blue=0 -> cyan -> green -> yellow -> red=max"
                Log.line "[angles] incidence/emission scaled 0..90 deg, phase 0..180 deg"
                Log.line "[angles] brightness = projected instrument image (band %d)" s.channel
                Log.warn "[angles] NO shadow ray casting: cos(i)>0 is assumed lit, so any"
                Log.warn "[angles] self-shadowed region in these images is WRONG."
                for mode, file in [ DebugNormal,      "debug_normal.png"
                                    DebugOutward,     "debug_outward.png"
                                    DebugOutwardSign, "debug_outward_sign.png"
                                    DebugModelTrafo,  "debug_modeltrafo.png"
                                    AngleIncidence,   "angle_incidence.png"
                                    AngleEmission,    "angle_emission.png"
                                    AnglePhase,       "angle_phase.png" ] do
                    Offscreen.render target 4 (buildSg (Some mode) target.signature |> withCamera)
                    |> Offscreen.save s.outputDir file
                    |> ignore
                [ AsComputed, "as-computed", "shaded.png"
                  Inverted,   "inverted",    "shaded_inverted.png" ]
                |> List.map (fun (mode, name, file) ->
                    let sh = Offscreen.render target 4 (buildSg (Some mode) target.signature |> withCamera)
                    Offscreen.save s.outputDir file sh |> ignore
                    name, file, Compare.ofPixImage sh)

        match Compare.loadReference img.path s.channel with
        | Result.Error e ->
            Log.warn "[compare] reference not loaded: %s" e
            Log.warn "[compare] wrote %s but could not score it" renderPath
            0
        | Ok reference ->
            Offscreen.save s.outputDir "reference.png" (Compare.toPixImage reference) |> ignore
            Offscreen.save s.outputDir "sidebyside.png" (Compare.sideBySide renderedGray reference) |> ignore
            Log.line "[compare] sidebyside.png: LEFT = render (projected texture), RIGHT = reference"
            Log.line "[compare] overlay.png:    RED = render, GREEN = reference, YELLOW = both"

            let results =
                if s.flipSweep then Compare.sweep renderedGray reference
                else [ AsIs, Compare.ncc renderedGray reference ]

            Log.line ""
            Log.line "  orientation      NCC"
            Log.line "  -----------      ---"
            for (o, score) in results do
                Log.line "  %-14s %+.4f" (Compare.orientationName o) score

            // The honest metrics. NCC over the disk is near-guaranteed when the render
            // camera IS the projector camera, so report silhouette agreement separately
            // and do not let the NCC headline the result.
            let threshold = 0.15
            Offscreen.save s.outputDir "overlay.png" (Compare.overlay threshold renderedGray reference) |> ignore
            let iou = Compare.silhouetteIoU threshold renderedGray reference
            Log.line ""
            Log.line "  silhouette IoU   %.4f   <- geometry; the NCC above is largely self-fulfilling" iou
            match Compare.centroid threshold renderedGray, Compare.centroid threshold reference with
            | Some (cr, nr), Some (cf, nf) ->
                let d = cr - cf
                Log.line "  centroid offset  %+.1f, %+.1f px  (|%.1f|)" d.X d.Y d.Length
                Log.line "  covered pixels   render %d / reference %d  (%.3f x)"
                    nr nf (float nr / float nf)
                // Decompose against the principal point (image centre). If the SILHOUETTE
                // centroid follows the projected vertex centre-of-figure, a rigid origin
                // shift explains the residual; if it does not, the offset is the model's
                // incomplete/asymmetric outline, not a placement error.
                let pp = V2d(float size.X / 2.0 - 0.5, float size.Y / 2.0 - 0.5)
                Log.line "  render   silhouette centroid %+.1f %+.1f px from principal point"
                    (cr.X - pp.X) (cr.Y - pp.Y)
                Log.line "  ref      silhouette centroid %+.1f %+.1f px from principal point"
                    (cf.X - pp.X) (cf.Y - pp.Y)
                Log.line "  -> compare render's to the [model] vertex-CoF projection above"
            | _ ->
                Log.warn "  centroid: one of the images has no pixels above threshold %.2f" threshold

            let dx, dy, scale, alignedIoU = Compare.bestFitAlignment threshold renderedGray reference
            let gain = alignedIoU - iou
            Log.line ""
            Log.line "  best-fit align   dx %+.0f dy %+.0f  scale %.2f  -> IoU %.4f (%+.4f)"
                dx dy scale alignedIoU gain
            if gain < 0.02 then
                Log.line "  -> alignment recovers almost nothing: the residual is model coverage/shape,"
                Log.line "     NOT pointing. The raw centroid offset above is an artifact of the"
                Log.line "     incomplete shape model, not a geometry error."
            else
                Log.warn "  -> alignment recovers %+.4f IoU at dx %+.0f dy %+.0f scale %.2f;" gain dx dy scale
                Log.warn "     that part IS a real pointing/scale error, not missing coverage."

            // The independent check. Everything above compares the projected texture to
            // the image it came from, with the render camera equal to the projector
            // camera -- so it largely measures itself. This does not: the sun-lit render
            // never sees the reference image, so structural agreement here is evidence
            // about the shape model rather than about the projection matrix.
            match shadedVariants with
            | [] -> Log.warn "  (no shaded pass -- sun direction unavailable)"
            | variants ->
                let selfCorr, _ = Compare.nccMasked threshold renderedGray reference
                Log.line ""
                Log.line "  === independent check: sun-lit model vs instrument image ==="
                Log.line "  normal sign    masked NCC   px"
                Log.line "  -----------    ----------   --"
                let scored =
                    variants |> List.map (fun (name, file, sh) ->
                        let corr, n = Compare.nccMasked threshold sh reference
                        Log.line "  %-12s   %+.4f   %d" name corr n
                        name, file, sh, corr, n)
                Log.line "  (projected-texture masked NCC %+.4f, for contrast -- circular)" selfCorr

                // Choose by lit coverage, NOT by score. The outward-facing sign is the one
                // that lights the body; the inward one lights only the handful of steep
                // limb faces that happen to point the other way. That tiny sample can post
                // a higher correlation while meaning nothing, so ranking by NCC picks the
                // wrong sign.
                let bestName, bestFile, bestSh, bestCorr, bestN =
                    scored |> List.maxBy (fun (_, _, _, _, n) -> n)
                Offscreen.save s.outputDir "shaded_sidebyside.png"
                    (Compare.sideBySide bestSh reference) |> ignore
                // The non-circular registration check: sun-lit model in red, real image in
                // green. Unlike overlay.png this cannot be satisfied by the projection
                // being self-consistent, because the reference image plays no part in
                // producing the red channel. Caveat: it still thresholds BRIGHTNESS, so
                // the terminator position depends on the lighting model as well as on the
                // geometry -- read the limb, not the shading.
                Offscreen.save s.outputDir "shaded_overlay.png"
                    (Compare.overlay threshold bestSh reference) |> ignore

                // The pure-geometry check. Model thresholded just above background (the
                // shader's ambient floor is 0.05, so 0.02 captures the whole rendered disk
                // including the unlit side); reference at the normal body/space threshold.
                // Decouples silhouette from the lighting model entirely.
                let modelThr = 0.02
                Offscreen.save s.outputDir "shaded_silhouette_overlay.png"
                    (Compare.silhouetteOverlay modelThr threshold bestSh reference) |> ignore
                let nModel, nRef, siou =
                    Compare.silhouetteStats modelThr threshold bestSh reference
                Log.line ""
                Log.line "  === silhouette only (lighting-independent) ==="
                Log.line "  shaded_silhouette_overlay.png: GREY = both, RED = model only, GREEN = reference only"
                Log.line "  mask IoU         %.4f" siou
                Log.line "  model %d px / reference %d px  (%.3f x)" nModel nRef (float nModel / float nRef)
                Log.line "  -> area ratio here is SHAPE, not shading."
                Log.line "  -> NOT directly comparable to the brightness IoU above: that one"
                Log.line "     masks both sides at the same threshold, this one does not."
                Log.warn "  -> the anti-sunward limb is unreliable: the REFERENCE falls below"
                Log.warn "     its threshold there while the model (at ambient) does not, so"
                Log.warn "     red on the dark limb is partly artifact. Read the lit limb."

                // Is the apparent offset real? A centroid difference cannot tell a genuine
                // displacement from asymmetric mask erosion. A translation that actually
                // improves overlap can.
                let bdx, bdy, bIoU, baseIoU =
                    Compare.silhouetteBestFit modelThr threshold bestSh reference 12
                Log.line ""
                Log.line "  best-fit shift   dx %+d dy %+d  -> IoU %.4f (from %.4f, %+.4f)"
                    bdx bdy bIoU baseIoU (bIoU - baseIoU)
                if abs bdx <= 1 && abs bdy <= 1 then
                    Log.line "  -> no real translation error; apparent offsets are thresholding"
                else
                    Log.warn "  -> REAL registration error of %+d, %+d px:" bdx bdy
                    Log.warn "     a translation genuinely improves mask overlap by %+.4f" (bIoU - baseIoU)

                // Per-body offsets separate a boresight ROLL from a POINTING error.
                // A roll rotates the image about its centre, so it displaces an off-axis
                // body far more than a near-centre one, and in a different direction.
                // A pointing error (or a spacecraft position error) translates everything
                // rigidly, giving both bodies the same vector.
                let refG = reference
                let halfW = bestSh.width / 4
                let regions =
                    [ "secondary (off-axis)", 0, 0, halfW, bestSh.height
                      "primary (near centre)", halfW, 0, bestSh.width - halfW, bestSh.height ]
                Log.line ""
                Log.line "  per-body best-fit shift (roll vs pointing discriminator):"
                for (name, rx, ry, rw, rh) in regions do
                    let a = Compare.crop rx ry rw rh bestSh
                    let b = Compare.crop rx ry rw rh (Compare.resampleTo bestSh.width bestSh.height refG)
                    let dx, dy, io, bs = Compare.silhouetteBestFit modelThr threshold a b 12
                    Log.line "    %-22s dx %+d dy %+d   IoU %.4f (from %.4f)" name dx dy io bs
                Log.line "    -> same vector = pointing/position error; different = roll"
                Log.line "  shaded_sidebyside.png: LEFT = %s (%s), RIGHT = real image" bestFile bestName
                if bestN < 1000 then
                    Log.warn "  -> only %d common lit pixels: the model is barely lit at all," bestN
                    Log.warn "     so this says nothing yet. Check the sun direction."
                elif bestCorr > 0.5 then
                    Log.line "  -> surface structure genuinely agrees; the relief is real"
                elif bestCorr > 0.2 then
                    Log.line "  -> weak but positive: gross shading agrees, fine relief may not"
                else
                    Log.warn "  -> no structural agreement. Either the shape model lacks the"
                    Log.warn "     relief visible in the image, or the sun direction is wrong."

            let best, bestScore = List.head results
            Log.line ""
            if bestScore < 0.2 then
                Log.warn "[compare] best score %.4f (%s) is very low -- this is not a flip."
                    bestScore (Compare.orientationName best)
                Log.warn "[compare] suspect the trafo chain: specialTrafos[%s] is Identity and uncalibrated"
                    img.spiceName
            elif best <> AsIs then
                Log.warn "[compare] best orientation is %s (%.4f), not as-is -- UV convention mismatch"
                    (Compare.orientationName best) bestScore
            else
                Log.line "[compare] best orientation is as-is (%.4f)" bestScore
            0

[<EntryPoint>]
let main argv =
    let result =
        Scenario.parse Scenario.didymosAspect argv
        |> Result.bind Scenario.validate

    match result with
    | Result.Error msg ->
        printfn "%s" msg
        1
    | Ok s ->
        match Setup.resolveImage s with
        | Result.Error e -> Log.error "%s" e; 1
        | Ok img0 ->
            // Apply the test time offset (if any) to every downstream SPICE call by
            // shifting the epoch at the source.
            let img =
                if s.timeOffsetSec = 0.0 then img0
                else
                    Log.warn "[time] shifting epoch by %+.1f s for this run" s.timeOffsetSec
                    { img0 with mbi = { img0.mbi with obs_date = img0.mbi.obs_date.AddSeconds s.timeOffsetSec } }
            Log.line "[image] %s" img.path
            Log.line "[image] instrument %s -> %s, obs %s"
                img.mbi.instrument img.spiceName (img.mbi.obs_date.ToString "o")

            match Setup.resolveKernel s img with
            | Result.Error e -> Log.error "%s" e; 1
            | Ok kernel ->
                use _spice = SpiceBoot.init (Some kernel)
                match Setup.projectorCamera s img with
                | Result.Error e -> Log.error "%s" e; 1
                | Ok cam ->
                    Log.line "[geom] target distance %.1f m, near %.1f, far %.1f"
                        cam.distance cam.near cam.far
                    run s img cam
