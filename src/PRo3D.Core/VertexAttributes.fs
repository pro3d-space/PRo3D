namespace PRo3D.Core.Surface

open System
open System.IO
open System.Text
open System.Collections.Generic

open Aardvark.Base
open Aardvark.Data.Opc
open Aardvark.SceneGraph.Opc

/// Where an attribute value under the cursor / along a profile came from.
[<RequireQualifiedAccess>]
type AttributeSource =
    /// interpolated from a per-vertex *.aara layer stored next to the patch geometry
    | VertexData
    /// point sampled from the patch's texture layer image
    | TextureSampling

/// A single attribute value sampled at a picked surface point. `values` holds one
/// entry per channel (e.g. 3 for LonLatRad or Normal, 1 for Elevation or Slope).
type SampledAttribute =
    {
        name   : string
        values : float[]
        source : AttributeSource
    }

/// The scalar element type of an *.aara file.
[<RequireQualifiedAccess>]
type AaraScalar =
    | Float32
    | Float64
    | Int32

    member x.ByteSize =
        match x with
        | AaraScalar.Float32 -> 4
        | AaraScalar.Float64 -> 8
        | AaraScalar.Int32   -> 4

/// Header of an *.aara file: a length-prefixed type name, the dimension count and
/// one int32 size per dimension, followed by the tightly packed payload.
type AaraHeader =
    {
        typeName   : string
        /// grid size; x is the fastest varying index (index = y * size.X + x)
        size       : V2i
        /// scalar components per grid element (1 for float/double, 2 for V2f, 3 for V3f, ...)
        components : int
        scalar     : AaraScalar
        /// byte offset of the payload within the file
        dataOffset : int64
    }

    member x.ElementSize = x.components * x.scalar.ByteSize

/// How a layer's payload is reached. Plain files are read with random access - a
/// pick only needs a few bytes per layer. ZIP entries inflate sequentially and
/// cannot seek, so their payload is held in memory instead.
[<RequireQualifiedAccess>]
type AaraPayload =
    | OnDisk   of path : string
    | InMemory of payload : byte[]

/// One per-vertex attribute layer of a patch.
type VertexAttributeLayer =
    {
        /// layer name as used by the *.opcx attribute layers, e.g. "Elevation"
        name    : string
        path    : string
        header  : AaraHeader
        payload : AaraPayload
    }

