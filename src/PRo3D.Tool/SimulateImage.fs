module PRo3D.Tool.SimulateImageVerb

open System
open System.Globalization
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application.Slim
open FSharp.Data.Adaptive

open Aardvark.Data.Opc
open Aardvark.GeoSpatial.Opc
open Aardvark.GeoSpatial.Opc.Load   // IRuntime.CreateLoadRunner

open MBrace.FsPickler

open PRo3D.Core
open PRo3D.SPICE
open PRo3D.ImageMapping
open PRo3D.InstrumentVisualization   // VisualizationProperties

// The simulate-image verb: time + SPICE kernels + OPC + instrument name in, one simulated
// asteroid image out. Sun position, spacecraft position and body orientation come from
// SPICE; the camera looks at the body centre with the instrument's frustum (no CK needed);
// shading is Lommel-Seeliger over an albedo obtained by dividing the baked illumination
// out of the OPC texture; sub-mesh detail is procedural (SimulateShaders); cast shadows
// come from a sun-side depth pass.

/// Native detector sizes per SPICE instrument frame, from the instrument kernels
/// (hera_afc_v06.ti: 1020x1020 active pixels). The FOV table lives in
/// PRo3D.Base.InstrumentProjection; pixel counts are not part of a Frustum, hence here.
let private nativeSizes =
    Map.ofList [
        "HERA_AFC-1", V2i(1020, 1020)
        "HERA_AFC-2", V2i(1020, 1020)
    ]

/// Texture values at or below this are treated as shadow/nodata in the source mosaic
/// (Dimorphos_DRACO1 marks nodata as DN 0 and has a hard shadowed tail below ~DN 16).
/// Shared between the CPU fit and the shader's fallback test.
let private deshadeShadowFloor = 0.06

let private rootPatchOf (basePath : string) =
    let serializer = FsPickler.CreateBinarySerializer()
    let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
    match h.tree with
    | QTree.Node (p, _) -> p
    | QTree.Leaf p -> p

// ---------------------------------------------------------------------------------
// De-shading fit: the OPC texture is a projected instrument image with illumination
// baked in (verified for Dimorphos_DRACO1: brightness vs n·L correlates r = 0.64 over a
// ~4.6x ramp). Recover the baked light direction by a linear least-squares fit of
// per-vertex brightness against the per-vertex normal, so the shader can divide the
// shading back out. A Lambert divisor on what is really a Lommel-Seeliger radiance is
// approximate -- the residual is documented, not hidden.

type DeshadeFit =
    {
        /// Fitted direction of the baked-in illumination, body-fixed frame.
        direction : V3d
        /// Multiplier taking texture/cos(i) to normal reflectance.
        scale : float
        samples : int
        /// Pearson correlation of brightness vs n·L on the lit samples -- how much of the
        /// texture the fit explains. Low correlation means the texture was already flat.
        correlation : float
    }

/// Solve the 4x4 normal equations for dn ≈ c0 + c·n. Returns None when the system is
/// numerically singular (e.g. all normals parallel), detected via non-finite results.
let private solveLeastSquares (samples : (V3d * float)[]) : Option<float[]> =
    let ata = Array.zeroCreate<float> 16
    let atb = Array.zeroCreate<float> 4
    for (n, dn) in samples do
        let b = [| 1.0; n.X; n.Y; n.Z |]
        for i in 0 .. 3 do
            atb.[i] <- atb.[i] + b.[i] * dn
            for j in 0 .. 3 do
                ata.[i * 4 + j] <- ata.[i * 4 + j] + b.[i] * b.[j]
    let m =
        M44d(ata.[0],  ata.[1],  ata.[2],  ata.[3],
             ata.[4],  ata.[5],  ata.[6],  ata.[7],
             ata.[8],  ata.[9],  ata.[10], ata.[11],
             ata.[12], ata.[13], ata.[14], ata.[15])
    let inv = m.Inverse
    let x =
        Array.init 4 (fun i ->
            inv.[i, 0] * atb.[0] + inv.[i, 1] * atb.[1] + inv.[i, 2] * atb.[2] + inv.[i, 3] * atb.[3])
    if x |> Array.forall Double.IsFinite then Some x else None

