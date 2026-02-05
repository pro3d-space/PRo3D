namespace Aardvark.PixImage.LibTiff

open System
open BitMiracle.LibTiff.Classic
open System.Globalization

type GeoInfo =
    { ScaleX: float
      ScaleY: float
      TiepointRasterI: float
      TiepointRasterJ: float
      TiepointModelX: float
      TiepointModelY: float
      TiepointModelZ: float }

module GeoTiffFields =

    let inline private getFieldDefaulted (t: Tiff) (tagId: int) : FieldValue[] option =
        let fv = t.GetFieldDefaulted(enum<TiffTag> tagId)
        if isNull fv then None else Some fv

    let inline private tryParseNumber (o: obj) : float option =
        match o with
        | :? float as d -> Some d
        | :? float32 as f -> Some (double f)
        | :? int as i -> Some (double i)
        | :? int16 as s -> Some (double s)
        | :? uint16 as us -> Some (double us)
        | :? int64 as i64 -> Some (double i64)
        | :? uint32 as u -> Some (double u)
        | :? uint64 as u64 -> Some (double u64)
        | :? string as s ->
            let ok, v = Double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture)
            if ok then Some v else None
        | _ -> None

    let inline private fieldValueToFloatArray (fv: FieldValue) : float[] option =
        match fv.Value with
        | :? array<float> as fa when fa.Length > 0 -> Some fa
        | :? array<float32> as fa32 when fa32.Length > 0 -> Some (Array.map double fa32)
        | :? array<int> as ia when ia.Length > 0 -> Some (Array.map double ia)
        | :? array<int16> as ia16 when ia16.Length > 0 -> Some (Array.map double ia16)
        | :? array<uint16> as ua16 when ua16.Length > 0 -> Some (Array.map double ua16)
        | :? array<uint32> as ua32 when ua32.Length > 0 -> Some (Array.map double ua32)
        | :? array<int64> as ia64 when ia64.Length > 0 -> Some (Array.map double ia64)
        | :? array<uint64> as ua64 when ua64.Length > 0 -> Some (Array.map double ua64)
        | :? array<obj> as oa when oa.Length > 0 ->
            oa
            |> Array.choose tryParseNumber
            |> fun parsed -> if parsed.Length > 0 then Some parsed else None
        | singleBoxed ->
            // single boxed numeric or string
            match tryParseNumber singleBoxed with
            | Some v -> Some [| v |]
            | None -> None

    let tryReadPixelScale (t: Tiff) : (float * float) option =
        match getFieldDefaulted t 33550 with
        | None -> None
        | Some fvArr when fvArr.Length > 0 ->
            match fieldValueToFloatArray fvArr.[0] with
            | Some darr when darr.Length >= 2 -> Some (darr.[0], darr.[1])
            | _ -> None
        | _ -> None

    let tryReadTiepoints (t: Tiff) : (float * float * float * float * float * float)[] option =
        match getFieldDefaulted t 33922 with
        | None -> None
        | Some fvArr when fvArr.Length > 0 ->
            match fieldValueToFloatArray fvArr.[0] with
            | Some darr when darr.Length >= 6 && darr.Length % 6 = 0 ->
                darr
                |> Array.chunkBySize 6
                |> Array.map (fun c -> (c.[0], c.[1], c.[2], c.[3], c.[4], c.[5]))
                |> Some
            | _ -> None
        | _ -> None

    let tryGetGeoInfo (t : Tiff) =
        match tryReadPixelScale t, tryReadTiepoints t with
        | Some (sx, sy), Some tiepts when tiepts.Length > 0 ->
            let (i, j, k, x, y, z) = tiepts.[0]
            Some { ScaleX = sx; ScaleY = sy
                   TiepointRasterI = i; TiepointRasterJ = j
                   TiepointModelX = x; TiepointModelY = y; TiepointModelZ = z }
        | _ -> None
