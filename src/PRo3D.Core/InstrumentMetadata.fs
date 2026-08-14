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

    let tryExtractDateObsFromMbi (mbi: JsonValue) : DateTime option =
        tryGetFitsHeader "DATE-OBS" mbi parseDate

    /// The SPICE meta-kernel name (e.g. "hera_plan_v180_20250616_001") the mbi
    /// sidecar itself declares it was generated against.
    let tryExtractSpiceMk (mbi : JsonValue) : Option<string> =
        tryGetFitsHeader "SPICE_MK" mbi (function JsonValue.String s -> Some s | _ -> None)

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


    let tryParseJson (content : string) = 
        match JsonValue.TryParse(content) with
        | Some mbi ->
            try
                let sunPos = tryExtractXyz "SUN_POSX" "SUN_POSY" "SUN_POSZ" mbi
                let earthPos = tryExtractXyz "EARTPOSX" "EARTPOSY" "EARTPOSZ" mbi
                let targetPos = tryExtractXyz "TRG_POSX" "TRG_POSY" "TRG_POSZ" mbi
                let instrument = tryGetFitsHeader "INSTRUME" mbi (function JsonValue.String s -> Some s | _ -> None)
                let spiceMk = tryExtractSpiceMk mbi
                match tryExtractDateObsFromMbi mbi, sunPos, earthPos, tryExtractSC_quat mbi, targetPos, instrument  with
                | Some d, Some sunPos, Some earthPos, Result.Ok quat, Some targetPos, Some instrument ->
                    {
                        obs_date = d; sunPos = sunPos;
                        earthPos = earthPos
                        sc_quat = quat
                        targetPos = targetPos
                        instrument = instrument
                        spiceMk = spiceMk
                    } |> Result.Ok
                | _ -> Result.Error (System.Exception("could not find DATE-OBS in mbi json"))
            with e ->
                Result.Error e
        | _ -> Result.Error (System.Exception("could not parse mbi json"))

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
    Directory.EnumerateFiles(dir, "*.mbi.json")
    |> Seq.collect (fun path ->
        try
            let content = File.ReadAllText path
            match Tiff_Mbi_Json.tryParseJson content with
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

let tryParseMetadataForImagePath (imagePath : string) : ParsedMetadata =
    let mbi = buildMbiIndex (Path.GetDirectoryName(imagePath)) |> Map.tryFind (Path.GetFileName imagePath)
    let tif = tryParseTifJson imagePath
    mbi, tif

let discoverInstrumentFolder (dir : string) : seq<string * ParsedMetadata> =
    let mbiIndex = buildMbiIndex dir
    Directory.EnumerateFiles(dir, "*.tif", SearchOption.TopDirectoryOnly)
    |> Seq.map (fun tifFilename ->
        let mbi = mbiIndex |> Map.tryFind (Path.GetFileName tifFilename)
        let tif = tryParseTifJson tifFilename
        tifFilename, (mbi, tif))
