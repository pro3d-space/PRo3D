module PRo3D.Tool.SampleLayersVerb

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading.Tasks

open Aardvark.Base
open Aardvark.Geometry
open Aardvark.Data.Opc
open Aardvark.SceneGraph.Opc
open FSharp.Data.Adaptive

open OpcViewer.Base
open OpcViewer.Base.KdTrees
open Aardvark.VRVis.Opc.KdTrees
open Aardvark.PixImage.LibTiff

open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.SPICE
open PRo3D.ImageMapping

// The sample-layers verb: many instrument observations (AFC frames, ASPECT and HyperScout
// cubes, ...) of one body, assembled onto that body's surface. Stage 1 samples at the
// vertices of the shape model: every vertex gets an id, and every observation a table of the
// vertices it sees, where in the image they land, the illumination geometry, and the value
// of every band at the nearest pixel. The result is one dataset keyed by vertex id, in
// which all instruments are aligned by construction.
//
// Visibility is decided on the CPU by casting a ray from the camera to each vertex through
// the OPC's kd-trees, in double precision. A GPU depth pass would be faster, but its answer
// depends on depth-buffer resolution at exactly the place it matters (the limb and steep
// walls), and the kd-trees are the same ones unproject already uses to go the other way.

// ---------------------------------------------------------------------------------------
// the surface: vertices, normals, attribute layers
// ---------------------------------------------------------------------------------------

/// One per-vertex attribute over all vertices: `values` holds `components` floats per
/// vertex, NaN where a patch does not carry the layer.
type AttributeColumn =
    {
        name       : string
        components : int
        values     : float32[]
    }

/// The points stage 1 samples at: every valid vertex of the finest level of detail, with
/// the vertex's index being its id.
type SurfaceVertices =
    {
        /// body-fixed, metres
        positions  : V3d[]
        /// unit and outward; V3d.Zero where the OPC ships no normal
        normals    : V3d[]
        attributes : list<AttributeColumn>
    }

/// A whole `*.aara` grid as floats. `VertexAttributes` reads single elements for picking;
/// here every element is wanted, so the payload is read in one go.
let private readGrid (path : string) : Option<V2i * int * float32[]> =
    if not (File.Exists path) then None
    else
        use stream = File.OpenRead path
        match VertexAttributes.tryReadHeader stream with
        | None -> None
        | Some h ->
            stream.Seek(h.dataOffset, SeekOrigin.Begin) |> ignore
            let count = h.size.X * h.size.Y * h.components
            let bytes = Array.zeroCreate<byte> (count * h.scalar.ByteSize)
            stream.ReadExactly(bytes, 0, bytes.Length)
            let values =
                match h.scalar with
                | AaraScalar.Float32 ->
                    let a = Array.zeroCreate<float32> count
                    Buffer.BlockCopy(bytes, 0, a, 0, bytes.Length)
                    a
                | AaraScalar.Float64 -> Array.init count (fun i -> float32 (BitConverter.ToDouble(bytes, i * 8)))
                | AaraScalar.Int32   -> Array.init count (fun i -> float32 (BitConverter.ToInt32(bytes, i * 4)))
            Some (h.size, h.components, values)

/// Offset of an attribute grid inside the position grid, which is larger by a symmetric
/// skirt (see VertexAttributes). None when the two cannot be aligned.
let private centring (positions : V2i) (attribute : V2i) : Option<V2i> =
    let d = positions - attribute
    if d.X >= 0 && d.Y >= 0 && d.X % 2 = 0 && d.Y % 2 = 0 then Some (d / 2) else None

/// The per-vertex layers the leaf patches declare, by name.
let availableAttributes (hierarchies : PatchHierarchy[]) : list<string> =
    [ for h in hierarchies do
        for leaf in QTree.getLeaves h.tree do
            for f in leaf.info.Attributes do
                yield Path.GetFileNameWithoutExtension f ]
    |> List.distinctBy (fun n -> n.ToLowerInvariant())
    |> List.sort