let fitBakedLight (basePath : string) (layerName : string) (albedo : float) : Result<DeshadeFit, string> =
    match (try Ok (rootPatchOf basePath) with e -> Result.Error (sprintf "cannot load patch hierarchy: %s" e.Message)) with
    | Result.Error e -> Result.Error e
    | Ok root ->
    let patchDir = Path.Combine(basePath, "Patches", root.info.Name)
    let normalPath = Path.Combine(patchDir, "Normal.aara")
    let layerPath = Path.Combine(patchDir, layerName + ".aara")
    if not (File.Exists normalPath) then
        Result.Error (sprintf "no per-vertex normals for the de-shading fit: %s" normalPath)
    elif not (File.Exists layerPath) then
        Result.Error (sprintf "no per-vertex '%s' layer for the de-shading fit: %s (see --deshade-layer)" layerName layerPath)
    else

    match
        (try Ok ((Aara.fromFile<V3f> normalPath).Data, (Aara.fromFile<V3f> layerPath).Data)
         with e -> Result.Error (sprintf "cannot read fit inputs (%s): %s" layerName e.Message))
      with
    | Result.Error e -> Result.Error e
    | Ok (normals, values) ->

    if normals.Length <> values.Length then
        Result.Error (sprintf "grid mismatch: %d normals vs %d '%s' values" normals.Length values.Length layerName)
    else

    // Normals in the .aara are patch-local; the shader compares against the fitted
    // direction in the BODY frame (it transforms LocalNormal by ModelTrafo). Apply the
    // same Local2Global rotation here -- for typical OPCs it is a pure translation and
    // this is a no-op, but a rotated patch frame would otherwise skew the fit silently.
    let toBody = root.info.Local2Global.Forward

    // The root patch is a decimated copy of the whole body -- ~1M vertices -- so a strided
    // subsample is representative and keeps the fit instant.
    let stride = max 1 (normals.Length / 30000)
    let collected = ResizeArray<V3d * float>()
    let mutable i = 0
    while i < normals.Length do
        let n = normals.[i]
        let v = values.[i]
        if not n.IsNaN && not v.IsNaN then
            let len = n.Length
            // brightness stored as 0..255 (V3f, grey replicated); shader samples 0..1
            let dn = float v.X / 255.0
            if len > 0.5f && len < 1.5f && dn > deshadeShadowFloor then
                collected.Add ((toBody.TransformDir (V3d n)).Normalized, dn)
        i <- i + stride
    let candidates = collected.ToArray()

    if candidates.Length < 100 then
        Result.Error (sprintf "only %d usable vertices for the de-shading fit" candidates.Length)
    else

    // Two passes: the first over everything lit, the second restricted to vertices the
    // first fit says face the light -- terminator and grazing vertices otherwise drag the
    // direction towards the shadowed hemisphere.
    match solveLeastSquares candidates with
    | None -> Result.Error "de-shading fit is singular (are the normals degenerate?)"
    | Some c1 ->
    let l1 = V3d(c1.[1], c1.[2], c1.[3])
    if l1.Length < 1e-9 then
        Result.Error "de-shading fit found no direction (the texture does not vary with the normal)"
    else
    let l1 = l1.Normalized
    let lit = candidates |> Array.filter (fun (n, _) -> Vec.dot n l1 > 0.2)
    if lit.Length < 100 then
        Result.Error (sprintf "only %d lit vertices after the first fit pass" lit.Length)
    else
    match solveLeastSquares lit with
    | None -> Result.Error "de-shading refinement is singular"
    | Some c2 ->
    let dir = V3d(c2.[1], c2.[2], c2.[3])
    if dir.Length < 1e-9 then
        Result.Error "de-shading refinement lost the direction"
    else
    let dir = dir.Normalized

    // scale so that the mean de-shaded value maps to the requested normal reflectance,
    // and correlation as a fit-quality report
    let mutable relSum = 0.0
    let mutable relCount = 0
    let mutable sx = 0.0
    let mutable sy = 0.0
    let mutable sxx = 0.0
    let mutable syy = 0.0
    let mutable sxy = 0.0
    for (n, dn) in lit do
        let mu = Vec.dot n dir
        if mu > 0.15 then
            relSum <- relSum + dn / mu
            relCount <- relCount + 1
        sx <- sx + mu
        sy <- sy + dn
        sxx <- sxx + mu * mu
        syy <- syy + dn * dn
        sxy <- sxy + mu * dn
    if relCount = 0 then
        Result.Error "no vertices survived the de-shading scale estimate"
    else
    let meanRel = relSum / float relCount
    let nf = float lit.Length
    let cov = sxy - sx * sy / nf
    let varX = sxx - sx * sx / nf
    let varY = syy - sy * sy / nf
    let correlation = if varX > 0.0 && varY > 0.0 then cov / sqrt (varX * varY) else 0.0

    Ok {
        direction = dir
        scale = albedo / meanRel
        samples = lit.Length
        correlation = correlation
    }

