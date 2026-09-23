module PRo3D.Tool.SimulateSeriesVerb

open System
open System.Globalization
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application.Slim
open FSharp.Data.Adaptive

open Aardvark.GeoSpatial.Opc
open Aardvark.GeoSpatial.Opc.Load   // IRuntime.CreateLoadRunner

open PRo3D.Core
open PRo3D.SPICE
open PRo3D.ImageMapping
open PRo3D.InstrumentVisualization   // VisualizationProperties
open PRo3D.Core.Surface

open PRo3D.Tool.SimulateImageVerb

// The simulate-series verb: one process renders a whole series.
//
// `simulate-image` is a complete program per frame -- it initialises the GL context, loads
// the shape model, fits the de-shading, builds a scene graph and warms the LOD tree, all
// to emit one PNG. Driving it once per frame put a 143-epoch series at ~50 minutes, of
// which the rendering was a rounding error.
//
// Everything in that list depends on the shape model, not on the epoch, so it happens
// ONCE here:
//
//   GL context and SPICE            once per process
//   the shape model                 once -- OPC hierarchies and texture layer, or the
//                                   OBJ parse (175 MB of ASCII for the DSK Dimorphos)
//   the de-shading fit              once -- it fits a baked light direction against a
//                                   per-vertex layer (OPC) or the texture (mesh); no
//                                   camera, sun or time enters it
//   bounding box                    once
//   both scene graphs               once -- the sun, the camera and the shadow frustum
//                                   are cvals, so an epoch is a transact, not a rebuild
//   render targets and tasks        once per variant
//
// Per epoch there remains: the camera and sun from SPICE, a shadow-map pass (the sun
// moves), and one render per variant. The lit variants differ ONLY in shading uniforms --
// micro-structure amplitude, whether the texture is used as albedo, and whether its baked
// illumination is divided out first -- so one scene graph serves all of them.

/// One shading preset. Each differs from its neighbour in exactly one thing, which is what
/// makes a pair of them worth comparing:
///
///   SMOOTH -> MICRO   adds procedural micro-structure to a constant albedo
///   MICRO  -> BAKED   adds the real texture, illumination and all
///   BAKED  -> DELIT   divides that baked illumination back out
///
/// The last pair is the one that answers "is de-lighting necessary for my reconstruction?"
/// -- a question about the reconstruction, which cannot be asked without the frame that
/// skips the step.
type Variant =
    {
        name : string
        /// uppercase tag in the filename, so an epoch is the same stamp in every variant
        tag  : string
        /// use the OPC texture as albedo at all (false = the constant --albedo)
        texture : bool
        /// divide the texture's baked illumination out first. Requires `texture`.
        deshade : bool
        /// normal perturbation strength -- SMOOTH pins it to 0 whatever the command line says
        micro : float
    }

module Variant =

    let private known (microAmplitude : float) (name : string) =
        match name with
        | "delit"  -> Ok { name = "delit";  tag = "DELIT";  texture = true;  deshade = true;  micro = microAmplitude }
        // DELITPLAIN is DELIT with the procedural micro-structure switched off and nothing
        // else changed. Micro-structure is SHADING, not geometry: it perturbs the normal,
        // casts no shadow and does not move the silhouette, so a reconstruction will
        // happily turn it into relief the shape model does not have. A consumer who cannot
        // afford that needs the frame without it, and pairing the two is what shows how
        // much of a result came from it.
        | "delitplain" ->
            Ok { name = "delitplain"; tag = "DELITPLAIN"; texture = true; deshade = true; micro = 0.0 }
        // BAKED is DELIT with the division switched off and nothing else changed, which
        // is what makes the pair worth having: it is the only difference between them.
        | "baked"  -> Ok { name = "baked";  tag = "BAKED";  texture = true;  deshade = false; micro = microAmplitude }
        | "micro"  -> Ok { name = "micro";  tag = "MICRO";  texture = false; deshade = false; micro = microAmplitude }
        | "smooth" -> Ok { name = "smooth"; tag = "SMOOTH"; texture = false; deshade = false; micro = 0.0 }
        | other ->
            Result.Error (sprintf "--variants: '%s' is not a variant; use any of delit, delitplain, baked, micro, smooth" other)

    let parse (spec : string) (microAmplitude : float) : Result<Variant list, string> =
        let names =
            (if isNull spec then "" else spec).Split(',')
            |> Array.map (fun s -> s.Trim().ToLowerInvariant())
            |> Array.filter (fun s -> s <> "")
            |> Array.distinct
            |> Array.toList
        if List.isEmpty names then Result.Error "--variants is empty"
        else
            (Ok [], names)
            ||> List.fold (fun acc n ->
                match acc, known microAmplitude n with
                | Result.Error e, _ -> Result.Error e
                | _, Result.Error e -> Result.Error e
                | Ok xs, Ok v -> Ok (xs @ [v]))