/// Collect the vertices of every leaf patch.
///
/// Only the vertices under the attribute grid are taken: the position grid's skirt
/// duplicates geometry the neighbouring patch owns, and carries no attributes. What remains
/// is still not unique -- neighbouring cores share their edge, and a global lon/lat grid
/// collapses a whole row onto each pole and doubles the 0/360 seam column -- so vertices are
/// merged by position (to 1 mm), the first occurrence keeping its normal and attributes.
let collectVertices (hierarchies : PatchHierarchy[]) (attributeNames : list<string>) : SurfaceVertices =
    let positions = ResizeArray<V3d>()
    let normals = ResizeArray<V3d>()
    let columns = attributeNames |> List.map (fun n -> n, ResizeArray<float32>(), ref 0)
    let seen = System.Collections.Generic.HashSet<struct (int64 * int64 * int64)>()
    let warned = System.Collections.Generic.HashSet<string>()

    let findLayer (info : PatchFileInfo) (name : string) =
        info.Attributes
        |> List.tryFind (fun f -> String.Equals(Path.GetFileNameWithoutExtension f, name, StringComparison.OrdinalIgnoreCase))

    for h in hierarchies do
        for leaf in QTree.getLeaves h.tree do
            let info = leaf.info
            let dir = h.opcPaths.Patches_DirAbsPath +/ info.Name
            match readGrid (dir +/ info.Positions) with
            | Some (psize, 3, pos) ->
                let l2g = info.Local2Global

                let layerOf (name : string) =
                    match findLayer info name |> Option.bind (fun f -> readGrid (dir +/ f)) with
                    | Some (size, components, values) ->
                        match centring psize size with
                        | Some offset -> Some (size, components, values, offset)
                        | None ->
                            Log.warn "[layers] %s/%s: %dx%d grid cannot be centred in the %dx%d position grid"
                                info.Name name size.X size.Y psize.X psize.Y
                            None
                    | None -> None

                let normalLayer = layerOf "Normal"
                let attributeLayers =
                    columns |> List.map (fun (name, _, _) ->
                        let l = layerOf name
                        if l.IsNone && warned.Add name then
                            Log.warn "[layers] patch %s has no '%s' layer: its vertices get NaN there" info.Name name
                        l)

                // the core: the attribute grid if there is one, else everything
                let core =
                    match normalLayer, attributeLayers |> List.tryPick id with
                    | Some (size, _, _, offset), _
                    | None, Some (size, _, _, offset) -> Box2i(offset, offset + size - V2i.II)
                    | None, None -> Box2i(V2i.Zero, psize - V2i.II)

                if normalLayer.IsNone && warned.Add "Normal" then
                    Log.warn "[layers] patch %s has no per-vertex normals: its vertices are not tested for facing and get no incidence/emission" info.Name

                for y in core.Min.Y .. core.Max.Y do
                    for x in core.Min.X .. core.Max.X do
                        let i = y * psize.X + x
                        let local = V3d(float pos.[3 * i], float pos.[3 * i + 1], float pos.[3 * i + 2])
                        if local.AllFinite then
                            let p = l2g.Forward.TransformPos local
                            let key = struct (int64 (Math.Round(p.X * 1000.0)), int64 (Math.Round(p.Y * 1000.0)), int64 (Math.Round(p.Z * 1000.0)))
                            if seen.Add key then
                                positions.Add p

                                let attributeIndex (size : V2i) (offset : V2i) = (y - offset.Y) * size.X + (x - offset.X)
                                let inGrid (size : V2i) (offset : V2i) =
                                    x >= offset.X && y >= offset.Y && x < offset.X + size.X && y < offset.Y + size.Y

                                let n =
                                    match normalLayer with
                                    | Some (size, 3, values, offset) when inGrid size offset ->
                                        let j = attributeIndex size offset
                                        let n = l2g.Forward.TransformDir (V3d(float values.[3 * j], float values.[3 * j + 1], float values.[3 * j + 2]))
                                        if n.AllFinite && n.Length > 1e-6 then n.Normalized else V3d.Zero
                                    | _ -> V3d.Zero
                                normals.Add n

                                List.iter2 (fun (_, (target : ResizeArray<float32>), (width : int ref)) layer ->
                                    match layer with
                                    | Some (size, components, (values : float32[]), offset) when inGrid size offset ->
                                        width.Value <- components
                                        let j = attributeIndex size offset
                                        for c in 0 .. components - 1 do target.Add values.[components * j + c]
                                    | Some (_, components, _, _) ->
                                        width.Value <- components
                                        for _ in 1 .. components do target.Add Single.NaN
                                    | None ->
                                        // width not known from this patch: pad with the width seen so far (1 until then)
                                        for _ in 1 .. max 1 width.Value do target.Add Single.NaN) columns attributeLayers
            | Some (_, c, _) ->
                Log.warn "[layers] %s: positions have %d components, expected 3 -- skipped" info.Name c
            | None ->
                Log.warn "[layers] %s: cannot read positions %s -- skipped" info.Name info.Positions

    // Normals are used for facing and for the angles, so their orientation matters. A body's
    // normals point away from its centre on average; flip them all if they do not.
    let positions = positions.ToArray()
    let normals = normals.ToArray()
    if positions.Length > 0 then
        let mutable centre = V3d.Zero
        for p in positions do centre <- centre + p
        centre <- centre / float positions.Length
        let mutable outward = 0.0
        for i in 0 .. positions.Length - 1 do
            outward <- outward + Vec.dot normals.[i] (positions.[i] - centre).Normalized
        if outward < 0.0 then
            Log.warn "[layers] the OPC's normals point inwards -- flipping them"
            for i in 0 .. normals.Length - 1 do normals.[i] <- -normals.[i]

    let attributes =
        columns |> List.choose (fun (name, values, width) ->
            let components = max 1 width.Value
            if values.Count <> positions.Length * components then
                // a layer whose width changed between patches cannot be tabulated
                Log.warn "[layers] '%s' has inconsistent widths across patches -- skipped" name
                None
            else Some { name = name; components = components; values = values.ToArray() })

    { positions = positions; normals = normals; attributes = attributes }

