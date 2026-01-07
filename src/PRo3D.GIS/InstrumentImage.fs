namespace PRo3D.InstrumentData

open System
open System.IO
open Aardvark.Base
open Aardvark.PixImage
open Aardvark.Rendering
open Aardvark.PixImage.LibTiff


module InstrumentImageTextures = 

    [<Struct>]
    type StatsResult<'T> =
        { Min: 'T
          Max: 'T
          Average: float }

    module GenericStats =

        let inline checkNonEmpty (arr: 'T[]) =
            if arr = null || arr.Length = 0 then 
                invalidArg "arr" "Sequence must not be null or empty."

        let inline toFloat (x: ^T) : float =
            // relies on F# numeric conversion operator
            float x

        let inline minMax (arr: ^T[]) : ^T * ^T when ^T : comparison =
            checkNonEmpty arr
            let mutable mn = arr.[0]
            let mutable mx = arr.[0]
            for i = 1 to arr.Length - 1 do
                let v = arr.[i]
                if v < mn then mn <- v
                if v > mx then mx <- v
            mn, mx

        let inline sumAsFloat (arr: ^T[]) : float =
            // accumulate as float to avoid overflow for the requested types
            let mutable acc = 0.0
            for v in arr do
                acc <- acc + toFloat v
            acc

        let inline stats (arr: ^T[]) : StatsResult<'T> when ^T : comparison =
            checkNonEmpty arr
            let mn, mx = minMax arr
            let total = sumAsFloat arr
            let avg = total / float arr.Length
            { Min = mn; Max = mx; Average = avg }

    module StatsResult =
        let inline toFloat x = { Min = float x.Min; Max = float x.Max; Average = x.Average }


    let textureFormats = 
        Map.ofList [
            Int16, TextureFormat.R16i
            Uint16, TextureFormat.R16i
            Int32, TextureFormat.R32i
            UInt32, TextureFormat.R16ui
            Float32, TextureFormat.R32f
        ]

    type Band = 
        {
            pi : PixImage
            stats : Option<StatsResult<float>>
        }

    let instrumentImageToTexture (computeStatistics : bool) 
                                 { width = width; height = height; bands = bands; buffers = buffers; format = format } 
                                 = 



        let arrays : array<System.Array * Option<StatsResult<float>>> = 
            match buffers with
            | PixelBuffers.Float32Bands bands -> 
                bands |> Array.map (fun b -> b :> Array, if computeStatistics then GenericStats.stats b |> StatsResult.toFloat |> Some else None)
            | PixelBuffers.Int16Bands bands -> 
                bands |> Array.map (fun b -> b :> Array, if computeStatistics then GenericStats.stats b |> StatsResult.toFloat |> Some else None)
            | PixelBuffers.Int32Bands bands -> 
                bands |> Array.map (fun b -> b :> Array, if computeStatistics then GenericStats.stats b |> StatsResult.toFloat |> Some else None)
            | PixelBuffers.UInt32Bands bands ->
                bands |> Array.map (fun b -> b :> Array, if computeStatistics then GenericStats.stats b |> StatsResult.toFloat |> Some else None)
            | PixelBuffers.UInt16Bands bands ->
                bands |> Array.map (fun b -> b :> Array, if computeStatistics then GenericStats.stats b |> StatsResult.toFloat |> Some else None)

        let pis = 
            arrays |> Array.mapi (fun bandIndex (bandArray, stat) -> 
                let pi =
                    match format with
                    | Format.Float32 -> 
                        let pi = PixImage<float32>(Col.Format.Gray, V2i(width, height))
                        Array.Copy(bandArray, pi.Array, bandArray.Length)
                        { pi = pi :> PixImage; stats = stat }
                    | Format.Uint16 -> 
                        let pi = PixImage<uint16>(Col.Format.Gray, V2i(width, height))
                        Array.Copy(bandArray, pi.Array, bandArray.Length)
                        { pi = pi :> PixImage; stats = stat }
                    | Format.UInt32 -> 
                        let pi = PixImage<uint32>(Col.Format.Gray, V2i(width, height))
                        Array.Copy(bandArray, pi.Array, bandArray.Length)
                        { pi = pi :> PixImage; stats = stat }
                    | Format.Int32 -> 
                        let pi = PixImage<int32>(Col.Format.Gray, V2i(width, height))
                        Array.Copy(bandArray, pi.Array, bandArray.Length)
                        { pi = pi :> PixImage; stats = stat }
                    | Format.Int16 -> 
                        let pi = PixImage<int16>(Col.Format.Gray, V2i(width, height))
                        Array.Copy(bandArray, pi.Array, bandArray.Length)
                        { pi = pi :> PixImage; stats = stat }
                pi
            ) 

        pis