// ---------------------------------------------------------------------------------
// Sun shadow map: one depth pass from an orthographic sun-side camera covering the whole
// body. At Dimorphos scale (~180 m) a 4096^2 map resolves ~5 cm/texel -- finer than the
// mesh itself -- so nothing is gained by going larger.

type private SunShadowMap =
    {
        /// Body-fixed world -> sun-camera clip space (the shader's SunShadowViewProj).
        viewProj : Trafo3d
        depth : ITexture
        cleanup : unit -> unit
    }

/// Rgba8 + Depth32f target for the shadow passes. The depth texture is the product; the
/// colour attachment exists only because the pass needs one. Returns the pieces plus a
/// cleanup closure that releases all of them.
let private createShadowTarget (runtime : IRuntime) (size : V2i) =
    let signature =
        runtime.CreateFramebufferSignature([
            DefaultSemantic.Colors, TextureFormat.Rgba8
            DefaultSemantic.DepthStencil, TextureFormat.DepthComponent32f
        ], 1)
    let color = runtime.CreateTexture(V3i(size.X, size.Y, 1), TextureDimension.Texture2D, TextureFormat.Rgba8, 1, 1)
    let depth = runtime.CreateTexture(V3i(size.X, size.Y, 1), TextureDimension.Texture2D, TextureFormat.DepthComponent32f, 1, 1)
    let output =
        runtime.CreateFramebuffer(
            signature,
            Map.ofList [
                DefaultSemantic.Colors, color.GetOutputView()
                DefaultSemantic.DepthStencil, depth.GetOutputView()
            ]) |> OutputDescription.ofFramebuffer
    let cleanup () =
        runtime.DeleteTexture color
        runtime.DeleteTexture depth
        signature.Dispose()
    signature, depth, output, cleanup

/// A 1x1 far-plane depth texture for --no-shadows: the comparison sampler still needs a
/// depth texture bound even though the shader never takes the shadow branch.
let private dummyShadowMap (runtime : IRuntime) : SunShadowMap =
    let signature, depth, output, cleanup = createShadowTarget runtime V2i.II
    let clear = runtime.CompileClear(signature, AVal.constant (C4f(0.0f, 0.0f, 0.0f, 0.0f)), AVal.constant 1.0)
    clear.Run(output)
    clear.Dispose()
    {
        viewProj = Trafo3d.Identity
        depth = depth :> ITexture
        cleanup = cleanup
    }