// ---------------------------------------------------------------------------------------
// observations: an .mbi.json and the band files it declares
// ---------------------------------------------------------------------------------------

/// One column of an observation's table: a plane of one of its band files.
type BandPlane =
    {
        column     : string
        file       : string
        /// index of the plane inside `file`
        plane      : int
        wavelength : Option<float>
        size       : V2i
        values     : float32[]
    }

/// An observation as found on disk, before its camera is resolved.
type ObservationFiles =
    {
        /// the .mbi.json, whose name without the extension names the observation
        sidecar : string
        /// band image paths, in the sidecar's order
        bands   : list<string * Option<string> * Option<float>>
    }

let private tryString (e : JsonElement) (name : string) =
    match e.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.String -> Some (v.GetString())
    | _ -> None

let private tryNumber (e : JsonElement) (name : string) =
    match e.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.Number -> Some (v.GetDouble())
    | _ -> None

let observationStem (sidecar : string) =
    let name = Path.GetFileName sidecar
    name.Substring(0, name.Length - ".mbi.json".Length)

/// Every observation in a folder: one per `.mbi.json`, with the band files it declares
/// (`mbi_bands` in ASPECT exports, `bands` elsewhere), or the image named like the sidecar
/// when it declares none (the COP delivery).
let discoverObservations (folder : string) : list<ObservationFiles> =
    Directory.EnumerateFiles(folder, "*.mbi.json")
    |> Seq.sort
    |> Seq.choose (fun sidecar ->
        try
            use doc = JsonDocument.Parse(File.ReadAllText sidecar)
            let root = doc.RootElement
            let declared =
                [ "mbi_bands"; "bands" ]
                |> List.tryPick (fun key ->
                    match root.TryGetProperty key with
                    | true, arr when arr.ValueKind = JsonValueKind.Array && arr.GetArrayLength() > 0 ->
                        Some [ for b in arr.EnumerateArray() do
                                 match tryString b "file_path" with
                                 | Some f when not (String.IsNullOrWhiteSpace f) ->
                                     yield Path.Combine(folder, Path.GetFileName f), tryString b "label", tryNumber b "wavelength"
                                 | _ -> () ]
                    | _ -> None)
                |> Option.defaultValue []
            let bands =
                match declared with
                | [] ->
                    let stem = observationStem sidecar
                    [ ".png"; ".tif"; ".tiff" ]
                    |> List.map (fun ext -> Path.Combine(folder, stem + ext))
                    |> List.tryFind File.Exists
                    |> Option.map (fun p -> [ p, None, None ])
                    |> Option.defaultValue []
                | xs -> xs
            match bands with
            | [] ->
                Log.warn "[images] %s declares no band file that exists -- skipped" (Path.GetFileName sidecar)
                None
            | _ -> Some { sidecar = sidecar; bands = bands }
        with e ->
            Log.warn "[images] cannot read %s: %s -- skipped" sidecar e.Message
            None)
    |> Seq.toList

