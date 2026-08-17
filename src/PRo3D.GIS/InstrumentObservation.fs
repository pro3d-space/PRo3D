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
