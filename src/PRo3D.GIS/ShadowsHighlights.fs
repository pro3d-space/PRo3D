namespace PRo3D.ImageMapping

open System
open System.Collections.Generic

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.Rendering

/// Shadows/highlights for projected images (docs/ShadowsHighlights.md), after
/// LuciaKloos/PRo3D.ImageMapping (branch rgbRatiosComposite, ImageShader.fs): a
/// shadow and a highlight mask select the dark and the bright tones (tone), are
/// softened by a separable 9-tap box blur (radius), and the image value is pulled
/// along a gamma curve by mask * amount.
///
/// The masks are computed here on the CPU, once per image and settings, and cached;
/// the 2D preview and the projection on the surface both sample the same mask, so
/// they always agree. Amount is applied in the shaders (`apply`), so moving an amount
/// slider recomputes nothing. With both amounts at 0 no mask exists at all.
///
/// Single-band data: the value the masks select on is the image's normalised value
/// (its min/max range), the same value the colour map is looked up with.
module ShadowsHighlights =

    /// Everything that shapes a mask. Amount does not, it only scales the effect.
    type MaskKey =
        {
            path            : string
            channel         : int
            /// display min/max of the image (falseColorModel bounds)
            minMax          : V2d
            /// DataType as int: 2 = float, sampled as-is; otherwise scaled by 65000
            dataType        : int
            shadowTone      : float
            shadowRadius    : float
            highlightTone   : float
            highlightRadius : float
        }

    // ---- the adjustment (shared by the preview and the stack shader) ---------------

    /// Pulls the normalised value `c` toward a gamma curve: highlights toward c^2.8
    /// (darker), shadows toward 1-(1-c)^2.8 (brighter), each weighted by its mask and
    /// amount. Identical to the original's applyShadowsHighlights.
    [<ReflectedDefinition>]
    let apply (c : float32) (shadowMask : float32) (highlightMask : float32) (shadowAmount : float32) (highlightAmount : float32) =
        let exponent = 2.8f
        let c = clamp 0.0f 1.0f c
        let hStrength = clamp 0.0f 1.0f (highlightAmount * highlightMask)
        let sStrength = clamp 0.0f 1.0f (shadowAmount * shadowMask)
        let hDelta = hStrength * (pow c exponent - c)
        let sDelta = sStrength * ((1.0f - pow (1.0f - c) exponent) - c)
        clamp 0.0f 1.0f (c + hDelta + sDelta)

    // ---- mask computation -------------------------------------------------------

    /// GLSL smoothstep, defined also for e0 >= e1 (a hard step there).
    let private smoothstep (e0 : float32) (e1 : float32) (x : float32) =
        if e1 <= e0 then (if x >= e1 then 1.0f else 0.0f)
        else
            let t = clamp 0.0f 1.0f ((x - e0) / (e1 - e0))
            t * t * (3.0f - 2.0f * t)

    /// The band as the shader samples it (.X of the texture): bytes and ushorts are
    /// normalised by the texture format, floats come raw. Grayscale images carry
    /// their value in Gray, colour images in Red. None for pixel types the
    /// projection does not produce.
    let private sampledValues (pi : PixImage) : Option<array<float32>> =
        let size = pi.Size
        let channel =
            if pi.Format = Col.Format.Gray || pi.Format = Col.Format.GrayAlpha then Col.Channel.Gray
            else Col.Channel.Red
        let read (m : Matrix<'t>) (toFloat : 't -> float32) =
            let values = Array.zeroCreate (size.X * size.Y)
            for y in 0 .. size.Y - 1 do
                for x in 0 .. size.X - 1 do
                    values.[y * size.X + x] <- toFloat m.[x, y]
            Some values
        match pi with
        | :? PixImage<byte> as p    -> read (p.GetChannel channel) (fun v -> float32 v / 255.0f)
        | :? PixImage<uint16> as p  -> read (p.GetChannel channel) (fun v -> float32 v / 65535.0f)
        | :? PixImage<float32> as p -> read (p.GetChannel channel) id
        | _ ->
            Log.warn "[ShadowsHighlights] no masks for %A images" pi.PixFormat
            None

    /// The value the colour map is looked up with, in [0, 1] (the preview's remap).
    let private normalise (dataType : int) (minMax : V2d) (v : float32) =
        let lo = float32 minMax.X
        let hi = float32 minMax.Y
        let range = hi - lo
        if range <= 0.0f then 0.0f
        else
            let t =
                if dataType = 2 then (v - lo) / range
                else ((min hi (max lo (v * 65000.0f))) - lo) / range
            clamp 0.0f 1.0f t

    /// One pass of the original's boxBlur: 9 taps at k * radius/4 pixels along one
    /// axis, linearly interpolated, clamped at the border.
    let private blurPass (src : array<float32>) (w : int) (h : int) (horizontal : bool) (radius : float) =
        let step = float32 (max 0.0 radius / 4.0)
        if step <= 0.0f then src
        else
            let dst = Array.zeroCreate src.Length
            let n = if horizontal then w else h
            let sampleAt (line : int) (pos : float32) =
                let p = clamp 0.0f (float32 (n - 1)) pos
                let i0 = int p
                let i1 = min (n - 1) (i0 + 1)
                let f = p - float32 i0
                let idx i = if horizontal then line * w + i else i * w + line
                src.[idx i0] * (1.0f - f) + src.[idx i1] * f
            let lines = if horizontal then h else w
            for line in 0 .. lines - 1 do
                for i in 0 .. n - 1 do
                    let mutable sum = 0.0f
                    for k in -4 .. 4 do
                        sum <- sum + sampleAt line (float32 i + float32 k * step)
                    let idx = if horizontal then line * w + i else i * w + line
                    dst.[idx] <- sum / 9.0f
            dst

    /// The blurred masks of one image as an RGBA texture image: R = shadow,
    /// G = highlight (B, A unused). The original's mask + horizontal + vertical
    /// blur passes.
    let computeMasks (key : MaskKey) (pi : PixImage) : Option<PixImage> =
        sampledValues pi |> Option.map (fun values ->
            let w, h = pi.Size.X, pi.Size.Y
            let shadowEnd = clamp 0.0f 1.0f (float32 key.shadowTone)
            let highlightStart = 1.0f - clamp 0.0f 1.0f (float32 key.highlightTone)
            let shadow = Array.zeroCreate values.Length
            let highlight = Array.zeroCreate values.Length
            for i in 0 .. values.Length - 1 do
                let t = normalise key.dataType key.minMax values.[i]
                shadow.[i] <- 1.0f - smoothstep 0.0f shadowEnd t
                highlight.[i] <- smoothstep highlightStart 1.0f t
            let blur (mask : array<float32>) (radius : float) =
                blurPass (blurPass mask w h true radius) w h false radius
            let shadow = blur shadow key.shadowRadius
            let highlight = blur highlight key.highlightRadius
            let out = PixImage<byte>(Col.Format.RGBA, V2i(w, h))
            let mutable r = out.GetChannel Col.Channel.Red
            let mutable g = out.GetChannel Col.Channel.Green
            let toByte (v : float32) = byte (clamp 0.0f 255.0f (v * 255.0f + 0.5f))
            for y in 0 .. h - 1 do
                for x in 0 .. w - 1 do
                    r.[x, y] <- toByte shadow.[y * w + x]
                    g.[x, y] <- toByte highlight.[y * w + x]
            out :> PixImage)

    // ---- cache ------------------------------------------------------------------

    /// Busy-indicator hook: PRo3D.GIS cannot reference the viewer, so the viewer
    /// registers Busy.scopeBackground here at start (a mask for an AFC frame takes
    /// tens of milliseconds, on a render thread).
    let mutable private busy : string -> (unit -> Option<PixImage>) -> Option<PixImage> = fun _ f -> f ()
    let registerBusy (f : string -> (unit -> Option<PixImage>) -> Option<PixImage>) = busy <- f

    let private cacheLock = obj ()
    // bounded: 24 masks of an AFC frame (RGBA, 1024^2) ~ 100 MB; a full clear is
    // crude but correct, a miss just computes again
    let private masks = Dictionary<MaskKey, Option<PixImage>>()
    // the last decoded bands, so dragging a tone or radius slider does not decode
    // the image again for every step
    let private decoded = Dictionary<string * int, Option<PixImage>>()

    /// The masks for `key`, computed on a miss. Thread-safe: the preview and the
    /// surface stack render on different threads; one lock also keeps their image
    /// decodes apart.
    let tryMask (key : MaskKey) : Option<PixImage> =
        lock cacheLock (fun () ->
            match masks.TryGetValue key with
            | true, m -> m
            | _ ->
                let m =
                    busy "adjusting shadows/highlights" (fun () ->
                        try
                            let band =
                                match decoded.TryGetValue ((key.path, key.channel)) with
                                | true, b -> b
                                | _ ->
                                    if decoded.Count >= 8 then decoded.Clear()
                                    let b = PRo3D.InstrumentProjection.Visualization.decodeBand key.path key.channel
                                    decoded.[(key.path, key.channel)] <- b
                                    b
                            band |> Option.bind (computeMasks key)
                        with e ->
                            Log.warn "[ShadowsHighlights] no masks for %s: %s" key.path e.Message
                            None)
                if masks.Count >= 24 then masks.Clear()
                masks.[key] <- m
                m)

    /// The key of an image's masks, None while both amounts are 0 (nothing to do).
    let maskKey (path : string) (channel : int) (minMax : V2d) (dataType : int) (shadows : ToneAdjustment) (highlights : ToneAdjustment) : Option<MaskKey> =
        if ToneAdjustment.isActive shadows || ToneAdjustment.isActive highlights then
            Some {
                path            = path
                channel         = channel
                minMax          = minMax
                dataType        = dataType
                shadowTone      = shadows.tone.value
                shadowRadius    = shadows.radius.value
                highlightTone   = highlights.tone.value
                highlightRadius = highlights.radius.value
            }
        else None

    /// The masks of the projection stack, a texture-array slot per distinct mask; layer
    /// i samples slot slices.[i]. A layer without adjustment (None) gets a black slot
    /// the shader never reads (its amounts are 0).
    let createMaskArray (runtime : ITextureRuntime) (keys : aval<array<Option<MaskKey>>>) =
        PRo3D.InstrumentProjection.Visualization.createSlotTextureArray
            "ProjectedStackMasks" runtime (Option.bind tryMask) keys
