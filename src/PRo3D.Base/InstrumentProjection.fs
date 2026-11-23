namespace PRo3D.SPICE

open System

open Aardvark.Base
open Aardvark.Rendering

open PRo3D.Extensions
open PRo3D.Extensions.FSharp
open PRo3D.Core

module InstrumentImages =

    open Aardvark.Rendering

    type Extrinsics =
        | Plain of CameraView

    type Intrinsics =
        | Plain of Frustum

    type ImageData =
        | FilePath of string

    type ProjectedImage =
        {
            intrinsics : Intrinsics
            extrinsics : Extrinsics
            image      : Option<ImageData>
        }

    type Intrinsics with
        member x.ProjTrafo =
            match x with
            | Intrinsics.Plain frustum -> Frustum.projTrafo frustum

/// Where a projector's pose (camera position and attitude) comes from: the
/// *Orientation Sources* of the GIS tab plus tool-only poses; every one ends in the same composition
/// (InstrumentProjection.resolve), so they differ only in these inputs.
type ProjectorPose =
    /// SPICE only. Spacecraft position from the SPK, attitude (including roll) from
    /// the spacecraft CK, then turned the shortest way onto the target's centre. The
    /// turn absorbs the ephemeris error between where SPICE thinks the target is and
    /// where the camera pointed; the image therefore always lands centred on the body.
    | SpiceAimed
    /// The sidecar's own pose: measured attitude `SC_QUAT` (spacecraft -> J2000) and
    /// `TRG_POS` (target minus spacecraft, km, J2000, centred on the target).
    | Sidecar of scQuat : QuaternionD * targetPosKm : V3d
    /// The sidecar's attitude (`SC_QUAT`, including roll) with the target assumed on the
    /// boresight, at the range SPICE gives between spacecraft and target. For sidecars
    /// without a usable `TRG_POS`, and for kernels whose spacecraft-to-target direction
    /// is less accurate than the measured attitude.
    | SidecarAttitudeSpiceRange of scQuat : QuaternionD
    /// The sidecar's attitude (`SC_QUAT`, including roll) at the spacecraft position SPICE
    /// gives, NOT aimed: the target lands wherever that attitude puts it. The only pose
    /// that keeps a pointing error visible for sidecars without `TRG_POS` -- the other two
    /// SPICE-assisted poses put the target on the boresight by construction. Used by
    /// `pro3d-tool simulate-image --method mbi-spice-position` for comparing renders with
    /// real frames (docs/Pro3DTool-SimulateImage.md); not an orientation source of the viewer.
    | SidecarAttitudeSpicePosition of scQuat : QuaternionD
    /// SPICE only, and the instrument's own frame: position from the SPK, orientation
    /// from `pxform(<instrument>, J2000)` -- CK x FK, so the frames kernel's mounting,
    /// misalignment and twist are all in -- and NOT aimed. Every other pose builds the
    /// instrument from the SPACECRAFT attitude plus the fixed axis swap of
    /// `specialTrafos` and never reads the instrument frame; `SpiceAimed` then turns
    /// onto the target, hiding any misalignment. Image axes: `imageAxes` -- the
    /// instrument kernel's pixel layout, or, where the loaded frames kernel names the
    /// axes differently or there is no layout, the relabelling of the instrument frame
    /// nearest to the spacecraft-attitude camera.
    | SpiceInstrument

/// Everything needed to place one instrument image as a projector.
type ProjectorRequest =
    {
        /// SPICE frame name of the instrument, e.g. "HERA_AFC-1": selects FOV and axis remap.
        instrument     : string
        /// The spacecraft carrying it, e.g. "HERA".
        spacecraft     : string
        /// The body the image is of (the sidecar's TARGET). The projector aims at it /
        /// measures from it; when SPICE cannot place it, `origin` stands in (with a warning).
        target         : string
        /// The body whose body-fixed coordinates the surface is in: the projector is
        /// expressed relative to its centre.
        origin         : string
        /// That body's body-fixed frame, e.g. "DIDYMOS_FIXED".
        originFrame    : string
        time           : DateTime
        pose           : ProjectorPose
        /// Registration correction, a rotation in projector space (see
        /// InstrumentProjection.boresightCorrection): the projector turns about its own
        /// position.
        boresight      : Option<Trafo3d>
        /// Overrides the depth range; default from the resolved distance to the target.
        nearFar        : Option<float * float>
        /// The scene's full field of view (horizontal, vertical) in degrees for this
        /// instrument; None = the kernel default (InstrumentProjection.defaultFovs).
        fov            : Option<float * float>
        /// Mirror the image's rows: for image files whose first row is the bottom of the
        /// frame (FITS order) while the projector expects the top. A projector-space flip,
        /// so it composes with every orientation source and the boresight correction.
        flipRows       : bool
    }

