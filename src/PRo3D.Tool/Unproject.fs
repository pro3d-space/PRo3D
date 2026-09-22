module PRo3D.Tool.UnprojectVerb

open System
open System.IO
open System.Globalization
open System.Collections.Generic

open Aardvark.Base
open Aardvark.Data.Opc
open Aardvark.SceneGraph.Opc
open Aardvark.VRVis.Opc.KdTrees
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.ImageMapping

// ---------------------------------------------------------------------------------------
// input
// ---------------------------------------------------------------------------------------

/// One line of the input table. `extras` is everything past the third column and is written
/// back out untouched, so the caller's own identifiers survive the round trip.
type InputRow =
    {
        line   : int
        /// the row exactly as it was read, written back out unchanged
        cells  : string[]
        image  : string
        pixel  : V2d
        error  : Option<string>
    }

let private splitOn (delimiter : char) (line : string) =
    if delimiter = ' ' then line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
    else line.Split delimiter |> Array.map (fun s -> s.Trim())

/// Comma, semicolon, tab or whitespace, whichever the file uses. Semicolon matters: it is what
/// Excel writes on a European locale.
let private detectDelimiter (line : string) =
    if line.Contains '\t' then '\t'
    elif line.Contains ';' then ';'
    elif line.Contains ',' then ','
    else ' '

let private tryFloat (s : string) =
    match Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, v -> Some v
    | _ -> None

/// Parse the table. A row that cannot be read becomes an error row rather than aborting the
/// run: one malformed line in a long list should not cost the caller the other results.
let parseInput (path : string) : Result<string[] * InputRow list, string> =
    let lines = File.ReadAllLines path
    let firstContent = lines |> Array.tryFind (fun l -> not (String.IsNullOrWhiteSpace l))
    match firstContent with
    | None -> Result.Error "input file is empty"
    | Some first ->

    let delimiter = detectDelimiter first

    // A header is a first row whose x/y columns are not numbers. Detecting it beats a flag:
    // there is nothing to get wrong, and a file with and without a header both just work.
    let isHeader (fields : string[]) =
        fields.Length < 3 || (tryFloat fields.[1]).IsNone || (tryFloat fields.[2]).IsNone

    let headerFields =
        let f = splitOn delimiter first
        if isHeader f then Some f else None

    let rows =
        lines
        |> Array.mapi (fun i l -> i + 1, l)
        |> Array.filter (fun (_, l) -> not (String.IsNullOrWhiteSpace l))
        |> Array.skip (if headerFields.IsSome then 1 else 0)
        |> Array.map (fun (n, l) ->
            let f = splitOn delimiter l
            if f.Length < 3 then
                { line = n; cells = f; image = ""; pixel = V2d.Zero
                  error = Some (sprintf "expected at least 3 columns, got %d" f.Length) }
            else
                match tryFloat f.[1], tryFloat f.[2] with
                | Some x, Some y ->
                    { line = n; cells = f; image = f.[0]; pixel = V2d(x, y); error = None }
                | _ ->
                    { line = n; cells = f; image = f.[0]; pixel = V2d.Zero
                      error = Some (sprintf "could not read '%s','%s' as pixel coordinates" f.[1] f.[2]) })
        |> Array.toList

    let header =
        match headerFields with
        | Some h when h.Length >= 3 -> h
        | _ ->
            let extras = (rows |> List.fold (fun m r -> max m (r.cells.Length - 3)) 0)
            Array.append [| "image"; "x"; "y" |] (Array.init extras (fun i -> sprintf "extra%d" (i + 1)))

    Ok (header, rows)

// ---------------------------------------------------------------------------------------
// output
// ---------------------------------------------------------------------------------------

/// One resolved input row.
type OutputRow =
    {
        input      : InputRow
        status     : string
        position   : Option<V3d>
        lonLatAlt  : Option<CooTransformation.SphericalCoo>
        range      : Option<float>
        attributes : (string * float[]) list
    }

/// An image resolved to everything a ray needs.
type private ResolvedCamera =
    {
        camera : ProjectorCamera
        size   : V2i
    }

