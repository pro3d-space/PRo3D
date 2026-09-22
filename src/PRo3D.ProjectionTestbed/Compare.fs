namespace PRo3D.ProjectionTestbed

open System
open System.IO

open Aardvark.Base
open Aardvark.PixImage.LibTiff
open PRo3D.InstrumentData

/// A single-channel image normalised to 0..1, which is the only form the comparison
/// cares about. The reference tif and the render arrive in wildly different formats
/// (float32 / uint16 / Rgba8), so everything is flattened to this first.
type Gray =
    {
        data   : float[]
        width  : int
        height : int
    }

/// How the reference was oriented before scoring.
type Orientation =
    | AsIs
    | FlipU
    | FlipV
    | FlipUV
    | Transpose

module Compare =

    let private orientations = [ AsIs; FlipU; FlipV; FlipUV; Transpose ]

    let orientationName =
        function
        | AsIs -> "as-is" | FlipU -> "flip-u" | FlipV -> "flip-v"
        | FlipUV -> "flip-uv" | Transpose -> "transpose"

    /// Rescale to 0..1 using the actual min/max. Instrument data has no meaningful
    /// absolute scale here and the render is already display-ranged, so comparing raw
    /// values would measure exposure rather than geometry.
    let private normalise (values : float[]) =
        if values.Length = 0 then values
        else
            let mutable lo = Double.MaxValue
            let mutable hi = Double.MinValue
            for v in values do
                if Double.IsFinite v then
                    if v < lo then lo <- v
                    if v > hi then hi <- v
            if hi - lo < 1e-12 then Array.zeroCreate values.Length
            else values |> Array.map (fun v -> if Double.IsFinite v then (v - lo) / (hi - lo) else 0.0)

    let private bandToFloats (buffers : PixelBuffers) (band : int) : Result<float[], string> =
        let pick (arr : 'a[][]) (conv : 'a -> float) =
            if band < 0 || band >= arr.Length then
                Result.Error (sprintf "channel %d out of range (image has %d bands)" band arr.Length)
            else Ok (arr.[band] |> Array.map conv)
        match buffers with
        | Float32Bands b -> pick b float
        | UInt16Bands b  -> pick b float
        | Int16Bands b   -> pick b float
        | Int32Bands b   -> pick b float
        | UInt32Bands b  -> pick b float

    /// Note the vertical flip.
    ///
    /// TIFF rows run top-down; the framebuffer download does not, so the reference and the
    /// render disagree on row order. Didymos alone is symmetric enough to hide this -- the
    /// silhouette IoU barely moves either way -- which is exactly why it went unnoticed
    /// until Dimorphos was added and gave the frame an off-centre feature. It then showed
    /// up as Dimorphos mirrored in Y about Didymos, with X matching exactly.
    ///
    /// X matching is what rules out a SPICE position sign error: negating the position
    /// would displace both axes. See GetRelState in PRo3D-Extensions, which passes
    /// spkezr_c's target-relative-to-observer state through unnegated.
    let loadReference (path : string) (channel : int) : Result<Gray, string> =
        match MultiBandReader.tryReadMultiBandTiff path false with
        | Result.Error e -> Result.Error (sprintf "could not read %s: %s" path e)
        | Ok r ->
            bandToFloats r.buffers channel
            |> Result.map (fun d -> { data = normalise d; width = r.width; height = r.height })

    /// Rendered frames come back as Rgba8; collapse to luminance.
    let ofPixImage (image : PixImage) : Gray =
        let pi = image.ToPixImage<byte>(Col.Format.RGBA)
        let m = pi.GetMatrix<C4b>()
        let w = int m.Size.X
        let h = int m.Size.Y
        let data = Array.zeroCreate (w * h)
        for y in 0 .. h - 1 do
            for x in 0 .. w - 1 do
                let c = m.[V2l(int64 x, int64 y)]
                data.[y * w + x] <- (float c.R + float c.G + float c.B) / (3.0 * 255.0)
        { data = data; width = w; height = h }

    let private sample (g : Gray) (x : int) (y : int) = g.data.[y * g.width + x]

    let orient (o : Orientation) (g : Gray) : Gray =
        let w, h = g.width, g.height
        let nw, nh = (match o with Transpose -> h, w | _ -> w, h)
        let out = Array.zeroCreate (nw * nh)
        for y in 0 .. nh - 1 do
            for x in 0 .. nw - 1 do
                let sx, sy =
                    match o with
                    | AsIs      -> x, y
                    | FlipU     -> w - 1 - x, y
                    | FlipV     -> x, h - 1 - y
                    | FlipUV    -> w - 1 - x, h - 1 - y
                    | Transpose -> y, x
                out.[y * nw + x] <- sample g sx sy
        { data = out; width = nw; height = nh }

    /// Nearest-neighbour resample. Deliberately not interpolating: this compares
    /// silhouettes and gross registration, and blurring would flatter a bad match.
    let resampleTo (w : int) (h : int) (g : Gray) : Gray =
        if g.width = w && g.height = h then g
        else
            let out = Array.zeroCreate (w * h)
            for y in 0 .. h - 1 do
                let sy = min (g.height - 1) (y * g.height / h)
                for x in 0 .. w - 1 do
                    let sx = min (g.width - 1) (x * g.width / w)
                    out.[y * w + x] <- sample g sx sy
            { data = out; width = w; height = h }

    /// Normalised cross-correlation in -1..1. Insensitive to brightness and contrast
    /// offsets, which is what we want -- the question is whether structure lines up.
    let ncc (a : Gray) (b : Gray) : float =
        let b = resampleTo a.width a.height b
        let n = float a.data.Length
        if n = 0.0 then 0.0
        else
            let ma = Array.average a.data
            let mb = Array.average b.data
            let mutable num = 0.0
            let mutable da = 0.0
            let mutable db = 0.0
            for i in 0 .. a.data.Length - 1 do
                let x = a.data.[i] - ma
                let y = b.data.[i] - mb
                num <- num + x * y
                da <- da + x * x
                db <- db + y * y
            if da < 1e-12 || db < 1e-12 then 0.0 else num / sqrt (da * db)

    /// Score the render against every orientation of the reference, best first.
    ///
    /// A clean win for one non-`as-is` orientation means a UV convention bug. All of them
    /// scoring near zero means the trafo chain itself is wrong and no flip will fix it --
    /// look at specialTrafos for the instrument, which is Identity and uncalibrated for
    /// ASPECT.
    let sweep (rendered : Gray) (reference : Gray) =
        orientations
        |> List.map (fun o -> o, ncc rendered (orient o reference))
        |> List.sortByDescending snd

    /// NCC restricted to pixels where BOTH images show body.
    ///
    /// For the sun-lit comparison the plain `ncc` above is misleading: both images are
    /// mostly black space, and space agrees with space perfectly, so a large constant
    /// background inflates the correlation regardless of whether any surface structure
    /// matches. Masking to the common body region asks the only question that matters --
    /// do the highlights and shadows fall in the same places? Returns the score and the
    /// number of pixels it was computed over, because a high score over a handful of
    /// pixels means nothing.
    let nccMasked (threshold : float) (a : Gray) (b : Gray) : float * int =
        let b = resampleTo a.width a.height b
        let idx =
            [| for i in 0 .. a.data.Length - 1 do
                 if a.data.[i] > threshold && b.data.[i] > threshold then yield i |]
        if idx.Length < 64 then 0.0, idx.Length
        else
            let ma = idx |> Array.averageBy (fun i -> a.data.[i])
            let mb = idx |> Array.averageBy (fun i -> b.data.[i])
            let mutable num = 0.0
            let mutable da = 0.0
            let mutable db = 0.0
            for i in idx do
                let x = a.data.[i] - ma
                let y = b.data.[i] - mb
                num <- num + x * y
                da <- da + x * x
                db <- db + y * y
            if da <= 0.0 || db <= 0.0 then 0.0, idx.Length
            else num / sqrt (da * db), idx.Length

    let toPixImage (g : Gray) : PixImage<byte> =
        let pi = PixImage<byte>(Col.Format.RGBA, V2i(g.width, g.height))
        let mutable m = pi.GetMatrix<C4b>()
        for y in 0 .. g.height - 1 do
            for x in 0 .. g.width - 1 do
                let v = byte (clamp 0.0 1.0 (sample g x y) * 255.0)
                m.[V2l(int64 x, int64 y)] <- C4b(v, v, v, 255uy)
        pi

    /// Everything above a threshold counts as "body", the rest as space. Instrument
    /// images of a lit body against black separate cleanly, so a fixed fraction of the
    /// range is enough; no need for anything adaptive.
    let mask (threshold : float) (g : Gray) =
        g.data |> Array.map (fun v -> v > threshold)

    /// Intersection over union of the two silhouettes.
    ///
    /// This -- not the NCC -- is the honest number when rendering from the instrument's
    /// own camera. Projecting with matrix M and viewing with the same M puts every texel
    /// at the screen position its texture coordinate already implies, whatever the
    /// geometry, so the disk interior matches by construction and carries no information.
    /// The silhouette is the only place model shape, scale and pointing can disagree.
    let silhouetteIoU (threshold : float) (rendered : Gray) (reference : Gray) =
        let r = resampleTo rendered.width rendered.height reference
        let a = mask threshold rendered
        let b = mask threshold r
        let mutable inter = 0
        let mutable union = 0
        for i in 0 .. a.Length - 1 do
            if a.[i] && b.[i] then inter <- inter + 1
            if a.[i] || b.[i] then union <- union + 1
        if union = 0 then 0.0 else float inter / float union

    /// Centroid of the masked region, in pixels. Comparing render vs reference centroids
    /// separates a pointing error (whole silhouette displaced) from a shape or scale
    /// error (silhouette centred but the wrong size).
    let centroid (threshold : float) (g : Gray) =
        let m = mask threshold g
        let mutable sx, sy, n = 0.0, 0.0, 0
        for y in 0 .. g.height - 1 do
            for x in 0 .. g.width - 1 do
                if m.[y * g.width + x] then
                    sx <- sx + float x
                    sy <- sy + float y
                    n <- n + 1
        if n = 0 then None else Some (V2d(sx / float n, sy / float n), n)

    /// Best-fit alignment of the render's silhouette onto the reference's.
    ///
    /// This is what separates "the model is pointed wrong" from "the model is
    /// incomplete". Both depress the raw IoU and both displace the centroid, so neither
    /// raw number can be attributed on its own. But they respond differently to a search
    /// over rigid transforms:
    ///
    ///   - a genuine pointing error is *removable* -- some (dx, dy) recovers the overlap,
    ///     and IoU jumps sharply at that offset
    ///   - missing coverage is *not* removable -- every translation merely trades
    ///     uncovered reference for uncovered model, so the optimum sits near zero and IoU
    ///     barely improves
    ///
    /// So: large best-fit offset with a large IoU gain => real pointing error.
    /// Near-zero offset with negligible gain => the residual is shape/coverage.
    /// Same logic applies to `scale` for FOV or range errors.
    ///
    /// Coordinate descent (translation, then scale, then translation again) on
    /// downsampled masks; an exhaustive joint search is far too slow and this surface is
    /// smooth enough that it does not need one.
    let bestFitAlignment (threshold : float) (rendered : Gray) (reference : Gray) =
        let ds = 4
        let w, h = rendered.width / ds, rendered.height / ds
        let small (g : Gray) = resampleTo w h g
        let refMask = mask threshold (small (resampleTo rendered.width rendered.height reference))
        let renMask = mask threshold (small rendered)

        // IoU of the render mask shifted by (dx,dy) and scaled about its own centre.
        let scoreAt (dx : int) (dy : int) (scale : float) =
            let cx, cy = float w / 2.0, float h / 2.0
            let mutable inter = 0
            let mutable union = 0
            for y in 0 .. h - 1 do
                for x in 0 .. w - 1 do
                    // sample the render mask at the inverse-transformed location
                    let sx = int (cx + (float (x - dx) - cx) / scale)
                    let sy = int (cy + (float (y - dy) - cy) / scale)
                    let a = sx >= 0 && sx < w && sy >= 0 && sy < h && renMask.[sy * w + sx]
                    let b = refMask.[y * w + x]
                    if a && b then inter <- inter + 1
                    if a || b then union <- union + 1
            if union = 0 then 0.0 else float inter / float union

        let searchTranslation scale =
            let mutable best = (0, 0, scoreAt 0 0 scale)
            for dy in -15 .. 15 do
                for dx in -15 .. 15 do
                    let s = scoreAt dx dy scale
                    let (_, _, bs) = best
                    if s > bs then best <- (dx, dy, s)
            best

        let (dx0, dy0, _) = searchTranslation 1.0
        let mutable bestScale = 1.0
        let mutable bestScore = scoreAt dx0 dy0 1.0
        for i in -20 .. 20 do
            let sc = 1.0 + float i * 0.01
            let s = scoreAt dx0 dy0 sc
            if s > bestScore then bestScore <- s; bestScale <- sc
        let (dx, dy, finalScore) = searchTranslation bestScale
        // scale back up from the downsampled grid
        (float dx * float ds, float dy * float ds, bestScale, finalScore)

    /// Render into red, reference into green. Agreement is yellow; red-only is model
    /// where there is no body, green-only is body the model misses. Misregistration
    /// reads immediately as coloured fringing along the limb.
    let overlay (threshold : float) (rendered : Gray) (reference : Gray) : PixImage<byte> =
        let r = resampleTo rendered.width rendered.height reference
        let pi = PixImage<byte>(Col.Format.RGBA, V2i(rendered.width, rendered.height))
        let mutable m = pi.GetMatrix<C4b>()
        for y in 0 .. rendered.height - 1 do
            for x in 0 .. rendered.width - 1 do
                let a = byte (clamp 0.0 1.0 (sample rendered x y) * 255.0)
                let b = byte (clamp 0.0 1.0 (sample r x y) * 255.0)
                m.[V2l(int64 x, int64 y)] <- C4b(a, b, 0uy, 255uy)
        pi

    /// Binary silhouette agreement: model mask versus reference mask.
    ///
    /// `overlay` above composites raw BRIGHTNESS into two channels (its threshold argument
    /// is not used), so what it shows conflates geometry with photometry: a Lambertian
    /// model darkens towards the limb as cos(i) -> 0 and fades out before the real body
    /// does, which reads as "model too small" whether or not it is. This thresholds both
    /// into masks first, so the picture answers only "is the body in the same place, at
    /// the same size".
    ///
    /// The two thresholds are deliberately different, and that asymmetry is the point.
    /// For a render we know exactly what "geometry is present" means -- anything at or
    /// above the ambient floor -- so it can sit just above background and capture the full
    /// disk regardless of how it is lit. For the real image the only thing separating body
    /// from space is brightness, and it carries a noise floor, so it needs a real one.
    ///
    /// Agreement is drawn dark grey rather than a bright colour: the question this image
    /// exists to answer is where the two DISAGREE, and those should be what catches the eye.
    let silhouetteOverlay (modelThreshold : float) (refThreshold : float)
                          (model : Gray) (reference : Gray) : PixImage<byte> =
        let r = resampleTo model.width model.height reference
        let pi = PixImage<byte>(Col.Format.RGBA, V2i(model.width, model.height))
        let mutable m = pi.GetMatrix<C4b>()
        for y in 0 .. model.height - 1 do
            for x in 0 .. model.width - 1 do
                let inModel = sample model x y > modelThreshold
                let inRef = sample r x y > refThreshold
                let c =
                    match inModel, inRef with
                    | true,  true  -> C4b( 70uy,  70uy,  70uy, 255uy)   // both
                    | true,  false -> C4b(255uy,  40uy,  40uy, 255uy)   // model only
                    | false, true  -> C4b( 40uy, 255uy,  40uy, 255uy)   // reference only
                    | false, false -> C4b(  0uy,   0uy,   0uy, 255uy)
                m.[V2l(int64 x, int64 y)] <- c
        pi

    /// Mask areas and IoU for the same asymmetric-threshold pair as `silhouetteOverlay`.
    /// This is the geometry number; `silhouetteIoU` above thresholds both sides at the same
    /// brightness and therefore is not.
    let silhouetteStats (modelThreshold : float) (refThreshold : float)
                        (model : Gray) (reference : Gray) =
        let r = resampleTo model.width model.height reference
        let mutable nModel = 0
        let mutable nRef = 0
        let mutable inter = 0
        for i in 0 .. model.data.Length - 1 do
            let a = model.data.[i] > modelThreshold
            let b = r.data.[i] > refThreshold
            if a then nModel <- nModel + 1
            if b then nRef <- nRef + 1
            if a && b then inter <- inter + 1
        let union = nModel + nRef - inter
        let iou = if union > 0 then float inter / float union else 0.0
        nModel, nRef, iou

    /// Sub-rectangle of an image, for per-body analysis. Everything outside is dropped
    /// rather than blanked, so masks and areas refer only to the region asked for.
    let crop (x0 : int) (y0 : int) (w : int) (h : int) (g : Gray) : Gray =
        let w = min w (g.width - x0)
        let h = min h (g.height - y0)
        let out = Array.zeroCreate (w * h)
        for y in 0 .. h - 1 do
            for x in 0 .. w - 1 do
                out.[y * w + x] <- sample g (x0 + x) (y0 + y)
        { data = out; width = w; height = h }

    /// Best-fit integer translation of the model mask onto the reference mask.
    ///
    /// Exists to answer "is the apparent offset real, or an artifact of thresholding two
    /// differently-lit images?" A centroid difference cannot distinguish those: eroding one
    /// mask asymmetrically (the reference loses its dark anti-sunward limb) moves the
    /// centroid without anything being displaced. A translation that genuinely improves
    /// mask overlap is much harder to fake.
    ///
    /// Reports the best offset AND the IoU gain. A large gain at a non-zero offset means a
    /// real registration error; a negligible gain means the fringing is thresholding.
    let silhouetteBestFit (modelThreshold : float) (refThreshold : float)
                          (model : Gray) (reference : Gray) (radius : int) =
        let r = resampleTo model.width model.height reference
        let w = model.width
        let h = model.height
        let mMask = model.data |> Array.map (fun v -> v > modelThreshold)
        let rMask = r.data |> Array.map (fun v -> v > refThreshold)
        let iouAt dx dy =
            let mutable inter = 0
            let mutable union = 0
            for y in 0 .. h - 1 do
                let sy = y + dy
                for x in 0 .. w - 1 do
                    let sx = x + dx
                    let a =
                        if sx >= 0 && sx < w && sy >= 0 && sy < h then mMask.[sy * w + sx]
                        else false
                    let b = rMask.[y * w + x]
                    if a && b then inter <- inter + 1
                    if a || b then union <- union + 1
            if union > 0 then float inter / float union else 0.0
        let baseline = iouAt 0 0
        let mutable best = (0, 0, baseline)
        for dy in -radius .. radius do
            for dx in -radius .. radius do
                let s = iouAt dx dy
                let (_, _, bs) = best
                if s > bs then best <- (dx, dy, s)
        let (bdx, bdy, bs) = best
        bdx, bdy, bs, baseline

    /// Render and reference side by side at the render's resolution.
    /// LEFT is the render, RIGHT is the reference -- a separator column is drawn between
    /// them so which is which is not something you have to remember or guess.
    let sideBySide (rendered : Gray) (reference : Gray) : PixImage<byte> =
        let r = resampleTo rendered.width rendered.height reference
        let w = rendered.width * 2
        let h = rendered.height
        let combined = Array.zeroCreate (w * h)
        for y in 0 .. h - 1 do
            for x in 0 .. rendered.width - 1 do
                combined.[y * w + x] <- sample rendered x y
                combined.[y * w + rendered.width + x] <- sample r x y
        toPixImage { data = combined; width = w; height = h }