/// `HERA_AFC-1` -> `AFC1`: the instrument without its spacecraft prefix or punctuation.
/// Only a filename, so a name that does not fit the pattern degrades to itself rather
/// than failing the run.
let stemPrefixOf (instrument : string) =
    let tail =
        match instrument.Split('_') with
        | [||] -> instrument
        | parts -> parts.[parts.Length - 1]
    let cleaned = tail.Replace("-", "").Replace(" ", "")
    if String.IsNullOrWhiteSpace cleaned then instrument else cleaned.ToUpperInvariant()

/// Epochs, one ISO-8601 UTC time per line. Blank lines and `#` comments are skipped so
/// the list can carry its own provenance.
let readTimes (path : string) : Result<DateTime[], string> =
    if not (File.Exists path) then Result.Error (sprintf "--times-file not found: %s" path)
    else
    let parsed =
        File.ReadAllLines path
        |> Array.mapi (fun i line -> i + 1, line.Trim())
        |> Array.filter (fun (_, l) -> l <> "" && not (l.StartsWith "#"))
        |> Array.map (fun (lineNo, l) ->
            match DateTime.TryParse(l, CultureInfo.InvariantCulture,
                                    DateTimeStyles.AdjustToUniversal ||| DateTimeStyles.AssumeUniversal) with
            | true, t -> Ok t
            | _ -> Result.Error (sprintf "%s line %d: '%s' is not an ISO-8601 time" path lineNo l))
    match parsed |> Array.tryPick (function Result.Error e -> Some e | _ -> None) with
    | Some e -> Result.Error e
    | None ->
        let times = parsed |> Array.choose (function Ok t -> Some t | _ -> None)
        if times.Length = 0 then Result.Error (sprintf "%s holds no epochs" path)
        else Ok (times |> Array.sort)

// ---------------------------------------------------------------------------------
// The sun-side shadow pass, built once and re-rendered per epoch.
//
// simulate-image's renderSunShadowMap builds a second scene graph and warms its LOD tree
// on every call. Across a series that is the single most expensive per-frame item, and
// none of it depends on the epoch except the sun direction -- so the graph is built once
// over cvals and `update` only moves the sun camera and re-runs the pass.

type private ShadowPass =
    {
        /// body-fixed world -> sun clip space, for the shader's SunShadowViewProj
        viewProj : aval<M44d>
        depth    : aval<ITexture>
        /// point the sun camera at this direction and re-render the depth map
        update   : V3d -> unit
        cleanup  : unit -> unit
    }