/// Reads OPC per-vertex attribute layers - the `*.aara` files listed in a patch's
/// `<Attributes>` element - and samples them at a picked surface point. This is the
/// fast path for attribute extraction; the alternative is decoding the patch's
/// texture layer images and sampling them at the hit's UV, which costs a full image
/// decode per layer per sample.
///
/// **Grid layout.** Attribute layers do *not* share the position grid's size: the
/// positions carry a symmetric skirt whose width shrinks with the hierarchy level
/// (1032 / 1028 / 1026 against a constant 1024 attribute grid for the HERA Dimorphos
/// exports). The attribute grid is centred in the position grid:
///
///     attributeIndex(x, y) = positionIndex(x + off, y + off)
///     off = (positionGridSize - attributeGridSize) / 2
///
/// Verified for every patch of `g_01960mm_spc_dtm_dimo_0000n00000_v003` by comparing
/// `LonLatRad.z` against `|Local2Global * XYZ_Local|` - median error 2e-6 m, i.e.
/// float32 round-off. Every other offset is off by at least 0.19 m.
module VertexAttributes =

    /// Payload bytes held in memory for ZIP-backed layers before the cache is dropped.
    /// Irrelevant for plain directories, which are read with random access.
    let private inMemoryCacheLimit = 512L * 1024L * 1024L

    /// Cached layers keyed by absolute file path, and cached layer *sets* keyed by
    /// absolute patch directory. Attribute layers are immutable on disk, so a patch is
    /// discovered once and reused for every pick.
    let private fileCache = Dictionary<string, Option<VertexAttributeLayer>>()
    let private layerCache = Dictionary<string, VertexAttributeLayer[]>()
    let mutable private inMemoryBytes = 0L

    let private tryParseTypeName (typeName : string) =
        match typeName with
        | "float"  -> Some (1, AaraScalar.Float32)
        | "V2f"    -> Some (2, AaraScalar.Float32)
        | "V3f"    -> Some (3, AaraScalar.Float32)
        | "V4f"    -> Some (4, AaraScalar.Float32)
        | "double" -> Some (1, AaraScalar.Float64)
        | "V2d"    -> Some (2, AaraScalar.Float64)
        | "V3d"    -> Some (3, AaraScalar.Float64)
        | "V4d"    -> Some (4, AaraScalar.Float64)
        | "int"    -> Some (1, AaraScalar.Int32)
        | "V2i"    -> Some (2, AaraScalar.Int32)
        | "V3i"    -> Some (3, AaraScalar.Int32)
        | _        -> None

    /// Reads the header of an already opened *.aara stream, leaving the stream
    /// positioned at the start of the payload.
    let tryReadHeader (stream : Stream) =
        let typeName = Aara.readString Encoding.ASCII stream
        let dimensions = stream.ReadByte()
        if dimensions <= 0 || dimensions > 3 then None
        else
            let reader = new BinaryReader(stream, Encoding.ASCII, true)
            let sizes = Array.init dimensions (fun _ -> reader.ReadInt32())
            let size =
                match sizes with
                | [| x |]       -> V2i(x, 1)
                | [| x; y |]    -> V2i(x, y)
                | [| x; y; _ |] -> V2i(x, y)
                | _             -> V2i.Zero

            match tryParseTypeName typeName with
            | Some (components, scalar) when size.X > 0 && size.Y > 0 ->
                Some {
                    typeName   = typeName
                    size       = size
                    components = components
                    scalar     = scalar
                    // name length byte + name + dimension count byte + one int32 per dimension
                    dataOffset = int64 (1 + typeName.Length + 1 + 4 * dimensions)
                }
            | _ -> None

    let private loadPayload (path : string) (header : AaraHeader) (stream : Stream) =
        if stream.CanSeek then
            AaraPayload.OnDisk path
        else
            let byteCount = int64 header.size.X * int64 header.size.Y * int64 header.ElementSize
            if inMemoryBytes + byteCount > inMemoryCacheLimit then
                Log.warn "[VertexAttributes] in-memory layer cache above %d MB - dropping cached layers" (inMemoryCacheLimit / 1048576L)
                fileCache.Clear()
                layerCache.Clear()
                inMemoryBytes <- 0L

            use ms = new MemoryStream()
            stream.CopyTo ms
            inMemoryBytes <- inMemoryBytes + ms.Length
            AaraPayload.InMemory (ms.ToArray())

    let private loadLayer (path : string) =
        if not (Prinziple.fileExists path) then None
        else
            try
                use stream = Prinziple.openRead path
                match tryReadHeader stream with
                | None ->
                    Log.warn "[VertexAttributes] unsupported aara layout in %s" path
                    None
                | Some header ->
                    Some {
                        name    = Path.GetFileNameWithoutExtension path
                        path    = path
                        header  = header
                        payload = loadPayload path header stream
                    }
            with e ->
                Log.warn "[VertexAttributes] could not read %s: %s" path e.Message
                None

    /// Reads a single *.aara file as a sampleable layer, cached by path. Used for grids
    /// that are not attribute layers, e.g. the patch's texture coordinates.
    let tryGetLayer (path : string) =
        lock fileCache (fun () ->
            match fileCache.TryGetValue path with
            | true, layer -> layer
            | _ ->
                let layer = loadLayer path
                fileCache.[path] <- layer
                layer
        )

    /// The per-vertex attribute layers of a patch, cached by patch directory. Empty
    /// for patches that ship no `<Attributes>` (older OPCs), which is the caller's
    /// signal to fall back to texture sampling.
    let getLayers (patchDir : string) (patchInfo : PatchFileInfo) =
        lock layerCache (fun () ->
            match layerCache.TryGetValue patchDir with
            | true, layers -> layers
            | _ ->
                let layers =
                    patchInfo.Attributes
                    // Positions2d.aara is synthesised by PatchFileInfo.load' for the 2D
                    // modality and is not an attribute layer.
                    |> List.filter (fun f -> Path.GetFileNameWithoutExtension(f) <> "Positions2d")
                    |> List.choose (fun fileName -> tryGetLayer (patchDir +/ fileName))
                    |> List.toArray

                layerCache.[patchDir] <- layers
                if layers.Length > 0 then
                    Log.line "[VertexAttributes] %s: %d per-vertex layers (%s)"
                        (Path.GetFileName patchDir) layers.Length
                        (layers |> Array.map (fun l -> l.name) |> String.concat ", ")
                layers
        )

    /// Decodes one grid element out of `buffer` into `target`.
    let private decodeElement (header : AaraHeader) (buffer : byte[]) (offset : int) (target : float[]) =
        for c in 0 .. header.components - 1 do
            let o = offset + c * header.scalar.ByteSize
            target.[c] <-
                match header.scalar with
                | AaraScalar.Float32 -> float (BitConverter.ToSingle(buffer, o))
                | AaraScalar.Float64 -> BitConverter.ToDouble(buffer, o)
                | AaraScalar.Int32   -> float (BitConverter.ToInt32(buffer, o))

    /// Reads a full element from a seekable stream. Returns false on a short read.
    let private tryReadElement (stream : Stream) (header : AaraHeader) (buffer : byte[]) (elementIndex : int) =
        let elementSize = header.ElementSize
        stream.Seek(header.dataOffset + int64 elementIndex * int64 elementSize, SeekOrigin.Begin) |> ignore
        let mutable read = 0
        let mutable eof = false
        while not eof && read < elementSize do
            let n = stream.Read(buffer, read, elementSize - read)
            if n <= 0 then eof <- true else read <- read + n
        read = elementSize

    /// Maps a position grid index onto the layer's attribute grid. Returns -1 when the
    /// vertex lies in the position grid's skirt, which carries no attribute values.
    let private attributeIndex (positionsGridSize : V2i) (attrSize : V2i) (positionIndex : int) =
        let offX = (positionsGridSize.X - attrSize.X) / 2
        let offY = (positionsGridSize.Y - attrSize.Y) / 2
        let ax = (positionIndex % positionsGridSize.X) - offX
        let ay = (positionIndex / positionsGridSize.X) - offY
        if ax < 0 || ay < 0 || ax >= attrSize.X || ay >= attrSize.Y then -1
        else ay * attrSize.X + ax

    let private sampleLayer (positionsGridSize : V2i) (gridIndices : int[]) (w : float[]) (layer : VertexAttributeLayer) =
        let header = layer.header
        let indices = gridIndices |> Array.map (attributeIndex positionsGridSize header.size)

        if indices |> Array.exists (fun i -> i < 0) then None
        else
            let values = Array.zeroCreate<float> header.components
            let corner = Array.zeroCreate<float> header.components

            let accumulate (read : int -> bool) =
                let mutable ok = true
                for k in 0 .. 2 do
                    if ok then
                        if read indices.[k] then
                            for c in 0 .. values.Length - 1 do
                                values.[c] <- values.[c] + w.[k] * corner.[c]
                        else ok <- false
                ok

            let ok =
                match layer.payload with
                | AaraPayload.InMemory payload ->
                    accumulate (fun i ->
                        let o = i * header.ElementSize
                        if o < 0 || o + header.ElementSize > payload.Length then false
                        else
                            decodeElement header payload o corner
                            true
                    )
                | AaraPayload.OnDisk path ->
                    use stream = Prinziple.openRead path
                    let buffer = Array.zeroCreate<byte> header.ElementSize
                    accumulate (fun i ->
                        if tryReadElement stream header buffer i then
                            decodeElement header buffer 0 corner
                            true
                        else false
                    )

            if ok then Some { name = layer.name; values = values; source = AttributeSource.VertexData }
            else None

    /// Barycentrically interpolates every per-vertex layer of a patch at a picked point.
    ///
    /// `gridIndices` are the three indices into the *position* grid of the hit triangle
    /// (as produced by `TriangleSet.computeGridIndices`) and `weights` the corresponding
    /// barycentric coordinates. Layers that do not cover the hit are omitted so the
    /// caller can fall back to texture sampling for them.
    ///
    /// Interpolation is component-wise, so a layer that wraps - a longitude crossing the
    /// 0/360 seam - is interpolated across the seam rather than around it.
    let sample
        (layers            : VertexAttributeLayer[])
        (positionsGridSize : V2i)
        (gridIndices       : int[])
        (weights           : V3d) =

        if layers.Length = 0 || gridIndices.Length < 3 || positionsGridSize.X <= 0 then []
        else
            let w = [| weights.X; weights.Y; weights.Z |]
            layers |> Array.toList |> List.choose (sampleLayer positionsGridSize gridIndices w)

    /// Barycentric sample of a single grid, e.g. the patch's texture coordinates.
    let sampleOne (layer : VertexAttributeLayer) (positionsGridSize : V2i) (gridIndices : int[]) (weights : V3d) =
        if gridIndices.Length < 3 || positionsGridSize.X <= 0 then None
        else
            let w = [| weights.X; weights.Y; weights.Z |]
            sampleLayer positionsGridSize gridIndices w layer

    /// Drops all cached layers so ZIP payloads and stale headers do not outlive their scene.
    let clearCache () =
        lock layerCache (fun () ->
            lock fileCache (fun () -> fileCache.Clear())
            layerCache.Clear()
            inMemoryBytes <- 0L
        )