let private formatFloat (v : float) = v.ToString("G", CultureInfo.InvariantCulture)

/// Vector-valued layers become one column per component: these tables get loaded into other
/// tools, which cannot split a packed cell.
let attributeColumns (name : string) (values : float[]) =
    if values.Length = 1 then [ name, values.[0] ]
    elif values.Length = 3 then
        [ name + "_x", values.[0]; name + "_y", values.[1]; name + "_z", values.[2] ]
    else values |> Array.toList |> List.mapi (fun i v -> sprintf "%s_%d" name i, v)

let writeTable (path : string) (header : string[]) (rows : OutputRow list) =
    let delimiter = if Path.GetExtension(path).ToLowerInvariant() = ".csv" then "," else "\t"

    // The attribute set comes from the data, so it is only known once every row is resolved.
    let attributeNames =
        rows
        |> List.collect (fun r -> r.attributes |> List.collect (fun (n, v) -> attributeColumns n v |> List.map fst))
        |> List.distinct

    // Named with units and distinct from the input's own x/y, which would otherwise collide.
    let geometry = [ "status"; "x_m"; "y_m"; "z_m"; "lat_deg"; "lon_deg"; "alt_m"; "range_m" ]

    use w = new StreamWriter(path)
    w.WriteLine(String.Join(delimiter, Array.concat [ header; List.toArray geometry; List.toArray attributeNames ]))

    for r in rows do
        let inputCells = r.input.cells
        let padded =
            if inputCells.Length >= header.Length then inputCells
            else Array.append inputCells (Array.create (header.Length - inputCells.Length) "")
        let p = r.position
        let s = r.lonLatAlt
        let geometryCells =
            [|
                r.status
                (match p with Some p -> formatFloat p.X | None -> "")
                (match p with Some p -> formatFloat p.Y | None -> "")
                (match p with Some p -> formatFloat p.Z | None -> "")
                (match s with Some s -> formatFloat s.latitude | None -> "")
                (match s with Some s -> formatFloat s.longitude | None -> "")
                (match s with Some s -> formatFloat s.altitude | None -> "")
                (match r.range with Some d -> formatFloat d | None -> "")
            |]
        let byName =
            r.attributes
            |> List.collect (fun (n, v) -> attributeColumns n v)
            |> dict
        let attributeCells =
            attributeNames
            |> List.map (fun n -> match byName.TryGetValue n with | true, v -> formatFloat v | _ -> "")
            |> List.toArray
        w.WriteLine(String.Join(delimiter, Array.concat [ padded; geometryCells; attributeCells ]))
// ---------------------------------------------------------------------------------------
// options
// ---------------------------------------------------------------------------------------

/// `--config` supplies the same names as the flags. Flags win, so a stored configuration can
/// be overridden for one run without editing it.
let private applyConfig (o : UnprojectOptions) : Result<UnprojectOptions, string> =
    if String.IsNullOrWhiteSpace o.config then Ok o
    elif not (File.Exists o.config) then Result.Error (sprintf "config not found: %s" o.config)
    else
        try
            use doc = Text.Json.JsonDocument.Parse(File.ReadAllText o.config)
            let str (name : string) (current : string) =
                if not (String.IsNullOrWhiteSpace current) then current
                else
                    match doc.RootElement.TryGetProperty name with
                    | true, v when v.ValueKind = Text.Json.JsonValueKind.String -> v.GetString()
                    | _ -> current
            Ok { o with
                    opc = str "opc" o.opc
                    images = str "images" o.images
                    input = str "input" o.input
                    out = str "out" o.out
                    body = str "body" o.body
                    frame = str "frame" o.frame
                    observer = str "observer" o.observer
                    kernel = str "kernel" o.kernel
                    kernelRoot = str "kernel-root" o.kernelRoot
                    method = str "method" o.method
                    pixelConvention = str "pixel-convention" o.pixelConvention }
        with e -> Result.Error (sprintf "could not read %s: %s" o.config e.Message)