let private createShadowPass (runtime : IRuntime) (shape : ShapeSource)
                             (projectedImages : aval<Option<Sg.ProjectedImages>>)
                             (mapSize : int) (noShadows : bool) : ShadowPass =
    let bbox = shape.bbox
    if noShadows then
        let dummy = dummyShadowMap runtime
        {
            viewProj = AVal.constant M44d.Identity
            depth = AVal.constant dummy.depth
            update = ignore
            cleanup = dummy.cleanup
        }
    else
    let signature, depth, output, cleanup = createShadowTarget runtime (V2i(mapSize, mapSize))
    let viewC = cval Trafo3d.Identity
    let projC = cval Trafo3d.Identity
    let viewProjC = cval M44d.Identity

    let sg =
        shape.build signature projectedImages
        |> Sg.shader {
            do! PRo3D.SPICE.Shaders.stableTrafo
            do! DefaultSurfaces.constantColor C4f.White
        }
        |> SunAnglesVerb.withOpcScaffolding
        |> Sg.viewTrafo viewC
        |> Sg.projTrafo projC

    let clear = runtime.CompileClear(signature, AVal.constant (C4f(0.0f, 0.0f, 0.0f, 0.0f)), AVal.constant 1.0)
    let task = runtime.CompileRender(signature, sg)

    let update (sunDir : V3d) =
        let center = bbox.Center
        let radius = 0.5 * bbox.Size.Length
        let up = if abs (Vec.dot sunDir V3d.OOI) > 0.98 then V3d.OIO else V3d.OOI
        let view =
            CameraView.lookAt (center + sunDir * (3.0 * radius)) center up
            |> CameraView.viewTrafo
        // Frustum.ortho takes the box's Z verbatim as near/far, but the camera looks down
        // -Z, so the body sits at NEGATIVE view-space Z and near/far must be the negated
        // maximum/minimum -- otherwise the depth map stays empty and every fragment
        // reprojects behind it, i.e. the whole body renders shadowed.
        let vbox = bbox.Transformed view
        let proj =
            { Frustum.ortho vbox with near = -vbox.Max.Z; far = -vbox.Min.Z }
            |> Frustum.projTrafo
        transact (fun () ->
            viewC.Value <- view
            projC.Value <- proj
            viewProjC.Value <- (view * proj).Forward)
        // Warm-up for the same reason as the main pass: an OPC's LOD tree refines only
        // after a frame has been rendered with the final camera. Cheap after the first
        // epoch -- the patches are already resident, so this is GPU work, not disk. A mesh
        // asks for one pass, and on 3.1 M triangles the difference is the run.
        for _ in 1 .. max 1 shape.warmupFrames do
            clear.Run(output)
            task.Run(output)

    {
        viewProj = viewProjC :> aval<M44d>
        depth = AVal.constant (depth :> ITexture)
        update = update
        cleanup = fun () ->
            task.Dispose()
            clear.Dispose()
            cleanup ()
    }

// ---------------------------------------------------------------------------------

/// What went wrong with one frame, for the summary at the end.
type private Failure = { time : DateTime; variant : string; why : string }

