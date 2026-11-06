namespace PRo3D.Core

open System
open System.IO
open System.Globalization
open System.Text.RegularExpressions

open Aardvark.Base
open Aardvark.Data.Opc

open FSharp.Data

open PRo3D.Base.Annotation

/// The DEM reference model an OPC was exported against, as recorded in the `DemModel`
/// block of the OPC's `*.opc.json` sidecar.
///
/// The block's shape depends on `ModelType`:
///   * `DemSphere` declares `Center` and a single rotation `Axis`.
///   * `DemEllipsoid` declares `Center`, a full frame `AxisX`/`AxisY`/`AxisZ`, and the
///     three semi-axis lengths `Radii`.
/// Fields absent for a given model type stay None.
type DemModel =
    {
        /// e.g. "DemSphere" or "DemEllipsoid"
        modelType : string
        /// centre of the reference body in the OPC's coordinate system
        center    : Option<V3d>
        /// `DemSphere`: the rotation axis
        axis      : Option<V3d>
        /// `DemEllipsoid`: the reference frame's axes (X, Y, Z)
        frame     : Option<V3d * V3d * V3d>
        /// `DemEllipsoid`: semi-axis lengths, in the OPC's unit
        radii     : Option<V3d>
    }

/// Key facts lifted out of the DSKBRIEF summary of the `*.bds` (SPICE DSK) shape
/// model an OPC was derived from. Everything is optional - DSKBRIEF output is free
/// text and its layout is not contractual.
type DskSummary =
    {
        /// e.g. "402 (DEIMOS)"
        body             : Option<string>
        /// e.g. "IAU_DEIMOS" - the frame the shape model's vertices are given in
        referenceFrame   : Option<string>
        /// e.g. "Planetocentric Latitudinal"
        coordinateSystem : Option<string>
        longitudeRange   : Option<Range1d>
        latitudeRange    : Option<Range1d>
        /// radius range in kilometres, as reported by DSKBRIEF
        radiusRangeKm    : Option<Range1d>
        vertexCount      : Option<int>
        plateCount       : Option<int>
    }

/// Contents of an OPC's `*.opc.json` sidecar, written next to the `*.opcx`.
type OpcMetadata =
    {
        path             : string
        productType      : Option<string>
        productState     : Option<string>
        creatorId        : Option<string>
        creationDateTime : Option<string>
        /// `metadata_file` entries of `input_products`
        inputProducts    : list<string>
        demModel         : Option<DemModel>
        /// verbatim DSKBRIEF text, kept as-is because it carries more than is parsed below
        dskBrief         : Option<string>
        dskSummary       : Option<DskSummary>
        /// keyword -> value of the FITS header of the source product (`fits2mbi.fits_header`),
        /// present for OPCs built from a FITS DTM such as the HERA V4 global Dimorphos OPC
        fitsHeader       : Map<string, string>
        /// who made the shape model, as the product states it: product_information.credit, a
        /// top-level "credit", or the FITS AUTHOR / CREDIT / REFERENC keyword. None in every
        /// product seen so far (#887)
        credit           : Option<string>
    }

