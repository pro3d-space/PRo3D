module ProjectorPoseTest

open System

open Expecto
open Aardvark.Base

open PRo3D.SPICE

// InstrumentProjection.resolve: the one composition every orientation source goes
// through (see ProjectorPose). The first group needs no SPICE at all -- origin frame
// J2000, target = origin -- and pins the geometry; the second group checks that the three
// sources agree with each other on real kernels (self-skipping without them).

let private afc = "HERA_AFC-1"
let private afcHalfFovDeg = 2.75

let private someAttitude =
    let r = Rot3d.RotationEuler(0.3, -0.5, 1.1)
    let q : QuaternionD = Rot3d.op_Explicit r
    q

let private request (pose : ProjectorPose) (boresight : Option<Trafo3d>) : ProjectorRequest =
    {
        instrument  = afc
        spacecraft  = "HERA"
        target      = "DIDYMOS"
        origin      = "DIDYMOS"
        originFrame = "J2000"
        time        = DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc)
        pose        = pose
        boresight   = boresight
        nearFar     = None
        fov         = None
        flipRows    = false
    }

let private resolveOk (req : ProjectorRequest) =
    match InstrumentProjection.resolve req with
    | Ok p -> p
    | Result.Error e -> failtestf "expected the projector to resolve: %s" e

/// The boresight of a projector, in its origin frame (projector -Z, see specialTrafos).
let private boresightOf (p : ResolvedProjector) =
    p.view.Backward.TransformDir(-V3d.OOI) |> Vec.normalize

let private cameraPosition (p : ResolvedProjector) =
    p.view.Backward.TransformPos V3d.Zero

let private ndc (p : ResolvedProjector) (x : V3d) =
    p.full.Forward.TransformPosProj x

/// A sidecar pose whose TRG_POS lies exactly on the boresight, `rangeKm` away.
let private onAxisSidecar (rangeKm : float) =
    let probe = resolveOk (request (ProjectorPose.Sidecar (someAttitude, V3d(0.0, 0.0, 1.0))) None)
    ProjectorPose.Sidecar (someAttitude, boresightOf probe * rangeKm)

let private angleDeg (a : V3d) (b : V3d) =
    Vec.dot (Vec.normalize a) (Vec.normalize b) |> max -1.0 |> min 1.0 |> acos |> Conversion.DegreesFromRadians

let private rotationOf (p : ResolvedProjector) : Rot3d =
    Rot3d.FromM33d(p.view.Forward.UpperLeftM33(), 1e-6)

let private rotationAngleDeg (a : Rot3d) (b : Rot3d) =
    let q : QuaternionD = Rot3d.op_Explicit(a.Inverse * b)
    2.0 * acos (max -1.0 (min 1.0 (abs q.[0]))) |> Conversion.DegreesFromRadians