/// Wavelengths a multi-plane TIFF's statistics sidecar lists (HyperScout `_Stacked.tif.json`).
let private stackWavelengths (imagePath : string) : list<float> =
    let json = imagePath + ".json"
    if not (File.Exists json) then []
    else
        try
            use doc = JsonDocument.Parse(File.ReadAllText json)
            match doc.RootElement.TryGetProperty "wavelengths" with
            | true, arr when arr.ValueKind = JsonValueKind.Array ->
                [ for w in arr.EnumerateArray() do if w.ValueKind = JsonValueKind.Number then yield w.GetDouble() ]
            | _ -> []
        with _ -> []

/// The planes of one band file, as stored: float TIFFs in their own units, 8/16-bit images
/// as raw DN (not normalised).
let readBandFile (path : string) (label : Option<string>) (wavelength : Option<float>) : Result<list<BandPlane>, string> =
    let name = label |> Option.defaultValue (Path.GetFileNameWithoutExtension path)
    let file = Path.GetFileName path
    try
        match Path.GetExtension(path).ToLowerInvariant() with
        | ".tif" | ".tiff" ->
            match MultiBandReader.tryReadMultiBandTiff path false with
            | Result.Error e -> Result.Error (sprintf "%s: %s" file e)
            | Ok r ->
                let planes : float32[][] =
                    match r.buffers with
                    | Float32Bands b -> b
                    | UInt16Bands b -> b |> Array.map (Array.map float32)
                    | Int16Bands b -> b |> Array.map (Array.map float32)
                    | Int32Bands b -> b |> Array.map (Array.map float32)
                    | UInt32Bands b -> b |> Array.map (Array.map float32)
                let wavelengths = if planes.Length > 1 then stackWavelengths path else []
                Ok [
                    for k in 0 .. planes.Length - 1 do
                        yield {
                            column = if planes.Length = 1 then name else sprintf "%s_%d" name k
                            file = file
                            plane = k
                            wavelength = if planes.Length = 1 then wavelength else List.tryItem k wavelengths
                            size = V2i(r.width, r.height)
                            values = planes.[k]
                        }
                ]
        | _ ->
            let img = PixImage.Load path
            let grey (read : int -> int -> float32) =
                let size = img.Size
                Array.init (size.X * size.Y) (fun i -> read (i % size.X) (i / size.X))
            let values =
                match img with
                // Decoders may hand an 8-bit greyscale PNG back as BGR with three equal
                // channels; only a real colour image is worth a warning.
                | :? PixImage<byte> as b when b.Format = Col.Format.Gray ->
                    let m = b.GetChannel Col.Channel.Gray
                    Some (grey (fun x y -> float32 m.[x, y]), true)
                | :? PixImage<byte> as b ->
                    let r, g = b.GetChannel Col.Channel.Red, b.GetChannel Col.Channel.Green
                    let v = grey (fun x y -> float32 r.[x, y])
                    Some (v, grey (fun x y -> float32 g.[x, y]) = v)
                | :? PixImage<uint16> as b when b.Format = Col.Format.Gray ->
                    let m = b.GetChannel Col.Channel.Gray
                    Some (grey (fun x y -> float32 m.[x, y]), true)
                | _ -> None
            match values with
            | None -> Result.Error (sprintf "%s: unsupported pixel format %A of %s" file img.Format (img.PixFormat.Type.Name))
            | Some (values, isGrey) ->
                if not isGrey then
                    Log.warn "[images] %s is a colour image -- sampling its red channel" file
                Ok [ { column = name; file = file; plane = 0; wavelength = wavelength; size = img.Size; values = values } ]
    with e ->
        Result.Error (sprintf "%s: %s" file e.Message)

// ---------------------------------------------------------------------------------------
// visibility
// ---------------------------------------------------------------------------------------

/// The kd-trees of the body, loaded up front so that rays can be cast from many threads:
/// the lazy loading `HeadlessPicking.intersectAll` does threads a cache through each call.
type Occluders = (Box3d * KdIntersectionTree)[]

