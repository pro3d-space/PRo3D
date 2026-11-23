namespace PRo3D.GIS

open System
open System.IO
open System.Text

open Aardvark.Base
open Aardvark.PixImage.LibTiff
open PRo3D.InstrumentData

/// Thumbnails of the projected-image library, for the image list's Preview column
/// (docs/MultiImageProjection.md). Served by the viewer at `thumbnailPath` (Program.fs),
/// like the catalog's overviews (StacSearch.overviewPath): rendered on the web server's
/// request thread on first request, then cached in %TEMP%\PRo3D\thumbnails. Only
/// registered library images are served (ProjectedImageApp.loadFile registers them),
/// so the route cannot read arbitrary files.
module LibraryThumbnails =

    /// Edge length of a thumbnail, pixels.
    [<Literal>]
    let size = 64

    [<Literal>]
    let route = "/library-thumbnail/"

    let private registered = System.Collections.Concurrent.ConcurrentDictionary<string, string>()

    let private keyOf (path : string) =
        use sha = Security.Cryptography.SHA256.Create()
        sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath path))
        |> Array.take 12
        |> Array.map (fun b -> b.ToString "x2")
        |> String.concat ""

    /// Allow the route to serve this image's thumbnail.
    let register (path : string) =
        registered.[keyOf path] <- Path.GetFullPath path

    /// The URL of an image's thumbnail.
    let thumbnailPath (path : string) = route + keyOf path

    let cacheDirectory = Path.Combine(Path.GetTempPath(), "PRo3D", "thumbnails")

    /// Grey values sampled (nearest) onto a size x size grid, the image's aspect kept.
    let private sampleGrey (width : int) (height : int) (value : int -> int -> float) =
        let scale = float (max width height) / float size
        let w = max 1 (int (float width / scale))
        let h = max 1 (int (float height / scale))
        let samples = Array.zeroCreate<float> (w * h)
        for y in 0 .. h - 1 do
            for x in 0 .. w - 1 do
                let sx = min (width - 1) (int ((float x + 0.5) * scale))
                let sy = min (height - 1) (int ((float y + 0.5) * scale))
                samples.[y * w + x] <- value sx sy
        w, h, samples

    /// Stretched by the 1st and 99th percentile of the finite samples: instrument data
    /// has no fixed range (uint16 counts, float radiances), and a few hot pixels must
    /// not darken the rest.
    let private stretch (w : int) (h : int) (samples : float[]) : PixImage<byte> =
        let finite = samples |> Array.filter Double.IsFinite
        let pi = PixImage<byte>(Col.Format.Gray, V2i(w, h))
        if finite.Length > 0 then
            let sorted = Array.sort finite
            let at (q : float) = sorted.[min (sorted.Length - 1) (int (q * float (sorted.Length - 1)))]
            let lo, hi = at 0.01, at 0.99
            let span = if hi > lo then hi - lo else 1.0
            let mutable channel = pi.GetChannel 0L
            for y in 0 .. h - 1 do
                for x in 0 .. w - 1 do
                    let v = samples.[y * w + x]
                    let t = if Double.IsFinite v then (v - lo) / span else 0.0
                    channel.[int64 x, int64 y] <- byte (255.0 * (clamp 0.0 1.0 t))
        pi

    /// A single-band image's grey value reader, for the pixel types the instrument
    /// readers produce. None for anything else.
    let private greyReader (pi : PixImage) : Option<int -> int -> float> =
        match pi with
        | :? PixImage<float32> as p -> let c = p.GetChannel 0L in Some (fun x y -> float c.[int64 x, int64 y])
        | :? PixImage<uint16> as p -> let c = p.GetChannel 0L in Some (fun x y -> float c.[int64 x, int64 y])
        | :? PixImage<int16> as p -> let c = p.GetChannel 0L in Some (fun x y -> float c.[int64 x, int64 y])
        | :? PixImage<uint32> as p -> let c = p.GetChannel 0L in Some (fun x y -> float c.[int64 x, int64 y])
        | :? PixImage<int32> as p -> let c = p.GetChannel 0L in Some (fun x y -> float c.[int64 x, int64 y])
        | :? PixImage<byte> as p -> let c = p.GetChannel 0L in Some (fun x y -> float c.[int64 x, int64 y])
        | _ -> None

    /// The thumbnail of an image file: the first band of an instrument TIFF, stretched;
    /// a PNG/JPG as it is (RGB). None for other formats or unreadable files.
    let render (path : string) : Option<PixImage<byte>> =
        try
            match (Path.GetExtension path).ToLowerInvariant() with
            | ".tif" | ".tiff" ->
                match MultiBandReader.tryReadMultiBandTiff path false with
                | Result.Ok img ->
                    InstrumentImageTextures.instrumentImageToTexture false img
                    |> Array.tryHead
                    |> Option.bind (fun band ->
                        greyReader band.pi |> Option.map (fun read ->
                            let w, h, samples = sampleGrey band.pi.Size.X band.pi.Size.Y read
                            stretch w h samples))
                | _ -> None
            | ".png" | ".jpg" | ".jpeg" ->
                let rgba = PixImage.Load(path).ToPixImage<byte>(Col.Format.RGBA)
                let scale = float (max rgba.Size.X rgba.Size.Y) / float size
                let w = max 1 (int (float rgba.Size.X / scale))
                let h = max 1 (int (float rgba.Size.Y / scale))
                let out = PixImage<byte>(Col.Format.RGBA, V2i(w, h))
                for c in 0L .. 3L do
                    let src = rgba.GetChannel c
                    let mutable dst = out.GetChannel c
                    for y in 0 .. h - 1 do
                        for x in 0 .. w - 1 do
                            let sx = min (rgba.Size.X - 1) (int ((float x + 0.5) * scale))
                            let sy = min (rgba.Size.Y - 1) (int ((float y + 0.5) * scale))
                            dst.[int64 x, int64 y] <- src.[int64 sx, int64 sy]
                Some out
            | _ -> None
        with e ->
            Log.warn "[thumbnail] %s: %s" path e.Message
            None

    /// The cached PNG for a route key, rendered on first request. Blocks: the web
    /// server calls it per request, off the update thread. None for an unregistered
    /// key, or an image that has no thumbnail.
    let thumbnailFile (key : string) : Option<string> =
        match registered.TryGetValue key with
        | true, path ->
            let file = Path.Combine(cacheDirectory, key + ".png")
            if File.Exists file && File.GetLastWriteTimeUtc file >= File.GetLastWriteTimeUtc path then Some file
            else
                match render path with
                | Some pi ->
                    try
                        Directory.CreateDirectory cacheDirectory |> ignore
                        // via a temp file: two requests rendering at once must not leave
                        // a half-written PNG
                        let tmp = file + "." + string (Guid.NewGuid()) + ".png"
                        pi.Save tmp
                        File.Move(tmp, file, true)
                        Some file
                    with e ->
                        Log.warn "[thumbnail] could not write %s: %s" file e.Message
                        None
                | None -> None
        | _ -> None