/// Reads the `*.opc.json` sidecar that JOANNEUM's ExportGpc writes next to an OPC.
/// For OPCs derived from a SPICE DSK (`*.bds`) shape model it carries the DEM
/// reference model and the DSKBRIEF summary of the source shape - body, reference
/// frame, radius range, vertex/plate counts.
module OpcMetadata =

    let private invariant = CultureInfo.InvariantCulture

    let private tryFloat (s : string) =
        match Double.TryParse(s, NumberStyles.Float, invariant) with
        | true, v -> Some v
        | _       -> None

    let private tryInt (s : string) =
        match Int32.TryParse(s, NumberStyles.Integer, invariant) with
        | true, v -> Some v
        | _       -> None

    let private nonEmpty (s : string) =
        if String.IsNullOrWhiteSpace s then None else Some (s.Trim())

    let private tryString (json : JsonValue) (name : string) =
        match json.TryGetProperty name with
        | Some v -> try Some (v.AsString()) with _ -> None
        | None   -> None

    /// A three element numeric array. A present but unreadable value is reported rather than
    /// silently treated like an absent one - otherwise a schema change looks like missing data.
    let private tryV3d (json : JsonValue) (name : string) =
        match json.TryGetProperty name with
        | None -> None
        | Some (JsonValue.Array values) when values.Length >= 3 ->
            try Some (V3d(values.[0].AsFloat(), values.[1].AsFloat(), values.[2].AsFloat()))
            with e ->
                Log.warn "[OpcMetadata] %s is not a numeric triple: %s" name e.Message
                None
        | Some value ->
            Log.warn "[OpcMetadata] %s is not a three element array: %s" name (value.ToString())
            None

    let private tryMatch (pattern : string) (text : string) =
        let m = Regex.Match(text, pattern, RegexOptions.IgnoreCase)
        if m.Success then Some m else None

    let private tryGroup (pattern : string) (text : string) =
        text |> tryMatch pattern |> Option.map (fun m -> m.Groups.[1].Value.Trim())

    let private tryRange (label : string) (text : string) =
        // e.g. "Min, max radius      (km):          3.58220     8.70680"
        text
        |> tryMatch (sprintf @"Min,\s*max\s+%s\s*\([^)]*\)\s*:\s*(\S+)\s+(\S+)" label)
        |> Option.bind (fun m ->
            match tryFloat m.Groups.[1].Value, tryFloat m.Groups.[2].Value with
            | Some a, Some b -> Some (Range1d(min a b, max a b))
            | _ -> None
        )

    /// Pulls the interesting fields out of a DSKBRIEF summary. Returns None when the
    /// text does not look like DSKBRIEF output at all.
    let parseDskBrief (text : string) =
        if String.IsNullOrWhiteSpace text || not (text.Contains "DSKBRIEF") then None
        else
            Some {
                body             = text |> tryGroup @"Body:\s*(.+)"
                referenceFrame   = text |> tryGroup @"Reference frame:\s*(\S+)"
                coordinateSystem = text |> tryGroup @"Coordinate system:\s*(.+)"
                longitudeRange   = text |> tryRange "longitude"
                latitudeRange    = text |> tryRange "latitude"
                radiusRangeKm    = text |> tryRange "radius"
                vertexCount      = text |> tryGroup @"Number of vertices:\s*(\d+)" |> Option.bind tryInt
                plateCount       = text |> tryGroup @"Number of plates:\s*(\d+)" |> Option.bind tryInt
            }

    let private parseDemModel (json : JsonValue) =
        match json.TryGetProperty "DemModel" with
        | None -> None
        | Some dem ->
            let frame =
                match tryV3d dem "AxisX", tryV3d dem "AxisY", tryV3d dem "AxisZ" with
                | Some x, Some y, Some z -> Some (x, y, z)
                | _ -> None

            Some {
                modelType = tryString dem "ModelType" |> Option.defaultValue "unknown"
                center    = tryV3d dem "Center"
                axis      = tryV3d dem "Axis"
                frame     = frame
                radii     = tryV3d dem "Radii"
            }

    let private headerValue (v : JsonValue) =
        match v with
        | JsonValue.String s  -> Some (s.Trim())
        | JsonValue.Number n  -> Some (n.ToString(invariant))
        | JsonValue.Float f   -> Some (f.ToString("R", invariant))
        | JsonValue.Boolean b -> Some (if b then "T" else "F")
        | JsonValue.Null      -> None
        | other               -> Some (other.ToString())

    /// `fits2mbi.fits_header`: an array of objects, each mapping keywords to
    /// `{ "value": ..., "comment": ... }`. Keywords without a value are left out.
    let private parseFitsHeader (json : JsonValue) =
        match json.TryGetProperty "fits2mbi" |> Option.bind (fun f -> f.TryGetProperty "fits_header") with
        | Some (JsonValue.Array entries) ->
            let mutable header = Map.empty
            for entry in entries do
                match entry with
                | JsonValue.Record props ->
                    for (key, card) in props do
                        let value =
                            match card with
                            | JsonValue.Record _ -> card.TryGetProperty "value" |> Option.bind headerValue
                            | v -> headerValue v
                        match value with
                        | Some v -> header <- Map.add (key.ToUpperInvariant()) v header
                        | None -> ()
                | _ -> ()
            header
        | _ -> Map.empty

    let ofJsonString (path : string) (content : string) =
        let json = JsonValue.Parse content
        let productInfo = json.TryGetProperty "product_information"

        let inputProducts =
            match json.TryGetProperty "input_products" with
            | Some (JsonValue.Array entries) ->
                entries |> Array.toList |> List.choose (fun e -> tryString e "metadata_file")
            | _ -> []

        let dskBrief = tryString json "DskBrief"
        let fitsHeader = parseFitsHeader json

        {
            path             = path
            productType      = productInfo |> Option.bind (fun p -> tryString p "product_type")
            productState     = productInfo |> Option.bind (fun p -> tryString p "product_state")
            creatorId        = productInfo |> Option.bind (fun p -> tryString p "creator_id")
            creationDateTime = productInfo |> Option.bind (fun p -> tryString p "creation_datetime")
            inputProducts    = inputProducts
            demModel         = parseDemModel json
            dskBrief         = dskBrief
            dskSummary       = dskBrief |> Option.bind parseDskBrief
            fitsHeader       = fitsHeader
            credit           =
                // product_information.credit (the OPC producer's schema), a top-level "credit",
                // then the FITS keywords the shape-model producer may set (docs/AnnotationMetadata.md)
                [
                    productInfo |> Option.bind (fun p -> tryString p "credit")
                    tryString json "credit"
                    Map.tryFind "AUTHOR" fitsHeader
                    Map.tryFind "CREDIT" fitsHeader
                    Map.tryFind "REFERENC" fitsHeader
                ]
                |> List.tryPick (Option.bind nonEmpty)
        }

    /// The `*.opc.json` sidecar belonging to an `*.opcx` file, if present.
    let sidecarPath (opcxPath : string) =
        let dir = Path.GetDirectoryName opcxPath
        let name = Path.GetFileNameWithoutExtension opcxPath
        if String.IsNullOrEmpty dir then name + ".opc.json"
        else dir +/ (name + ".opc.json")

    let tryRead (path : string) =
        if not (Prinziple.fileExists path) then None
        else
            try
                use stream = Prinziple.openRead path
                use reader = new StreamReader(stream)
                Some (ofJsonString path (reader.ReadToEnd()))
            with e ->
                Log.warn "[OpcMetadata] could not read %s: %s" path e.Message
                None

    /// Reads the sidecar belonging to an `*.opcx` file.
    let tryReadForOpcx (opcxPath : string) =
        tryRead (sidecarPath opcxPath)

    /// `vNNN` at the end of an SPC product name, e.g. `dimorphos_l_0050mm_spc_obj_0897s26313_v004`
    let tryVersionOfFileName (name : string) =
        name |> tryGroup @"_(v\d{2,})$"

    /// The shape model an annotation on this OPC is drawn on (#887): the FITS header's
    /// DATASRCV / MAP_VER / PRODNAME when the sidecar carries one, otherwise the version in the
    /// SPC file name. `opcxPath` names the product when the header does not.
    let shapeModelOf (opcxPath : string) (metadata : Option<OpcMetadata>) : ShapeModelRef =
        // an `*.opcx`, or the `*.opc.json` sidecar itself
        let fileName =
            let n = Path.GetFileNameWithoutExtension opcxPath
            if n.EndsWith(".opc", StringComparison.OrdinalIgnoreCase) then n.Substring(0, n.Length - 4) else n
        let header key =
            metadata |> Option.bind (fun m -> Map.tryFind key m.fitsHeader) |> Option.bind nonEmpty
        let modelName = header "DATASRCV"
        let mapVersion = header "MAP_VER"
        // stated by the product first; otherwise the citation of the archive it comes from
        let credit =
            match metadata |> Option.bind (fun m -> m.credit) with
            | Some c -> Some c
            | None ->
                let product = header "PRODNAME" |> Option.map Path.GetFileNameWithoutExtension
                ShapeModelCredits.tryCredit (List.choose id [ product; Some fileName; modelName ])
        match modelName, mapVersion with
        | None, None ->
            match tryVersionOfFileName fileName with
            | Some v -> { product = fileName; modelName = None; version = Some v; source = "file-name"; credit = credit }
            | None   -> { product = fileName; modelName = None; version = None;   source = "none"; credit = credit }
        | _ ->
            let product =
                header "PRODNAME"
                |> Option.map Path.GetFileNameWithoutExtension
                |> Option.defaultValue fileName
            { product = product; modelName = modelName; version = mapVersion; source = "fits-header"; credit = credit }

    // The Surfaces panel's Source section reads the sidecar while it is drawn; a sidecar does
    // not change during a session.
    let private cache = System.Collections.Concurrent.ConcurrentDictionary<string, Option<OpcMetadata>>()

    /// tryReadForOpcx, read once per session and path.
    let tryReadForOpcxCached (opcxPath : string) =
        cache.GetOrAdd(opcxPath, fun p -> tryReadForOpcx p)

    let private headerRow (md : OpcMetadata) (key : string) (label : string) (format : string -> string) =
        md.fitsHeader |> Map.tryFind key |> Option.bind nonEmpty |> Option.map (fun v -> label, format v)

    /// The Surfaces panel's Source section (#887, docs/AnnotationMetadata.md): the credit, then
    /// label/value rows of what the sidecar says about the shape model and the OPC, missing
    /// ones left out. `allHeader` is the whole FITS header, for the fold-out.
    let sourceRows (opcxPath : string) (metadata : Option<OpcMetadata>) =
        let sm = shapeModelOf opcxPath metadata
        let credit = sm.credit
        let shapeRows =
            match metadata with
            | Some md when sm.source = "fits-header" ->
                List.choose id [
                    headerRow md "DATASRCV" "Shape model" id
                    headerRow md "MAP_VER"  "Version" id
                    headerRow md "DATASRCD" "Model date" (fun d -> d.Split('T').[0])
                    headerRow md "DATASRC"  "Method" id
                    headerRow md "PRODNAME" "Product" id
                    headerRow md "MAP_TYPE" "Map type" id
                    headerRow md "GSD"      "Grid spacing" (fun g -> match tryFloat g with Some v -> sprintf "%.1f mm" v | None -> g)
                    headerRow md "MISSION"  "Mission" id
                    headerRow md "TARGET"   "Target" id
                    headerRow md "ORIGIN"   "Origin" id
                    headerRow md "SOFTWARE" "Software" id
                    headerRow md "DATEPRD"  "Produced" (fun d -> d.Split('T').[0])
                ]
            | _ ->
                [
                    "Product", sm.product
                    "Version", (match sm.version with
                                | Some v -> v + " (from the file name; the sidecar has no FITS header)"
                                | None -> "unknown (no FITS header, no version in the file name)")
                ]
        let opcRows =
            match metadata with
            | None -> [ "Sidecar", "none (no *.opc.json next to the OPC)" ]
            | Some md ->
                List.choose id [
                    md.creationDateTime |> Option.map (fun d ->
                        "OPC created",
                        sprintf "%s%s" (d.Split('T').[0]) (md.creatorId |> Option.map (sprintf " by %s") |> Option.defaultValue ""))
                    md.demModel |> Option.map (fun d -> "DEM model", d.modelType)
                    md.dskSummary |> Option.bind (fun d -> d.body) |> Option.map (fun b -> "DSK body", b)
                    md.dskSummary |> Option.bind (fun d -> d.referenceFrame) |> Option.map (fun f -> "DSK frame", f)
                    Some ("Sidecar", md.path)
                ]
        let allHeader = metadata |> Option.map (fun md -> md.fitsHeader |> Map.toList) |> Option.defaultValue []
        credit, shapeRows @ opcRows, allHeader

    let private formatRange (unit : string) (r : Option<Range1d>) =
        match r with
        | Some r -> sprintf "%.5f .. %.5f %s" r.Min r.Max unit
        | None   -> "n/a"

    /// Logs what was found. The sidecar is not persisted into the scene, so this is
    /// currently the only place the BDS provenance shows up.
    let log (surfaceName : string) (metadata : OpcMetadata) =
        Log.line "[OpcMetadata] %s: %s" surfaceName (Path.GetFileName metadata.path)
        Log.line "[OpcMetadata]   product: %s (%s), created %s by %s"
            (metadata.productType      |> Option.defaultValue "?")
            (metadata.productState     |> Option.defaultValue "?")
            (metadata.creationDateTime |> Option.defaultValue "?")
            (metadata.creatorId        |> Option.defaultValue "?")

        for input in metadata.inputProducts do
            Log.line "[OpcMetadata]   input: %s" input

        Log.line "[OpcMetadata]   shape model: %s" (AnnotationMetadata.describeShapeModel (shapeModelOf metadata.path (Some metadata)))

        match metadata.demModel with
        | Some dem ->
            Log.line "[OpcMetadata]   DEM model: %s center=%s"
                dem.modelType
                (dem.center |> Option.map (fun c -> c.ToString()) |> Option.defaultValue "n/a")

            dem.axis  |> Option.iter (fun a -> Log.line "[OpcMetadata]     axis: %s" (a.ToString()))
            dem.radii |> Option.iter (fun r -> Log.line "[OpcMetadata]     semi-axes: %s" (r.ToString()))
            dem.frame |> Option.iter (fun (x, y, z) ->
                Log.line "[OpcMetadata]     frame: x=%s y=%s z=%s" (x.ToString()) (y.ToString()) (z.ToString()))
        | None -> ()

        match metadata.dskSummary with
        | Some dsk ->
            Log.line "[OpcMetadata]   DSK body: %s, frame %s, %s"
                (dsk.body             |> Option.defaultValue "?")
                (dsk.referenceFrame   |> Option.defaultValue "?")
                (dsk.coordinateSystem |> Option.defaultValue "?")
            Log.line "[OpcMetadata]   DSK radius %s, longitude %s, latitude %s"
                (formatRange "km"  dsk.radiusRangeKm)
                (formatRange "deg" dsk.longitudeRange)
                (formatRange "deg" dsk.latitudeRange)
            Log.line "[OpcMetadata]   DSK plates: %s, vertices: %s"
                (dsk.plateCount  |> Option.map string |> Option.defaultValue "?")
                (dsk.vertexCount |> Option.map string |> Option.defaultValue "?")
        | None ->
            if metadata.dskBrief.IsSome then
                Log.line "[OpcMetadata]   DskBrief present but not recognised as DSKBRIEF output"