let loadOccluders (kdTreeMap : HashMap<Box3d, Level0KdTree>) : Occluders =
    let mutable cache = HashMap.empty
    kdTreeMap
    |> HashMap.toArray
    |> Array.choose (fun (bb, tree) ->
        let kd, c = DebugKdTreesX.loadObjectSet cache tree
        cache <- c
        let kdi = kd.KdIntersectionTree
        if isNull kdi.ObjectSet then None else Some (bb, kdi))

let private keepEveryHit =
    Func<IIntersectableObjectSet, int, int, RayHit3d, bool>(fun _ _ _ _ -> false)

/// Whether anything lies on the ray before `tmax`.
let occluded (occluders : Occluders) (ray : FastRay3d) (tmax : float) =
    let mutable blocked = false
    let mutable k = 0
    while not blocked && k < occluders.Length do
        let bb, kdi = occluders.[k]
        let mutable t0 = 0.0
        let mutable t1 = tmax
        if ray.Intersects(bb, &t0, &t1) then
            let mutable hit = ObjectRayHit.MaxRange
            if kdi.Intersect(ray, null, keepEveryHit, 0.0, tmax, &hit) then blocked <- true
        k <- k + 1
    blocked

/// Where a visible vertex lands in one observation, and under which geometry.
[<Struct>]
type Sample =
    {
        pixel     : V2d
        /// radians; NaN where the vertex has no normal
        incidence : float
        emission  : float
        phase     : float
    }

/// For every vertex: None if the observation does not see it, else where and how.
///
/// Seen means: in front of the camera, inside the frame, facing it (when the vertex has a
/// normal), and with nothing on the line of sight closer than `tolerance` metres before
/// the vertex -- the ray ends ON the surface, so without the tolerance a vertex would
/// occlude itself through the triangles it belongs to.
/// The counts along the way, for the log: in the frame, of those facing the camera, of
/// those not occluded.
type VisibilityFunnel = { inFrame : int; facing : int; seen : int }

let sampleVisibility (vertices : SurfaceVertices) (occluders : Occluders) (cam : ProjectorCamera)
                     (size : V2i) (sun : V3d) (tolerance : float) : Option<Sample>[] * VisibilityFunnel =
    let eye = cam.view.Backward.TransformPos V3d.Zero
    let full = cam.full.Forward
    let result = Array.zeroCreate<Option<Sample>> vertices.positions.Length
    let counts = Array.zeroCreate<int> 3
    Parallel.For(0, vertices.positions.Length, fun i ->
        let p = vertices.positions.[i]
        let h = full.Transform(V4d(p.X, p.Y, p.Z, 1.0))
        if h.W > 0.0 then
            let ndc = V2d(h.X / h.W, h.Y / h.W)
            if ndc.X >= -1.0 && ndc.X <= 1.0 && ndc.Y >= -1.0 && ndc.Y <= 1.0 then
                Threading.Interlocked.Increment(&counts.[0]) |> ignore
                let toEye = eye - p
                let distance = toEye.Length
                let view = toEye / distance
                let n = vertices.normals.[i]
                let hasNormal = n <> V3d.Zero
                if not hasNormal || Vec.dot n view > 0.0 then
                    Threading.Interlocked.Increment(&counts.[1]) |> ignore
                    let ray = FastRay3d(Ray3d(eye, -view))
                    if not (occluded occluders ray (distance - tolerance)) then
                        Threading.Interlocked.Increment(&counts.[2]) |> ignore
                        let angle (a : V3d) (b : V3d) = acos (clamp -1.0 1.0 (Vec.dot a b))
                        result.[i] <-
                            Some {
                                pixel = InstrumentObservation.ndcToPixel size InstrumentObservation.PixelConvention.Image ndc
                                incidence = if hasNormal then angle n sun else nan
                                emission = if hasNormal then angle n view else nan
                                phase = angle sun view
                            }) |> ignore
    result, { inFrame = counts.[0]; facing = counts.[1]; seen = counts.[2] }

/// Nearest pixel to a continuous image coordinate (integers at pixel centres), or None
/// when it falls outside the image.
let nearestPixel (size : V2i) (pixel : V2d) : Option<V2i> =
    let x = int (floor (pixel.X + 0.5))
    let y = int (floor (pixel.Y + 0.5))
    if x >= 0 && y >= 0 && x < size.X && y < size.Y then Some (V2i(x, y)) else None

