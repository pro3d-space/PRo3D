#if INTERACTIVE
#r "nuget: FSharp.Data"
#r "nuget: Aardvark.Base"
#else
module PRo3D.Core.InstrumentMetadata
#endif

open System.IO
open Aardvark.Base

module Tiff_Json = 
    open System
    open System.Text.Json
    open System.Text.Json.Serialization

    type ImageStatistics = {
        minimum: float
        maximum: float
        mean: float
        median: float
        standard_deviation: float
        variance: float
    }

    type ProductInformation = {
        schema_id: string
        schema_version: int
        product_type: string
        product_state: string
        creator_id: string
        creation_datetime: string
    }

    type ImageMetadata = {
        product_information: ProductInformation
        image_width: int
        image_height: int
        channels: int
        data_type: string
        file_md5: string
        image_statistics: ImageStatistics[]
        mission_name: string
        camera_system: string
    }

    let tryParseJson (jsonString: string) =    
        try
            let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
            JsonSerializer.Deserialize<ImageMetadata>(jsonString, options) |> Result.Ok
        with e -> 
            Result.Error e

    let test () =

        // Example usage
        let jsonString = """
            {
                "label": "AFC1",
                "description": "AFC gray image",
                "exposure": 0.001285,
                "product_information": {
                    "schema_id": "https://www.joanneum.at/jim/product_information.schema.json",
                    "schema_version": 7262,
                    "product_type": "FITS imported MultiBandImage (MBI)",
                    "product_state": "imported",
                    "creator_id": "Fits2Mbi",
                    "creation_datetime": "2025-10-16T10:33:16.518865+0000"
                },
                "schema_id": "https://www.joanneum.at/jim/camera_image.schema.json",
                "schema_version": 8198,
                "product_type": "image",
                "image_width": 1020,
                "image_height": 1020,
                "channels": 1,
                "data_type": "float",
                "file_md5": "e83b582bde9d9b72287153a38eeada60",
                "image_file_format": "tif",
                "compression_type": "none",
                "image_statistics": [
                    {
                        "minimum": 0.0,
                        "maximum": 7.51392936706543,
                        "mean": 2.6648557319765795,
                        "median": 2.8954658227294314,
                        "standard_deviation": 0.9310177481678147,
                        "variance": 0.8667940474034683
                    }
                ]
            }
        """

        let metadata = tryParseJson jsonString
        printfn "%A" metadata