let private renderSunShadowMap (runtime : IRuntime) (body : string)
                               (projectedImages : aval<Option<Sg.ProjectedImages>>)
                               (hierarchies : string[]) (sunDir : V3d) (bbox : Box3d) : SunShadowMap =
    let signature, depth, output, cleanup = createShadowTarget runtime (V2i(4096, 4096))

    let center = bbox.Center
    let radius = 0.5 * bbox.Size.Length
    let up = if abs (Vec.dot sunDir V3d.OOI) > 0.98 then V3d.OIO else V3d.OOI
    let view =
        CameraView.lookAt (center + sunDir * (3.0 * radius)) center up
        |> CameraView.viewTrafo
    // Fit the ortho frustum to the body's bounds as seen from the sun. Frustum.ortho
    // takes the box's Z verbatim as near/far, but the camera looks down -Z, so the body
    // sits at NEGATIVE view-space Z and near/far must be the negated maximum/minimum --
    // otherwise everything lands outside the clip volume, the depth map stays empty, and
    // every fragment reprojects behind it, i.e. the whole body renders shadowed.
    let vbox = bbox.Transformed view
    let proj =
        { Frustum.ortho vbox with near = -vbox.Max.Z; far = -vbox.Min.Z }
        |> Frustum.projTrafo

    let runner = runtime.CreateLoadRunner 1
    let cfg =
        { OpcSg.defaultConfig signature runner DefaultMetrics.mars2 body with
            asyncLoading = false }
    let sg =
        OpcSg.build cfg projectedImages VisualizationProperties.empty hierarchies
        |> Sg.ofList
        |> Sg.shader {
            do! PRo3D.SPICE.Shaders.stableTrafo
            do! DefaultSurfaces.constantColor C4f.White
        }
        |> SunAnglesVerb.withOpcScaffolding
        |> Sg.viewTrafo (AVal.constant view)
        |> Sg.projTrafo (AVal.constant proj)

    let clear = runtime.CompileClear(signature, AVal.constant (C4f(0.0f, 0.0f, 0.0f, 0.0f)), AVal.constant 1.0)
    let task = runtime.CompileRender(signature, sg)
    // Warm-up for the same reason as FloatTarget.render: the LOD tree refines only after
    // a frame has been rendered with the final (here: sun) camera.
    for _ in 1 .. 8 do
        clear.Run(output)
        task.Run(output)
    task.Dispose()
    clear.Dispose()

    {
        viewProj = view * proj
        depth = depth :> ITexture
        cleanup = cleanup
    }

// ---------------------------------------------------------------------------------
// Camera: position from the spacecraft SPK, orientation looking at the body centre.
// Deliberately NOT the CK attitude: coverage-independent, and AFC tracks the asteroid
// anyway. The roll around the boresight follows the up convention and is therefore
// arbitrary -- documented as a caveat.

type SimCamera =
    {
        view : Trafo3d
        proj : Trafo3d
        distance : float
        /// The instrument frustum's width/height ratio -- rendering into a viewport with
        /// a different ratio stretches the image.
        aspect : float
    }

/// `distanceOverride` > 0 moves the camera to that range along the direction SPICE puts
/// the spacecraft -- the viewpoint stays real, only the standoff changes. Useful when the
/// body would otherwise be a handful of pixels, and for validation renders.
let cameraAt (observer : string) (frame : string) (body : string) (instrument : string)
             (distanceOverride : float) (time : DateTime) : Result<SimCamera, string> =
    match CooTransformation.getRelState observer "SUN" body time frame with
    | None ->
        Result.Error (sprintf "no ephemeris for %s relative to %s in %s at %s (kernel coverage?)"
                          observer body frame (time.ToString "o"))
    | Some st ->
        let pos =
            if distanceOverride > 0.0 && st.pos.Length > 0.0 then st.pos.Normalized * distanceOverride
            else st.pos
        let distance = pos.Length
        if not (Double.IsFinite distance) || distance <= 0.0 then
            Result.Error (sprintf "SPICE returned a degenerate position for %s (|r| = %f)" observer distance)
        else
        let near, far = InstrumentProjection.nearFarForDistance distance
        match Map.tryFind instrument (InstrumentProjection.instruments near far) with
        | None ->
            Result.Error (sprintf "no frustum defined for instrument '%s' (see PRo3D.Base.InstrumentProjection)" instrument)
        | Some frustum ->
            let boresight = (-pos).Normalized
            let up = if abs (Vec.dot boresight V3d.OOI) > 0.98 then V3d.OIO else V3d.OOI
            Ok {
                view = CameraView.lookAt pos V3d.Zero up |> CameraView.viewTrafo
                proj = Frustum.projTrafo frustum
                distance = distance
                aspect = (frustum.right - frustum.left) / (frustum.top - frustum.bottom)
            }

// ---------------------------------------------------------------------------------
// Tonemap: linear I/F -> 8-bit DN. Auto-exposure anchors the 99.5th percentile of the
// covered pixels at DN 245 -- deterministic, headroom for the top half-percent, and
// independent of how much sky the frame contains.