// ---------------------------------------------------------------------------------------
// output
// ---------------------------------------------------------------------------------------

let private inv = CultureInfo.InvariantCulture

let private openCsv (path : string) =
    new StreamWriter(path, false, UTF8Encoding(false), 1 <<< 20, NewLine = "\n")

let private f32 (v : float32) = if Single.IsFinite v then v.ToString("G9", inv) else ""
let private deg (v : float) = if Double.IsFinite v then (v * Constant.DegreesPerRadian).ToString("F4", inv) else ""

let writeVertices (path : string) (vertices : SurfaceVertices) =
    use w = openCsv path
    w.WriteLine "id,x,y,z"
    for i in 0 .. vertices.positions.Length - 1 do
        let p = vertices.positions.[i]
        w.Write(i)
        w.Write(',')
        w.Write(p.X.ToString("F5", inv))
        w.Write(',')
        w.Write(p.Y.ToString("F5", inv))
        w.Write(',')
        w.WriteLine(p.Z.ToString("F5", inv))

let writeAttribute (path : string) (column : AttributeColumn) =
    use w = openCsv path
    let names =
        if column.components = 1 then [ column.name ]
        else [ for c in 0 .. column.components - 1 -> sprintf "%s_%d" column.name c ]
    w.WriteLine("id," + String.Join(",", names))
    let count = column.values.Length / column.components
    for i in 0 .. count - 1 do
        w.Write(i)
        for c in 0 .. column.components - 1 do
            w.Write(',')
            w.Write(f32 column.values.[i * column.components + c])
        w.WriteLine()

/// One row per visible vertex. Returns the number of rows.
let writeObservation (path : string) (samples : Option<Sample>[]) (size : V2i) (planes : list<BandPlane>) =
    let planes = List.toArray planes
    use w = openCsv path
    let header =
        [ yield "id"; yield "imageCoordX"; yield "imageCoordY"
          yield "incidence_deg"; yield "emission_deg"; yield "phase_deg"
          for p in planes -> p.column ]
    w.WriteLine(String.Join(",", header))
    let mutable rows = 0
    for i in 0 .. samples.Length - 1 do
        match samples.[i] with
        | Some s ->
            match nearestPixel size s.pixel with
            | Some px ->
                w.Write(i)
                w.Write(',')
                w.Write(s.pixel.X.ToString("F3", inv))
                w.Write(',')
                w.Write(s.pixel.Y.ToString("F3", inv))
                w.Write(',')
                w.Write(deg s.incidence)
                w.Write(',')
                w.Write(deg s.emission)
                w.Write(',')
                w.Write(deg s.phase)
                let index = px.Y * size.X + px.X
                for p in planes do
                    w.Write(',')
                    w.Write(f32 p.values.[index])
                w.WriteLine()
                rows <- rows + 1
            | None -> ()
        | None -> ()
    rows

// ---------------------------------------------------------------------------------------
// the verb
// ---------------------------------------------------------------------------------------