/// Entry point for the `simulate-series` verb.
let run (o : SimulateSeriesOptions) : int =
    let body       = if String.IsNullOrWhiteSpace o.body       then "DIMORPHOS"       else o.body
    let frame      = if String.IsNullOrWhiteSpace o.frame      then "DIMORPHOS_FIXED" else o.frame
    let observer   = if String.IsNullOrWhiteSpace o.observer   then "HERA"            else o.observer
    let instrument = if String.IsNullOrWhiteSpace o.instrument then "HERA_AFC-1"      else o.instrument
    let stemPrefix = if String.IsNullOrWhiteSpace o.stemPrefix then stemPrefixOf instrument else o.stemPrefix

    match readTimes o.timesFile with
    | Result.Error e -> Log.error "%s" e; 1
    | Ok times ->

    match Variant.parse o.variants o.microAmplitude with
    | Result.Error e -> Log.error "%s" e; 1
    | Ok variants ->

    // Before anything is deleted or loaded: the run recreates the variant folders further
    // down, and a typo in --obj must not cost the frames that are already there.
    match ShapeSource.precheck o.opc o.obj o.objTexture with
    | Result.Error e -> Log.error "%s" e; 1
    | Ok () ->

    // Refused rather than degraded, and refused here for the same reason: a textured
    // variant on a shape that carries no texture cannot be rendered as what it says on the
    // folder. `delit` would come out pixel for pixel identical to `micro`, and nothing in
    // the delivery would say so -- exactly the class of thing a variant folder exists to
    // make visible. An OPC always declares texture layers; only a mesh can lack one.
    let textured = variants |> List.filter (fun v -> v.texture)
    if not (String.IsNullOrWhiteSpace o.obj) && String.IsNullOrWhiteSpace o.objTexture
       && not (List.isEmpty textured) then
        Log.error "this shape model carries no texture, so the %s variant(s) cannot be rendered: \
                   they would be identical to 'micro' and the folder would not say so. \
                   Pass --obj-texture, or --variants %s."
            (textured |> List.map (fun v -> v.name) |> String.concat ", ")
            (variants |> List.filter (fun v -> not v.texture) |> List.map (fun v -> v.name)
             |> function [] -> "micro,smooth" | xs -> String.concat "," xs)
        1
    else

    match PointingSource.parse o.pointing with
    | Result.Error e -> Log.error "%s" e; 1
    | Ok pointing ->

    match Spice.resolveKernelRoot o.kernelRoot with
    | Result.Error e -> Spice.reportMissingKernelRoot e; 1
    | Ok kernelRoot ->

    let kernel =
        if not (String.IsNullOrWhiteSpace o.kernel) then
            if File.Exists o.kernel then Ok o.kernel
            else Result.Error (sprintf "kernel not found: %s" o.kernel)
        else
            let f = Path.Combine(kernelRoot, "mk", "hera_plan.tm")
            if File.Exists f then Ok f
            else Result.Error (sprintf "no metakernel: pass --kernel or provide %s" f)

    match kernel with
    | Result.Error e -> Log.error "%s" e; 1
    | Ok kernel ->

    let usingObj = not (String.IsNullOrWhiteSpace o.obj)

    // --deshade-layer selects the texture layer too: the fit and the divisor must be the
    // same layer, and defaulting one from the other is what stops them drifting apart.
    // A mesh has no layers -- it draws --obj-texture or nothing.
    let wantedTexture =
        if usingObj then ""
        elif not (String.IsNullOrWhiteSpace o.textureLayer) then o.textureLayer
        else o.deshadeLayer
    let textureLayer =
        match wantedTexture with
        | null | "" -> Ok None
        | wanted ->
            match OpcTextureLayers.resolve o.opc wanted with
            | Some idx -> Ok (Some idx)
            | None -> Result.Error ()

    match textureLayer with
    | Result.Error () -> 1
    | Ok textureLayer ->

    if o.gain <= 0.0 then
        Log.warn "[expose] --gain 0 auto-exposes EVERY frame on its own, which removes the \
                  one thing a series shows: the body getting brighter and darker as the \
                  illumination changes. Pass a fixed gain to keep the series comparable."

    let size =
        let native =
            match Map.tryFind instrument nativeSizes with
            | Some n -> n
            | None ->
                if o.width <= 0 || o.height <= 0 then
                    Log.warn "no native detector size known for %s -- using 1024x1024 (override with --width/--height)" instrument
                V2i(1024, 1024)
        V2i((if o.width > 0 then o.width else native.X),
            (if o.height > 0 then o.height else native.Y))

    Log.line "[series] %d epoch(s) x %d variant(s) = %d frames, %s .. %s"
        times.Length variants.Length (times.Length * variants.Length)
        ((Array.head times).ToString "o") ((Array.last times).ToString "o")
    Log.line "[series] variants: %s" (variants |> List.map (fun v -> v.name) |> String.concat ", ")

    // The output folders are recreated, not merged into. A resumed folder is how frames
    // from two different builds ended up side by side, 90 degrees apart, with nothing in
    // the data saying so -- and a series that renders in one process has nothing to gain
    // from resuming.
    let variantDir (v : Variant) = Path.Combine(o.out, v.name)
    // Where one frame goes. With --day-folders the variant folder is split by UTC date,
    // which is what an 85-day set needs to stay navigable -- and is how the reference COP
    // delivery is laid out. The stamp in the filename is unchanged either way, so a frame
    // is still findable by epoch alone.
    let frameDir (v : Variant) (t : DateTime) =
        if o.dayFolders then Path.Combine(variantDir v, t.ToString "yyyy-MM-dd")
        else variantDir v
    Directory.CreateDirectory o.out |> ignore
    for v in variants do
        let d = variantDir v
        if Directory.Exists d then Directory.Delete(d, true)
        Directory.CreateDirectory d |> ignore

    Aardvark.Init()
    use app = new OpenGlApplication()
    let runtime = app.Runtime :> IRuntime

    use _spice = SpiceBoot.init (Some kernel)
    Log.line "[spice] %s" kernel

    match ShapeSource.resolve runtime body o.opc o.obj o.objScale o.objTexture textureLayer with
    | Result.Error e -> Log.error "%s" e; 1
    | Ok shape ->

    Log.line "[shape] %s" shape.describe

    // empty is "the patch default", not "look up the empty layer"
    let occluderLayer =
        if String.IsNullOrWhiteSpace o.occluderOpc
           || String.IsNullOrWhiteSpace o.occluderTextureLayer then None
        else OpcTextureLayers.resolve o.occluderOpc o.occluderTextureLayer
    match (if String.IsNullOrWhiteSpace o.occluderBody then Ok None
           else ShapeSource.occluder runtime o.occluderBody o.occluderOpc o.occluderObj
                    o.occluderObjScale occluderLayer EclipseOccluder.didymosRadii
                |> Result.map Some) with
    | Result.Error e -> Log.error "[eclipse] %s" e; 1
    | Ok occluderShape ->

    let bbox = shape.bbox

    // How the primary is surfaced. Its own numbers, and worked out once for the run like
    // the target's fit below -- no epoch enters either.
    let occluderSurface =
        occluderSurfaceOf occluderShape o.occluderBody o.occluderDeshade o.occluderTextureAlbedo
            (occluderLayerName o.occluderDeshadeLayer o.occluderTextureLayer) o.albedo

    // Once for the whole series: the fit solves for the baked light direction against a
    // per-vertex layer (OPC) or the texture (mesh). No epoch, camera or sun enters it, so
    // its three uniforms are constant across every frame and every variant.
    let deshade =
        if List.isEmpty textured then None
        else
            let layerName =
                if not (String.IsNullOrWhiteSpace o.deshadeLayer) then o.deshadeLayer
                elif not (String.IsNullOrWhiteSpace o.textureLayer) then o.textureLayer
                else "DRACO"
            fitOrFallBack shape layerName o.albedo

    // The exposure of the `baked` variant, which needs no fit -- see `rawScaleFor`. Asked
    // for unconditionally because `shading` decides per variant whether it is used, the
    // same way the fit is.
    let targetRawScale =
        if List.isEmpty textured then None
        else rawScaleFor shape deshade o.albedo body

    // The epoch-varying inputs. Everything below is built once over these, so an epoch
    // costs a transact and a render rather than a scene graph.
    let viewC = cval Trafo3d.Identity
    let projC = cval Trafo3d.Identity
    let sunC  = cval (None : Option<V3d>)
    let imageProjC = cval (None : Option<Trafo3d>)
    // Where the occluder sits this epoch, for --occluder-in-scene. Separate from the
    // shadow pass's own placement because the shadow pass owns a scene graph of its own;
    // both are driven from the same EclipseOccluder, so they cannot disagree.
    let occluderPlacementC = cval Trafo3d.Identity
    let occluderVisibleC = cval false
    let occluderFrame =
        if String.IsNullOrWhiteSpace o.occluderFrame then o.occluderBody + "_FIXED"
        else o.occluderFrame
    if not (String.IsNullOrWhiteSpace o.occluderBody) then
        Log.line "[eclipse] %s cast onto %s (%s)" o.occluderBody body
            (occluderShape |> Option.map (fun s -> s.describe) |> Option.defaultValue "no shape")

    let projectedImages : aval<Option<Sg.ProjectedImages>> =
        // Must go through this record, not Sg.uniform': projectionUniformMap installs
        // SunDirectionWorld as a PER-PATCH uniform sourced from here, and being deeper in
        // the graph it wins over any outer uniform of the same name.
        AVal.constant (
            Some {
                imageProjection = imageProjC :> aval<_>
                stackProjections = AVal.constant [||]
                stackCoverageEnabled = AVal.constant false
                hoveredProjection = AVal.constant None
                windingCorrection = AVal.constant false
                sunDirection = sunC :> aval<_>
                sunLightEnabled = AVal.constant true
                lightViewProj = AVal.constant None
            })

    Log.line "[shadow] sun depth map %d^2 over %.0f m = %.3f m a texel"
        o.shadowMap bbox.Size.Length (bbox.Size.Length / float o.shadowMap)
    let shadow = createShadowPass runtime shape projectedImages o.shadowMap o.noShadows
    // The occluder's own sun-side depth map, built once and re-aimed per epoch like the
    // target's. Created even with --no-shadows: an eclipse is the other body blocking the
    // sun, not the target's self-shadowing, and switching off the one has never been a
    // reason to switch off the other.
    let eclipse =
        match occluderShape with
        | Some occ -> EclipseShadow.create runtime occ o.shadowMap
        | None -> EclipseShadow.disabled runtime
    let target = SunAnglesVerb.FloatTarget.create runtime size

    try
        let opc = shape.build target.signature projectedImages

        // --occluder-in-scene: the primary in the image, not only in the shadow map. Its
        // placement moves every epoch, so it rides on cvals like everything else here and
        // an epoch stays a transact rather than a rebuild.
        let scene =
            match occluderShape with
            | Some occ when o.occluderInScene ->
                Log.line "[eclipse] %s is in the scene as well as casting" o.occluderBody
                Sg.ofList [ opc
                            occluderSg occ target.signature projectedImages occluderSurface
                                (occluderPlacementC :> aval<_>) (occluderVisibleC :> aval<_>) ]
            | _ -> opc

        // One compiled task per variant over the SAME geometry: they differ only in the
        // shading uniforms, and micro/smooth never sample the texture, so the texture
        // layer bound for delit is harmless to them.
        let tasks =
            variants
            |> List.map (fun v ->
                let shading =
                    {
                        albedo = o.albedo; microScale = o.microScale; microAmplitude = v.micro
                        ambient = o.ambient; shadowBias = o.shadowBias; noShadows = o.noShadows
                        noLighting = o.noLighting; textureOnly = false
                        textureAlbedo = v.texture && not v.deshade
                        deshade = v.deshade
                    }
                // The fit goes to every variant, including the ones that ignore it: it is
                // what carries both scale factors, and which of them a variant uses is
                // decided by `shading` alone. MICRO and SMOOTH ask for neither and get the
                // constant albedo.
                let sg =
                    shadedShaders scene
                    |> applyShading shading deshade targetRawScale eclipse shadow.viewProj shadow.depth
                    |> Sg.viewTrafo viewC
                    |> Sg.projTrafo projC
                v, runtime.CompileRender(target.signature, sg))

        let clear =
            runtime.CompileClear(target.signature, AVal.constant (C4f(0.0f, 0.0f, 0.0f, 0.0f)), AVal.constant 1.0)

        let failures = ResizeArray<Failure>()
        let mutable written = 0
        let mutable aspectChecked = false
        let sw = System.Diagnostics.Stopwatch.StartNew()

        let mutable i = 0
        let mutable stop = false
        while not stop && i < times.Length do
            let time = times.[i]
            i <- i + 1

            let epoch =
                match cameraAt observer frame body instrument pointing o.distance time with
                | Result.Error e -> Result.Error e
                | Ok cam ->
                    match pointingComplaint bbox cam body instrument time with
                    | Some e -> Result.Error e
                    | None ->
                        match InstrumentObservation.sunDirection frame body time with
                        | Result.Error e -> Result.Error (sprintf "no sun direction: %s" e)
                        | Ok sun -> Ok (cam, sun)

            match epoch with
            | Result.Error e ->
                // One entry per variant: the epoch produced no frame in any of them, and
                // the summary counts frames, not epochs.
                for v in variants do failures.Add { time = time; variant = v.name; why = e }
                Log.warn "[%d/%d] %s -- %s" i times.Length (time.ToString "o") e
                if not o.keepGoing then stop <- true
            | Ok (cam, sun) ->

            if not aspectChecked then
                aspectChecked <- true
                if abs (float size.X / float size.Y - cam.aspect) > 1e-3 then
                    Log.warn "output %dx%d (ratio %.3f) does not match %s's frustum aspect %.3f -- the images will be stretched"
                        size.X size.Y (float size.X / float size.Y) instrument cam.aspect

            // The occluding body moves and turns relative to the target, so this is per
            // epoch like the sun.
            let occluderAt =
                match occluderShape with
                | None -> None
                | Some occ ->
                    EclipseOccluder.at o.occluderBody occluderFrame occ.bbox body frame time

            transact (fun () ->
                viewC.Value <- cam.view
                projC.Value <- cam.proj
                sunC.Value <- Some sun
                imageProjC.Value <- Some (cam.view * cam.proj)
                occluderVisibleC.Value <- Option.isSome occluderAt
                match occluderAt with
                | Some e -> occluderPlacementC.Value <- e.toTarget
                | None -> ())

            // The sun moves between epochs, so both depth maps do.
            shadow.update sun
            eclipse.update occluderAt sun

            for (v, task) in tasks do
                let stem = sprintf "%s_%s_%s" stemPrefix v.tag (time.ToString "yyyyMMdd_HHmmss")
                let dir = frameDir v time
                if o.dayFolders then Directory.CreateDirectory dir |> ignore
                let outPath = Path.Combine(dir, stem + ".png")

                // More warm-up than sun-angles on an OPC: the LOD tree descends one
                // refinement per rendered frame. Cheap there -- after the first epoch the
                // patches are resident and these are GPU passes, not disk reads. A mesh
                // asks for one pass, which on 3.1 M triangles is the difference between a
                // minute of rendering and eight.
                for _ in 1 .. max 1 shape.warmupFrames do
                    clear.Run(target.output)
                    task.Run(target.output)
                let rendered = runtime.Download(target.color).ToPixImage<float32>()

                match toneMapToPng rendered o.gain with
                | Result.Error e ->
                    failures.Add { time = time; variant = v.name; why = e }
                | Ok (png, _) ->
                    png.Save(outPath)

                    // Validation is part of rendering, not a later pass: the sidecar is
                    // written from the trafo the render actually used and then read back
                    // through the VIEWER's own path, so what is checked is the real round
                    // trip. A frame whose sidecar does not reconstruct its own camera
                    // cannot be projected, and finding that out after the series is
                    // finished costs the series.
                    let ctx : MbiSidecar.Context =
                        { instrument = instrument; body = body; frame = frame
                          time = time; kernel = kernel; size = size }
                    match MbiSidecar.write ctx outPath cam.view with
                    | Result.Error e ->
                        failures.Add { time = time; variant = v.name; why = sprintf "no sidecar: %s" e }
                    | Ok _ ->
                        match MbiSidecar.writeStatistics ctx outPath png with
                        | Result.Error e ->
                            failures.Add { time = time; variant = v.name
                                           why = sprintf "no statistics sidecar: %s" e }
                        | Ok _ ->
                            match MbiSidecar.verify ctx outPath (cam.view * cam.proj) observer with
                            | Result.Error e ->
                                failures.Add { time = time; variant = v.name
                                               why = sprintf "could not verify the sidecar: %s" e }
                            | Ok r when r.pixels > o.maxReprojectionError ->
                                failures.Add
                                    { time = time; variant = v.name
                                      why = sprintf "the viewer reconstructs a DIFFERENT camera from this \
                                                     sidecar (%.3f px > --max-reprojection-error %.3f); \
                                                     projecting this frame would not overlay the render"
                                                r.pixels o.maxReprojectionError }
                            | Ok _ ->
                                written <- written + 1

            let done_ = i * variants.Length
            if i = 1 || i % 10 = 0 || i = times.Length then
                let per = sw.Elapsed.TotalSeconds / float done_
                let left = float ((times.Length - i) * variants.Length) * per
                Log.line "[%d/%d] %s  %d frames, %.2f s/frame, ~%.0f s left"
                    i times.Length (time.ToString "o") done_ per left

            if failures.Count > 0 && not o.keepGoing then stop <- true

        sw.Stop()
        Log.line "[series] %d frame(s) in %.1f s (%.2f s/frame)"
            written sw.Elapsed.TotalSeconds
            (sw.Elapsed.TotalSeconds / float (max 1 written))

        if failures.Count > 0 then
            Log.error "[series] %d frame(s) FAILED:" failures.Count
            for f in Seq.truncate 20 failures do
                Log.error "   %s %-7s %s" (f.time.ToString "o") f.variant f.why
            if failures.Count > 20 then
                Log.error "   ... and %d more" (failures.Count - 20)
            if not o.keepGoing then
                Log.error "[series] stopped at the first failure -- pass --keep-going to render the rest anyway"
            1
        else
            Log.line "[series] every frame rendered and every sidecar reconstructs its own camera \
                      to within %.3f px" o.maxReprojectionError
            0
    finally
        SunAnglesVerb.FloatTarget.dispose target
        shadow.cleanup ()
        eclipse.cleanup ()