/// A resolved projector, in the origin body's body-fixed frame.
type ResolvedProjector =
    {
        /// origin frame -> clip space; what the projection shader consumes.
        full           : Trafo3d
        /// origin frame -> projector (view) space, i.e. full * proj⁻¹.
        view           : Trafo3d
        proj           : Trafo3d
        /// Projector position in the origin frame, metres.
        location       : V3d
        /// Distance from the projector to the target's centre, metres.
        distance       : float
        near           : float
        far            : float
        /// The body actually aimed at / measured from (the request's target, or the
        /// origin when the target could not be placed).
        target         : string
        /// Non-fatal problems the caller should show the user.
        warnings       : list<string>
    }

module InstrumentProjection =

    /// Full field of view of one instrument in degrees: `horizontal` along the image's
    /// width, `vertical` along its height.
    type Fov = { horizontal : float; vertical : float }

    /// The kernels' field of view of each instrument, keyed by SPICE frame name.
    /// These are only the defaults: a scene can override them per instrument (the
    /// projected-images panel's Instruments section), see `instrumentsWith` and
    /// ProjectorRequest.fov.
    /// Near/far are NOT part of an instrument's intrinsics -- they depend on how far
    /// away the target is -- hence `instruments` takes them as parameters.
    /// Where a stored image's pixel grid lies in the instrument frame: the directions of
    /// increasing column and increasing row (row 0 = the first row stored), with the
    /// boresight along the instrument's +Z. Taken from the instrument kernel's FOV
    /// layout, which defines the frame axes against the detector's samples and lines.
    type ImageLayout =
        {
            columns    : V3d
            rows       : V3d
            /// the spacecraft the instrument is mounted on (key of `spacecraftFrames`)
            spacecraft : string
        }

    /// The instrument kernels' pixel layout, per SPICE frame name. HERA's IKs: pixel
    /// (0,0) in the lower left corner, samples along +X, lines along -Y, boresight +Z --
    /// so a file storing line 0 first stores its rows bottom-up. Only instruments whose
    /// layout is confirmed against PRo3D's camera are listed (docs/InstrumentFieldOfView.md);
    /// the others keep `specialTrafos` and project unflipped by default.
    let imageLayouts : Map<string, ImageLayout> =
        let hera = { columns = V3d.IOO; rows = -V3d.OIO; spacecraft = "HERA" }
        Map.ofList [
            "HERA_AFC-1", hera
            "HERA_AFC-2", hera
        ]

    /// The camera of a layout in instrument coordinates (columns: image right, image up,
    /// towards the viewer = -boresight), always a proper rotation, and whether the image
    /// rows have to be flipped for it. A layout whose right/up/back is a mirror (rows
    /// stored bottom-up) takes the nearest proper camera and flips the rows.
    let layoutCamera (l : ImageLayout) : M33d * bool =
        let back = -V3d.OOI
        let up = -l.rows
        let m = M33d.FromCols(l.columns, up, back)
        if m.Determinant > 0.0 then m, false
        else M33d.FromCols(l.columns, -up, back), true

    /// Whether an instrument's images project with flipped rows by default: what its
    /// `imageLayouts` entry says; unflipped without one. A scene can override it
    /// (InstrumentSetup.flipRows); the tools use this default.
    let defaultFlipRows (instrument : string) =
        match Map.tryFind instrument imageLayouts with
        | Some l -> snd (layoutCamera l)
        | None -> false

    /// The projector-space row flip (ProjectorRequest.flipRows): between view and projection.
    let rowFlip : Trafo3d = Trafo3d.Scale(1.0, -1.0, 1.0)

    let defaultFovs : Map<string, Fov> =
        Map.ofList [
            // Full angles = twice the instrument kernel's FOV_REF_ANGLE / FOV_CROSS_ANGLE
            // (pinned by InstrumentFovTests). AFC was 5.5306897, 0.56% too wide (#801).
            "HERA_AFC-1",         { horizontal = 5.5;  vertical = 5.5 }
            "HERA_AFC-2",         { horizontal = 5.5;  vertical = 5.5 }
            // The paraxial field, which fits a pinhole frustum better than the full one.
            // Width = the long detector axis. Was 15.24 x 8.09.
            "HERA_HSH",           { horizontal = 15.5; vertical = 8.3 }
            // Width along +X, the long detector axis.
            "HERA_TIRI",          { horizontal = 13.3; vertical = 10.0 }
            "MILANI_ASPECT_NIR1", { horizontal = 6.7;  vertical = 5.4 }
        ]

    /// How a scene projects one instrument's images (one row of the projected-images
    /// panel's Instruments section): its field of view, and whether its image rows are
    /// mirrored (ProjectorRequest.flipRows). Comparable, so a map of them can key
    /// projector caches.
    type InstrumentSetup =
        {
            fov      : Fov
            flipRows : bool
        }

    /// A scene's instruments, keyed by SPICE frame name; an instrument missing here
    /// projects with its kernel field of view, unflipped. Map.empty = the kernels'
    /// values throughout (what the tools use).
    type Instruments = Map<string, InstrumentSetup>

    /// A centred perspective frustum with these full fields of view.
    let frustumOf (fov : Fov) (near : float) (far : float) =
        // Frustum.perspective: right = near * tan(h/2), top = right / aspect
        let aspect = tan (Conversion.RadiansFromDegrees(fov.horizontal / 2.0))
                   / tan (Conversion.RadiansFromDegrees(fov.vertical / 2.0))
        Frustum.perspective fov.horizontal near far aspect

    /// Near/far planes for a target observed from `distance` metres away.
    ///
    /// Purely a heuristic: instrument kernels define no depth range, so it is picked
    /// relative to the observation distance. Two orders of magnitude either side keeps
    /// bodies far larger and far smaller than the range comfortably inside the frustum
    /// while holding the far/near ratio at 1e4, which 32-bit depth handles without
    /// visible z-fighting. Callers with better knowledge should pass explicit planes.
    let nearFarForDistance (distance : float) =
        let d = max 1.0 (abs distance)
        d / 100.0, d * 100.0

    /// Instrument frusta for a given depth range. Prefer deriving near/far from the
    /// observation distance via nearFarForDistance rather than hardcoding a scale:
    /// values tuned for Mars clip a body the size of Didymos outright.
    let instruments (near : float) (far : float) =
        defaultFovs |> Map.map (fun _ fov -> frustumOf fov near far)

    /// `instruments`, with `overrides` (a scene's own fields of view) replacing the
    /// kernel defaults per instrument. Instruments only in `overrides` are added.
    let instrumentsWith (overrides : Map<string, Fov>) (near : float) (far : float) =
        overrides
        |> Map.fold (fun m name fov -> Map.add name fov m) defaultFovs
        |> Map.map (fun _ fov -> frustumOf fov near far)

    // CSPICE's global kernel pool/error state isn't thread-safe, and CooTransformation's
    // own lock only covers one native call at a time -- it doesn't stop another thread's
    // unrelated SPICE call from interleaving between the getRelState/getRotationTrafo
    // calls that make up one logical projection here, which was observed to silently
    // corrupt results (not throw) under Expecto's default parallel test execution.
    // Serialize whole projection computations against each other; Monitor.Enter is
    // per-thread reentrant, so nesting is safe.
    let private spiceCallLock = obj()

    /// Runs `f` as one unit against every SPICE call in this module.
    ///
    /// For callers that replace the kernel pool rather than read it -- switchKernel is
    /// DeInit + Init + furnsh. A swap landing between the getRelState and getRotationTrafo
    /// of one projection answers it from two kernel sets; a swap landing inside a native
    /// call is an access violation. Reentrant, so a whole load-then-project sequence fits.
    let withSpiceLock (f : unit -> 'a) : 'a = lock spiceCallLock f

    /// Axis remap from the camera basis built out of the spacecraft attitude to the
    /// instrument's image axes. Every entry keeps +Z, so the boresight is -Z of
    /// projector space for all of them.
    let specialTrafos =
        Map.ofList [
            "HERA_AFC-2", Trafo3d.FromOrthoNormalBasis(V3d.OIO, V3d.IOO, V3d.OOI)
            "HERA_AFC-1", Trafo3d.FromOrthoNormalBasis(-V3d.OIO, -V3d.IOO, V3d.OOI)
            "HERA_HSH", Trafo3d.FromOrthoNormalBasis(-V3d.OIO, -V3d.IOO, V3d.OOI)
            // hera_milani_v05.tf defines all four ASPECT channel frames (VIS/NIR1/NIR2/SWIR)
            // as a zero-degree TKFRAME offset from MILANI_SPACECRAFT, so unlike the Hera-mounted
            // instruments above there is no known axis remap to apply here. Identity until this
            // is calibrated against a real rendered ASPECT image.
            // attitudeView builds the camera basis as FromBasis(-C0, -C1, -C2), whose
            // determinant is (-1)^3 = -1 -- an improper, mirroring transform. Every HERA
            // entry above happens to have det = -1 too (an X/Y swap), so the pair composes
            // back to a proper rotation. Identity here did NOT cancel it, leaving the
            // ASPECT camera basis left-handed and the render mirrored on one axis.
            // This basis is (X, -Y, Z): det = -1, restoring a proper composition.
            "MILANI_ASPECT_NIR1", Trafo3d.FromOrthoNormalBasis(V3d.IOO, -V3d.OIO, V3d.OOI)
            // NOT VALIDATED -- no TIRI image has been checked against this. HSH's entry
            // above puts the image width on frame -Y (HSH's long axis). TIRI's long axis
            // is +X in its instrument kernel, so the width goes on X here;
            // det = -1 like every entry above. The signs (mirroring) are a guess.
            "HERA_TIRI", Trafo3d.FromOrthoNormalBasis(V3d.IOO, -V3d.OIO, V3d.OOI)
        ]

    /// SPICE frame of each spacecraft's body, whose CK gives the attitude the sidecar's
    /// SC_QUAT also describes (SpiceAimed reads it from SPICE instead).
    let private spacecraftFrames =
        Map.ofList [
            "HERA",   "HERA_SPACECRAFT"
            "MILANI", "MILANI_SPACECRAFT"
        ]

    /// The registration correction as a projector-space rotation. Applied after the view,
    /// so the projector turns about its own position: yaw moves the image along its
    /// horizontal axis, pitch along its vertical axis, roll turns it about the boresight.
    /// (It used to be applied before the view, i.e. about the origin body's centre, which
    /// kept the body centre fixed in the image whatever pitch/yaw were set to.)
    let boresightCorrection (rollDeg : float) (pitchDeg : float) (yawDeg : float) : Trafo3d =
        let cameraRotation =
            Trafo3d.RotationZInDegrees rollDeg * Trafo3d.RotationXInDegrees pitchDeg * Trafo3d.RotationYInDegrees (-yawDeg)
        // points move opposite to the camera
        cameraRotation.Inverse

    let private rotationTrafo (m : M33d) =
        let f = M44d m
        Trafo3d(f, f.Transposed)

    /// J2000 -> spacecraft rotation from a sidecar quaternion (spacecraft -> J2000).
    let private j2000ToScOfQuat (q : QuaternionD) : M33d =
        M33d.Rotation (Rot3d(q.Conjugated))

    /// J2000 (relative to the origin body) -> projector space, for a camera at `location`
    /// with the spacecraft attitude `j2000ToSc`, followed by the instrument's axis remap.
    /// The convention the real HERA deliveries follow (src/Tests/MbiSidecarTest.fs).
    let private attitudeView (instrument : string) (j2000ToSc : M33d) (location : V3d) : Trafo3d =
        let t = Trafo3d.FromBasis(-j2000ToSc.C0, -j2000ToSc.C1, -j2000ToSc.C2, V3d.Zero)
        let view = CameraView.ofTrafo t |> CameraView.withLocation location
        let special = specialTrafos |> Map.tryFind instrument |> Option.defaultValue Trafo3d.Identity
        CameraView.viewTrafo view * special

    /// The signed permutation nearest to `d` (column by column); may be singular.
    let private nearestPermutation (d : M33d) : M33d =
        let mutable p = M33d.Zero
        for c in 0 .. 2 do
            let mutable best = 0
            for r in 1 .. 2 do
                if abs d.[r, c] > abs d.[best, c] then best <- r
            p.[best, c] <- float (sign d.[best, c])
        p

    /// Rotation angle (degrees) between the camera `p` and the camera `d`, both as
    /// camera axes in instrument coordinates.
    let private deviationDeg (p : M33d) (d : M33d) =
        let tr = p.Transposed * d
        Constant.DegreesPerRadian * acos (clamp -1.0 1.0 ((tr.M00 + tr.M11 + tr.M22 - 1.0) / 2.0))

    let private warnedAxisFallback = System.Collections.Concurrent.ConcurrentDictionary<string, bool>()

    /// Forget which instruments' axis fallback was already reported (`imageAxes`'s notes):
    /// called when another kernel is loaded, so its mismatches are reported again.
    let resetAxisWarnings () = warnedAxisFallback.Clear()

    /// The image axes of `instrument` in its own SPICE frame: the camera's right, up and
    /// towards-the-viewer as columns in instrument coordinates. Normally the instrument
    /// kernel's pixel layout (`imageLayouts`). That layout is drawn in the axes of the
    /// frames kernel the instrument kernel belongs to; a frames kernel that names the
    /// instrument axes differently (a different version) puts it far from PRo3D's
    /// spacecraft-attitude camera, and then -- as for instruments without a layout -- the
    /// relabelling of the instrument axes nearest to that camera is used, with a warning
    /// (once per instrument and kernel load, see `resetAxisWarnings`). The row flip is not
    /// affected: a relabelling is a rotation, and whether rows are flipped stays
    /// `defaultFlipRows`.
    ///
    /// Compares through the frames kernel's mounting (spacecraft frame -> instrument
    /// frame), which needs no attitude data: `time` only has to be an epoch SPICE can
    /// convert. The spacecraft is the layout's own; `spacecraft` is used for instruments
    /// without a layout. Error: no spacecraft frame known, SPICE cannot relate the
    /// instrument frame to it (no frames kernel), or no relabelling within 5 degrees of
    /// the spacecraft-attitude camera (wrong instrument frame).
    let imageAxes (instrument : string) (spacecraft : string) (time : DateTime) : Result<M33d * list<string>, string> =
        lock spiceCallLock (fun () ->
        let layout = Map.tryFind instrument imageLayouts
        let spacecraft = layout |> Option.map (fun l -> l.spacecraft) |> Option.defaultValue spacecraft
        match Map.tryFind spacecraft spacecraftFrames with
        | None -> Result.Error (sprintf "no spacecraft frame known for %s (InstrumentProjection.spacecraftFrames)" spacecraft)
        | Some scFrame ->
        match CooTransformation.getRotationTrafo scFrame instrument time with
        | None -> Result.Error (sprintf "SPICE cannot relate the instrument frame %s to %s at %s (frames kernel loaded?)" instrument scFrame (time.ToString "o"))
        | Some scToInst ->
            // PRo3D's spacecraft-attitude camera (identity attitude + specialTrafos): its
            // axes in spacecraft coordinates, then in instrument coordinates. The attitude
            // cancels: for any CK, (J2000 -> inst) * camToJ2000 is this same matrix.
            let camSc = (attitudeView instrument M33d.Identity V3d.Zero).Backward.UpperLeftM33()
            let d = scToInst.Forward.UpperLeftM33() * camSc
            let layoutCam = layout |> Option.map (layoutCamera >> fst)
            match layoutCam with
            | Some c when deviationDeg c d <= 5.0 -> Ok (c, [])
            | _ ->
                let p = nearestPermutation d
                let dev = deviationDeg p d
                if abs (p.Determinant - 1.0) > 1e-9 || dev > 5.0 then
                    Result.Error (sprintf "%s's frame is %.1f deg from any axis relabelling of PRo3D's %s camera -- wrong instrument frame?"
                                      instrument dev spacecraft)
                else
                    let notes =
                        match layoutCam with
                        | Some c when warnedAxisFallback.TryAdd(instrument, true) ->
                            [ sprintf "the loaded frames kernel names %s's axes %.0f deg away from its instrument kernel's pixel layout (a different frames kernel version?); using the axis relabelling nearest to the spacecraft-attitude camera"
                                  instrument (deviationDeg c d) ]
                        | _ -> []
                    Ok (p, notes))

    /// Instruments whose instrument-kernel layout the LOADED frames kernel names
    /// differently, with the angle between the layout camera and PRo3D's spacecraft-attitude
    /// camera: for these `imageAxes` falls back to the nearest relabelling. Checks the
    /// frames kernel's mounting (spacecraft frame -> instrument frame), which needs no
    /// attitude data, so `time` only has to be an epoch SPICE can convert. Instruments
    /// SPICE cannot relate to their spacecraft (no frames kernel loaded) are not listed.
    /// Meant to run once per loaded kernel set, to tell the user (Viewer:
    /// `withFrameKernelCheck`).
    let layoutMismatches (time : DateTime) : list<string * float> =
        lock spiceCallLock (fun () ->
            imageLayouts
            |> Map.toList
            |> List.choose (fun (instrument, layout) ->
                match Map.tryFind layout.spacecraft spacecraftFrames with
                | None -> None
                | Some scFrame ->
                match CooTransformation.getRotationTrafo scFrame instrument time with
                | None -> None
                | Some scToInst ->
                    // the spacecraft-attitude camera for an identity attitude: camera axes in
                    // spacecraft coordinates, then in instrument coordinates
                    let camSc = (attitudeView instrument M33d.Identity V3d.Zero).Backward.UpperLeftM33()
                    let d = scToInst.Forward.UpperLeftM33() * camSc
                    let dev = deviationDeg (fst (layoutCamera layout)) d
                    if dev > 5.0 then Some (instrument, dev) else None))

    /// The boresight (projector -Z, see specialTrafos) of a view trafo, in J2000.
    let private boresightOf (view : Trafo3d) : V3d =
        view.Backward.TransformDir(-V3d.OOI) |> Vec.normalize

    /// The same view with the camera turned by `r` about its own position.
    let private turnAbout (location : V3d) (r : Rot3d) (view : Trafo3d) : Trafo3d =
        Trafo3d.Translation(-location) * rotationTrafo (M33d.Rotation r.Inverse) * Trafo3d.Translation(location) * view

    /// `body` relative to `origin`, J2000, metres.
    let private relativePosition (body : string) (origin : string) (time : DateTime) : Option<V3d> =
        if String.Equals(body.Trim(), origin.Trim(), StringComparison.OrdinalIgnoreCase) then Some V3d.Zero
        else CooTransformation.getRelState body "SUN" origin time "J2000" |> Option.map (fun s -> s.pos)

    let private isFinite (v : V3d) = Double.IsFinite v.X && Double.IsFinite v.Y && Double.IsFinite v.Z

    /// Resolve a projector. All three poses go through the same composition
    ///
    ///     originFrame -> J2000 * J2000 -> projector space * boresight * (row flip) * projection
    ///
    /// so they differ only in where the camera position and attitude come from.
    let resolve (req : ProjectorRequest) : Result<ResolvedProjector, string> =
        lock spiceCallLock (fun () ->
            let when' = req.time.ToString("o")
            let fov =
                match req.fov with
                | Some (h, v) -> Some { horizontal = h; vertical = v }
                | None -> Map.tryFind req.instrument defaultFovs
            match fov with
            | None -> Result.Error (sprintf "no field of view known for instrument frame '%s'" req.instrument)
            | Some fov ->
            let originToJ2000 =
                if String.Equals(req.originFrame.Trim(), "J2000", StringComparison.OrdinalIgnoreCase) then Some Trafo3d.Identity
                else CooTransformation.getRotationTrafo req.originFrame "J2000" req.time
            match originToJ2000 with
            | None -> Result.Error (sprintf "SPICE cannot rotate %s to J2000 at %s" req.originFrame when')
            | Some originToJ2000 ->

            // the target, relative to the origin body
            let targetPos, target, warnings =
                match relativePosition req.target req.origin req.time with
                | Some p -> p, req.target, []
                | None ->
                    V3d.Zero, req.origin,
                    [ sprintf "SPICE cannot place the image's target '%s' relative to %s at %s; assuming the image is of %s"
                          req.target req.origin when' req.origin ]

            let spacecraftPos () =
                match relativePosition req.spacecraft req.origin req.time with
                | Some p -> Ok p
                | None -> Result.Error (sprintf "SPICE has no position of %s relative to %s at %s (no SPK coverage?)" req.spacecraft req.origin when')

            // An all-zero SC_QUAT (a sidecar without attitude) is no rotation at
            // all; Rot3d of it is NaN, which would surface as "not finite" further down.
            let zeroQuat (q : QuaternionD) =
                not (Double.IsFinite q.W && Double.IsFinite q.X && Double.IsFinite q.Y && Double.IsFinite q.Z)
                || q.W * q.W + q.X * q.X + q.Y * q.Y + q.Z * q.Z < 1e-12
            let noAttitude =
                "the sidecar's SC_QUAT is zero, so it does not say how the camera was pointed. \
                 Use a SPICE orientation source for this image (SPICE, aimed at target, needs no frames kernel)"

            let pose : Result<Trafo3d * V3d * list<string>, string> =
                match req.pose with
                | Sidecar (q, _) | SidecarAttitudeSpiceRange q | SidecarAttitudeSpicePosition q when zeroQuat q ->
                    Result.Error noAttitude
                | Sidecar (q, trgKm) ->
                    if not (isFinite trgKm) || trgKm.Length = 0.0 then
                        Result.Error "the sidecar's TRG_POS is zero, so it does not say where the camera was. \
                                      Use the Spice or MbiAttitudeSpiceRange orientation source for this image"
                    else
                        let location = targetPos - trgKm * 1000.0
                        Ok (attitudeView req.instrument (j2000ToScOfQuat q) location, location, [])
                | SidecarAttitudeSpiceRange q ->
                    spacecraftPos () |> Result.bind (fun sc ->
                        let range = (targetPos - sc).Length
                        if not (range > 0.0) then Result.Error (sprintf "%s and %s coincide at %s" req.spacecraft target when')
                        else
                            let attitude = j2000ToScOfQuat q
                            let boresight = attitudeView req.instrument attitude V3d.Zero |> boresightOf
                            let location = targetPos - boresight * range
                            Ok (attitudeView req.instrument attitude location, location, []))
                | SidecarAttitudeSpicePosition q ->
                    spacecraftPos () |> Result.bind (fun location ->
                        if not ((targetPos - location).Length > 0.0) then
                            Result.Error (sprintf "%s and %s coincide at %s" req.spacecraft target when')
                        else Ok (attitudeView req.instrument (j2000ToScOfQuat q) location, location, []))
                | SpiceInstrument ->
                    spacecraftPos () |> Result.bind (fun location ->
                        if not ((targetPos - location).Length > 0.0) then
                            Result.Error (sprintf "%s and %s coincide at %s" req.spacecraft target when')
                        else
                        match imageAxes req.instrument req.spacecraft req.time, CooTransformation.getRotationTrafo "J2000" req.instrument req.time with
                        | Result.Error e, _ -> Result.Error e
                        | _, None -> Result.Error (sprintf "SPICE cannot orient the instrument frame %s at %s (no CK of %s, or no frames kernel)" req.instrument when' req.spacecraft)
                        | Ok (p, notes), Some inst ->
                            let cam = inst.Forward.UpperLeftM33().Transposed * p   // camera axes in J2000
                            let camToWorld = Trafo3d.FromBasis(cam.C0, cam.C1, cam.C2, location)
                            Ok (camToWorld.Inverse, location, notes))
                | SpiceAimed ->
                    spacecraftPos () |> Result.bind (fun location ->
                        let toTarget = targetPos - location
                        if not (toTarget.Length > 0.0) then Result.Error (sprintf "%s and %s coincide at %s" req.spacecraft target when')
                        else
                            let ck =
                                Map.tryFind req.spacecraft spacecraftFrames
                                |> Option.bind (fun scFrame -> CooTransformation.getRotationTrafo "J2000" scFrame req.time)
                            let attitude, notes =
                                match ck with
                                | Some r -> r.Forward.UpperLeftM33(), []
                                | None ->
                                    // without an attitude the aim still works, the roll does not
                                    M33d.Identity,
                                    [ sprintf "SPICE has no attitude (CK) of %s at %s: the projection is aimed at %s but its roll is arbitrary; correct it with the Registration roll"
                                          req.spacecraft when' target ]
                            let view = attitudeView req.instrument attitude location
                            let aim = Rot3d.RotateInto(boresightOf view, Vec.normalize toTarget)
                            Ok (turnAbout location aim view, location, notes))

            match pose with
            | Result.Error e -> Result.Error e
            | Ok (view, location, notes) ->
                let distance = (targetPos - location).Length
                let near, far = req.nearFar |> Option.defaultValue (nearFarForDistance distance)
                // the row flip mirrors projector space, so the image is sampled bottom-up
                let flip = if req.flipRows then rowFlip else Trafo3d.Identity
                let proj = flip * (frustumOf fov near far |> Frustum.projTrafo)
                let boresight = req.boresight |> Option.defaultValue Trafo3d.Identity
                let viewInOrigin = originToJ2000 * view * boresight
                let full = viewInOrigin * proj
                // The target must land in front of the projector with finite coordinates: a
                // pose with the camera inside or behind it renders nothing at all, which reads
                // as "the geometry is wrong" rather than "the pose never resolved".
                let targetInOrigin = originToJ2000.Backward.TransformDir targetPos
                let h = full.Forward.Transform(V4d(targetInOrigin, 1.0))
                if not (full.Forward.ToArray() |> Array.forall Double.IsFinite) || not (Double.IsFinite h.W) then
                    Result.Error (sprintf "the projector for %s at %s is not finite" req.instrument when')
                elif h.W <= 0.0 then
                    Result.Error (sprintf "%s is behind the %s projector at %s: the image's pointing does not face its target"
                                      target req.instrument when')
                else
                    Ok {
                        full = full
                        view = viewInOrigin
                        proj = proj
                        location = originToJ2000.Backward.TransformDir location
                        distance = distance
                        near = near
                        far = far
                        target = target
                        warnings = warnings @ notes
                    })

    // maps fits INSTRUME names to spice frame names
    let instrumentNames =
        Map.ofList [
            "AFC1", "HERA_AFC-1"
            "HSH", "HERA_HSH"
            "AFC2", "HERA_AFC-2"
            "TIRI", "HERA_TIRI"
            // ASPECT (Vis/NIR1/NIR2/SWIR) shares one attitude quaternion in the mbi sidecar and
            // all four channel frames coincide with MILANI_SPACECRAFT (see specialTrafos above),
            // so any one of them is representative; NIR1 is used as the canonical choice.
            "ASPECT", "MILANI_ASPECT_NIR1"
        ]

    let instrument2SpiceName (fitsName : string) =
        Map.tryFind fitsName instrumentNames

    /// The kernels' horizontal field of view in degrees of an instrument, by its FITS
    /// INSTRUME name (defaultFovs; a scene's own fields of view are not considered).
    let tryHorizontalFov (fitsName : string) : Option<float> =
        instrument2SpiceName fitsName
        |> Option.bind (fun spiceName -> Map.tryFind spiceName defaultFovs)
        |> Option.map (fun fov -> fov.horizontal)

    /// Ground resolution in metres per pixel at `distanceKm` from an instrument whose
    /// image is `widthPx` wide: the distance times the angle one pixel spans. Measured at
    /// the distance to the target's centre, so on the surface facing the camera pixels
    /// are slightly finer (by the body's radius over the distance).
    let tryResolution (fitsName : string) (distanceKm : float) (widthPx : int) : Option<float> =
        if widthPx <= 0 || not (distanceKm > 0.0) then None
        else
            tryHorizontalFov fitsName
            |> Option.map (fun fov ->
                distanceKm * 1000.0 * 2.0 * tan (Conversion.RadiansFromDegrees fov / 2.0) / float widthPx)

    // maps fits INSTRUME names to the spacecraft that physically carries the instrument.
    // AFC1/AFC2/HSH are mounted on Hera itself; ASPECT flies on the Milani cubesat.
    let instrumentCameraSource =
        Map.ofList [
            "AFC1", "HERA"
            "HSH", "HERA"
            "AFC2", "HERA"
            "TIRI", "HERA"
            "ASPECT", "MILANI"
        ]

    let instrument2CameraSource (fitsName : string) =
        Map.tryFind fitsName instrumentCameraSource |> Option.defaultValue "HERA"

    /// The distance from the spacecraft carrying `fitsName` to the first of `targets`
    /// SPICE can place at `time`, in km: the image lists' Distance (and so the
    /// Resolution) for a sidecar without TRG_POS. Several targets because the projector
    /// does the same: a TARGET the kernels do not define (e.g. a system barycenter they
    /// lack) gives way to the surface's body (resolve). None when SPICE places none of
    /// them (no kernel, no coverage).
    let trySpiceDistanceKm (fitsName : string) (targets : list<string>) (time : DateTime) : Option<float> =
        let spacecraft = instrument2CameraSource fitsName
        targets
        |> List.tryPick (fun target ->
            try
                lock spiceCallLock (fun () ->
                    relativePosition target spacecraft time
                    |> Option.map (fun p -> p.Length / 1000.0)
                    |> Option.filter (fun d -> Double.IsFinite d && d > 0.0))
            with _ -> None)