let private toneMapToPng (img : PixImage<float32>) (explicitGain : float) : Result<PixImage<byte> * float, string> =
    let size = img.Size
    let lum = img.GetChannel Col.Channel.Red
    let alpha = img.GetChannel Col.Channel.Alpha

    let covered = ResizeArray<float32>()
    for y in 0 .. size.Y - 1 do
        for x in 0 .. size.X - 1 do
            if alpha.[x, y] > 0.5f then covered.Add lum.[x, y]

    if covered.Count = 0 then
        Result.Error "the body does not appear in the frame (no covered pixels) -- wrong time, body or instrument?"
    else

    let gain =
        if explicitGain > 0.0 then explicitGain
        else
            let sorted = covered.ToArray()
            Array.sortInPlace sorted
            let idx = min (sorted.Length - 1) (int (0.995 * float sorted.Length))
            let p = float sorted.[idx]
            if p <= 0.0 then 1.0 else (245.0 / 255.0) / p

    let out = PixImage<byte>(Col.Format.Gray, size)
    let mutable m = out.GetChannel Col.Channel.Gray
    for y in 0 .. size.Y - 1 do
        for x in 0 .. size.X - 1 do
            let v =
                if alpha.[x, y] > 0.5f then
                    float lum.[x, y] * gain * 255.0 |> max 0.0 |> min 255.0
                else 0.0
            m.[x, y] <- byte v
    Ok (out, gain)

// ---------------------------------------------------------------------------------