let private geometry =
    testList "geometry (no SPICE)" [

        test "a sidecar with TRG_POS = 0 is an error naming TRG_POS, not an empty projection" {
            match InstrumentProjection.resolve (request (ProjectorPose.Sidecar (someAttitude, V3d.Zero)) None) with
            | Ok _ -> failtest "a zero TRG_POS puts the camera inside the target; it must not resolve"
            | Result.Error e -> Expect.stringContains e "TRG_POS" "the error says which field is unusable"
        }

        // a sidecar may carry SC_QUAT = 0,0,0,0 (no attitude)
        test "a sidecar with SC_QUAT = 0 is an error naming SC_QUAT, in both sidecar poses" {
            let zero = QuaternionD(0.0, 0.0, 0.0, 0.0)
            for pose in [ ProjectorPose.Sidecar (zero, V3d(0.0, 0.0, 29.2)); ProjectorPose.SidecarAttitudeSpiceRange zero ] do
                match InstrumentProjection.resolve (request pose None) with
                | Ok _ -> failtestf "a zero quaternion is no attitude; %A must not resolve" pose
                | Result.Error e -> Expect.stringContains e "SC_QUAT" "the error says which field is unusable"
        }

        test "the depth range follows the distance to the target" {
            let p = resolveOk (request (onAxisSidecar 29.2) None)
            Expect.floatClose Accuracy.high p.distance 29200.0 "distance from TRG_POS, metres"
            Expect.isLessThan p.near p.distance "near plane in front of the target"
            Expect.isGreaterThan p.far p.distance "far plane behind the target"
            let z = (ndc p V3d.Zero).Z
            Expect.isTrue (z > -1.0 && z < 1.0) (sprintf "the target centre is inside the depth range (ndc z = %f)" z)
        }

        test "a target on the boresight projects to the image centre" {
            let p = resolveOk (request (onAxisSidecar 29.2) None)
            let c = ndc p V3d.Zero
            Expect.isLessThan (abs c.X + abs c.Y) 1e-9 (sprintf "target at ndc %A" c)
        }

        // The registration correction turns the projector about its own position. Applied
        // about the origin body's centre (as before), pitch/yaw left the centre fixed in the
        // image and only swung the camera around the body.
        test "boresight yaw and pitch turn the projector about its own position" {
            let pose = onAxisSidecar 29.2
            let plain = resolveOk (request pose None)
            let expected = tan (Conversion.RadiansFromDegrees 1.0) / tan (Conversion.RadiansFromDegrees afcHalfFovDeg)
            for (label, corr, axis) in [ "yaw", InstrumentProjection.boresightCorrection 0.0 0.0 1.0, 0
                                         "pitch", InstrumentProjection.boresightCorrection 0.0 1.0 0.0, 1 ] do
                let p = resolveOk (request pose (Some corr))
                Expect.isLessThan (Vec.length (cameraPosition p - cameraPosition plain)) 1e-6
                    (sprintf "%s: the projector stays where it is" label)
                Expect.floatClose Accuracy.medium (angleDeg (boresightOf p) (boresightOf plain)) 1.0
                    (sprintf "%s: the boresight turns by the correction" label)
                let c = ndc p V3d.Zero
                let along, across = if axis = 0 then c.X, c.Y else c.Y, c.X
                Expect.floatClose Accuracy.medium (abs along) expected
                    (sprintf "%s: the target moves %f of the half-field along its axis (ndc %A)" label expected c)
                Expect.isLessThan (abs across) 1e-9 (sprintf "%s: and not across it (ndc %A)" label c)
        }

        // Image files stored bottom-up (FITS row order): the flip must mirror the image's
        // vertical axis and nothing else -- same projector position, same boresight.
        test "flipRows mirrors the image vertically and leaves the pose alone" {
            let pose = onAxisSidecar 29.2
            let plain = resolveOk (request pose None)
            let flipped = resolveOk { request pose None with flipRows = true }
            Expect.isLessThan (Vec.length (cameraPosition flipped - cameraPosition plain)) 1e-6 "same projector position"
            let side = plain.view.Backward.TransformDir(V3d(0.3, 0.4, 0.0)) * 500.0
            let a, b = ndc plain side, ndc flipped side
            Expect.floatClose Accuracy.high b.X a.X "columns unchanged"
            Expect.floatClose Accuracy.high b.Y -a.Y "rows mirrored"
            Expect.isTrue (abs a.Y > 0.01) "the probe point is off the horizontal axis"
            Expect.floatClose Accuracy.high (ndc flipped V3d.Zero).Z (ndc plain V3d.Zero).Z "depth unchanged"
        }

        test "the scene's field of view replaces the kernel's" {
            let pose = onAxisSidecar 29.2
            let plain = resolveOk (request pose None)
            let wider = resolveOk { request pose None with fov = Some (11.0, 5.5) }
            let side = plain.view.Backward.TransformDir(V3d(0.3, 0.4, 0.0)) * 500.0
            let a, b = ndc plain side, ndc wider side
            let expected = tan (Conversion.RadiansFromDegrees 2.75) / tan (Conversion.RadiansFromDegrees 5.5)
            Expect.floatClose Accuracy.medium (b.X / a.X) expected "twice the horizontal field: columns shrink accordingly"
            Expect.floatClose Accuracy.high b.Y a.Y "the vertical field is unchanged"
        }

        test "boresight roll turns the image about its centre" {
            let pose = onAxisSidecar 29.2
            let plain = resolveOk (request pose None)
            let rolled = resolveOk (request pose (Some (InstrumentProjection.boresightCorrection 10.0 0.0 0.0)))
            let centre = ndc rolled V3d.Zero
            Expect.isLessThan (abs centre.X + abs centre.Y) 1e-9 "the target stays centred"
            // a point off the axis, in the target plane
            let side = plain.view.Backward.TransformDir V3d.IOO * 500.0
            let a = (ndc plain side).XY
            let b = (ndc rolled side).XY
            Expect.floatClose Accuracy.medium (angleDeg (V3d(a, 0.0)) (V3d(b, 0.0))) 10.0
                "an off-axis point turns by the roll angle"
        }
    ]

/// An epoch the public hera_ops.tm covers with Hera's SPK and attitude. Kernel sets
/// without that coverage (e.g. the pinned test set) skip the group.
let private epoch = DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc)

