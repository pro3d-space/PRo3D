module PRo3D.Tool.SunAnglesVerb

open System
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application.Slim
open FSharp.Data.Adaptive

open Aardvark.GeoSpatial.Opc
open Aardvark.GeoSpatial.Opc.Load   // IRuntime.CreateLoadRunner

open Aardvark.PixImage.LibTiff

open PRo3D.Core
open PRo3D.Core.InstrumentMetadata
open PRo3D.ImageMapping
open PRo3D.InstrumentVisualization   // VisualizationProperties

/// Offscreen rendering into a float32 colour target.
///
/// A separate, minimal target rather than PRo3D.SimulatedViews.SnapshotApp: that path is
/// bound to the Suave server and the snapshot animation model, none of which a one-shot CLI
/// needs. The testbed has its own 8-bit equivalent; this one differs in format, because
/// quantising an angle to a byte is exactly what this verb exists to avoid.
///
/// Not private: the simulate-image verb renders into the same kind of target (float32,
/// because auto-exposure needs the unquantised values before the PNG tonemap).
module FloatTarget =

    type Target =
        {
            runtime   : IRuntime
            signature : IFramebufferSignature
            color     : IBackendTexture
            depth     : IBackendTexture
            output    : OutputDescription
            size      : V2i
        }

    let create (runtime : IRuntime) (size : V2i) =
        let res = V3i(size.X, size.Y, 1)
        let color = runtime.CreateTexture(res, TextureDimension.Texture2D, TextureFormat.Rgba32f, 1, 1)
        let depth = runtime.CreateTexture(res, TextureDimension.Texture2D, TextureFormat.DepthComponent32f, 1, 1)
        let signature =
            runtime.CreateFramebufferSignature([
                DefaultSemantic.Colors, TextureFormat.Rgba32f
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

    let dispose (t : Target) =
        t.runtime.DeleteTexture t.color
        t.runtime.DeleteTexture t.depth
        t.signature.Dispose()

    /// Render and download.
    ///
    /// `warmupFrames` is not superstition: even with synchronous patch loading the LOD tree
    /// only refines once a frame has been rendered with the final camera, because the
    /// decider needs a view to decide against. A single pass captures whatever the initial
    /// tree was.
    ///
    /// Clears alpha to 0. Alpha is the coverage mask -- the angle channels are only
    /// meaningful where the surface was actually rasterised, and 0 rad is a perfectly
    /// plausible angle, so the mask is what distinguishes "no data" from "zero".
    let render (t : Target) (warmupFrames : int) (sg : ISg) =
        let clear = t.runtime.CompileClear(t.signature, AVal.constant (C4f(0.0f, 0.0f, 0.0f, 0.0f)), AVal.constant 1.0)
        let task = t.runtime.CompileRender(t.signature, sg)
        for _ in 1 .. max 1 warmupFrames do
            clear.Run(t.output)
            task.Run(t.output)
        let image = t.runtime.Download(t.color).ToPixImage<float32>()
        task.Dispose()
        clear.Dispose()
        image

/// One angle raster to emit.
type private Band =
    {
        name    : string
        channel : Col.Channel
        /// Full-scale angle for false-colour mapping: the value that maps to red.
        range   : float32
        /// Paint values above 90 degrees magenta instead of colour-mapping them. Meaningful
        /// for emission, where it flags a facet facing away from the observer that was
        /// nevertheless rasterised -- expected along the limb, suspicious elsewhere.
        flagObtuse : bool
    }

let private halfPi = float32 Math.PI * 0.5f

let private bands =
    [
        { name = "incidence"; channel = Col.Channel.Red;   range = halfPi;             flagObtuse = false }
        { name = "emission";  channel = Col.Channel.Green; range = halfPi;             flagObtuse = true  }
        { name = "phase";     channel = Col.Channel.Blue;  range = float32 Math.PI;    flagObtuse = false }
    ]

/// An OPC directory holds its patch hierarchies as immediate subdirectories -- but not
/// every subdirectory is one. Real data folders sit next to saved scenes and annotation
/// files, and handing one of those to PatchHierarchy.load throws. A hierarchy is
/// identified by containing a `Patches` directory. Shared with the simulate-image verb.
let patchHierarchiesOf (opcPath : string) =
    Directory.GetDirectories opcPath
    |> Array.filter (fun d -> Directory.Exists(Path.Combine(d, "Patches")))

let private applyAngleShaders (sg : ISg) =
    sg
    |> Sg.shader {
        // Order matters twice over: stableImageProjectionTrafo must precede stableTrafo
        // because it stashes the object-space position while [<Position>] still holds it,
        // and generateNormal is a geometry shader building the face normal from that
        // stashed position, so the result does not depend on the render camera.
        do! ImageProjection.Shaders.stableImageProjectionTrafo
        do! ImageProjection.Shaders.generateNormal
        do! ImageProjection.Shaders.applyNormalFlip
        do! PRo3D.SPICE.Shaders.stableTrafo
        do! SunAngles.Shaders.sunAnglesFloat
    }

/// Attributes the OPC surface shaders inherit whether or not this verb uses them. Without
/// each of these the Ag lookup throws at CompileRender rather than at graph construction,
/// so the failure surfaces as an opaque "could not get inh attribute X" deep in a scope
/// path. Kept as one block so the next added attribute has an obvious home. Shared with
/// the simulate-image verb, whose graphs inherit the same attributes.
let withOpcScaffolding (sg : ISg) =
    sg
    // The angle shaders do not sample the instrument image -- they need only geometry and
    // the sun direction -- but the surface shaders still expect the sampler to be bound.
    |> Sg.texture "ProjectedTexture" DefaultTextures.blackTex
    |> Sg.uniform' "ProjectedImageModelViewProjValid" true
    |> Sg.uniform' "LodVisEnabled" false
    |> PRo3D.Core.Surface.Sg.applyFootprint (AVal.constant M44d.Identity)
    // Cross-section clipping (releases/6.0.0) is another OPC-surface Ag attribute.
    |> PRo3D.Core.SgExtensions.Sg.applyCrossSection (AVal.constant None)
    |> Aardvark.GeoSpatial.Opc.SecondaryTexture.Sg.applySecondaryTextureId
        (AVal.constant (Some { texture = TextureReference.LegacyId 0
                               channel = ChannelReference.NoChannelSelection }))

/// Split the packed RGBA readback into one float array per angle, substituting NaN wherever
/// the coverage mask says nothing was drawn.
let private extractBand (img : PixImage<float32>) (band : Band) =
    let size = img.Size
    let values = img.GetChannel band.channel
    let alpha = img.GetChannel Col.Channel.Alpha
    let out = Array.zeroCreate<float32> (size.X * size.Y)
    for y in 0 .. size.Y - 1 do
        let row = y * size.X
        for x in 0 .. size.X - 1 do
            out.[row + x] <- if alpha.[x, y] > 0.5f then values.[x, y] else Single.NaN
    out

/// False-colour PNG for one angle: blue (0) through green to red (the band's full scale),
/// using the same ramp as PRo3D.ProjectionTestbed so the two are directly comparable.
///
/// Radians in a float TIFF are the data but are not interpretable at a glance; this is what
/// makes a result reviewable without loading it into a GIS. Nodata stays black, which is
/// outside the ramp and so cannot be confused with a low angle.
let private writeFalseColor (path : string) (img : PixImage<float32>) (band : Band) =
    let size = img.Size
    let values = img.GetChannel band.channel
    let alpha = img.GetChannel Col.Channel.Alpha
    let out = PixImage<byte>(Col.Format.RGB, size)
    // Matrix<_> is a struct, so the local must be mutable to write through it.
    let mutable m = out.GetMatrix<C3b>()
    let toByte (v : float32) = byte (min 1.0f (max 0.0f v) * 255.0f)
    for y in 0 .. size.Y - 1 do
        for x in 0 .. size.X - 1 do
            let v = values.[x, y]
            m.[int64 x, int64 y] <-
                if alpha.[x, y] <= 0.5f || Single.IsNaN v then
                    C3b(0uy, 0uy, 0uy)
                elif band.flagObtuse && v > halfPi then
                    C3b(255uy, 0uy, 255uy)
                else
                    let c = SunAngles.Shaders.colormap (v / band.range)
                    C3b(toByte c.X, toByte c.Y, toByte c.Z)
    out.Save(path)

let private writeSidecar (path : string) (o : SunAnglesOptions) (img : ResolvedImage)
                         (cam : ProjectorCamera) (kernel : string) (unit : string)
                         (body : string) (frame : string) (observer : string) =
    // Unprovenanced float rasters are close to useless a month later: which kernel, which
    // epoch, what units, and -- crucially -- that self-shadowing was not evaluated.
    let json =
        String.concat "\n" [
            "{"
            sprintf "  \"source_image\": %s," (Text.Json.JsonSerializer.Serialize (Path.GetFileName img.path))
            sprintf "  \"instrument_frame\": %s," (Text.Json.JsonSerializer.Serialize img.spiceName)
            sprintf "  \"epoch\": %s," (Text.Json.JsonSerializer.Serialize (img.mbi.obs_date.ToString "o"))
            sprintf "  \"body\": %s," (Text.Json.JsonSerializer.Serialize body)
            sprintf "  \"reference_frame\": %s," (Text.Json.JsonSerializer.Serialize frame)
            sprintf "  \"observer\": %s," (Text.Json.JsonSerializer.Serialize observer)
            sprintf "  \"spice_metakernel\": %s," (Text.Json.JsonSerializer.Serialize kernel)
            sprintf "  \"units\": %s," (Text.Json.JsonSerializer.Serialize unit)
            "  \"nodata\": \"NaN\","
            sprintf "  \"target_distance_m\": %f," cam.distance
            sprintf "  \"near_m\": %f," cam.near
            sprintf "  \"far_m\": %f," cam.far
            "  \"bands\": [\"incidence\", \"emission\", \"phase\"],"
            "  \"caveat\": \"Local illumination geometry only. Terrain self-shadowing is NOT evaluated: a point lying in the shadow of nearby relief still reports its geometric incidence angle. Emission above 90 degrees is preserved rather than clamped, and indicates a facet facing away from the observer was rasterised.\""
            "}"
        ]
    File.WriteAllText(path, json)

/// Render the angle rasters for one image. Returns the files written, or why it could not.
///
/// Public, and takes the metakernel path rather than loading it: SPICE state is
/// process-global and there is no working unload (DeInit does not call kclear_c), so
/// every Init/DeInit cycle costs a kernel swap and swaps degrade DAF handles. `run`
/// owns that lifetime for CLI use; tests drive this directly against a kernel the test
/// suite already has active, adding no swaps.
let processImage (runtime : IRuntime) (o : SunAnglesOptions)
                         (body : string) (frame : string) (observer : string)
                         (methodValue : ProjectionMethod) (outDir : string) (kernel : string)
                         (hierarchies : string[]) (img : ResolvedImage) : Result<string list, string> =

    match InstrumentObservation.projectorCamera None observer frame body methodValue img with
    | Result.Error e -> Result.Error e
    | Ok cam ->

    let size =
        let native = img.size |> Option.defaultValue (V2i(1024, 1024))
        V2i(
            (if o.width  > 0 then o.width  else native.X),
            (if o.height > 0 then o.height else native.Y))

    // The instrument frustum's aspect is fixed; rendering into a viewport with a different
    // aspect stretches the result, so the angles would no longer correspond to the source
    // pixels. Say so rather than silently emitting a misregistered raster.
    match img.size with
    | Some native when size <> native ->
        Log.warn "[%s] output %dx%d differs from the source's %dx%d -- rasters will not be pixel-aligned to the image"
            (Path.GetFileName img.path) size.X size.Y native.X native.Y
    | _ -> ()

    let sunDir = InstrumentObservation.sunDirection frame body img.mbi.obs_date
    match sunDir with
    | Result.Error e ->
        // Without a sun direction the incidence and phase channels are meaningless. The
        // shader would happily produce numbers from a zero vector, which is the worst
        // outcome: plausible-looking data that is wrong.
        Result.Error (sprintf "no sun direction: %s" e)
    | Ok sun ->

    let projectedImages : aval<Option<Sg.ProjectedImages>> =
        // Must go through this record, not Sg.uniform': projectionUniformMap installs
        // SunDirectionWorld as a PER-PATCH uniform sourced from here, and being deeper in
        // the graph it wins over any outer uniform of the same name.
        AVal.constant (
            Some {
                imageProjection = AVal.constant (Some cam.full)
                localImageProjectionTrafos = AVal.constant [||]
                sunDirection = AVal.constant (Some sun)
                sunLightEnabled = AVal.constant true
                lightViewProj = AVal.constant None
            })

    let target = FloatTarget.create runtime size
    try
        let runner = runtime.CreateLoadRunner 1
        let cfg =
            { OpcSg.defaultConfig target.signature runner DefaultMetrics.mars2 body with
                // Blocking loads: with async loading the readback captures whatever subset
                // of the LOD tree happened to have arrived, which is not reproducible.
                asyncLoading = false }

        let sg =
            OpcSg.build cfg projectedImages VisualizationProperties.empty hierarchies
            |> Sg.ofList
            |> applyAngleShaders
            |> withOpcScaffolding
            |> Sg.viewTrafo (AVal.constant cam.view)
            |> Sg.projTrafo (AVal.constant cam.proj)

        let rendered = FloatTarget.render target 3 sg

        let stem = Path.GetFileNameWithoutExtension img.path
        let written =
            bands |> List.choose (fun band ->
                let data = extractBand rendered band
                let path = Path.Combine(outDir, sprintf "%s_%s.tif" stem band.name)
                match Float32Writer.write path size.X size.Y data with
                | Ok () ->
                    Log.line "[out] %s" path
                    if o.falseColor then
                        let colorPath = Path.Combine(outDir, sprintf "%s_%s_color.png" stem band.name)
                        writeFalseColor colorPath rendered band
                        Log.line "[out] %s" colorPath
                    Some path
                | Result.Error e -> Log.error "[out] %s: %s" path e; None)

        if written.Length <> bands.Length then
            Result.Error "one or more rasters could not be written"
        else

        let sidecarPath = Path.Combine(outDir, sprintf "%s_angles.json" stem)
        writeSidecar sidecarPath o img cam kernel "radians" body frame observer
        Log.line "[out] %s" sidecarPath

        Ok (written @ [ sidecarPath ])
    finally
        FloatTarget.dispose target

/// Entry point for the `sun-angles` verb.
let run (o : SunAnglesOptions) : int =
    let body     = if String.IsNullOrWhiteSpace o.body     then "DIDYMOS"       else o.body
    let frame    = if String.IsNullOrWhiteSpace o.frame    then "DIDYMOS_FIXED" else o.frame
    let observer = if String.IsNullOrWhiteSpace o.observer then "MILANI"        else o.observer
    let outDir   = if String.IsNullOrWhiteSpace o.out      then Path.Combine(".", "sun-angles") else o.out

    let methodValue =
        match (if isNull o.method then "mbi" else o.method).ToLowerInvariant() with
        | "spice" -> Some ProjectionMethod.Spice
        | "mbi"   -> Some ProjectionMethod.MbiBased
        | _       -> None

    match methodValue with
    | None -> Log.error "unknown --method '%s' (expected spice or mbi)" o.method; 1
    | Some methodValue ->

    if not (Directory.Exists o.opc) then Log.error "OPC directory not found: %s" o.opc; 1
    elif not (Directory.Exists o.images) then Log.error "image folder not found: %s" o.images; 1
    else

    // Checked up front: without kernels nothing downstream can succeed, and loading an OPC
    // first only delays the message by several seconds.
    match Spice.resolveKernelRoot o.kernelRoot with
    | Result.Error e ->
        Spice.reportMissingKernelRoot e
        1
    | Ok kernelRoot ->

    let hierarchies = patchHierarchiesOf o.opc
    if hierarchies.Length = 0 then
        Log.error "no patch hierarchies (subdirectories containing 'Patches') under %s" o.opc
        1
    else

    // Batch: every image in the folder that has an mbi sidecar, unless one was named.
    // resolveImage returns the first match, so enumerate here and resolve each explicitly.
    let imageFiles =
        if not (String.IsNullOrWhiteSpace o.image) then [| o.image |]
        else
            discoverInstrumentFolder o.images
            |> Seq.choose (fun (path, meta) -> match meta with | Some _, _ -> Some (Path.GetFileName path) | _ -> None)
            |> Seq.toArray

    if imageFiles.Length = 0 then
        Log.error "no image with an .mbi.json sidecar found in %s" o.images
        1
    else

    let resolved =
        imageFiles
        |> Array.choose (fun f ->
            match InstrumentObservation.resolveImage o.images (Some f) with
            | Ok img -> Some img
            | Result.Error e -> Log.warn "[skip] %s: %s" f e; None)

    if resolved.Length = 0 then
        Log.error "none of the %d candidate images could be resolved" imageFiles.Length
        1
    else

    Log.line "processing %d image(s) from %s" resolved.Length o.images

    // One kernel for the whole batch. SPICE allows only one active metakernel -- layering a
    // second silently corrupts state -- and switching per image would be both slow and a
    // source of hard-to-see inconsistency between outputs of the same run.
    let explicitKernel = if String.IsNullOrWhiteSpace o.kernel then None else Some o.kernel
    let kernelResult =
        match Array.tryHead resolved with
        | Some img -> InstrumentObservation.resolveKernel explicitKernel kernelRoot img
        | None -> Result.Error "no resolvable image to take a kernel declaration from"

    match kernelResult with
    | Result.Error e -> Log.error "%s" e; 1
    | Ok kernel ->

    Directory.CreateDirectory outDir |> ignore

    Aardvark.Init()
    // Created here, never at startup: the kdtree verb must keep working on machines with no
    // GPU and no display, and creating a runtime before verb dispatch would break that.
    use app = new OpenGlApplication()
    let runtime = app.Runtime :> IRuntime

    use _spice = SpiceBoot.init (Some kernel)
    Log.line "[spice] %s" kernel

    let mutable failures = 0
    for img in resolved do
        let name = Path.GetFileName img.path
        Log.startTimed "%s" name
        try
            match processImage runtime o body frame observer methodValue outDir kernel hierarchies img with
            | Ok _ -> ()
            | Result.Error e ->
                Log.error "[%s] %s" name e
                failures <- failures + 1
        with e ->
            // Per-image isolation: a batch that dies on image 3 of 400 is useless.
            Log.error "[%s] unhandled: %s" name e.Message
            failures <- failures + 1
        Log.stop()

    if failures > 0 then
        Log.error "%d of %d image(s) failed" failures resolved.Length
        1
    else
        Log.line "wrote rasters for %d image(s) to %s" resolved.Length outDir
        0