/// Render one simulated image. Returns the written file, or why it could not.
///
/// Public and takes everything pre-resolved (same pattern as SunAnglesVerb.processImage):
/// `run` owns the SPICE lifetime, tests drive this directly against the suite's already
/// active kernel, adding no kernel swaps.
let processImage (runtime : IRuntime) (o : SimulateImageOptions)
                 (body : string) (frame : string) (observer : string) (instrument : string)
                 (time : DateTime) (outPath : string) (hierarchies : string[]) : Result<string, string> =

    let size =
        // Each axis independently: --width alone keeps the native height, like sun-angles.
        let native =
            match Map.tryFind instrument nativeSizes with
            | Some native -> native
            | None ->
                if o.width <= 0 || o.height <= 0 then
                    Log.warn "no native detector size known for %s -- using 1024x1024 (override with --width/--height)" instrument
                V2i(1024, 1024)
        V2i(
            (if o.width  > 0 then o.width  else native.X),
            (if o.height > 0 then o.height else native.Y))

    match cameraAt observer frame body instrument o.distance time with
    | Result.Error e -> Result.Error e
    | Ok cam ->

    if o.distance > 0.0 then
        Log.warn "[camera] distance OVERRIDDEN to %.1f m -- the standoff is not the spacecraft's real range" o.distance

    // The frustum's aspect is fixed by the instrument; a differently-shaped viewport
    // stretches the image. Say so rather than silently emitting a distorted render.
    if abs (float size.X / float size.Y - cam.aspect) > 1e-3 then
        Log.warn "output %dx%d (ratio %.3f) does not match %s's frustum aspect %.3f -- the image will be stretched"
            size.X size.Y (float size.X / float size.Y) instrument cam.aspect

    Log.line "[camera] %s at %.1f km from %s, %dx%d px" observer (cam.distance / 1000.0) body size.X size.Y

    match InstrumentObservation.sunDirection frame body time with
    | Result.Error e -> Result.Error (sprintf "no sun direction: %s" e)
    | Ok sun ->

    let phase = acos (clamp -1.0 1.0 (Vec.dot sun (cam.view.Backward.TransformPos V3d.Zero).Normalized))
    Log.line "[sun] direction in %s: %.4f %.4f %.4f (phase angle %.1f deg)"
        frame sun.X sun.Y sun.Z (phase * Constant.DegreesPerRadian)

    // De-shading: fit on the first hierarchy's root patch (all hierarchies of one OPC
    // share the acquisition, so one fit serves them all). A failed fit degrades loudly to
    // the constant albedo -- a wrong divisor is worse than none.
    let layerName = if String.IsNullOrWhiteSpace o.deshadeLayer then "DRACO" else o.deshadeLayer
    let deshade =
        if not o.deshade then None
        else
            match Array.tryHead hierarchies with
            | None -> None
            | Some h ->
                match fitBakedLight h layerName o.albedo with
                | Ok fit ->
                    Log.line "[deshade] baked light direction %.4f %.4f %.4f (r = %.2f over %d vertices, scale %.3f)"
                        fit.direction.X fit.direction.Y fit.direction.Z fit.correlation fit.samples fit.scale
                    if abs fit.correlation < 0.2 then
                        Log.warn "[deshade] brightness barely follows the normals (r = %.2f) -- texture may already be flat"
                            fit.correlation
                    Some fit
                | Result.Error e ->
                    Log.warn "[deshade] %s" e
                    Log.warn "[deshade] falling back to constant albedo %.2f" o.albedo
                    None

    let projectedImages : aval<Option<Sg.ProjectedImages>> =
        // Must go through this record, not Sg.uniform': projectionUniformMap installs
        // SunDirectionWorld as a PER-PATCH uniform sourced from here, and being deeper in
        // the graph it wins over any outer uniform of the same name.
        AVal.constant (
            Some {
                imageProjection = AVal.constant (Some (cam.view * cam.proj))
                stackProjections = AVal.constant [||]
                stackCoverageEnabled = AVal.constant false
                hoveredProjection = AVal.constant None
                sunDirection = AVal.constant (Some sun)
                sunLightEnabled = AVal.constant true
                // this verb feeds its shadow map through its own shader stack
                // (simulatedImage), not the viewer's per-patch shadow lookup
                lightViewProj = AVal.constant None
            })

    let bbox =
        hierarchies
        |> Array.map (fun h -> (rootPatchOf h).info.GlobalBoundingBox)
        |> Array.fold (fun (b : Box3d) x -> b.ExtendedBy x) Box3d.Invalid

    let shadowMap =
        if o.noShadows then dummyShadowMap runtime
        else
            Log.line "[shadow] rendering sun depth map (4096^2, ortho over %.0f m)" bbox.Size.Length
            renderSunShadowMap runtime body projectedImages hierarchies sun bbox

    let target = SunAnglesVerb.FloatTarget.create runtime size
    try
        let runner = runtime.CreateLoadRunner 1
        let cfg =
            { OpcSg.defaultConfig target.signature runner DefaultMetrics.mars2 body with
                // Blocking loads: reproducible offscreen output, same as sun-angles.
                asyncLoading = false }

        let sg =
            OpcSg.build cfg projectedImages VisualizationProperties.empty hierarchies
            |> Sg.ofList
            |> Sg.shader {
                // Order is load-bearing (see SunAnglesVerb.applyAngleShaders):
                // stableImageProjectionTrafo stashes the object-space position while
                // [<Position>] still holds it, generateNormal builds the face normal from
                // that stash, and stashSunShadowPos must equally precede stableTrafo.
                do! ImageProjection.Shaders.stableImageProjectionTrafo
                do! ImageProjection.Shaders.generateNormal
                do! ImageProjection.Shaders.applyNormalFlip
                do! SimulateShaders.stashSunShadowPos
                do! PRo3D.SPICE.Shaders.stableTrafo
                do! SimulateShaders.simulatedImage
            }
            |> SunAnglesVerb.withOpcScaffolding
            |> Sg.uniform' "SunShadowEnabled" (not o.noShadows)
            |> Sg.uniform' "SunShadowViewProj" shadowMap.viewProj.Forward
            |> Sg.texture "SunShadowMap" (AVal.constant shadowMap.depth)
            |> Sg.uniform' "SunShadowBias" (float32 o.shadowBias)
            |> Sg.uniform' "DeshadeEnabled" (Option.isSome deshade)
            |> Sg.uniform' "BakedSunDirection" (V3f (deshade |> Option.map (fun f -> f.direction) |> Option.defaultValue V3d.ZAxis))
            |> Sg.uniform' "DeshadeScale" (float32 (deshade |> Option.map (fun f -> f.scale) |> Option.defaultValue 1.0))
            |> Sg.uniform' "DeshadeShadowFloor" (float32 deshadeShadowFloor)
            |> Sg.uniform' "AlbedoConst" (float32 o.albedo)
            |> Sg.uniform' "MicroScale" (float32 o.microScale)
            |> Sg.uniform' "MicroAmplitude" (float32 o.microAmplitude)
            |> Sg.uniform' "AmbientFloor" (float32 o.ambient)
            |> Sg.viewTrafo (AVal.constant cam.view)
            |> Sg.projTrafo (AVal.constant cam.proj)

        // More warm-up than sun-angles: the LOD tree descends one refinement per rendered
        // frame, and with a distance override the camera can sit much closer than the
        // spacecraft, needing every level the hierarchy has.
        let rendered = SunAnglesVerb.FloatTarget.render target 8 sg

        match toneMapToPng rendered o.gain with
        | Result.Error e -> Result.Error e
        | Ok (png, gain) ->
            if o.gain <= 0.0 then
                Log.line "[expose] auto gain %.3f (pass --gain %.3f to reproduce across a series)" gain gain
            png.Save(outPath)
            Log.line "[out] %s" outPath
            Ok outPath
    finally
        SunAnglesVerb.FloatTarget.dispose target
        shadowMap.cleanup ()