let private parseConvention (s : string) =
    match (if isNull s then "image" else s).ToLowerInvariant() with
    | "image" -> Some InstrumentObservation.PixelConvention.Image
    | "fits" -> Some InstrumentObservation.PixelConvention.Fits
    | _ -> None

// ---------------------------------------------------------------------------------------
// the verb
// ---------------------------------------------------------------------------------------

let run (o : UnprojectOptions) : int =
    match applyConfig o with
    | Result.Error e -> Log.error "%s" e; 1
    | Ok o ->

    let body = if String.IsNullOrWhiteSpace o.body then "DIDYMOS" else o.body
    let frame = if String.IsNullOrWhiteSpace o.frame then "DIDYMOS_FIXED" else o.frame
    let outPath = if String.IsNullOrWhiteSpace o.out then Path.Combine(".", "unproject.csv") else o.out

    let projectionMethod =
        match (if isNull o.method then "mbi" else o.method).ToLowerInvariant() with
        | "spice" -> Some ProjectionMethod.Spice
        | "mbi" -> Some ProjectionMethod.MbiBased
        | _ -> None

    match projectionMethod, parseConvention o.pixelConvention with
    | None, _ -> Log.error "unknown --method '%s' (expected spice or mbi)" o.method; 1
    | _, None -> Log.error "unknown --pixel-convention '%s' (expected image or fits)" o.pixelConvention; 1
    | Some projectionMethod, Some convention ->

    if String.IsNullOrWhiteSpace o.input || not (File.Exists o.input) then Log.error "input file not found: %s" o.input; 1
    elif String.IsNullOrWhiteSpace o.opc || not (Directory.Exists o.opc) then Log.error "OPC directory not found: %s" o.opc; 1
    elif String.IsNullOrWhiteSpace o.images || not (Directory.Exists o.images) then Log.error "image folder not found: %s" o.images; 1
    else

    match Spice.resolveKernelRoot o.kernelRoot with
    | Result.Error e -> Spice.reportMissingKernelRoot e; 1
    | Ok kernelRoot ->

    match parseInput o.input with
    | Result.Error e -> Log.error "%s" e; 1
    | Ok (header, rows) ->

    Log.line "[unproject] %d row(s) from %s" rows.Length o.input
    Log.line "[unproject] pixel convention: %s" (if convention = InstrumentObservation.PixelConvention.Fits then "fits (1-based, bottom-left)" else "image (0-based, top-left)")

    // Resolve each distinct image once; the rows of an image share its camera.
    let imageNames = rows |> List.choose (fun r -> if r.error.IsSome then None else Some r.image) |> List.distinct

    // Windows opens a file whatever its case, but the mbi sidecar index is keyed by the exact
    // file name, so a differently-cased row would find the image and then fail to find its
    // metadata -- reported as "no mbi sidecar found", which blames the wrong thing.
    let canonical = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    for f in Directory.EnumerateFiles o.images do
        canonical.[Path.GetFileName f] <- Path.GetFileName f
    let onDisk (name : string) =
        match canonical.TryGetValue(Path.GetFileName name) with
        | true, actual -> actual
        | _ -> name

    let resolved =
        imageNames
        |> List.map (fun name ->
            name, InstrumentObservation.resolveImage o.images (Some (onDisk name)))

    let firstOk = resolved |> List.tryPick (fun (_, r) -> match r with Ok img -> Some img | _ -> None)

    match firstOk with
    | None ->
        Log.error "none of the %d image(s) named in %s could be resolved in %s" imageNames.Length o.input o.images
        1
    | Some sample ->

    // One metakernel for the whole run: SPICE keeps a single active metakernel and layering a
    // second corrupts its state.
    match InstrumentObservation.resolveKernel (if String.IsNullOrWhiteSpace o.kernel then None else Some o.kernel) kernelRoot sample with
    | Result.Error e -> Log.error "%s" e; 1
    | Ok kernel ->

    HeadlessPicking.init ()
    use _spice = SpiceBoot.init (Some kernel)
    Log.line "[spice] %s" kernel

    let hierarchies = HeadlessPicking.loadHierarchies o.opc
    if hierarchies.Length = 0 then
        Log.error "no patch hierarchies (subdirectories containing 'Patches') under %s" o.opc
        1
    else

    let kdTreeMap = HeadlessPicking.loadKdTreeMap hierarchies
    if HashMap.isEmpty kdTreeMap then
        Log.error "no kd-trees found for %s" o.opc
        Log.error "build them first:  pro3d-tool kdtree \"%s\"" o.opc
        1
    else

    let patchInfos = HeadlessPicking.buildPatchInfos hierarchies
    // Unknown bodies simply get no lat/lon columns rather than a made-up convention.
    let planet = CooTransformation.planetFromString body
    if planet.IsNone then
        Log.warn "[unproject] '%s' has no known coordinate convention: lat/lon/alt will be empty" body

    // Camera per image, resolved once.
    // The failure kind is carried along so a row can say whether the image was missing or its
    // pointing failed to resolve -- different problems with different fixes.
    let cameras = Dictionary<string, Result<ResolvedCamera, string * string>>()
    for (name, r) in resolved do
        let entry =
            match r with
            | Result.Error e -> Result.Error ("no-image", e)
            | Ok img ->
                let observer =
                    if not (String.IsNullOrWhiteSpace o.observer) then o.observer
                    else
                        // AFC sits on Hera, ASPECT on Milani; a list may mix them, so the
                        // spacecraft follows the instrument unless the caller overrides it.
                        PRo3D.SPICE.InstrumentProjection.instrument2CameraSource img.mbi.instrument
                match InstrumentObservation.projectorCamera None observer frame body projectionMethod img with
                | Result.Error e -> Result.Error ("no-pointing", e)
                | Ok cam ->
                    match img.size with
                    | None -> Result.Error ("no-pointing", "the sidecar does not declare the image size")
                    | Some size -> Ok { camera = cam; size = size }
        cameras.[name] <- entry

    let mutable cache = HashMap.empty
    let mutable failures = 0

    let outputs =
        rows
        |> List.map (fun row ->
            let report status message =
                Log.warn "[unproject] line %d: %s" row.line message
                // A pixel that misses the body is a legitimate answer -- it says the feature is
                // off the limb. Only an input or resolution problem makes the run fail.
                if status <> "no-hit" then failures <- failures + 1
                { input = row; status = status; position = None; lonLatAlt = None; range = None; attributes = [] }

            match row.error with
            | Some e -> report "bad-input" e
            | None ->

            match cameras.TryGetValue row.image with
            | false, _ -> report "no-image" (sprintf "no image named '%s'" row.image)
            | true, Result.Error (status, e) -> report status e
            | true, Ok rc ->
                let ray = InstrumentObservation.pixelRay rc.camera rc.size convention row.pixel
                let hit, newCache = HeadlessPicking.intersectAll kdTreeMap cache (FastRay3d ray)
                cache <- newCache
                match hit with
                | None -> report "no-hit" (sprintf "pixel (%g, %g) of %s does not meet the surface" row.pixel.X row.pixel.Y row.image)
                | Some hit ->
                    { input = row
                      status = "ok"
                      position = Some hit.position
                      lonLatAlt = planet |> Option.bind (fun p -> CooTransformation.tryGetLatLonAlt p hit.position)
                      range = Some (Vec.length (hit.position - ray.Origin))
                      attributes = HeadlessPicking.sampleAttributes patchInfos hit })

    writeTable outPath header outputs
    let ok = outputs |> List.filter (fun r -> r.status = "ok") |> List.length

    if ok > 0 && outputs |> List.forall (fun r -> r.attributes.IsEmpty) then
        Log.warn "[unproject] this OPC carries no per-vertex attribute layers, so there are no"
        Log.warn "[unproject] slope/gravity/potential/normal columns -- only newer exports ship them"

    Log.line "[out] %s" outPath
    Log.line "[unproject] %d of %d row(s) hit the surface" ok outputs.Length

    if failures > 0 then 1 else 0
