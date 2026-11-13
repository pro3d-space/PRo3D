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

    type CameraFocus = 
        | FocusBody of focusedBody : string

    type CameraSource =
        | InBody of body : string

    type Intrinsics with
        member x.ProjTrafo = 
            match x with
            | Intrinsics.Plain frustum -> Frustum.projTrafo frustum

type InstrumentProjection = 
    {
        instrumentReferenceFrame : string
        target : InstrumentImages.CameraFocus
        cameraSource : InstrumentImages.CameraSource
        instrumentName : string
        supportBody : string
        time : DateTime
        boresightAdjustment : Option<Trafo3d>
    }

module InstrumentProjection =

    /// Full field of view of one instrument in degrees: `horizontal` along the image's
    /// width, `vertical` along its height.
    type Fov = { horizontal : float; vertical : float }

    /// The kernels' field of view of each instrument, keyed by SPICE frame name (the
    /// key projectOnto/projectOntoQuat look up via InstrumentProjection.instrumentName).
    /// These are only the defaults: a scene can override them per instrument (the
    /// projected-images panel's Instruments section), see `instrumentsWith`.
    /// Near/far are NOT part of an instrument's intrinsics -- they depend on how far
    /// away the target is -- hence `instruments` takes them as parameters.
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
    // per-thread reentrant, so nesting (projectOnto* calls getLookAt*) is safe.
    let private spiceCallLock = obj()

    /// Runs `f` as one unit against every SPICE call in this module.
    ///
    /// For callers that replace the kernel pool rather than read it -- switchKernel is
    /// DeInit + Init + furnsh. A swap landing between the getRelState and getRotationTrafo
    /// of one projection answers it from two kernel sets; a swap landing inside a native
    /// call is an access violation. Reentrant, so a whole load-then-project sequence fits.
    let withSpiceLock (f : unit -> 'a) : 'a = lock spiceCallLock f

    let getLookAt (viewerBody : string) (observer : string) (referenceFrame : string) (supportBody : string) (time : DateTime) =
        lock spiceCallLock (fun () ->
            let afc1Pos = CooTransformation.getRelState viewerBody supportBody observer time referenceFrame
            match afc1Pos with
            | Some targetState ->
                let rot = targetState.rot
                let t = Trafo3d.FromBasis(-rot.C1, rot.C0, rot.C2, targetState.pos)
                CameraView.ofTrafo t.Inverse |> Some
                CameraView.lookAt targetState.pos V3d.OOO V3d.OOI |> Some
            | _ ->
                None)

    let projectOnto (referenceFrame : string) (observer : string) (instruments : Map<string, Frustum>) (p : InstrumentProjection) =
        lock spiceCallLock (fun () ->
            let bodyToWorld = CooTransformation.getRotationTrafo referenceFrame p.instrumentReferenceFrame p.time
            match bodyToWorld, p.target, p.cameraSource, Map.tryFind p.instrumentName instruments with
            | Some bodyToWorld, InstrumentImages.FocusBody target, InstrumentImages.InBody source, Some frustum ->
                match getLookAt source observer p.instrumentReferenceFrame p.supportBody p.time with
                | Some view ->
                    let boresightAdjustedView = p.boresightAdjustment |> Option.defaultValue Trafo3d.Identity
                    bodyToWorld * boresightAdjustedView * CameraView.viewTrafo view * (Frustum.projTrafo frustum) |> Some
                | None -> None
            | _ -> None)

    let getLookAtQuat (viewerBody : string) (observer : string) (referenceFrame : string)
                      (supportBody : string) (time : DateTime) (position : V3d) (sc_quat : QuaternionD) =
        lock spiceCallLock (fun () ->
            let afc1Pos = CooTransformation.getRelState viewerBody supportBody observer time referenceFrame
            match afc1Pos with
            | Some targetState ->
                let pos = targetState.pos
                let rot = Rot3d(sc_quat.Conjugated)
                let frame = M33d.Rotation rot
                let t = Trafo3d.FromBasis(-frame.C0, -frame.C1, -frame.C2, V3d.Zero)
                let u = CameraView.ofTrafo t
                let z = CameraView.withLocation position u
                Some z
            | _ ->
                None)

    let specialTrafos =
        Map.ofList [
            "HERA_AFC-2", Trafo3d.FromOrthoNormalBasis(V3d.OIO, V3d.IOO, V3d.OOI)
            "HERA_AFC-1", Trafo3d.FromOrthoNormalBasis(-V3d.OIO, -V3d.IOO, V3d.OOI)
            "HERA_HSH", Trafo3d.FromOrthoNormalBasis(-V3d.OIO, -V3d.IOO, V3d.OOI)
            // hera_milani_v05.tf defines all four ASPECT channel frames (VIS/NIR1/NIR2/SWIR)
            // as a zero-degree TKFRAME offset from MILANI_SPACECRAFT, so unlike the Hera-mounted
            // instruments above there is no known axis remap to apply here. Identity until this
            // is calibrated against a real rendered ASPECT image.
            // getLookAtQuat builds the camera basis as FromBasis(-C0, -C1, -C2), whose
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

    let projectOntoQuat (referenceFrame : string) (observer : string) (instruments : Map<string, Frustum>)
                        (p : InstrumentProjection) (position : V3d) (sc_quat : QuaternionD) =
        lock spiceCallLock (fun () ->
            let toSpaceCraft = CooTransformation.getRotationTrafo referenceFrame "J2000" p.time
            match p.target, p.cameraSource, Map.tryFind p.instrumentName instruments, toSpaceCraft with
            | InstrumentImages.FocusBody target, InstrumentImages.InBody source, Some frustum, Some toSpaceCraft ->
                // Only getLookAtQuat is required: attitude comes from the mbi sidecar's
                // sc_quat, not from a spacecraft CK. This used to also demand getLookAt
                // resolve, which needlessly failed the whole projection whenever the CK
                // had no coverage at the epoch -- e.g. Milani/ASPECT at Didymos, where
                // the plan kernel ships SPK but no real spacecraft attitude.
                match getLookAtQuat source observer referenceFrame p.supportBody p.time position sc_quat with
                | Some view ->
                    toSpaceCraft * CameraView.viewTrafo view * specialTrafos[p.instrumentName] * (Frustum.projTrafo frustum) |> Some
                | _ -> None
            | _ -> None)

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