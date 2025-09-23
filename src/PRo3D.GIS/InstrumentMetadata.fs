#if INTERACTIVE
#r "nuget: FSharp.Data"
#else
module PRo3D.Core.InstrumentMetadata
#endif

open System.IO

module Tiff_Json = 
    open System
    open System.Text.Json
    open System.Text.Json.Serialization

    type ImageStatistics = {
        minimum: int
        maximum: int
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

    let parseJson (jsonString: string) =
        let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        JsonSerializer.Deserialize<ImageMetadata>(jsonString, options)

    let test () =

        // Example usage
        let jsonString = """
        {
            "product_information": {
                "schema_id": "https://www.joanneum.at/jim/product_information.schema.json",
                "schema_version": 7262,
                "product_type": "image",
                "product_state": "imported",
                "creator_id": "ImportFits",
                "creation_datetime": "2025-03-14T10:31:01.946966+0000"
            },
            "image_width": 1024,
            "image_height": 1088,
            "channels": 1,
            "data_type": "uint16",
            "file_md5": "fd504222c6c33395377eb41e35292d29",
            "image_statistics": [
                {
                    "minimum": 0,
                    "maximum": 1647,
                    "mean": 155.49385429831113,
                    "median": 157.04812761839145,
                    "standard_deviation": 14.130619261267585,
                    "variance": 199.67440070690645
                }
            ],
            "mission_name": "ESA - HERA",
            "camera_system": "HSH"
        }
        """

        let metadata = parseJson jsonString
        printfn "%A" metadata

module Tiff_Mbi_Json = 
    
    open FSharp.Data

    let parseJson (content : string) = 
        JsonValue.Parse(content)

    let test () = 
        File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "exampleFiles", "HSH_0CRQSV_250312T115349_1A.mbi.json"))

let discoverInstrumentFolder (dir : string) = 
    let getJsonInfoPath (imagePath : string) (suffix : string) = 
       Path.Combine(Path.GetDirectoryName(imagePath), Path.GetFileNameWithoutExtension(imagePath) + suffix)

    let tifs = Directory.EnumerateFiles(dir, "*.tif", SearchOption.TopDirectoryOnly)
    tifs
    |> Seq.choose (fun tifFilename -> 
        let mbi_json = getJsonInfoPath tifFilename ".mbi.json"
        let json = tifFilename + ".json"
        match File.Exists(mbi_json), File.Exists(json) with
        | true, true -> 
            let jimMetadata = File.ReadAllText(json) |> Tiff_Json.parseJson
            let mbi_json = File.ReadAllText(mbi_json) |> Tiff_Mbi_Json.parseJson
            Some (tifFilename, jimMetadata, mbi_json)
        | f, e -> 
            printfn "%s, %A" mbi_json  (f,e) 
            None
    )

let test () = 
    discoverInstrumentFolder @"C:\pro3ddata\HERA\20250314\HSH_converted\HSH_converted"