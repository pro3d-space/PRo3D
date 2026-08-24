namespace PRo3D.Core

open System
open System.IO

open Aardvark.Base
open Aardvark.Rendering

open PRo3D.SPICE
open PRo3D.Core.InstrumentMetadata

/// The instrument's camera for one observation, split into view and projection so the
/// renderer can use it directly. `full` is what the projection shader consumes.
type ProjectorCamera =
    {
        view     : Trafo3d
        proj     : Trafo3d
        /// view * proj, i.e. exactly what projectDirect returns.
        full     : Trafo3d
        /// Distance to the target in metres, from the sidecar.
        distance : float
        near     : float
        far      : float
    }

type ResolvedImage =
    {
        path      : string
        metadata  : ParsedMetadata
        mbi       : Tiff_Mbi_Json.Mbi
        /// SPICE frame name of the instrument, e.g. "MILANI_ASPECT_NIR1".
        spiceName : string
        /// Native pixel dimensions, when the sidecar declares them.
        size      : Option<V2i>
    }

/// Resolving one instrument observation into the things needed to render it: the image
/// and its sidecar, the metakernel, the sun direction, and the projector camera.
///
/// Lives here rather than in a tool so that PRo3D.ProjectionTestbed and pro3d-tool share
/// one implementation. `projectorCamera` in particular encodes failure modes that were
/// found empirically and are easy to lose in a copy.
module InstrumentObservation =

    /// Pick the image to project: the one named, else the first the folder yields that
    /// actually has an mbi sidecar behind it.
    let resolveImage (imageFolder : string) (imageFile : Option<string>) : Result<ResolvedImage, string> =
        let candidates =
            match imageFile with
            | Some f ->
                let p = if Path.IsPathRooted f then f else Path.Combine(imageFolder, f)
                if File.Exists p then [ p, tryParseMetadataForImagePath p ]
                else []
            | None ->
                discoverInstrumentFolder imageFolder |> Seq.toList

        let withMbi =
            candidates |> List.choose (fun (path, meta) ->
                match meta with
                | Some mbi, _ -> Some (path, meta, mbi)
                | _ -> None)

        match withMbi with
        | [] ->
            match imageFile with
            | Some f -> Result.Error (sprintf "no mbi sidecar found for image '%s' in %s" f imageFolder)
            | None -> Result.Error (sprintf "no image with an mbi sidecar found in %s" imageFolder)
        | (path, meta, mbi) :: _ ->
            match InstrumentProjection.instrument2SpiceName mbi.instrument with
            | None -> Result.Error (sprintf "no SPICE frame known for instrument '%s'" mbi.instrument)
            | Some spiceName ->
                let size =
                    match meta with
                    | _, Some m -> Some (V2i(m.image_width, m.image_height))
                    | _ -> None
                Ok { path = path; metadata = meta; mbi = mbi; spiceName = spiceName; size = size }

    /// Resolve which metakernel to load. Sidecars routinely name a version that is not on
    /// disk, so falling back is normal -- but say so loudly, because a substituted kernel
    /// can change the answer.
    let resolveKernel (spiceKernel : Option<string>) (kernelRoot : string) (img : ResolvedImage) : Result<string, string> =
        match spiceKernel with
        | Some explicitPath ->
            if File.Exists explicitPath then Ok explicitPath
            else Result.Error (sprintf "kernel not found: %s" explicitPath)
        | None ->
            let declared = img.mbi.spiceMk
            let fromSidecar =
                declared |> Option.bind (SpiceBoot.resolveSidecarKernel kernelRoot)
            match fromSidecar with
            | Some p ->
                Log.line "[spice] using sidecar-declared kernel %s" p
                Ok p
            | None ->
                let fallback = Path.Combine(kernelRoot, "mk", "hera_plan.tm")
                if File.Exists fallback then
                    Log.warn "[spice] sidecar declares metakernel %A which was not found under %s"
                        declared kernelRoot
                    Log.warn "[spice] SUBSTITUTING %s -- geometry may differ from the image" fallback
                    Ok fallback
                else
                    Result.Error (sprintf "no kernel found: sidecar wants %A, fallback %s missing"
                              declared fallback)

    /// Unit vector from the body towards the sun, in the render reference frame.
    ///
    /// Returned as a Result rather than defaulting to some arbitrary direction: a wrong sun
    /// direction produces a plausible-looking but meaningless result, which is worse than
    /// none at all.
    let sunDirection (renderFrame : string) (primary : string) (time : DateTime) : Result<V3d, string> =
        match CooTransformation.getRelState "SUN" "EARTH" primary time renderFrame with
        | Some st when st.pos.Length > 0.0 -> Ok st.pos.Normalized
        | Some _ -> Result.Error "SPICE returned a zero-length sun vector"
        | None ->
            Result.Error (sprintf "no sun ephemeris relative to %s in %s at %s"
                              primary renderFrame (time.ToString "o"))

    /// Build the instrument's camera for this observation.
    ///
    /// The projector trafo comes back as a single view*proj matrix, but rendering needs the
    /// two separately (stableTrafo relies on the model-view split for precision). The
    /// projection half is reconstructible from the instrument's frustum, so view falls out
    /// as full * proj⁻¹.
    let projectorCamera (nearFar : Option<float * float>)
                        (observer : string) (referenceFrame : string) (body : string)
                        (projectionMethod : PRo3D.ImageMapping.ProjectionMethod)
                        (img : ResolvedImage) : Result<ProjectorCamera, string> =
        // targetPos is in km
        let distance = img.mbi.targetPos.Length * 1000.0
        let near, far =
            match nearFar with
            | Some (n, f) -> n, f
            | None -> InstrumentProjection.nearFarForDistance distance

        match Map.tryFind img.spiceName (InstrumentProjection.instruments near far) with
        | None -> Result.Error (sprintf "no frustum defined for instrument frame '%s'" img.spiceName)
        | Some frustum ->
            let full =
                PRo3D.InstrumentProjection.Visualization.projectDirectWithNearFar
                    (Some (near, far)) observer referenceFrame img.metadata
                    body None projectionMethod
            match full with
            | None ->
                Result.Error (sprintf
                    "projection did not resolve for %s at %s (frame %s, observer %s). \
                     Most likely the loaded kernel has no coverage at this epoch."
                    img.spiceName (img.mbi.obs_date.ToString("o")) referenceFrame observer)
            // Checking the matrix elements alone is not enough: with no CK coverage the
            // chain came back with finite entries but a camera sitting exactly on the
            // target, so the body centre projected to w = 0 and only the perspective
            // divide produced the NaN. Test the thing we actually rely on.
            | Some full when not (full.Forward.ToArray() |> Array.forall Double.IsFinite)
                          || not (full.Forward.TransformPosProj V3d.Zero
                                  |> fun p -> Double.IsFinite p.X && Double.IsFinite p.Y && Double.IsFinite p.Z) ->
                // A non-finite matrix is worse than no matrix: it renders a blank frame,
                // which reads as "the geometry is wrong" rather than "the pointing never
                // resolved". Observed with the spice method at epochs where the frame chain
                // has no CK coverage yet something still returned Some rather than None.
                Result.Error (sprintf
                    "projection resolved to a non-finite matrix for %s at %s (frame %s). \
                     The kernel most likely lacks coverage for this frame chain and the \
                     failure was not reported as such."
                    img.spiceName (img.mbi.obs_date.ToString("o")) referenceFrame)
            | Some full ->
                let proj = Frustum.projTrafo frustum
                Ok {
                    view = full * proj.Inverse
                    proj = proj
                    full = full
                    distance = distance
                    near = near
                    far = far
                }

    /// Where the image coordinate system's origin sits and which way rows count.
    type PixelOrigin =
        | TopLeft
        | BottomLeft

    /// A complete image addressing convention. Image tools (OpenCV, scipy) are 0-based with
    /// the origin top-left; FITS tools are 1-based with the origin bottom-left. Deliberately
    /// not configurable: whether an integer names a pixel's centre or its corner -- every
    /// source of a centroid puts integers at pixel CENTRES.
    type PixelConvention =
        {
            origin    : PixelOrigin
            /// Index of the first pixel: 0 for image tools, 1 for FITS.
            baseIndex : int
        }
        /// 0-based, origin top-left, y downwards.
        static member Image = { origin = TopLeft; baseIndex = 0 }
        /// 1-based, origin bottom-left, y upwards.
        static member Fits  = { origin = BottomLeft; baseIndex = 1 }

    /// Pixel coordinate -> normalised device coordinates in [-1,1].
    ///
    /// NDC spans the whole image extent [0,w] while the input names a pixel: pixel i covers
    /// [i, i+1) and its centre is i+0.5, hence the half-pixel shift. Continuous in `pixel`, so
    /// a sub-pixel centroid needs no special case.
    let pixelToNdc (size : V2i) (conv : PixelConvention) (pixel : V2d) : V2d =
        let b = float conv.baseIndex
        let x = pixel.X - b
        let y = pixel.Y - b
        let u = 2.0 * (x + 0.5) / float size.X - 1.0
        let v =
            match conv.origin with
            | TopLeft    -> 1.0 - 2.0 * (y + 0.5) / float size.Y
            | BottomLeft -> 2.0 * (y + 0.5) / float size.Y - 1.0
        V2d(u, v)

    /// Inverse of pixelToNdc.
    let ndcToPixel (size : V2i) (conv : PixelConvention) (ndc : V2d) : V2d =
        let b = float conv.baseIndex
        let x = (ndc.X + 1.0) * 0.5 * float size.X - 0.5
        let y =
            match conv.origin with
            | TopLeft    -> (1.0 - ndc.Y) * 0.5 * float size.Y - 0.5
            | BottomLeft -> (ndc.Y + 1.0) * 0.5 * float size.Y - 0.5
        V2d(x + b, y + b)

    /// Ray through a pixel, in the reference frame `projectorCamera` was built for.
    ///
    /// `full.Forward` is world -> clip (that is how the projection shader uses it), so
    /// `Backward` unprojects clip back to world. Same technique the viewer's own mouse picking
    /// uses in `pickRayNdc`.
    let pixelRay (cam : ProjectorCamera) (size : V2i) (conv : PixelConvention) (pixel : V2d) : Ray3d =
        let n = pixelToNdc size conv pixel
        let near = cam.full.Backward.TransformPosProj(V3d(n.X, n.Y, -1.0))
        let far  = cam.full.Backward.TransformPosProj(V3d(n.X, n.Y,  1.0))
        Ray3d(near, Vec.normalize (far - near))

    /// World point -> pixel, the inverse of `pixelRay`.
    ///
    /// Returns None for a point at or behind the projection centre. The homogeneous divide is
    /// done explicitly rather than through TransformPosProj so the sign of w can be checked:
    /// a point behind the camera otherwise comes back as a perfectly plausible pixel.
    let projectToPixel (cam : ProjectorCamera) (size : V2i) (conv : PixelConvention) (p : V3d) : Option<V2d> =
        let h = cam.full.Forward.Transform(V4d(p.X, p.Y, p.Z, 1.0))
        if h.W <= 0.0 || not (Double.IsFinite h.W) then None
        else
            let n = V2d(h.X / h.W, h.Y / h.W)
            if Double.IsFinite n.X && Double.IsFinite n.Y then Some (ndcToPixel size conv n)
            else None