/// Entry point for the `simulate-image` verb.
let run (o : SimulateImageOptions) : int =
    let body       = if String.IsNullOrWhiteSpace o.body       then "DIMORPHOS"       else o.body
    let frame      = if String.IsNullOrWhiteSpace o.frame      then "DIMORPHOS_FIXED" else o.frame
    let observer   = if String.IsNullOrWhiteSpace o.observer   then "HERA"            else o.observer
    let instrument = if String.IsNullOrWhiteSpace o.instrument then "HERA_AFC-1"      else o.instrument
    let outPath    = if String.IsNullOrWhiteSpace o.out        then Path.Combine(".", "simulated.png") else o.out

    let time =
        match DateTime.TryParse(o.time, CultureInfo.InvariantCulture,
                                DateTimeStyles.AdjustToUniversal ||| DateTimeStyles.AssumeUniversal) with
        | true, t -> Some t
        | _ -> None

    match time with
    | None -> Log.error "cannot parse --time '%s' (expected ISO-8601, e.g. 2027-03-15T12:00:00Z)" o.time; 1
    | Some time ->

    if not (Directory.Exists o.opc) then Log.error "OPC directory not found: %s" o.opc; 1
    else

    match Spice.resolveKernelRoot o.kernelRoot with
    | Result.Error e ->
        Spice.reportMissingKernelRoot e
        1
    | Ok kernelRoot ->

    // No image sidecar to declare a metakernel here, so the choice is explicit or the
    // planning kernel: with a user-supplied time only hera_plan.tm reliably has coverage.
    let kernel =
        if not (String.IsNullOrWhiteSpace o.kernel) then
            if File.Exists o.kernel then Ok o.kernel
            else Result.Error (sprintf "kernel not found: %s" o.kernel)
        else
            let fallback = Path.Combine(kernelRoot, "mk", "hera_plan.tm")
            if File.Exists fallback then Ok fallback
            else Result.Error (sprintf "no metakernel: pass --kernel or provide %s" fallback)

    match kernel with
    | Result.Error e -> Log.error "%s" e; 1
    | Ok kernel ->

    let hierarchies = SunAnglesVerb.patchHierarchiesOf o.opc
    if hierarchies.Length = 0 then
        Log.error "no patch hierarchies (subdirectories containing 'Patches') under %s" o.opc
        1
    else

    let outDir = Path.GetDirectoryName(Path.GetFullPath outPath)
    Directory.CreateDirectory outDir |> ignore

    Log.line "[options] albedo %.3f, micro scale %.2f m, micro amplitude %.2f, ambient %.3f, shadow bias %.4f"
        o.albedo o.microScale o.microAmplitude o.ambient o.shadowBias

    Aardvark.Init()
    // Created here, never at startup: the kdtree verb must keep working on machines with
    // no GPU and no display (see Program.fs).
    use app = new OpenGlApplication()
    let runtime = app.Runtime :> IRuntime

    use _spice = SpiceBoot.init (Some kernel)
    Log.line "[spice] %s" kernel

    // An unexpected exception (corrupt OPC, driver failure) should surface as a clean
    // error and exit code, not a raw stack trace.
    try
        match processImage runtime o body frame observer instrument time outPath hierarchies with
        | Ok _ -> 0
        | Result.Error e -> Log.error "%s" e; 1
    with e ->
        Log.error "unhandled: %s" e.Message
        1
