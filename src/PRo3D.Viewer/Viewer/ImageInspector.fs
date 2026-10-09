namespace PRo3D.Viewer

open System

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Gis
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.ImageMapping
open PRo3D.InstrumentProjection

/// Image Inspector (EXPERIMENTAL, milestone 1 of docs/dev/shadowEstimation.md): the selected
/// projected image as a 2D panel, wired both ways to the 3D view.
///
/// - hover in 2D: the pixel's camera ray through the image's projector, intersected with the
///   surfaces, marked in 3D (`hoverSg`);
/// - hover in 3D: the main view's surface hit projected into the image, marked in 2D.
///
/// The panel draws the image with panel NDC = projector NDC, up to zoom/pan (ImageView) (a quad sampled at
/// tc = 0.5 + 0.5 * ndc, exactly as the projection shader samples it), so a pointer position
/// in the panel IS the projector NDC: no pixel convention sits between panel and projection.
/// Pixel coordinates are only a read-out.
///
/// Panel id "imageinspector": "instrumentview" is the View Planner's rover camera.
module ImageInspector =

    /// Everything needed to go between the selected image and render space.
    type Context =
        {
            imageId : Guid
            /// projection surface's body-fixed frame -> projector clip space, the same matrix
            /// the projection stack uses for this image
            full    : Trafo3d
            /// projection surface's body-fixed frame -> render space (its placement)
            toWorld : Trafo3d
            /// native image size from the statistics sidecar, when present
            size    : Option<V2i>
            /// unit vector towards the sun at the image's acquisition time, render space
            sun     : Result<V3d, string>
            /// the body centre (body-fixed origin), render space
            centre  : V3d
            /// the image file and band, for edge snapping
            texture : string
            channel : int
        }

    // tryContext runs on every hover: everything it derives from files or SPICE is cached.
    // Only successes are kept -- a projector or sun that failed (kernels not loaded yet) is
    // tried again next time rather than failing for the rest of the session.
    // projectDirect calls SPICE behind a global lock; keyed like ProjectedImagesListHelpers'.
    let private projectorCache =
        System.Collections.Concurrent.ConcurrentDictionary<Guid * ProjectionMethod * (float * float * float) * string * string, Trafo3d>()

    // sun direction per (image, frame, body): SPICE, and constant for an image
    let private sunCache =
        System.Collections.Concurrent.ConcurrentDictionary<Guid * string * string, V3d>()

    /// Look up, or compute and keep only when it succeeded.
    let private cachedOk (cache : System.Collections.Concurrent.ConcurrentDictionary<'k, 'v>) (key : 'k) (compute : unit -> Result<'v, string>) =
        match cache.TryGetValue key with
        | true, v -> Ok v
        | _ ->
            let r = compute ()
            match r with
            | Ok v -> cache.[key] <- v
            | Result.Error _ -> ()
            r

    /// Width and height from a PNG's IHDR chunk -- png frames carry no statistics sidecar.
    let private pngSize (path : string) : Option<V2i> =
        try
            use fs = IO.File.OpenRead path
            let b = Array.zeroCreate<byte> 24
            if fs.Read(b, 0, 24) = 24 && b.[1] = 0x50uy && b.[2] = 0x4Euy && b.[3] = 0x47uy then
                let be (o : int) = (int b.[o] <<< 24) ||| (int b.[o + 1] <<< 16) ||| (int b.[o + 2] <<< 8) ||| int b.[o + 3]
                Some (V2i(be 16, be 20))
            else None
        with _ -> None

    /// Sidecar metadata and image size per texture path. Parsing scans the image folder's
    /// sidecars (and logs for COP or png data), so it must not run per mouse move; a
    /// sidecar does not change during a session.
    let private imageInfoCache =
        System.Collections.Concurrent.ConcurrentDictionary<string, InstrumentMetadata.ParsedMetadata * Option<V2i>>()

    let private imageInfo (path : string) =
        imageInfoCache.GetOrAdd(path, fun _ ->
            let md = InstrumentMetadata.tryParseMetadataForImagePath path
            let size =
                match md with
                | _, Some meta when meta.image_width > 0 && meta.image_height > 0 -> Some (V2i(meta.image_width, meta.image_height))
                | _ -> pngSize path
            md, size)

    /// The surface the projection is placed on: the first one bound to a SPICE body, else one
    /// inheriting the scene body -- the same choice as the fly-to (Viewer.flyToImageCamera).
    let private projectionSurface (gis : Gis.GisApp) (surfaces : SurfaceModel) =
        let bound =
            gis.gisSurfaces
            |> HashMap.toSeq
            |> Seq.tryPick (fun (sid, gs) ->
                match gs.entity, gs.referenceFrame with
                | Some _, Some frame -> Some (sid, frame)
                | _ -> None)
        match bound with
        | Some _ -> bound
        | None ->
            surfaces.surfaces.flat
            |> HashMap.toSeq
            |> Seq.tryPick (fun (sid, _) ->
                Gis.GisApp.getSpiceReferenceSystem gis sid
                |> Option.map (fun r -> sid, r.referenceFrame))

    let tryContext (gis : Gis.GisApp) (surfaces : SurfaceModel) (refSystem : ReferenceSystem) : Result<Context, string> =
        let list = gis.projectedImageList
        let image = list.selectedImage |> Option.bind (fun id -> ProjectedImageListModel.tryFind id list)
        match image, Gis.GisApp.getObserverSystem gis, projectionSurface gis surfaces with
        | None, _, _ -> Result.Error "no image selected"
        | _, None, _ -> Result.Error "no observed body set"
        | _, _, None -> Result.Error "no surface bound to a SPICE body"
        | Some image, Some observerSystem, Some (surfaceId, frame) ->
            let (EntitySpiceName observer) = observerSystem.body
            let b = list.boresightAdjustment
            let boresightKey = (b.roll.value, b.pitch.value, b.yaw.value)
            let key = (image.id, list.projectionMethod, boresightKey, observer, frame.Value)
            let metadata, size = imageInfo image.texture
            let full =
                cachedOk projectorCache key (fun () ->
                    // same boresight composition as ProjectedImagesListHelpers.computeBoresight
                    let boresight =
                        Trafo3d.RotationXInDegrees(b.yaw.value) * Trafo3d.RotationYInDegrees(b.pitch.value) * Trafo3d.RotationZInDegrees(b.roll.value)
                    // "MARS" only as the fallback target, as the viewer passes it
                    match Visualization.projectDirect observer frame.Value metadata "MARS" (Some boresight) list.projectionMethod with
                    | Some t -> Ok t
                    | None -> Result.Error "no projector")
                |> Result.toOption
            let surface =
                surfaces.surfaces.flat |> HashMap.tryFind surfaceId |> Option.map Leaf.toSurface
            match full, surface with
            | None, _ -> Result.Error "the image's projector did not resolve (SPICE coverage?)"
            | _, None -> Result.Error "projection surface not found"
            | Some full, Some surface ->
                let observedSystem = Gis.GisApp.getSpiceReferenceSystem gis surfaceId
                let placement = TransformationApp.fullTrafo' surface.transformation refSystem observedSystem (Some observerSystem)
                let toWorld = placement * surface.preTransform
                // at the image's own time -- never the scene's current SPICE time
                let sunBody =
                    match observedSystem, metadata with
                    | Some r, (Some mbi, _) ->
                        let (EntitySpiceName body) = r.body
                        cachedOk sunCache (image.id, frame.Value, body) (fun () ->
                            PRo3D.SPICE.InstrumentProjection.withSpiceLock (fun () ->
                                InstrumentObservation.sunDirection frame.Value body mbi.obs_date))
                    | None, _ -> Result.Error "the projection surface has no SPICE body"
                    | _ -> Result.Error "the image has no mbi sidecar (no acquisition time)"
                let sun = sunBody |> Result.map (fun d -> toWorld.Forward.TransformDir d |> Vec.normalize)
                Ok { imageId = image.id; full = full; toWorld = toWorld; size = size
                     sun = sun; centre = toWorld.Forward.TransformPos V3d.Zero
                     texture = image.texture; channel = image.selectedChannel.idx }

    /// Camera ray through a projector NDC point, render space.
    let ray (ctx : Context) (ndc : V2d) : Ray3d =
        let near = ctx.full.Backward.TransformPosProj(V3d(ndc.X, ndc.Y, -1.0))
        let far  = ctx.full.Backward.TransformPosProj(V3d(ndc.X, ndc.Y,  1.0))
        let o = ctx.toWorld.Forward.TransformPos near
        let d = ctx.toWorld.Forward.TransformPos far - o
        Ray3d(o, Vec.normalize d)

    /// Render-space point -> projector NDC; None behind the projector or outside the image.
    let project (ctx : Context) (p : V3d) : Option<V2d> =
        let b = ctx.toWorld.Backward.TransformPos p
        let h = ctx.full.Forward.Transform(V4d(b.X, b.Y, b.Z, 1.0))
        if h.W <= 0.0 || not (Double.IsFinite h.W) then None
        else
            let n = V2d(h.X / h.W, h.Y / h.W)
            if abs n.X <= 1.0 && abs n.Y <= 1.0 then Some n else None

    /// Projector NDC -> pixel, 0-based, origin top-left (InstrumentObservation's Image convention).
    let pixelOf (ctx : Context) (ndc : V2d) =
        ctx.size |> Option.map (fun s ->
            InstrumentObservation.ndcToPixel s InstrumentObservation.PixelConvention.Image ndc)

    /// The hover for a pointer position, given a picking function (Picking.pickRay lives
    /// after this file). The instrument frame at the pixel comes from the camera rays of two
    /// neighbouring points: image right and image up, orthogonal to the viewing ray.
    let hover (pick : Ray3d -> Option<V3d>) (ctx : Context) (ndc : V2d) : ImageHover =
        let r = ray ctx ndc
        let d = r.Direction
        let towards (delta : V2d) =
            let v = (ray ctx (ndc + delta)).Direction - d
            Vec.normalize (v - d * Vec.dot v d)
        let right = towards (V2d(1e-4, 0.0))
        let up = Vec.normalize (Vec.cross right d |> fun u -> if Vec.dot u (towards (V2d(0.0, 1e-4))) < 0.0 then -u else u)
        { ndc = ndc; pixel = pixelOf ctx ndc; direction = d; right = right; imageUp = up; hit = pick r }

    /// Hover picking off the UI thread, latest wins: a request replaces any that has not
    /// started yet, so a fast pointer costs one pick per pick-time rather than one per move.
    /// Every request and every cancel advances the request number; a result is applied only
    /// while its number is still the current one, so a pick that finishes after the pointer
    /// moved on or left the panel is dropped.
    module HoverWorker =
        let private gate = obj ()
        let private signal = new System.Threading.SemaphoreSlim(0)
        let mutable private current = 0
        let mutable private pending : Option<int * (unit -> ImageHover) * (int * ImageHover -> unit)> = None
        let mutable private started = false

        let private run () =
            while true do
                signal.Wait()
                let job = lock gate (fun () -> let j = pending in pending <- None; j)
                match job with
                | Some (id, compute, post) ->
                    try post (id, compute ())
                    with e -> Log.warn "[ImageInspector] hover pick failed: %s" e.Message
                | None -> ()

        /// The number a result must carry to be applied.
        let latest () = lock gate (fun () -> current)

        /// Queue a pick; `post` hands its result back to the update loop.
        let request (compute : unit -> ImageHover) (post : int * ImageHover -> unit) =
            lock gate (fun () ->
                if not started then
                    started <- true
                    System.Threading.Thread(run, IsBackground = true, Name = "ImageInspector hover").Start()
                current <- current + 1
                let hadPending = pending.IsSome
                pending <- Some (current, compute, post)
                if not hadPending then signal.Release() |> ignore)

        /// Invalidate whatever is queued or running (the pointer left).
        let cancel () = lock gate (fun () -> current <- current + 1; pending <- None)

    // ---------------------------------------------------------------- shadow measurement

    /// Render-space point -> projector NDC, unbounded (the sun line leaves the image).
    let private projectAny (ctx : Context) (p : V3d) : Option<V2d> =
        let b = ctx.toWorld.Backward.TransformPos p
        let h = ctx.full.Forward.Transform(V4d(b.X, b.Y, b.Z, 1.0))
        if h.W <= 0.0 || not (Double.IsFinite h.W) then None
        else Some (V2d(h.X / h.W, h.Y / h.W))

    /// The direction from the anchor along which the other end lies: away from the sun for a
    /// crater (the shadow falls that way), towards it for a boulder (the top that cast it).
    let private alongSun (kind : ShadowKind) (sun : V3d) =
        match kind with
        | ShadowKind.Height -> sun
        | _ -> -sun

    /// That ray from the anchor, projected into the image. A projected 3D line is a 2D line,
    /// so two points fix it. Origin and unit direction, projector NDC.
    let sunLine (ctx : Context) (kind : ShadowKind) (sun : V3d) (anchor : V3d) : Option<V2d * V2d> =
        let step = 1e-3 * Vec.distance (ray ctx V2d.Zero).Origin anchor
        match projectAny ctx anchor, projectAny ctx (anchor + alongSun kind sun * step) with
        | Some a, Some b when Vec.distance a b > 1e-12 -> Some (a, Vec.normalize (b - a))
        | _ -> None

    /// Nearest point on the (forward) sun line.
    let snap (origin : V2d, dir : V2d) (q : V2d) =
        origin + dir * max 0.0 (Vec.dot (q - origin) dir)

    /// Where the camera ray through `ndc` meets the sun ray from the anchor (closest approach
    /// of the two lines); the parameter along the sun ray must be positive.
    let private triangulate (ctx : Context) (u : V3d) (anchor : V3d) (ndc : V2d) : Option<V3d> =
        let r = ray ctx ndc
        let c = r.Direction
        let w0 = anchor - r.Origin
        let b = Vec.dot u c
        let d = Vec.dot u w0
        let e = Vec.dot c w0
        let denom = 1.0 - b * b
        if denom < 1e-12 then None
        else
            let t = (b * e - d) / denom
            if t > 0.0 then Some (anchor + u * t) else None

    /// Below this phase angle (degrees) camera and sun rays are too close to parallel for the
    /// triangulation to mean much; the read-out says so.
    let lowPhase = 15.0

    /// A mesh offset above this many image pixels means the clicks are not where the shadow's
    /// edges are (with ideal clicks it stays well below a pixel on a rendered test frame).
    let offsetWarnPixels = 0.5

    // ---------------------------------------------------------------- image samples

    /// The selected band of an image as a pixel accessor, 0-based, origin top-left -- the
    /// Image convention the panel and the projection share. Cached per (path, channel).
    let private bandCache = System.Collections.Concurrent.ConcurrentDictionary<string * int, Option<V2i * (int -> int -> float)>>()

    let private accessor (pi : PixImage) : Option<int -> int -> float> =
        let ch (m : Matrix<'a>) (f : 'a -> float) = Some (fun (x : int) (y : int) -> f m.[int64 x, int64 y])
        match pi with
        | :? PixImage<byte> as p -> ch (p.GetChannel 0L) float
        | :? PixImage<uint16> as p -> ch (p.GetChannel 0L) float
        | :? PixImage<int16> as p -> ch (p.GetChannel 0L) float
        | :? PixImage<uint32> as p -> ch (p.GetChannel 0L) float
        | :? PixImage<int32> as p -> ch (p.GetChannel 0L) float
        | :? PixImage<float32> as p -> ch (p.GetChannel 0L) float
        | _ -> None

    let band (path : string) (channel : int) : Option<V2i * (int -> int -> float)> =
        match bandCache.TryGetValue((path, channel)) with
        | true, b -> b
        | _ ->
        let b =
            try
                let pi =
                    match IO.Path.GetExtension(path).ToLowerInvariant() with
                    | ".tif" | ".tiff" ->
                        match Aardvark.PixImage.LibTiff.MultiBandReader.tryReadMultiBandTiff path false with
                        | Ok img ->
                            PRo3D.InstrumentData.InstrumentImageTextures.instrumentImageToTexture false img
                            |> Array.tryItem channel |> Option.map (fun b -> b.pi)
                        | Result.Error _ -> None
                    | ".png" | ".jpg" | ".jpeg" -> Some (PixImage.Load path)
                    | _ -> None
                pi |> Option.bind (fun pi -> accessor pi |> Option.map (fun f -> pi.Size, f))
            with e ->
                Log.warn "[ImageInspector] cannot read %s for edge snapping: %s" path e.Message
                None
        if b.IsSome then bandCache.[(path, channel)] <- b
        b

    /// Bilinear sample at a continuous pixel coordinate (integers are pixel centres).
    let private sampleAt (size : V2i, f : int -> int -> float) (p : V2d) =
        let x = clamp 0.0 (float size.X - 1.001) p.X
        let y = clamp 0.0 (float size.Y - 1.001) p.Y
        let x0, y0 = int (floor x), int (floor y)
        let fx, fy = x - float x0, y - float y0
        let v (i : int) (j : int) = f (min (size.X - 1) i) (min (size.Y - 1) j)
        (v x0 y0 * (1.0 - fx) + v (x0 + 1) y0 * fx) * (1.0 - fy) + (v x0 (y0 + 1) * (1.0 - fx) + v (x0 + 1) (y0 + 1) * fx) * fy

    /// Edge snapping: moves a click along `dir` (projector NDC, the direction the shadow
    /// falls) onto the nearest shadow edge -- `entering` lit -> dark, else dark -> lit -- within
    /// `radius` image pixels, sub-pixel. None when there is no clear edge there.
    ///
    /// Where on the edge: entering the shadow (a crater rim) the brightness falls GRADUALLY
    /// over several pixels as the rounded crest turns away from the sun; the point that casts
    /// the shadow is where the sun grazes the crest, i.e. where the ramp reaches the dark
    /// level -- not halfway down it (measured on a rendered Didymos frame: halfway sits 2.5 px
    /// early, 2.6 m of depth at that light). Leaving the shadow (its cast end) is a sharp
    /// edge; there the halfway crossing is right.
    let snapToEdge (img : V2i * (int -> int -> float)) (ctx : Context) (entering : bool) (radius : float) (ndc : V2d) (dir : V2d) : Option<V2d> =
        match ctx.size with
        | None -> None
        | Some size ->
            let conv = InstrumentObservation.PixelConvention.Image
            let p0 = InstrumentObservation.ndcToPixel size conv ndc
            // NDC y is up, pixel y is down
            let d = Vec.normalize (V2d(dir.X * float size.X, -dir.Y * float size.Y))
            let step = 0.25
            let n = int (radius / step)
            let ts = [| for i in -n .. n -> float i * step |]
            // light smoothing along the line: a 1 px box
            let profile = ts |> Array.map (fun t -> (sampleAt img (p0 + d * (t - 0.5)) + sampleAt img (p0 + d * t) + sampleAt img (p0 + d * (t + 0.5))) / 3.0)
            let range = Array.max profile - Array.min profile
            if range < 1e-9 then None
            else
                // signed slope along the shadow direction; entering the shadow = negative
                let slope = Array.init ts.Length (fun i ->
                    let a = profile.[max 0 (i - 2)]
                    let b = profile.[min (ts.Length - 1) (i + 2)]
                    (b - a) * (if entering then -1.0 else 1.0))
                let strongest = Array.max slope
                if strongest < 0.15 * range then None
                else
                    // the strong edge nearest the click
                    let candidates = [| for i in 0 .. ts.Length - 1 do if slope.[i] >= 0.6 * strongest then yield i |]
                    let i = candidates |> Array.minBy (fun i -> abs ts.[i])
                    let window = int (2.0 / step)
                    let crossing (level : float) (k : int) =
                        let a, b = profile.[k - 1] - level, profile.[k] - level
                        if a = 0.0 then Some ts.[k - 1]
                        elif a * b < 0.0 then Some (ts.[k - 1] + step * (a / (a - b)))
                        else None
                    let t =
                        if entering then
                            // a gradual ramp: levels from wider windows (4 px lit side, 5 px
                            // into the shadow), then the FIRST point after the steepest one
                            // where the ramp has come down to the dark level
                            let lit = Array.max profile.[max 0 (i - int (4.0 / step)) .. i]
                            let dark = Array.min profile.[i .. min (ts.Length - 1) (i + int (5.0 / step))]
                            let level = dark + 0.1 * (lit - dark)
                            [| i + 1 .. min (ts.Length - 1) (i + int (5.0 / step)) |]
                            |> Array.tryPick (crossing level)
                            |> Option.defaultValue ts.[i]
                        else
                            // a sharp (cast) edge: halfway, the crossing nearest the steepest point
                            let lit = Array.max profile.[i .. min (ts.Length - 1) (i + window)]
                            let dark = Array.min profile.[max 0 (i - window) .. i]
                            let level = 0.5 * (lit + dark)
                            let cs = [| max 1 (i - window) .. min (ts.Length - 1) (i + window) |] |> Array.choose (crossing level)
                            if cs.Length = 0 then ts.[i] else cs |> Array.minBy (fun c -> abs (c - ts.[i]))
                    Some (InstrumentObservation.pixelToNdc size conv (p0 + d * t))

    /// Image pixels between two projector NDC points.
    let private pixelDistance (ctx : Context) (a : V2d) (b : V2d) =
        ctx.size |> Option.map (fun s -> Vec.length (V2d((b.X - a.X) * float s.X, (b.Y - a.Y) * float s.Y) * 0.5))

    // ---------------------------------------------------------------- reference planes

    /// The local surface normal at a pixel: mesh hits of a ring of pixels around it, normal by
    /// Newell's method (robust for a near-planar ring), oriented away from the body centre.
    /// Too small a ring follows the crater wall at the rim and can tilt the sun below the
    /// "horizon"; 24 pixels follows the surrounding terrain.
    let localNormal (pick : Ray3d -> Option<V3d>) (ctx : Context) (ndc : V2d) : Option<V3d> =
        let radius =
            match ctx.size with
            | Some s -> 2.0 * 24.0 / float s.X
            | None -> 0.045
        let ring =
            [| for i in 0 .. 15 ->
                let a = 2.0 * Math.PI * float i / 16.0
                pick (ray ctx (ndc + V2d(cos a, sin a) * radius)) |]
            |> Array.choose id
        if ring.Length < 8 then None
        else
            let mutable n = V3d.Zero
            for i in 0 .. ring.Length - 1 do
                let p, q = ring.[i], ring.[(i + 1) % ring.Length]
                n <- n + V3d((p.Y - q.Y) * (p.Z + q.Z), (p.Z - q.Z) * (p.X + q.X), (p.X - q.X) * (p.Y + q.Y))
            if n.Length < 1e-12 then None
            else
                let n = Vec.normalize n
                let c = (ring |> Array.fold (+) V3d.Zero) / float ring.Length
                Some (if Vec.dot n (c - ctx.centre) < 0.0 then -n else n)

    /// The normal of the plane through clicked points (at least 3): Newell's method over the
    /// points in angular order around their centroid, so the click order does not matter.
    let planeNormal (ctx : Context) (points : list<V3d>) : Option<V3d> =
        let ps = List.toArray points
        if ps.Length < 3 then None
        else
            let c = (ps |> Array.fold (+) V3d.Zero) / float ps.Length
            let radial = Vec.normalize (c - ctx.centre)
            let e1 = Vec.normalize (Vec.cross radial (if abs radial.Z < 0.9 then V3d.OOI else V3d.IOO))
            let e2 = Vec.cross radial e1
            let ordered = ps |> Array.sortBy (fun p -> atan2 (Vec.dot (p - c) e2) (Vec.dot (p - c) e1))
            let mutable n = V3d.Zero
            for i in 0 .. ordered.Length - 1 do
                let p, q = ordered.[i], ordered.[(i + 1) % ordered.Length]
                n <- n + V3d((p.Y - q.Y) * (p.Z + q.Z), (p.Z - q.Z) * (p.X + q.X), (p.X - q.X) * (p.Y + q.Y))
            if n.Length < 1e-12 then None
            else
                let n = Vec.normalize n
                Some (if Vec.dot n radial < 0.0 then -n else n)

    // ---------------------------------------------------------------- the measurement

    /// The measurement for an anchor (mesh) and the other end's (snapped) pixel.
    let measure (pick : Ray3d -> Option<V3d>) (ctx : Context) (s : ShadowMeasure) (anchorNdc : V2d) (anchor : V3d) (ndc : V2d) : Result<ShadowResult, string> =
        match ctx.sun with
        | Result.Error e -> Result.Error e
        | Ok sun ->
            let radial = Vec.normalize (anchor - ctx.centre)
            let up =
                match s.upMode with
                | ShadowUp.Radial -> Ok radial
                | ShadowUp.Plane ->
                    let missing = 3 - (1 + List.length s.planePoints)
                    if missing > 0 then
                        Result.Error (sprintf "click %d more point%s on the %s to define the reference plane"
                                        missing (if missing > 1 then "s" else "") (if s.kind = ShadowKind.Height then "ground around the boulder" else "crater rim"))
                    else
                        match planeNormal ctx (anchor :: (s.planePoints |> List.map snd)) with
                        | Some n -> Ok n
                        | None -> Result.Error "the plane points are in a line -- click points further apart"
                | _ ->
                    match localNormal pick ctx anchorNdc with
                    | Some n -> Ok n
                    | None -> Result.Error "no local surface around the first click -- try another up"
            match up with
            | Result.Error e -> Result.Error e
            | Ok up ->
            let deg (r : float) = r * Constant.DegreesPerRadian
            let elevation = asin (clamp -1.0 1.0 (Vec.dot sun up))
            let horizontalSun = sun - up * Vec.dot sun up
            if elevation <= 0.0 || horizontalSun.Length < 1e-9 then
                Result.Error (sprintf "the sun is %.1f° below this horizon -- try another up" (-deg elevation))
            else
                let u = alongSun s.kind sun
                match triangulate ctx u anchor ndc with
                | None ->
                    match s.kind with
                    | ShadowKind.Height -> Result.Error "the top must lie towards the sun from the shadow tip"
                    | _ -> Result.Error "the tip must lie on the shadow side of the rim"
                | Some p ->
                    // positive: depth below the rim, or height above the ground
                    let valueOf (q : V3d) = abs (Vec.dot (q - anchor) up)
                    let value = valueOf p
                    let horizontal = (p - anchor) - up * Vec.dot (p - anchor) up
                    // one pixel further along the sun line
                    let perPixel =
                        match ctx.size, sunLine ctx s.kind sun anchor with
                        | Some sz, Some (_, dir) ->
                            triangulate ctx u anchor (ndc + dir * (2.0 / float sz.X))
                            |> Option.map (fun q -> abs (valueOf q - value))
                        | _ -> None
                    let r = ray ctx ndc
                    let meshPoint = pick r
                    // ground size of a pixel: the angle between neighbouring pixel rays, at range
                    let gsd =
                        match ctx.size, meshPoint with
                        | Some sz, Some m ->
                            let r1 = ray ctx (ndc + V2d(2.0 / float sz.X, 0.0))
                            let angle = acos (clamp -1.0 1.0 (Vec.dot r.Direction r1.Direction))
                            Some (Vec.distance r.Origin m * angle)
                        | _ -> None
                    Ok {
                        point = p
                        meshPoint = meshPoint
                        bottom = (match s.kind with ShadowKind.Height -> anchor | _ -> p)
                        up = up
                        sunAzimuth = Vec.normalize horizontalSun
                        value = value
                        length = Vec.length horizontal
                        sunElevation = deg elevation
                        radialElevation = deg (asin (clamp -1.0 1.0 (Vec.dot sun radial)))
                        tilt = deg (acos (clamp -1.0 1.0 (Vec.dot up radial)))
                        gsd = gsd
                        phase = deg (acos (clamp -1.0 1.0 (Vec.dot sun (Vec.normalize (r.Origin - anchor)))))
                        perPixel = perPixel
                    }

    /// A measurement click.
    /// - first: the anchor (crater rim / boulder shadow tip), snapped along the shadow direction
    ///   onto the shadow's edge, then picked on the mesh;
    /// - second: the other end, onto the sun line, then (crater) onto the shadow's end;
    /// - further: with Up: plane, points of the reference plane; otherwise a new measurement.
    let click (pick : Ray3d -> Option<V3d>) (ctx : Context) (s : ShadowMeasure) (ndc : V2d) : ShadowMeasure =
        let image = if s.snap then band ctx.texture ctx.channel else None
        let shadowDir (at : V3d) =
            ctx.sun |> Result.toOption |> Option.bind (fun sun -> sunLine ctx ShadowKind.Depth sun at) |> Option.map snd
        // clicks belong to one image: another selected image starts a new measurement
        let s =
            if s.imageId = Some ctx.imageId then s
            else { s with anchor = None; second = None; anchorSnap = None; secondSnap = None; planePoints = []
                          result = None; secondOk = false; anchorUp = None; imageId = Some ctx.imageId }
        let setAnchor () =
            match pick (ray ctx ndc) with
            | None ->
                { s with anchor = None; second = None; anchorSnap = None; secondSnap = None; planePoints = []; secondOk = false
                         anchorUp = None; result = Some (Result.Error "that pixel does not hit the surface") }
            | Some p ->
                // crater: the rim, where the shadow begins (lit -> dark along the shadow);
                // boulder: the shadow's tip on the ground, where it ends (dark -> lit)
                let snapped =
                    match image, shadowDir p with
                    | Some img, Some dir -> snapToEdge img ctx (s.kind <> ShadowKind.Height) 6.0 ndc dir
                    | _ -> None
                let anchorNdc, anchor =
                    match snapped |> Option.bind (fun q -> pick (ray ctx q) |> Option.map (fun a -> q, a)) with
                    | Some (q, a) -> q, a
                    | None -> ndc, p
                { s with anchor = Some (anchorNdc, anchor); second = None; planePoints = []; result = None; secondOk = false
                         anchorUp = Some (Vec.normalize (anchor - ctx.centre))
                         anchorSnap = snapped |> Option.bind (fun q -> pixelDistance ctx ndc q); secondSnap = None }
        match s.anchor, s.second with
        | Some (anchorNdc, anchor), None ->
            match ctx.sun |> Result.map (fun sun -> sunLine ctx s.kind sun anchor) with
            | Ok (Some line) ->
                let q = snap line ndc
                // crater: the shadow's end (dark -> lit along the line); a boulder's top edge
                // has no reliable image edge, it stays where clicked
                let snapped =
                    match image, s.kind with
                    | Some img, ShadowKind.Depth -> snapToEdge img ctx false 6.0 q (snd line) |> Option.map (snap line)
                    | _ -> None
                let q' = defaultArg snapped q
                // on the line and triangulating: only then may further clicks be plane points
                let ok =
                    match ctx.sun with
                    | Ok sun -> (triangulate ctx (alongSun s.kind sun) anchor q').IsSome
                    | Result.Error _ -> false
                { s with second = Some q'; secondOk = ok; secondSnap = snapped |> Option.bind (fun x -> pixelDistance ctx q x)
                         result = Some (measure pick ctx s anchorNdc anchor q') }
            // a failed second click still ends the attempt: the next click starts over
            | Ok None -> { s with second = Some ndc; result = Some (Result.Error "no sun line in this image") }
            | Result.Error e -> { s with second = Some ndc; result = Some (Result.Error e) }
        | Some (anchorNdc, anchor), Some q when s.upMode = ShadowUp.Plane && s.secondOk ->
            match pick (ray ctx ndc) with
            | Some p ->
                let s = { s with planePoints = s.planePoints @ [ ndc, p ] }
                { s with result = Some (measure pick ctx s anchorNdc anchor q) }
            | None -> { s with result = Some (Result.Error "that plane point misses the surface -- click on the terrain") }
        | _ -> setAnchor ()

    /// Recompute a finished measurement (after the up choice changed).
    let remeasure (pick : Ray3d -> Option<V3d>) (ctx : Context) (s : ShadowMeasure) : ShadowMeasure =
        match s.anchor, s.second with
        | Some (anchorNdc, anchor), Some q -> { s with result = Some (measure pick ctx s anchorNdc anchor q) }
        | _ -> s

    // ---------------------------------------------------------------- 3D markers

    /// A unit tube: radius 1, along +Z from 0 to 1, with outward normals. Shared by every
    /// marker; each instance is placed by a trafo, so the vertex data stays small and local.
    let private unitTube =
        lazy (
            let n = 12
            let ring (z : float32) = Array.init n (fun i ->
                let a = 2.0 * Math.PI * float i / float n
                V3f(float32 (cos a), float32 (sin a), z))
            let bottom, top = ring 0.0f, ring 1.0f
            let positions = ResizeArray<V3f>()
            let normals = ResizeArray<V3f>()
            for i in 0 .. n - 1 do
                let j = (i + 1) % n
                let ni, nj = V3f(bottom.[i].X, bottom.[i].Y, 0.0f), V3f(bottom.[j].X, bottom.[j].Y, 0.0f)
                for (p, nrm) in [ bottom.[i], ni; bottom.[j], nj; top.[j], nj; bottom.[i], ni; top.[j], nj; top.[i], ni ] do
                    positions.Add p
                    normals.Add nrm
            IndexedGeometry(
                Mode = IndexedGeometryMode.TriangleList,
                IndexedAttributes = SymDict.ofList [
                    DefaultSemantic.Positions, positions.ToArray() :> Array
                    DefaultSemantic.Normals, normals.ToArray() :> Array
                ]))

    /// One tube from a to b, `radius` times the distance to the camera thick (so it keeps its
    /// screen size); hidden while `segment` is None. Geometry is built once; only the trafo
    /// follows the segment and the camera, composed in double precision.
    let private tube (colour : C4b) (radius : float) (segment : aval<Option<V3d * V3d>>) (view : aval<CameraView>) : ISg<'msg> =
        let trafo =
            (segment, view) ||> AVal.map2 (fun seg view ->
                match seg with
                | Some (a, b) when Vec.distance a b > 1e-9 ->
                    let d = b - a
                    let r = radius * Vec.distance view.Location ((a + b) * 0.5)
                    Trafo3d.Scale(r, r, d.Length) * Trafo3d.RotateInto(V3d.OOI, Vec.normalize d) * Trafo3d.Translation a
                | _ -> Trafo3d.Scale 0.0)
        Sg.ofIndexedGeometry unitTube.Value
        |> Sg.shader {
            do! DefaultSurfaces.stableTrafo
            do! DefaultSurfaces.constantColor (colour.ToC4f())
            do! DefaultSurfaces.stableHeadlight
        }
        |> Sg.trafo trafo
        |> Sg.onOff (segment |> AVal.map Option.isSome)
        |> Sg.noEvents

    /// The size of a marker arm at p: a fixed fraction of the camera distance.
    let private armAt (view : CameraView) (p : V3d) = 0.002 * Vec.distance view.Location p

    /// A cross of three tubes through p along the given frame's axes (red, green, blue).
    let private cross (frame : aval<Option<V3d * V3d * V3d * V3d>>) (view : aval<CameraView>) : list<ISg<'msg>> =
        let arm (pick : V3d * V3d * V3d -> V3d) =
            (frame, view) ||> AVal.map2 (fun f view ->
                f |> Option.map (fun (p, x, y, z) ->
                    let a = pick (x, y, z) * armAt view p
                    p - a, p + a))
        [
            tube C4b.Red   0.00015 (arm (fun (x, _, _) -> x)) view
            tube C4b.Green 0.00015 (arm (fun (_, y, _) -> y)) view
            tube C4b.Blue  0.00015 (arm (fun (_, _, z) -> z)) view
        ]

    /// Hover marker, depth-tested real geometry at the surface hit, in the INSTRUMENT frame:
    /// red = image right, green = image up, yellow = the camera ray (the viewing axis)
    /// leading to the hit.
    let hoverSg (hover : aval<Option<ImageHover>>) (view : aval<CameraView>) : ISg<'msg> =
        let hit = hover |> AVal.map (Option.bind (fun h -> h.hit |> Option.map (fun p -> p, h)))
        let arm (f : ImageHover -> V3d) =
            (hit, view) ||> AVal.map2 (fun hit view ->
                hit |> Option.map (fun (p, h) -> let a = f h * armAt view p in p - a, p + a))
        let rayStretch =
            (hit, view) ||> AVal.map2 (fun hit view ->
                hit |> Option.map (fun (p, h) -> p - h.direction * (5.0 * armAt view p), p))
        Sg.ofList [
            tube C4b.Red    0.00015 (arm (fun h -> h.right)) view
            tube C4b.Green  0.00015 (arm (fun h -> h.imageUp)) view
            tube C4b.Yellow 0.0001 rayStretch view
        ]

    /// Measurement, depth-tested real geometry. Crosses in the LOCAL SURFACE frame
    /// (red = horizontal towards the sun, green = across, blue = up) at the anchor and at the
    /// triangulated end; yellow = the sun ray between them; cyan = the measured depth/height,
    /// vertical from the lower end; gray = where the mesh is under the triangulated end.
    let measureSg (measurement : aval<ShadowMeasure>) (selected : aval<Option<Guid>>) (view : aval<CameraView>) : ISg<'msg> =
        // nothing while Measure is off or another image is selected
        let s =
            (measurement, selected) ||> AVal.map2 (fun s sel ->
                if s.active && s.imageId.IsSome && s.imageId = sel then s
                else { s with anchor = None; result = None })
        let ok = s |> AVal.map (fun s ->
            match s.anchor, s.result with
            | Some (_, a), Some (Ok r) -> Some (a, r)
            | _ -> None)
        let localFrame (p : V3d) (r : ShadowResult) = (p, r.sunAzimuth, Vec.cross r.up r.sunAzimuth, r.up)
        // before the second click only the anchor shows, in a radial frame
        let anchorFrame =
            s |> AVal.map (fun s ->
                match s.anchor, s.result with
                | Some (_, a), Some (Ok r) -> Some (localFrame a r)
                | Some (_, a), _ ->
                    let up = s.anchorUp |> Option.defaultValue (Vec.normalize a)
                    let x = Vec.normalize (Vec.cross up (if abs up.Z < 0.9 then V3d.OOI else V3d.IOO))
                    Some (a, x, Vec.cross up x, up)
                | _ -> None)
        let pointFrame = ok |> AVal.map (Option.map (fun (_, r) -> localFrame r.point r))
        let meshFrame =
            ok |> AVal.map (Option.bind (fun (_, r) -> r.meshPoint |> Option.map (fun m -> localFrame m r)))
        let sunRay = ok |> AVal.map (Option.map (fun (a, r) -> a, r.point))
        let vertical = ok |> AVal.map (Option.map (fun (_, r) -> r.bottom, r.bottom + r.up * r.value))
        let meshArms =
            let arm (f : V3d * V3d * V3d -> V3d) =
                (meshFrame, view) ||> AVal.map2 (fun mf view ->
                    mf |> Option.map (fun (p, x, y, z) -> let a = f (x, y, z) * (0.5 * armAt view p) in p - a, p + a))
            [ tube C4b.Gray 0.0001 (arm (fun (x, _, _) -> x)) view
              tube C4b.Gray 0.0001 (arm (fun (_, y, _) -> y)) view
              tube C4b.Gray 0.0001 (arm (fun (_, _, z) -> z)) view ]
        Sg.ofList [
            yield! cross anchorFrame view
            yield! cross pointFrame view
            yield tube C4b.Yellow 0.00012 sunRay view
            yield tube C4b.Cyan 0.0002 vertical view
            yield! meshArms
        ]

    // ---------------------------------------------------------------- 2D panel

    /// Most magnification the panel allows: at AFC's 1020 px, 64x shows ~16 pixels across.
    let maxScale = 64.0

    /// Zoom/pan state for a panel event. Zoom keeps the image point under the cursor fixed;
    /// the centre stays inside the image so the view cannot be lost.
    let updateView (v : ImageView) (msg : ImageInspectorAction) : ImageView =
        let clampCenter (scale : float) (c : V2d) =
            let r = 1.0 - 1.0 / scale
            V2d(clamp -r r c.X, clamp -r r c.Y)
        match msg with
        | ImageInspectorAction.Zoom (steps, at) ->
            let fixedPoint = ImageView.toImage v at
            let scale = clamp 1.0 maxScale (v.scale * Math.Pow(2.0, steps))
            { v with scale = scale; center = clampCenter scale (fixedPoint - at / scale) }
        | ImageInspectorAction.DragStart p -> { v with dragFrom = Some p; dragMoved = false }
        | ImageInspectorAction.DragMove p ->
            match v.dragFrom with
            | Some from ->
                { v with
                    center = clampCenter v.scale (v.center - (p - from) / v.scale)
                    dragFrom = Some p
                    // a jitter is still a click
                    dragMoved = v.dragMoved || Vec.distance p from > 0.01 }
            | None -> v
        // a buttonless move (Hover) also ends a drag: a mouseup lost outside the panel, or
        // swallowed by the render control, must not leave hovering blocked
        | ImageInspectorAction.DragEnd
        | ImageInspectorAction.Hover _ -> { v with dragFrom = None }
        | ImageInspectorAction.ResetView -> ImageView.initial
        | ImageInspectorAction.HoverResult _
        | ImageInspectorAction.Click _
        | ImageInspectorAction.ToggleMeasure
        | ImageInspectorAction.ToggleShadowKind
        | ImageInspectorAction.ToggleShadowUp
        | ImageInspectorAction.ToggleSnap
        | ImageInspectorAction.ClearMeasure
        | ImageInspectorAction.CreateScaleBar -> v

    /// A mouse event carrying the panel NDC of the pointer (and optional extra values).
    /// Every move is sent: the expensive part, picking, runs on the hover worker, which keeps
    /// only the newest request. (Mouse, not pointer events: see the input layer in `view`.)
    let private panelEvent (name : string) (preventDefault : bool) (extra : list<string>)
                           (f : list<string> -> V2d -> Option<ImageInspectorAction>) =
        name, AttributeValue.Event {
            clientSide = fun send src ->
                String.concat ";" [
                    if preventDefault then yield "event.preventDefault()"
                    yield "var rect = this.getBoundingClientRect()"
                    // toFixed: FsPickler reads a V2d component only from a float literal
                    yield send src ([ "{ X: ((event.clientX - rect.left) / rect.width).toFixed(10), Y: ((event.clientY - rect.top) / rect.height).toFixed(10) }" ] @ extra)
                ]
            serverSide = fun _ _ args ->
                match args with
                | rel :: rest ->
                    let r : V2d = Pickler.json.UnPickleOfString rel
                    match f rest (V2d(2.0 * r.X - 1.0, 1.0 - 2.0 * r.Y)) with
                    | Some a -> Seq.singleton (ImageInspectorMessage a)
                    | None -> Seq.empty
                | _ -> Seq.empty
        }

    let private parseFloat (s : string) =
        match Double.TryParse(s.Trim('"'), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
        | true, v -> Some v
        | _ -> None

    module private Shaders =
        open FShade
        open Aardvark.Rendering.Effects

        /// Linear when the image is shrunk, POINT when it is magnified: zoomed in, each image
        /// pixel shows as the flat square it is, not a blend of its neighbours.
        let private instrumentSampler =
            sampler2d {
                texture uniform?InstrumentImage
                filter Filter.MinLinearMagPoint
                addressU WrapMode.Clamp
                addressV WrapMode.Clamp
            }

        let private colormapSampler =
            sampler2d {
                texture uniform?ColormapTexture
                filter Filter.MinMagLinear
                addressU WrapMode.Clamp
                addressV WrapMode.Clamp
            }

        type UniformScope with
            member x.InspectorMin : float32 = uniform?MinValue
            member x.InspectorMax : float32 = uniform?MaxValue
            member x.InspectorFalseColor : bool = uniform?UseFalseColor
            member x.InspectorDataType : int = uniform?DataType
            /// image size in pixels, for the grid
            member x.InspectorImageSize : V2f = uniform?InspectorImageSize

        /// The same transfer function as ProjectedImageApp.Shaders.hshColors, over the
        /// pixel-exact sampler.
        let colours (v : Vertex) =
            fragment {
                let x = instrumentSampler.Sample(v.tc).X
                let t =
                    if uniform.InspectorDataType = 2 then
                        (x - uniform.InspectorMin) / (uniform.InspectorMax - uniform.InspectorMin)
                    else
                        ((min uniform.InspectorMax (max uniform.InspectorMin (x * 65000.0f))) - uniform.InspectorMin) / (uniform.InspectorMax - uniform.InspectorMin)
                if uniform.InspectorFalseColor then
                    return V4f(t, t, t, 1.0f)
                else
                    return colormapSampler.Sample(V2f(t, 0.0f))
            }

        /// Light, thin borders between image pixels, faded in once a pixel is more than about
        /// 5 screen pixels wide -- so the grid shows pixel precision only where it can be seen.
        let pixelGrid (v : Vertex) =
            fragment {
                let p = v.tc * uniform.InspectorImageSize
                let w = V2f(abs (ddx p.X) + abs (ddy p.X), abs (ddx p.Y) + abs (ddy p.Y))
                // distance to the nearest pixel border, in screen pixels
                let dx = abs (p.X - floor (p.X + 0.5f)) / max w.X 1e-6f
                let dy = abs (p.Y - floor (p.Y + 0.5f)) / max w.Y 1e-6f
                let line = clamp 0.0f 1.0f (1.0f - min dx dy)
                // screen pixels per image pixel
                let zoom = 1.0f / max (max w.X w.Y) 1e-6f
                let fade = clamp 0.0f 1.0f ((zoom - 5.0f) / 5.0f)
                let a = 0.22f * line * fade
                return V4f(v.c.XYZ * (1.0f - a) + V3f(1.0f, 1.0f, 1.0f) * a, 1.0f)
            }

    let private imageSg (m : AdaptiveModel) : ISg<ViewerAction> =
        let list = m.scene.gisApp.projectedImageList
        let img = PRo3D.GIS.ProjectedImagesListAppHelper.getSelectedImage list
        let texture =
            Visualization.createProjectedTexture
                (PRo3D.GIS.ProjectedImagesListAppHelper.getSelectedTexture list)
                (PRo3D.GIS.ProjectedImagesListAppHelper.getSelectedImageChannel list)
        let extract' (d : aval<'a>) (f : AdaptiveProjectedImageModel -> aval<'a>) =
            img |> AVal.bind (function None -> d | Some i -> f i)
        let extract (d : 'a) f = extract' (AVal.constant d) f
        let imageSize =
            texture |> AVal.map (fun t ->
                match t with
                | :? PixTexture2d as p ->
                    match p.PixImageMipMap.ImageArray |> Array.tryHead with
                    | Some pi -> V2f(float32 pi.Size.X, float32 pi.Size.Y)
                    | None -> V2f.II
                | _ -> V2f.II)
        let colormap =
            extract' DefaultTextures.checkerboard (fun i ->
                i.colorMap |> AVal.map (ColorMap.getColorMapFileName >> PRo3D.InstrumentVisualization.InstrumentImageVisualization.getColorMapTexture))
        // same transfer function as the "Selected Image" preview (ProjectedImageApp); the
        // quad spans projector NDC [-1,1]^2 and the camera (zoom/pan) picks the visible part
        Sg.fullScreenQuad
        |> Sg.noEvents
        |> Sg.texture "InstrumentImage" texture
        |> Sg.texture "ColormapTexture" colormap
        |> Sg.uniform "MinValue" (extract 0.0 (fun i -> i.falseColorModel.lowerBound.value))
        |> Sg.uniform "MaxValue" (extract 1.0 (fun i -> i.falseColorModel.upperBound.value))
        |> Sg.uniform "UseFalseColor" (extract false (fun i -> i.falseColorPreview |> AVal.map not))
        |> Sg.uniform "DataType" (extract 2 (fun i -> i.dataType |> AVal.map int))
        |> Sg.uniform "InspectorImageSize" imageSize
        |> Sg.shader {
            do! DefaultSurfaces.trafo
            do! Shaders.colours
            do! Shaders.pixelGrid
        }

    /// Crosshair at a panel NDC, as an overlay in percent of the panel; None outside it.
    let private crosshair (colour : string) (panel : V2d) =
        if abs panel.X > 1.0 || abs panel.Y > 1.0 then None
        else
            let x = 50.0 * (panel.X + 1.0)
            let y = 50.0 * (1.0 - panel.Y)
            let line (s : string) = div [ style (sprintf "position:absolute; pointer-events:none; background:%s; %s" colour s) ] []
            Some (
                div [ style "position:absolute; inset:0; pointer-events:none" ] [
                    line (sprintf "left:%.4f%%; top:0; bottom:0; width:1px" x)
                    line (sprintf "top:%.4f%%; left:0; right:0; height:1px" y)
                ])

    /// A marker dot at a panel NDC; None outside the panel.
    let private dot (colour : string) (panel : V2d) =
        if abs panel.X > 1.0 || abs panel.Y > 1.0 then None
        else
            Some (
                div [ style (sprintf "position:absolute; pointer-events:none; left:calc(%.4f%% - 5px); top:calc(%.4f%% - 5px); width:10px; height:10px; border-radius:50%%; border:2px solid %s; box-sizing:border-box"
                                (50.0 * (panel.X + 1.0)) (50.0 * (1.0 - panel.Y)) colour) ] [])

    /// The sun line from `a` along `dir` (panel NDC), drawn far past the panel and clipped.
    let private sunLineSvg (a : V2d) (dir : V2d) =
        let b = a + dir * 8.0
        let px (p : V2d) = 50.0 * (p.X + 1.0)
        let py (p : V2d) = 50.0 * (1.0 - p.Y)
        Svg.svg [ attribute "viewBox" "0 0 100 100"; attribute "preserveAspectRatio" "none"
                  style "position:absolute; inset:0; width:100%; height:100%; pointer-events:none; overflow:hidden" ] [
            Svg.line [
                attribute "x1" (sprintf "%.5f" (px a)); attribute "y1" (sprintf "%.5f" (py a))
                attribute "x2" (sprintf "%.5f" (px b)); attribute "y2" (sprintf "%.5f" (py b))
                attribute "stroke" "#ffd400"; attribute "stroke-width" "1.5"; attribute "stroke-dasharray" "6 4"
                attribute "vector-effect" "non-scaling-stroke"
            ]
        ]

    let private fmt (v : float) = v.ToString("0.0", Globalization.CultureInfo.InvariantCulture)
    let private fmt2 (v : float) = v.ToString("0.00", Globalization.CultureInfo.InvariantCulture)

    let view (m : AdaptiveModel) : DomNode<ViewerAction> =
        // whole-model snapshots: they change rarely compared with the hover, and the projector
        // itself is memoized
        let context =
            (m.scene.gisApp.Current, m.scene.surfacesModel.Current, m.scene.referenceSystem.Current)
            |||> AVal.map3 tryContext

        // 3D -> 2D: the main view's surface hit in this image
        let fromMainView =
            (context, m.surfaceIntersection)
            ||> AVal.map2 (fun ctx hit ->
                match ctx, hit with
                | Ok ctx, Some h -> project ctx h.hitPoint
                | _ -> None)

        // the visible part of projector NDC: [center - 1/scale, center + 1/scale]
        let camera =
            m.imageView |> AVal.map (fun v ->
                let h = 1.0 / v.scale
                let box = Box3d(V3d(v.center.X - h, v.center.Y - h, 0.1), V3d(v.center.X + h, v.center.Y + h, 10.0))
                Camera.create (CameraView.lookAt V3d.OOI V3d.Zero V3d.OIO) (Frustum.ortho box))

        // The pointer handlers sit on a transparent layer above the render control: the panel
        // needs nothing from the render control but the picture (the camera is ours), and its
        // own pointer handling then cannot interfere.
        let inputLayer =
            AttributeMap.ofList [
                style "position:absolute; inset:0; width:100%; height:100%; cursor:crosshair; z-index:2"
                // a move with no button held ends a drag whose mouseup happened outside
                panelEvent "onmousemove" false [ "event.buttons" ] (fun rest p ->
                    match rest with
                    | "0" :: _ -> Some (ImageInspectorAction.Hover (Some p))
                    | _ -> Some (ImageInspectorAction.DragMove p))
                panelEvent "onmousedown" false [] (fun _ p -> Some (ImageInspectorAction.DragStart p))
                panelEvent "onmouseup" false [] (fun _ _ -> Some ImageInspectorAction.DragEnd)
                panelEvent "ondblclick" false [] (fun _ _ -> Some ImageInspectorAction.ResetView)
                panelEvent "onclick" false [] (fun _ p -> Some (ImageInspectorAction.Click p))
                // browser deltaY: about 100 per notch, positive when scrolling down (= zoom out)
                panelEvent "onwheel" true [ "event.deltaY" ] (fun rest p ->
                    match rest with
                    | d :: _ -> parseFloat d |> Option.map (fun dy -> ImageInspectorAction.Zoom(-dy / 200.0, p))
                    | [] -> None)
                "onmouseleave", AttributeValue.Event {
                    clientSide = fun send src -> send src []
                    serverSide = fun _ _ _ -> Seq.singleton (ImageInspectorMessage (ImageInspectorAction.Hover None))
                }
            ]

        // the panel extent: render control and overlays share it, so overlay percentages
        // and pointer fractions are both panel fractions. Only attributes and overlays
        // are incremental -- the render control itself is never rebuilt.
        let extentAttributes =
            amap {
                let! ctx = context
                let aspect =
                    match ctx with
                    | Ok { size = Some s } -> sprintf "%d / %d" s.X s.Y
                    | _ -> "1 / 1"
                yield style (sprintf "position:relative; aspect-ratio:%s; max-width:100%%; max-height:100%%; height:100%%" aspect)
            } |> AttributeMap.ofAMap

        let overlays =
            alist {
                let! view = m.imageView
                let! main = fromMainView
                match main |> Option.bind (ImageView.toPanel view >> crosshair "#00e5ff") with
                | Some c -> yield c
                | None -> ()
                let! hover = m.imageHover
                match hover |> Option.bind (fun h -> h.ndc |> ImageView.toPanel view |> crosshair "rgba(255,255,0,0.6)") with
                | Some c -> yield c
                | None -> ()
                // measurement: rim, sun line, snapped tip
                let! ctx = context
                let! s = m.shadowMeasure
                match ctx, s.anchor with
                | Ok ctx, Some (anchorNdc, anchor) when s.imageId = Some ctx.imageId ->
                    match ctx.sun |> Result.toOption |> Option.bind (fun sun -> sunLine ctx s.kind sun anchor) with
                    | Some (a, dir) ->
                        let pa = ImageView.toPanel view a
                        let pb = ImageView.toPanel view (a + dir * 0.01)
                        yield sunLineSvg pa (Vec.normalize (pb - pa))
                    | None -> ()
                    match dot "#ff8c00" (ImageView.toPanel view anchorNdc) with
                    | Some d -> yield d
                    | None -> ()
                    match s.second |> Option.bind (ImageView.toPanel view >> dot "#ff00ff") with
                    | Some d -> yield d
                    | None -> ()
                    for (pp, _) in s.planePoints do
                        match dot "#4da6ff" (ImageView.toPanel view pp) with
                        | Some d -> yield d
                        | None -> ()
                | _ -> ()
            }

        // The bar is two incremental blocks on purpose: the text follows every hover, the
        // controls only the measurement and the zoom. Rebuilding a button on hover replaces it
        // between mousedown and mouseup -- moving onto it changes the hover -- and the click
        // is lost.
        // The bar holds one short line; a measurement's details go into the result box (an
        // overlay, so it can neither resize the image nor be cut off by the bar).
        let readout =
            alist {
                let! ctx = context
                let! hover = m.imageHover
                let! s = m.shadowMeasure
                let crater = s.kind <> ShadowKind.Height
                let line =
                    match ctx with
                    | Result.Error e -> sprintf "%s -- select an image under GIS View / Projected Images" e
                    | Ok _ when s.active ->
                        let snapHint = if s.snap then " (snaps to the shadow's edge)" else ""
                        match s.anchor, s.second, s.result with
                        | None, _, Some (Result.Error e) -> e
                        | None, _, _ ->
                            if crater then "click the crater rim, where the shadow begins" + snapHint
                            else "click the tip of the boulder's shadow, on the ground" + snapHint
                        | Some _, None, _ ->
                            if crater then "click the end of the shadow" + snapHint
                            else "click the boulder's top edge (snaps to the dashed line)"
                        | Some _, Some _, Some (Result.Error e) when s.upMode = ShadowUp.Plane && s.secondOk -> e
                        | Some _, Some _, Some (Result.Error e) -> e + " -- click to start over"
                        | Some _, Some _, Some (Ok r) when s.upMode = ShadowUp.Plane ->
                            sprintf "%s %s m -- click more plane points, or Clear" (if crater then "depth" else "height") (fmt2 r.value)
                        | Some _, Some _, Some (Ok r) -> sprintf "%s %s m -- click to start over" (if crater then "depth" else "height") (fmt2 r.value)
                        | Some _, Some _, None -> ""
                    | Ok _ ->
                        match hover with
                        | None -> "wheel zooms, drag pans, double-click resets"
                        | Some h ->
                            let px =
                                match h.pixel with
                                | Some p -> sprintf "pixel %s, %s" (fmt p.X) (fmt p.Y)
                                | None -> sprintf "ndc %.4f, %.4f" h.ndc.X h.ndc.Y
                            px + (if h.hit.IsSome then " -- on the surface" else " -- off the surface")
                yield div [ style "overflow:hidden; text-overflow:ellipsis; white-space:nowrap" ] [ text line ]
            }

        // A measurement's result, one fact per line, the key figure first.
        let resultBox =
            alist {
                let! s = m.shadowMeasure
                let! selected = m.scene.gisApp.projectedImageList.selectedImage
                match s.active && s.imageId.IsSome && s.imageId = selected, s.result with
                | true, Some (Ok r) ->
                    let crater = s.kind <> ShadowKind.Height
                    let row (label : string) (value : string) =
                        div [ style "display:flex; gap:12px; justify-content:space-between" ] [
                            span [ style "color:#9a9a9a" ] [ text label ]
                            span [] [ text value ]
                        ]
                    let note (colour : string) (t : string) = div [ style (sprintf "color:%s; margin-top:2px; white-space:normal" colour) ] [ text t ]
                    let upName =
                        match s.upMode with
                        | ShadowUp.Radial -> "radial"
                        | ShadowUp.Plane -> sprintf "plane, %d pts" (1 + List.length s.planePoints)
                        | _ -> "local"
                    let inPixels (metres : float) = r.gsd |> Option.map (fun g -> metres / g)
                    yield div [ style "position:absolute; top:8px; left:8px; z-index:3; pointer-events:none; background:rgba(20,20,22,0.85); border:1px solid #444; border-radius:4px; padding:6px 8px; font-size:12px; line-height:1.45; min-width:200px; max-width:270px" ] [
                        yield div [ style "font-size:14px; font-weight:bold; margin-bottom:2px" ] [
                            text (sprintf "%s %s m" (if crater then "Depth" else "Height") (fmt2 r.value))
                        ]
                        match r.perPixel with
                        | Some d -> yield row "per pixel" (sprintf "± %s m" (fmt2 d))
                        | None -> ()
                        yield row "shadow" (sprintf "%s m" (fmt2 r.length))
                        yield row (sprintf "sun (up: %s)" upName) (sprintf "%s°" (fmt r.sunElevation))
                        if s.upMode <> ShadowUp.Radial then
                            yield row "sun (radial up)" (sprintf "%s°" (fmt r.radialElevation))
                            yield row "up tilt from radial" (sprintf "%s°" (fmt r.tilt))
                        yield row "phase" (sprintf "%s°" (fmt r.phase))
                        match r.gsd with
                        | Some g -> yield row "pixel on ground" (sprintf "%s m" (fmt2 g))
                        | None -> ()
                        match s.anchorSnap, s.secondSnap with
                        | None, None -> ()
                        | a, b ->
                            let f (v : Option<float>) = v |> Option.map (fun v -> sprintf "%s px" (fmt v)) |> Option.defaultValue "-"
                            yield row "snapped" (sprintf "%s / %s" (f a) (f b))
                        match r.meshPoint with
                        | Some mp ->
                            let off = Vec.distance mp r.point
                            match inPixels off with
                            | Some px ->
                                yield row "mesh offset" (sprintf "%s m (%s px)" (fmt2 off) (fmt px))
                                if px > offsetWarnPixels then
                                    yield note "#ffb000" (sprintf "the mesh disagrees by %s px: the clicks are probably not on the shadow's edges" (fmt px))
                            | None -> yield row "mesh offset" (sprintf "%s m" (fmt2 off))
                        | None -> ()
                        if r.phase < lowPhase then yield note "#ff6060" "low phase: sun and camera rays nearly parallel, result unreliable"
                    ]
                | _ -> ()
            }

        let controls =
            alist {
                let! s = m.shadowMeasure
                let! view = m.imageView
                if view.scale > 1.0 then
                    yield div [ style "white-space:nowrap; margin-left:8px" ] [ text (sprintf "zoom %sx" (fmt view.scale)) ]
                    yield button [ clazz "ui mini inverted basic button"; style "margin-left:8px"; onClick (fun _ -> ImageInspectorMessage ImageInspectorAction.ResetView) ] [ text "Reset" ]
                let measureButton = if s.active then "ui mini inverted button active" else "ui mini inverted basic button"
                yield button [ clazz measureButton; style "margin-left:8px"; onClick (fun _ -> ImageInspectorMessage ImageInspectorAction.ToggleMeasure) ] [ text "Measure shadow" ]
                if s.active then
                    let label = if s.kind = ShadowKind.Height then "Boulder height" else "Crater depth"
                    yield button [ clazz "ui mini inverted basic button"; attribute "title" "switch between crater depth and boulder height"
                                   onClick (fun _ -> ImageInspectorMessage ImageInspectorAction.ToggleShadowKind) ] [ text label ]
                    let upLabel =
                        match s.upMode with
                        | ShadowUp.Radial -> "Up: radial"
                        | ShadowUp.Plane -> "Up: plane"
                        | _ -> "Up: local"
                    yield button [ clazz "ui mini inverted basic button"; attribute "title" "local: surface around the first click; radial: from the body centre; plane: through the first click and further clicked points"
                                   onClick (fun _ -> ImageInspectorMessage ImageInspectorAction.ToggleShadowUp) ] [ text upLabel ]
                    yield button [ clazz (if s.snap then "ui mini inverted button active" else "ui mini inverted basic button")
                                   attribute "title" "move clicks along the sun line onto the nearest shadow edge in the image"
                                   onClick (fun _ -> ImageInspectorMessage ImageInspectorAction.ToggleSnap) ] [ text "Snap" ]
                if s.active && s.anchor.IsSome then
                    yield button [ clazz "ui mini inverted basic button"; onClick (fun _ -> ImageInspectorMessage ImageInspectorAction.ClearMeasure) ] [ text "Clear" ]
                match s.result with
                | Some (Ok _) ->
                    yield button [ clazz "ui mini inverted basic button"; onClick (fun _ -> ImageInspectorMessage ImageInspectorAction.CreateScaleBar) ] [ text "Create scale bar" ]
                | _ -> ()
            }

        div [ style "position:relative; width:100%; height:100%; display:flex; flex-direction:column; color:#ddd; font-size:12px" ] [
            div [ style "position:relative; flex:1; min-height:0; display:flex; align-items:center; justify-content:center; overflow:hidden" ] [
                Incremental.div (AttributeMap.ofList [ style "display:contents" ]) resultBox
                Incremental.div extentAttributes (
                    alist {
                        yield DomNode.RenderControl(AttributeMap.ofList [ style "position:absolute; inset:0; width:100%; height:100%; pointer-events:none" ], camera, imageSg m, RenderControlConfig.standard)
                        yield Incremental.div (AttributeMap.ofList [ style "position:absolute; inset:0; pointer-events:none; overflow:hidden; z-index:1" ]) overlays
                        yield Incremental.div inputLayer AList.empty
                    })
            ]
            // fixed-height bar: showing zoom/Reset must not resize the image above it, which
            // would move the image under a resting pointer
            div [ style "flex:none; height:34px; box-sizing:border-box; padding:0 8px; border-top:1px solid #333; display:flex; align-items:center; overflow:hidden; white-space:nowrap" ] [
                Incremental.div (AttributeMap.ofList [ style "flex:1; min-width:0; overflow:hidden" ]) readout
                Incremental.div (AttributeMap.ofList [ style "flex:none; display:flex; align-items:center" ]) controls
            ]
        ]