let private withOpsKernel (f : unit -> unit) =
    if not HeraSpiceTests.hasHera then skiptest "no HERA kernels (PRO3D_SPICE_KERNELS)"
    HeraSpiceTests.ensureOpsKernel ()
    let ck = CooTransformation.getRotationTrafo "HERA_SPACECRAFT" "J2000" epoch
    let hera = CooTransformation.getRelState "HERA" "SUN" "DIDYMOS" epoch "J2000"
    match ck, hera with
    | Some _, Some _ -> f ()
    | _ -> skiptest (sprintf "the loaded hera_ops.tm has no Hera SPK+CK at %s" (epoch.ToString "o"))

let private ckQuaternion () : QuaternionD =
    match CooTransformation.getRotationTrafo "HERA_SPACECRAFT" "J2000" epoch with
    | Some t -> Rot3d.op_Explicit (Rot3d.FromM33d(t.Forward.UpperLeftM33(), 1e-6))
    | None -> failtest "no Hera CK"

let private spiceRequest pose = { request pose None with time = epoch }

let private consistency =
    testList "the three sources agree (hera_ops kernels)" [

        test "SpiceAimed: centred on the target, at the SPICE range, inside the depth range" {
            withOpsKernel (fun () ->
                let p = resolveOk (spiceRequest ProjectorPose.SpiceAimed)
                let range =
                    CooTransformation.getRelState "HERA" "SUN" "DIDYMOS" epoch "J2000"
                    |> Option.map (fun s -> s.pos.Length) |> Option.defaultValue nan
                Expect.floatClose Accuracy.high p.distance range "distance is the SPICE range"
                Expect.isTrue (p.near < p.distance && p.distance < p.far) "the target is inside the depth range"
                let c = ndc p V3d.Zero
                Expect.isLessThan (abs c.X + abs c.Y) 1e-9 (sprintf "target at ndc %A" c))
        }

        test "MbiAttitudeSpiceRange: the SPICE range without TRG_POS, centred, same attitude as the sidecar pose" {
            withOpsKernel (fun () ->
                let q = ckQuaternion ()
                let p = resolveOk (spiceRequest (ProjectorPose.SidecarAttitudeSpiceRange q))
                let spice = resolveOk (spiceRequest ProjectorPose.SpiceAimed)
                Expect.floatClose Accuracy.high p.distance spice.distance "the same SPICE range"
                let c = ndc p V3d.Zero
                Expect.isLessThan (abs c.X + abs c.Y) 1e-9 (sprintf "target at ndc %A" c)
                // TRG_POS (target - spacecraft, km) along the line of sight: the target is the origin
                let sidecar = resolveOk (spiceRequest (ProjectorPose.Sidecar (q, -p.location / 1000.0)))
                Expect.isLessThan (rotationAngleDeg (rotationOf p) (rotationOf sidecar)) 1e-9
                    "the attitude is the quaternion's, exactly as in the sidecar pose")
        }

        // With the CK's own attitude as SC_QUAT, SpiceAimed and MbiAttitudeSpiceRange differ
        // only by SpiceAimed's turn onto the target: the rotation between them is exactly the
        // angle between the CK boresight and the direction SPICE gives to the target. The
        // old Spice mode had an arbitrary roll and failed this by up to 180 degrees.
        test "SpiceAimed keeps the attitude's roll: it differs from the attitude only by the aim" {
            withOpsKernel (fun () ->
                let q = ckQuaternion ()
                let aimed = resolveOk (spiceRequest ProjectorPose.SpiceAimed)
                let attitude = resolveOk (spiceRequest (ProjectorPose.SidecarAttitudeSpiceRange q))
                let aimAngle = angleDeg (boresightOf attitude) (-aimed.location)
                let between = rotationAngleDeg (rotationOf aimed) (rotationOf attitude)
                printfn "[projectorPose] CK boresight is %.3f deg off the SPICE target direction" aimAngle
                Expect.floatClose Accuracy.medium between aimAngle "the poses differ by the aim turn alone")
        }

        test "the sidecar pose with TRG_POS from SPICE puts the projector where SPICE puts the spacecraft" {
            withOpsKernel (fun () ->
                let q = ckQuaternion ()
                let spice = resolveOk (spiceRequest ProjectorPose.SpiceAimed)
                // TRG_POS = target - spacecraft, km, J2000; the origin is the target here
                let trgKm = -spice.location / 1000.0
                let p = resolveOk (spiceRequest (ProjectorPose.Sidecar (q, trgKm)))
                Expect.isLessThan (Vec.length (p.location - spice.location)) 1e-6 "same projector position"
                Expect.floatClose Accuracy.high p.distance spice.distance "same distance")
        }
    ]

let tests () =
    testList "projectorPose" [ geometry; consistency ]
