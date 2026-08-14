namespace Aardvark.PixImage.LibTiff

open System
open BitMiracle.LibTiff.Classic
open System.Globalization


type PixelBuffers =
    | UInt16Bands  of uint16[][]       // [band][index]
    | Int16Bands   of int16[][]
    | Int32Bands   of int[][]
    | UInt32Bands  of uint32[][]
    | Float32Bands of float32[][]

type Format = 
    | Int16 | Uint16 | Int32 | UInt32 | Float32

type TiffReadResult =
    { width: int
      height: int
      bands: int
      format : Format
      buffers: PixelBuffers }

// This one is based on a copilot implementation
module MultiBandReader =

    /// Find a suitable directory (IFD) and set it on the tif instance.
    let private chooseDirectory (tif: Tiff) (wantSampleFormat:  Option<int>) (wantBitsPerSample: Option<int>) =
        let nd = tif.NumberOfDirectories()
        let mutable chosen = -1s
        for i in 0s .. nd - 1s do
            if tif.SetDirectory(i) then
                try
                    let w = tif.GetFieldDefaulted(TiffTag.IMAGEWIDTH).[0].ToInt()
                    let h = tif.GetFieldDefaulted(TiffTag.IMAGELENGTH).[0].ToInt()
                    if w > 0 && h > 0 then
                        let sampleOk =
                            match wantSampleFormat with
                            | None -> true
                            | Some sf ->
                                let fv = tif.GetFieldDefaulted(TiffTag.SAMPLEFORMAT)
                                fv <> null && fv.[0].ToInt() = sf
                        let bitsOk =
                            match wantBitsPerSample with
                            | None -> true
                            | Some b ->
                                let fv = tif.GetFieldDefaulted(TiffTag.BITSPERSAMPLE)
                                fv <> null && fv.[0].ToInt() = b
                        if sampleOk && bitsOk && chosen = -1s then chosen <- i
                with _ -> () // missing tags or unreadable IFD: skip
        if chosen = -1s then failwith "No suitable image directory found in TIFF."
        if not (tif.SetDirectory(chosen)) then failwithf "Failed to SetDirectory(%d)." chosen
        chosen

    let private readPlaneUInt16 (tif:Tiff) (width:int) (height:int) (band:int16) (scanline:byte[]) (forceSwap:bool) =
        let out = Array.zeroCreate<uint16> (width * height)
        for row = 0 to height - 1 do
            if not (tif.ReadScanline(scanline, row, band)) then failwithf "ReadScanline failed row=%d band=%d" row band
            let mutable off = 0
            let baseIdx = row * width
            for col = 0 to width - 1 do
                let v = BitConverter.ToUInt16(scanline, off)
                out.[baseIdx + col] <- if forceSwap then (uint16 ((((v &&& 0x00us) <<< 8) ||| ((v &&& 0xFF00us) >>> 8)))) else v
                off <- off + 2
        out

    let private readPlaneInt16 (tif:Tiff) (width:int) (height:int) (band:int16) (scanline:byte[]) (forceSwap:bool) =
        let out = Array.zeroCreate<int16> (width * height)
        for row = 0 to height - 1 do
            if not (tif.ReadScanline(scanline, row, band)) then failwithf "ReadScanline failed row=%d band=%d" row band
            let mutable off = 0
            let baseIdx = row * width
            for col = 0 to width - 1 do
                let v = BitConverter.ToInt16(scanline, off)
                out.[baseIdx + col] <- if forceSwap then int16 (((uint16 v &&& 0x00us) <<< 8) ||| ((uint16 v &&& 0xFF00us) >>> 8)) else v
                off <- off + 2
        out

    let private readPlaneInt32 (tif:Tiff) (width:int) (height:int) (band:int16) (scanline:byte[]) (forceSwap:bool) =
        let out = Array.zeroCreate<int> (width * height)
        let swap32 (v:uint32) =
            ((v &&& 0x000000FFu) <<< 24) ||| ((v &&& 0x0000FF00u) <<< 8) |||
            ((v &&& 0x00FF0000u) >>> 8)  ||| ((v &&& 0xFF000000u) >>> 24)
        for row = 0 to height - 1 do
            if not (tif.ReadScanline(scanline, row, band)) then failwithf "ReadScanline failed row=%d band=%d" row band
            let mutable off = 0
            let baseIdx = row * width
            for col = 0 to width - 1 do
                let u = BitConverter.ToUInt32(scanline, off)
                let v = if forceSwap then swap32 u else u
                out.[baseIdx + col] <- int v
                off <- off + 4
        out

    let private readPlaneUInt32 (tif:Tiff) (width:int) (height:int) (band:int16) (scanline:byte[]) (forceSwap:bool) =
        let out = Array.zeroCreate<uint32> (width * height)
        let swap32 (v:uint32) =
            ((v &&& 0x000000FFu) <<< 24) ||| ((v &&& 0x0000FF00u) <<< 8) |||
            ((v &&& 0x00FF0000u) >>> 8)  ||| ((v &&& 0xFF000000u) >>> 24)
        for row = 0 to height - 1 do
            if not (tif.ReadScanline(scanline, row, band)) then failwithf "ReadScanline failed row=%d band=%d" row band
            let mutable off = 0
            let baseIdx = row * width
            for col = 0 to width - 1 do
                let u = BitConverter.ToUInt32(scanline, off)
                out.[baseIdx + col] <- if forceSwap then swap32 u else u
                off <- off + 4
        out

    let private readPlaneFloat32 (tif:Tiff) (width:int) (height:int) (band:int16) (scanline:byte[]) (forceSwap:bool) =
        let out = Array.zeroCreate<float32> (width * height)
        let swap32 (v:uint32) =
            ((v &&& 0x000000FFu) <<< 24) ||| ((v &&& 0x0000FF00u) <<< 8) |||
            ((v &&& 0x00FF0000u) >>> 8)  ||| ((v &&& 0xFF000000u) >>> 24)
        let tmp = Array.zeroCreate<byte> 4
        for row = 0 to height - 1 do
            if not (tif.ReadScanline(scanline, row, band)) then failwithf "ReadScanline failed row=%d band=%d" row band
            let mutable off = 0
            let baseIdx = row * width
            for col = 0 to width - 1 do
                let u = BitConverter.ToUInt32(scanline, off)
                let uu = if forceSwap then swap32 u else u
                BitConverter.GetBytes(uu).CopyTo(tmp, 0)
                out.[baseIdx + col] <- BitConverter.ToSingle(tmp, 0)
                off <- off + 4
        out



    type ImageInfo = { width: int; height : int; channels : int; channelNames : Option<string>; bitsPerSample : int; format : Format; geoInfo : Option<GeoInfo> }



    let tryGetChannels (path:string) : Option<ImageInfo> = 
        use tif = Tiff.Open(path, "r")
        if isNull tif then 
            None
        else
            // choose a suitable directory that looks like the main image
            ignore (chooseDirectory tif None None)

            let width = tif.GetFieldDefaulted(TiffTag.IMAGEWIDTH).[0].ToInt()
            let height = tif.GetFieldDefaulted(TiffTag.IMAGELENGTH).[0].ToInt()
            let bitsPerSample = tif.GetFieldDefaulted(TiffTag.BITSPERSAMPLE).[0].ToInt()
            let sampleFormat = tif.GetFieldDefaulted(TiffTag.SAMPLEFORMAT).[0].ToInt()
            let samplesPerPixel = tif.GetFieldDefaulted(TiffTag.SAMPLESPERPIXEL).[0].ToInt()

            let format = 
                match sampleFormat, bitsPerSample with
                | sf, 16 when sf = int SampleFormat.UINT ->
                    Format.UInt32 |> Some
                | sf, 16 when sf = int SampleFormat.INT ->
                    Format.Int16 |> Some
                | sf, 32 when sf = int SampleFormat.INT ->
                    Format.Int32 |> Some
                | sf, 32 when sf = int SampleFormat.UINT ->
                    Format.UInt32 |> Some
                | sf, 32 when sf = int SampleFormat.IEEEFP ->
                    Format.Float32 |> Some
                | _ -> 
                    None

            let geoInfo = GeoTiffFields.tryGetGeoInfo tif 

            match format with
            | None -> None
            | Some format -> 
                Some { channels = samplesPerPixel; channelNames = None; bitsPerSample = bitsPerSample; width = width; height = height; format = format; geoInfo = geoInfo}

    let tryReadMultiBandTiff (path:string) (forceByteSwap:bool) : Result<TiffReadResult, string> =
        use tif = Tiff.Open(path, "r")
        if isNull tif then failwith "Cannot open TIFF."

        // choose a suitable directory that looks like the main image
        ignore (chooseDirectory tif None None)

        let width = tif.GetFieldDefaulted(TiffTag.IMAGEWIDTH).[0].ToInt()
        let height = tif.GetFieldDefaulted(TiffTag.IMAGELENGTH).[0].ToInt()
        let bitsPerSample = tif.GetFieldDefaulted(TiffTag.BITSPERSAMPLE).[0].ToInt()
        let sampleFormat = tif.GetFieldDefaulted(TiffTag.SAMPLEFORMAT).[0].ToInt()
        let samplesPerPixel = tif.GetFieldDefaulted(TiffTag.SAMPLESPERPIXEL).[0].ToInt()
        let planar = tif.GetFieldDefaulted(TiffTag.PLANARCONFIG).[0].ToInt()

        if planar <> int PlanarConfig.SEPARATE then failwith "Only PLANARCONFIG = SEPARATE supported."

        if width <= 0 || height <= 0 then failwith "Invalid image dimensions."

        let expectedRowBytes = width * (bitsPerSample / 8)
        let scanlineSize = tif.ScanlineSize()
        if scanlineSize < expectedRowBytes then failwithf "ScanlineSize %d < expectedRowBytes %d" scanlineSize expectedRowBytes

        // shared scanline buffer reused for each read
        let scanline = Array.zeroCreate<byte> scanlineSize

        // dispatch by SAMPLEFORMAT / BITSPERSAMPLE and construct union directly
        match sampleFormat, bitsPerSample with
        | sf, 16 when sf = int SampleFormat.UINT ->
            let bands = Array.init samplesPerPixel (fun b -> readPlaneUInt16 tif width height (int16 b) scanline forceByteSwap)
            Result.Ok { width = width; height = height; bands = samplesPerPixel; buffers = UInt16Bands bands; format = Uint16 }
        | sf, 16 when sf = int SampleFormat.INT ->
            let bands = Array.init samplesPerPixel (fun b -> readPlaneInt16 tif width height (int16 b) scanline forceByteSwap)
            Result.Ok { width = width; height = height; bands = samplesPerPixel; buffers = Int16Bands bands; format = Int16 }
        | sf, 32 when sf = int SampleFormat.INT ->
            let bands = Array.init samplesPerPixel (fun b -> readPlaneInt32 tif width height (int16 b) scanline forceByteSwap)
            Result.Ok { width = width; height = height; bands = samplesPerPixel; buffers = Int32Bands bands; format = Int32 }
        | sf, 32 when sf = int SampleFormat.UINT ->
            let bands = Array.init samplesPerPixel (fun b -> readPlaneUInt32 tif width height (int16 b) scanline forceByteSwap)
            Result.Ok { width = width; height = height; bands = samplesPerPixel; buffers = UInt32Bands bands; format = UInt32 }
        | sf, 32 when sf = int SampleFormat.IEEEFP ->
            let bands = Array.init samplesPerPixel (fun b -> readPlaneFloat32 tif width height (int16 b) scanline forceByteSwap)
            Result.Ok { width = width; height = height; bands = samplesPerPixel; buffers = Float32Bands bands; format = Float32 }
        | _ ->
            Result.Error $"Unsupported SAMPLEFORMAT={sampleFormat} BITSPERSAMPLE={bitsPerSample}"