module Tiff_Mbi_Json = 
    
    open System
    open FSharp.Data
    open FSharp.Data.JsonExtensions
    open System.Globalization

    type Mbi = {
        obs_date : DateTime; sunPos : V3d; earthPos : V3d;
        sc_quat : QuaternionD; targetPos : V3d
        instrument : string
        /// The SPICE meta-kernel name (e.g. "hera_plan_v180_20250616_001") this
        /// sidecar was generated against, if declared. None for sidecars that
        /// don't carry a SPICE_MK header (e.g. some older exports).
        spiceMk : Option<string>
        /// The observed body from the TARGET FITS header (e.g. "Didymos") --
        /// the body the image projection is aimed at. None when the header is
        /// absent or empty (older Mars exports), in which case callers fall
        /// back to their own target-body configuration.
        target : Option<string>
    }

    let tryGetFitsHeader (headerName : string) (mbi : JsonValue) (m : JsonValue -> Option<'a>): Option<'a> =
        let tryGetMatchingHeader (header: JsonValue) =
            match header.TryGetProperty(headerName) with
            | Some header ->
                m header?value
            | _ -> None

        // The FITS keyword block has been observed under two names:
        //   "fits_hdu_headers" (older HERA/AFC exports)
        //   "fits_header"      (newer HERA/ASPECT exports)
        // Use TryGetProperty (not the ? operator) so a missing block yields
        // None instead of throwing.
        let headerBlock =
            match mbi.TryGetProperty("fits_hdu_headers") with
            | Some block -> Some block
            | None -> mbi.TryGetProperty("fits_header")

        match headerBlock with
        | Some (JsonValue.Array arr) ->
            arr
            |> Array.tryPick tryGetMatchingHeader
        | _ ->
            None


    let parseDate (s : JsonValue) : Option<DateTime> = 
        match s with
        | JsonValue.String s -> 
            match DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) with
            | (true, v) -> Some v
            | _ -> None
        | _ -> 
            None

    let parseFloat' (s : string) : Option<float> = 
        match System.Double.TryParse(s, NumberStyles.Float ||| NumberStyles.AllowThousands, CultureInfo.InvariantCulture) with
        | (true, v) -> Some v
        | _ -> None

    let parseFloat (s : JsonValue) : Option<float> = 
        match s with
        | JsonValue.Float s -> Some s
        | JsonValue.Number n -> Some (float n)
        | _ -> None

    let (|Float|_|) (s : string) = parseFloat' s

    /// "..._20270301_040000_..." -> 2027-03-01T04:00:00Z. The instrument file
    /// naming scheme (HERA_AFC_<seq>_<yyyyMMdd>_<HHmmss>_<phase>) carries the
    /// observation time; for the COP synthetic delivery it is the ONLY
    /// per-image time (see tryExtractDateObsFromMbi).
    let tryParseTimestampFromFileName (fileName : string) : Option<DateTime> =
        let m = System.Text.RegularExpressions.Regex.Match(fileName, @"_(\d{8})_(\d{6})_")
        if m.Success then
            match DateTime.TryParseExact(m.Groups.[1].Value + m.Groups.[2].Value,
                                         "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                                         DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal) with
            | (true, d) -> Some d
            | _ -> None
        else None

    /// The observation time. DATE-OBS is authoritative; the COP synthetic
    /// export (2026-08) fills it with a product id ("AFC1-Synthetic") instead
    /// of a timestamp, AND writes the same DATE (the sequence start) into
    /// every sidecar of the delivery -- so when DATE-OBS is unusable the only
    /// trustworthy per-image time is the file-name timestamp; DATE is the last
    /// resort. See docs/COP-sidecar-issues (data-generator report), issue 1.
    let tryExtractDateObs (fileName : Option<string>) (mbi : JsonValue) : Option<DateTime> =
        match tryGetFitsHeader "DATE-OBS" mbi parseDate with
        | Some d -> Some d
        | None ->
            match fileName |> Option.bind tryParseTimestampFromFileName with
            | Some d ->
                Log.warn "[InstrumentMetadata] DATE-OBS is missing or unparseable, using the file-name timestamp (%A)" d
                Some d
            | None ->
                match tryGetFitsHeader "DATE" mbi parseDate with
                | Some d ->
                    Log.warn "[InstrumentMetadata] DATE-OBS is missing or unparseable, falling back to DATE (%A)" d
                    Some d
                | None -> None

    let tryExtractDateObsFromMbi (mbi: JsonValue) : DateTime option =
        tryExtractDateObs None mbi

    /// A FITS string header, with "" (observed in synthetic exports where the
    /// generator left the value blank) treated as absent.
    let private tryGetNonEmptyString (headerName : string) (mbi : JsonValue) : Option<string> =
        tryGetFitsHeader headerName mbi (function
            | JsonValue.String s when not (String.IsNullOrWhiteSpace s) -> Some s
            | _ -> None)

    /// The SPICE meta-kernel name (e.g. "hera_plan_v180_20250616_001") the mbi
    /// sidecar itself declares it was generated against.
    let tryExtractSpiceMk (mbi : JsonValue) : Option<string> =
        tryGetNonEmptyString "SPICE_MK" mbi

    let tryExtractXyz (xName : string) (yName : string) (zName : string) (mbi : JsonValue) = 
        match tryGetFitsHeader xName mbi parseFloat, tryGetFitsHeader yName mbi parseFloat, tryGetFitsHeader zName mbi parseFloat with
        | Some x, Some y, Some z ->  
            V3d(x,y,z) |> Some
        | _ -> None

    /// Extracts the spacecraft quaternion, dispatching on the two component
    /// naming conventions observed in MBI sidecars:
    ///   - SC_QUAT0/1/2/3 (older HERA/AFC exports)
    ///   - SC_QUATW/X/Y/Z (newer HERA/ASPECT exports)
    /// In both conventions the components are ordered (w, x, y, z), matching
    /// the QuaternionD constructor. `getFloat keyword` resolves a numeric FITS
    /// header value, or None if the keyword is absent. Self-contained: it only
    /// depends on the lookup, so it can be tested without any JSON.
    let parseQuaternion (getFloat : string -> Option<float>) : Result<QuaternionD, string> =
        let tryConvention (w, x, y, z) =
            match getFloat w, getFloat x, getFloat y, getFloat z with
            | Some w, Some x, Some y, Some z -> Some (QuaternionD(w, x, y, z))
            | _ -> None

        let conventions =
            [ ("SC_QUAT0", "SC_QUAT1", "SC_QUAT2", "SC_QUAT3")
              ("SC_QUATW", "SC_QUATX", "SC_QUATY", "SC_QUATZ") ]

        match conventions |> List.tryPick tryConvention with
        | Some q -> Result.Ok q
        | None -> Result.Error "could not extract SC_QUAT from mbi json (tried SC_QUAT0..3 and SC_QUATW/X/Y/Z)"

    let tryExtractSC_quat (mbi : JsonValue) =
        parseQuaternion (fun keyword -> tryGetFitsHeader keyword mbi parseFloat)


    /// The position headers declare "[km]" but some synthetic exports (HERA COP
    /// simulation, 2026-08) actually write metres -- confirmed against the
    /// delivery's own PRo3D.json ground truth (camera at |r| = 14.8e3 m where
    /// TRG_DIST says "14846 km") and by the sun distance, which read as metres
    /// is 1.09 AU and read as km would be 1090 AU. The sun distance makes a
    /// clean unit detector: as km it must land in ~[0.3 AU, 7 AU] for anything
    /// this app projects (Mars ~1.5 AU, Didymos 1.0-2.3 AU). Returns the factor
    /// that brings the sidecar's positions to km.
    /// See docs/COP-sidecar-issues (data-generator report), issue 4.
    let private detectPositionUnit (sunPos : V3d) : float =
        let kmPerAu = 1.495978707e8
        let asKm = sunPos.Length
        if asKm >= 0.3 * kmPerAu && asKm <= 7.0 * kmPerAu then
            1.0                             // already km
        elif asKm / 1000.0 >= 0.3 * kmPerAu && asKm / 1000.0 <= 7.0 * kmPerAu then
            Log.warn "[InstrumentMetadata] sidecar positions are metres despite the [km] headers (sun at %.0f 'km'); converting to km" asKm
            1.0e-3                          // metres mislabeled as km
        else
            Log.warn "[InstrumentMetadata] sun position %.3e km is implausible in km and in metres; keeping values as declared" asKm
            1.0

    /// `fileName`: the image (or sidecar) file name, if known -- used only as
    /// the observation-time fallback when DATE-OBS is unusable.
    let tryParseJsonForFile (fileName : Option<string>) (content : string) =
        match JsonValue.TryParse(content) with
        | Some mbi ->
            try
                let sunPos = tryExtractXyz "SUN_POSX" "SUN_POSY" "SUN_POSZ" mbi
                let earthPos = tryExtractXyz "EARTPOSX" "EARTPOSY" "EARTPOSZ" mbi
                let targetPos = tryExtractXyz "TRG_POSX" "TRG_POSY" "TRG_POSZ" mbi
                // normalize all positions to km (what this record's consumers assume)
                let unitScale = sunPos |> Option.map detectPositionUnit |> Option.defaultValue 1.0
                let sunPos = sunPos |> Option.map ((*) unitScale)
                let earthPos = earthPos |> Option.map ((*) unitScale)
                let targetPos = targetPos |> Option.map ((*) unitScale)
                let instrument = tryGetFitsHeader "INSTRUME" mbi (function JsonValue.String s -> Some s | _ -> None)
                let spiceMk = tryExtractSpiceMk mbi
                let target = tryGetNonEmptyString "TARGET" mbi
                match tryExtractDateObs fileName mbi, sunPos, earthPos, tryExtractSC_quat mbi, targetPos, instrument  with
                | Some d, Some sunPos, Some earthPos, Result.Ok quat, Some targetPos, Some instrument ->
                    {
                        obs_date = d; sunPos = sunPos;
                        earthPos = earthPos
                        sc_quat = quat
                        targetPos = targetPos
                        instrument = instrument
                        spiceMk = spiceMk
                        target = target
                    } |> Result.Ok
                | _ -> Result.Error (System.Exception("could not extract a required field (DATE-OBS/DATE, SUN_POS*, EARTPOS*, SC_QUAT*, TRG_POS*, INSTRUME) from mbi json"))
            with e ->
                Result.Error e
        | _ -> Result.Error (System.Exception("could not parse mbi json"))

    let tryParseJson (content : string) = tryParseJsonForFile None content

    /// The band image file paths an MBI sidecar declares. Newer exports list
    /// them under "mbi_bands", older ones under "bands"; both carry a per-band
    /// "file_path". Pure/testable: operates on an already-parsed JsonValue.
    let getBandFilePaths (mbi : JsonValue) : string list =
        let fromArray (name : string) =
            match mbi.TryGetProperty(name) with
            | Some (JsonValue.Array arr) ->
                arr
                |> Array.choose (fun band ->
                    match band.TryGetProperty("file_path") with
                    | Some (JsonValue.String s) -> Some s
                    | _ -> None)
                |> Array.toList
            | _ -> []
        match fromArray "mbi_bands" with
        | [] -> fromArray "bands"
        | xs -> xs

    /// Bare band image file names declared by an MBI sidecar's JSON content.
    let tryParseBandFileNames (content : string) : string list =
        match JsonValue.TryParse(content) with
        | Some mbi -> getBandFilePaths mbi |> List.map System.IO.Path.GetFileName
        | None -> []


type ParsedMetadata = Option<Tiff_Mbi_Json.Mbi> * Option<Tiff_Json.ImageMetadata>

/// Parses every .mbi.json sidecar in a directory once and indexes it by the
/// band image file name(s) it declares (mbi_bands/bands -> file_path). A
/// sidecar's name has no fixed relationship to the image file names it
/// covers -- a single ASPECT export shares one .mbi.json across dozens of
/// per-band images (Vis_0, NIR1_3, ...) -- so content, not file naming, is
/// the only reliable way to associate an image with its mbi metadata.
let private buildMbiIndex (dir : string) : Map<string, Tiff_Mbi_Json.Mbi> =
    if not (Directory.Exists dir) then Map.empty else
    Directory.EnumerateFiles(dir, "*.mbi.json")
    |> Seq.collect (fun path ->
        try
            let content = File.ReadAllText path
            match Tiff_Mbi_Json.tryParseJsonForFile (Some (Path.GetFileName path)) content with
            | Result.Ok mbi ->
                Tiff_Mbi_Json.tryParseBandFileNames content
                |> List.map (fun bandFile -> bandFile, mbi)
            | Result.Error e ->
                printfn $"could not parse mbi json metadata {path}: {e}"
                []
        with e ->
            printfn $"could not read mbi json metadata {path}: {e}"
            [])
    |> Map.ofSeq

/// Importing a folder looks metadata up once per image, but the index costs a
/// full parse of every sidecar in the directory -- per image that is O(n^2)
/// parses (a 92-image COP folder = ~8500 sidecar parses). Cache the index per
/// directory, keyed by the sidecars' (count, newest write time), so an
/// unchanged directory parses exactly once and edits/additions still
/// invalidate. The cache only ever holds a handful of directories per session,
/// so no eviction.
let private mbiIndexCache =
    System.Collections.Concurrent.ConcurrentDictionary<string, struct (int * int64) * Map<string, Tiff_Mbi_Json.Mbi>>()

let private buildMbiIndexCached (dir : string) : Map<string, Tiff_Mbi_Json.Mbi> =
    let stamp =
        if not (Directory.Exists dir) then struct (0, 0L)
        else
            let mutable count = 0
            let mutable newest = 0L
            for f in Directory.EnumerateFiles(dir, "*.mbi.json") do
                count <- count + 1
                let t = File.GetLastWriteTimeUtc(f).Ticks
                if t > newest then newest <- t
            struct (count, newest)
    match mbiIndexCache.TryGetValue dir with
    | true, (s, idx) when s = stamp -> idx
    | _ ->
        let idx = buildMbiIndex dir
        mbiIndexCache.[dir] <- (stamp, idx)
        idx

/// The per-image statistics/product-info sidecar ("<image file name>.json") is
/// still located by naming convention: unlike the mbi sidecar, its content
/// carries nothing (no source file name/hash we could match on cheaply) that
/// would let us find it any other way. The kill-phrase/extension fallbacks
/// below only cover legacy exports where the image was renamed after the
/// sidecar was written.
let private tryParseTifJson (imagePath : string) : Option<Tiff_Json.ImageMetadata> =
    let getJsonInfoPath (imagePath : string) (suffix : string) : string =
        let killPhrases = ["_Stacked"; "_AFC1"; "_AFC2"; "_HSH"]
        let fi = Path.Combine(Path.GetDirectoryName(imagePath), Path.GetFileName(imagePath) + suffix)
        if File.Exists fi then
            fi
        else
            let fiv1 = List.fold (fun (path : string) kill -> path.Replace(kill, "")) fi killPhrases
            if File.Exists fiv1 then
                fiv1
            else
                let fiv2 = fiv1.Replace(".exr", ".tif")
                let fiv3 = fi.Replace(".exr", ".tif")
                if Path.Exists fiv2 then
                    fiv2
                else
                    fiv3

    let json = getJsonInfoPath imagePath ".json"
    if File.Exists json then
        try
            match Tiff_Json.tryParseJson (File.ReadAllText json) with
            | Result.Ok tif -> Some tif
            | Result.Error e ->
                printfn $"could not parse json metadata {json} for {imagePath}: {e}"
                None
        with e ->
            printfn $"could not read json metadata {json} for {imagePath}: {e}"
            None
    else
        printfn $"could not find json metadata {json} for {imagePath}"
        None

/// Fallback association for sidecars that declare no usable band file paths:
/// some synthetic exports (HERA COP simulation, 2026-08) leave every
/// bands[].file_path empty, so the content-based index can never match them.
/// For those, fall back to the naming convention "<image base>.mbi.json"
/// (HERA_AFC_..._COP.png <-> HERA_AFC_..._COP.mbi.json) and parse that one
/// sidecar directly. See docs/COP-sidecar-issues (data-generator report).
let private tryParseMbiByNamingConvention (imagePath : string) : Option<Tiff_Mbi_Json.Mbi> =
    let sidecar = Path.ChangeExtension(imagePath, ".mbi.json")
    if File.Exists sidecar then
        try
            match Tiff_Mbi_Json.tryParseJsonForFile (Some (Path.GetFileName imagePath)) (File.ReadAllText sidecar) with
            | Result.Ok mbi ->
                Log.warn "[InstrumentMetadata] %s declares no band file paths; associated it with %s by naming convention"
                    (Path.GetFileName sidecar) (Path.GetFileName imagePath)
                Some mbi
            | Result.Error e ->
                printfn $"could not parse mbi json metadata {sidecar}: {e}"
                None
        with e ->
            printfn $"could not read mbi json metadata {sidecar}: {e}"
            None
    else
        None

let tryParseMetadataForImagePath (imagePath : string) : ParsedMetadata =
    let mbi =
        match buildMbiIndexCached (Path.GetDirectoryName(imagePath)) |> Map.tryFind (Path.GetFileName imagePath) with
        | Some mbi -> Some mbi
        | None -> tryParseMbiByNamingConvention imagePath
    let tif = tryParseTifJson imagePath
    mbi, tif

let discoverInstrumentFolder (dir : string) : seq<string * ParsedMetadata> =
    let mbiIndex = buildMbiIndex dir
    Directory.EnumerateFiles(dir, "*.tif", SearchOption.TopDirectoryOnly)
    |> Seq.map (fun tifFilename ->
        let mbi =
            match mbiIndex |> Map.tryFind (Path.GetFileName tifFilename) with
            | Some mbi -> Some mbi
            | None -> tryParseMbiByNamingConvention tifFilename
        let tif = tryParseTifJson tifFilename
        tifFilename, (mbi, tif))