/// Entry point for the `sample-layers` verb.
let run (o : SampleLayersOptions) : int =
    let folders = if isNull (box o.images) then [] else Seq.toList o.images
    let outDir = if String.IsNullOrWhiteSpace o.out then Path.Combine(".", "sample-layers") else o.out
    let attributeNames =
        match (if isNull (box o.attributes) then [] else Seq.toList o.attributes) with
        | [] -> [ "Slope" ]
        | [ n ] when String.Equals(n, "none", StringComparison.OrdinalIgnoreCase) -> []
        | ns -> ns

    let projectionMethod =
        match (if isNull o.method then "mbi" else o.method).ToLowerInvariant() with
        | "spice" -> Some ProjectionMethod.Spice
        | "mbi" -> Some ProjectionMethod.MbiBased
        | _ -> None

    match projectionMethod with
    | None -> Log.error "unknown --method '%s' (expected spice or mbi)" o.method; 1
    | Some projectionMethod ->

    if String.IsNullOrWhiteSpace o.opc || not (Directory.Exists o.opc) then Log.error "OPC directory not found: %s" o.opc; 1
    elif folders.IsEmpty then Log.error "no --images folder given"; 1
    else
    match folders |> List.tryFind (fun f -> not (Directory.Exists f)) with
    | Some f -> Log.error "image folder not found: %s" f; 1
    | None ->

    match Spice.resolveKernelRoot o.kernelRoot with
    | Result.Error e -> Spice.reportMissingKernelRoot e; 1
    | Ok kernelRoot ->

    let observations = folders |> List.collect discoverObservations
    Log.line "[images] %d observation(s) in %d folder(s)" observations.Length folders.Length

    // Resolve each observation's sidecar through the viewer's own reader, via its first band.
    let resolved =
        observations |> List.map (fun obs ->
            match obs.bands with
            | (firstBand, _, _) :: _ ->
                obs, InstrumentObservation.resolveImage (Path.GetDirectoryName firstBand) (Some (Path.GetFileName firstBand))
            | [] -> obs, Result.Error "the sidecar declares no band file")

    match resolved |> List.tryPick (fun (_, r) -> match r with Ok img -> Some img | _ -> None) with
    | None -> Log.error "none of the observations could be resolved"; 1
    | Some sample ->

    // One metakernel for the whole run: SPICE keeps a single active metakernel.
    match InstrumentObservation.resolveKernel (if String.IsNullOrWhiteSpace o.kernel then None else Some o.kernel) kernelRoot sample with
    | Result.Error e -> Log.error "%s" e; 1
    | Ok kernel ->

    // The body defaults to what the images say they looked at, not to a fixed name: a
    // stack of Dimorphos frames sampled against DIDYMOS_FIXED would be silently wrong.
    let body =
        if not (String.IsNullOrWhiteSpace o.body) then o.body
        else sample.mbi.target |> Option.map (fun t -> t.ToUpperInvariant()) |> Option.defaultValue "DIDYMOS"
    let frame = if String.IsNullOrWhiteSpace o.frame then body + "_FIXED" else o.frame
    Log.line "[body] %s in %s" body frame

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

    // A misspelt layer would otherwise come out as a column of NaN, which a pipeline reads
    // as "no data here" rather than as the typo it is.
    let available = availableAttributes hierarchies
    match attributeNames |> List.filter (fun n -> not (available |> List.exists (fun a -> String.Equals(a, n, StringComparison.OrdinalIgnoreCase)))) with
    | _ :: _ as unknown ->
        Log.error "the OPC has no per-vertex layer %s. It declares: %s"
            (unknown |> List.map (sprintf "'%s'") |> String.concat ", ")
            (if available.IsEmpty then "(none)" else String.concat ", " available)
        1
    | [] ->
    // the OPC's own spelling names the column and the file
    let attributeNames =
        attributeNames |> List.choose (fun n ->
            available |> List.tryFind (fun a -> String.Equals(a, n, StringComparison.OrdinalIgnoreCase)))

    let watch = Diagnostics.Stopwatch.StartNew()
    let vertices = collectVertices hierarchies attributeNames
    Log.line "[layers] %d vertices (%.1f s)" vertices.positions.Length watch.Elapsed.TotalSeconds
    let occluders = loadOccluders kdTreeMap
    Log.line "[layers] %d kd-tree(s) loaded" occluders.Length

    let imagesDir = Path.Combine(outDir, "images")
    let attributesDir = Path.Combine(outDir, "attributes")
    Directory.CreateDirectory imagesDir |> ignore
    Directory.CreateDirectory attributesDir |> ignore

    writeVertices (Path.Combine(outDir, "vertices.csv")) vertices
    Log.line "[out] vertices.csv"
    for a in vertices.attributes do
        writeAttribute (Path.Combine(attributesDir, a.name + ".csv")) a
        Log.line "[out] attributes/%s.csv" a.name

    let mutable failures = 0
    let manifestImages = JsonArray()

    for (obs, r) in resolved do
        let stem = observationStem obs.sidecar
        let fail (e : string) =
            Log.error "[%s] %s" stem e
            failures <- failures + 1
        match r with
        | Result.Error e -> fail e
        | Ok img ->
        let observer =
            if not (String.IsNullOrWhiteSpace o.observer) then o.observer
            else InstrumentProjection.instrument2CameraSource img.mbi.instrument
        match InstrumentObservation.projectorCamera None observer frame body projectionMethod img with
        | Result.Error e -> fail e
        | Ok cam ->
        match InstrumentObservation.sunDirection frame body img.mbi.obs_date with
        | Result.Error e -> fail e
        | Ok sun ->

        let planes = obs.bands |> List.map (fun (path, label, wavelength) -> readBandFile path label wavelength)
        match planes |> List.tryPick (function Result.Error e -> Some e | Ok _ -> None) with
        | Some e -> fail e
        | None ->
        let planes = planes |> List.collect (function Ok ps -> ps | Result.Error _ -> [])

        // The camera maps onto one pixel grid; bands of a different size would be sampled
        // at the wrong place, so an observation has to agree on it.
        match img.size |> Option.orElse (planes |> List.tryHead |> Option.map (fun p -> p.size)) with
        | None -> fail "the observation has no bands"
        | Some size ->
        match planes |> List.tryFind (fun p -> p.size <> size) with
        | Some p -> fail (sprintf "band %s is %dx%d, the observation is %dx%d" p.column p.size.X p.size.Y size.X size.Y)
        | None ->

        let watch = Diagnostics.Stopwatch.StartNew()
        let samples, funnel = sampleVisibility vertices occluders cam size sun o.occlusionTolerance
        Log.line "[%s] %d vertices in the frame, %d facing the camera, %d of those unoccluded"
            stem funnel.inFrame funnel.facing funnel.seen
        let csv = Path.Combine(imagesDir, stem + ".csv")
        let rows = writeObservation csv samples size planes
        Log.line "[%s] %s at %s: %d vertices seen, %d band(s) (%.1f s)"
            stem img.spiceName (img.mbi.obs_date.ToString "o") rows planes.Length watch.Elapsed.TotalSeconds

        let entry = JsonObject()
        entry.["name"] <- JsonValue.Create stem
        entry.["csv"] <- JsonValue.Create ("images/" + stem + ".csv")
        entry.["sidecar"] <- JsonValue.Create (Path.GetFullPath obs.sidecar)
        entry.["instrument"] <- JsonValue.Create img.mbi.instrument
        entry.["spiceFrame"] <- JsonValue.Create img.spiceName
        entry.["observer"] <- JsonValue.Create observer
        entry.["time"] <- JsonValue.Create (img.mbi.obs_date.ToString("o", inv))
        entry.["width"] <- JsonValue.Create size.X
        entry.["height"] <- JsonValue.Create size.Y
        entry.["rangeMetres"] <- JsonValue.Create cam.distance
        entry.["verticesSeen"] <- JsonValue.Create rows
        let bands = JsonArray()
        for p in planes do
            let b = JsonObject()
            b.["column"] <- JsonValue.Create p.column
            b.["file"] <- JsonValue.Create p.file
            b.["plane"] <- JsonValue.Create p.plane
            p.wavelength |> Option.iter (fun w -> b.["wavelengthNm"] <- JsonValue.Create w)
            bands.Add b
        entry.["bands"] <- bands
        manifestImages.Add entry

    let manifest = JsonObject()
    manifest.["opc"] <- JsonValue.Create (Path.GetFullPath o.opc)
    manifest.["body"] <- JsonValue.Create body
    manifest.["frame"] <- JsonValue.Create frame
    manifest.["kernel"] <- JsonValue.Create kernel
    manifest.["method"] <- JsonValue.Create (if projectionMethod = ProjectionMethod.Spice then "spice" else "mbi")
    manifest.["occlusionToleranceMetres"] <- JsonValue.Create o.occlusionTolerance
    manifest.["vertices"] <- JsonValue.Create vertices.positions.Length
    let attrs = JsonArray()
    for a in vertices.attributes do
        let x = JsonObject()
        x.["name"] <- JsonValue.Create a.name
        x.["csv"] <- JsonValue.Create ("attributes/" + a.name + ".csv")
        x.["components"] <- JsonValue.Create a.components
        attrs.Add x
    manifest.["attributes"] <- attrs
    manifest.["images"] <- manifestImages
    File.WriteAllText(Path.Combine(outDir, "manifest.json"), manifest.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
    Log.line "[out] %s" (Path.GetFullPath outDir)

    if failures > 0 then 1 else 0
