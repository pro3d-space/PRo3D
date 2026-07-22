module InstrumentProjectionComparisonTest

open System
open System.IO

open Expecto
open Aardvark.Base
open Aardvark.Rendering

open PRo3D.SPICE
open PRo3D.Core

// Visualization.fs computes two orientations per image and only ever returns one:
//   t     = projectOntoQuat ... mbi.sc_quat   (measured spacecraft attitude)
//   spice = projectOnto ...                  (looks SPICE-based, but see below)
//   spice is what actually gets rendered
//
// Turns out getLookAt (used by projectOnto) computes a real attitude-based view,
// then throws it away and returns a plain "look at the target's origin" camera
// instead. So `spice` never depended on SPICE attitude at all. Tests below call
// the real production functions to measure how far apart the two really are.
//
// Also: found a real concurrency bug while writing these tests -- running HSH and
// ASPECT cases in parallel silently changed the HSH answer (122.98 vs 104.23,
// no crash, just wrong). Not two locks/two modules (checked: src/PRo3D.GIS/
// SpiceInterfacing.fs is a dead duplicate, no .fsproj compiles it). Real cause:
// CooTransformation locks each native call individually, but one projection needs
// several calls, so another thread's SPICE call can interleave between them and
// corrupt CSPICE's internal state. Fixed by locking each top-level call in
// InstrumentProjection.fs as one unit; last test below checks it holds.

let private hshFixture = Path.Combine(__SOURCE_DIRECTORY__, "data", "HSH_0CR7B2_250312T062000_1B.mbi.json")
let private aspectFixture = Path.Combine(__SOURCE_DIRECTORY__, "data", "ASP_000000_270323T060000_2B.mbi.json")

let private parseMbi (fixture : string) : InstrumentMetadata.Tiff_Mbi_Json.Mbi =
    if not (File.Exists fixture) then
        skiptest (sprintf "missing fixture: %s" fixture)
    let content = File.ReadAllText fixture
    // load the exact kernel this fixture's own SPICE_MK field names (falls back to
    // the hera_ops.tm baseline if that kernel isn't found -- see HeraSpiceTests.fs)
    HeraSpiceTests.loadKernelForMbiContent content
    match InstrumentMetadata.Tiff_Mbi_Json.tryParseJson content with
    | Result.Error e -> failtestf "expected the MBI metadata to parse, but got error: %A" e
    | Result.Ok mbi -> mbi

// Both functions end with the same Frustum.projTrafo, which isn't a rotation, so
// strip it before comparing: full = X * proj, therefore full * proj.Inverse = X.
let private rotationOf (frustum : Frustum) (full : Trafo3d) : Rot3d =
    let stripped = full * (Frustum.projTrafo frustum).Inverse
    Rot3d.FromM33d(stripped.Forward.UpperLeftM33(), 1e-6)

let private angleBetweenDeg (a : Rot3d) (b : Rot3d) : float =
    let rel = a.Inverse * b
    let q : QuaternionD = Rot3d.op_Explicit(rel)
    let w = max -1.0 (min 1.0 (abs q.[0]))
    2.0 * acos w * 180.0 / Math.PI

// only the rotation is compared, so the FOV/aspect here don't matter
let private placeholderFrustum = Frustum.perspective 5.0 1.0 1.0e9 1.0

// ImageProjection.fs's shader uses these trafos as `projectedPos = MVP * worldPos`
// (Forward = world -> clip), so Backward unprojects clip/NDC back to world -- same
// technique Viewer.fs's own pickRayNdc uses for mouse picking.
let private centerRay (full : Trafo3d) : Ray3d =
    let near = full.Backward.TransformPosProj(V3d(0.0, 0.0, -1.0))
    let far = full.Backward.TransformPosProj(V3d(0.0, 0.0, 1.0))
    Ray3d(near, Vec.normalize (far - near))

// The target body sits at this frame's origin by construction (getLookAt/
// getLookAtQuat literally build a camera looking at V3d.OOO), so a body-radius
// sphere at the origin is the right "round body" stand-in regardless of frame.
let private hitsBody (radiusMeters : float) (ray : Ray3d) : Option<float> =
    let mutable t = 0.0
    if ray.HitsSphere(V3d.Zero, radiusMeters, &t) then Some t else None

let private hshProjection (mbi : InstrumentMetadata.Tiff_Mbi_Json.Mbi) : InstrumentProjection =
    {
        instrumentReferenceFrame = "HERA_HSH"
        target = InstrumentImages.CameraFocus.FocusBody "MARS"
        cameraSource = InstrumentImages.CameraSource.InBody "HERA"
        instrumentName = "HERA_HSH"
        supportBody = "SUN"
        time = mbi.obs_date
        boresightAdjustment = None
    }

let private aspectProjection (mbi : InstrumentMetadata.Tiff_Mbi_Json.Mbi) : InstrumentProjection =
    {
        instrumentReferenceFrame = "MILANI_ASPECT_NIR1"
        target = InstrumentImages.CameraFocus.FocusBody "DIDYMOS"
        cameraSource = InstrumentImages.CameraSource.InBody "MILANI"
        instrumentName = "MILANI_ASPECT_NIR1"
        supportBody = "SUN"
        time = mbi.obs_date
        boresightAdjustment = None
    }

let private computeHshAngle () : Option<float> =
    let mbi = parseMbi hshFixture
    let p = hshProjection mbi
    let referenceFrame = "IAU_MARS"
    let observer = "MARS"
    let instruments = Map.ofList [ p.instrumentName, placeholderFrustum ]
    let position = -mbi.targetPos * 1000.0
    match InstrumentProjection.projectOntoQuat referenceFrame observer instruments p position mbi.sc_quat,
          InstrumentProjection.projectOnto referenceFrame observer instruments p with
    | Some t, Some spice -> Some (angleBetweenDeg (rotationOf placeholderFrustum t) (rotationOf placeholderFrustum spice))
    | _ -> None

let private computeAspectResolves () : bool * bool =
    let mbi = parseMbi aspectFixture
    let p = aspectProjection mbi
    let referenceFrame = "ECLIPJ2000"
    let observer = "DIDYMOS"
    let instruments = Map.ofList [ p.instrumentName, placeholderFrustum ]
    let position = -mbi.targetPos * 1000.0
    let t = InstrumentProjection.projectOntoQuat referenceFrame observer instruments p position mbi.sc_quat
    let spice = InstrumentProjection.projectOnto referenceFrame observer instruments p
    (Option.isSome t, Option.isSome spice)

let tests () =
    testSequenced <| testList "instrumentProjectionComparison" [

        test "getLookAt discards the real SPICE rotation and always looks at the target-body origin" {
            let time = DateTime.Parse("2025-03-12T10:30:20.482190Z", System.Globalization.CultureInfo.InvariantCulture)
            match InstrumentProjection.getLookAt "HERA" "MARS" "IAU_MARS" "SUN" time with
            | None -> skiptest "no SPICE relative state for HERA/MARS/IAU_MARS at this time -- cannot demonstrate"
            | Some view ->
                let expectedForward = (V3d.OOO - view.Location) |> Vec.normalize
                let angle = Vec.dot view.Forward expectedForward |> max -1.0 |> min 1.0 |> acos
                Expect.isLessThan angle 1e-6
                    (sprintf "expected getLookAt to always point at the origin (dead code); got %f rad off, so it may have been fixed" angle)
        }

        test "HSH (Hera/Mars): projectOntoQuat vs projectOnto orientation" {
            match computeHshAngle () with
            | None -> failtest "no SPICE coverage for HERA/MARS/IAU_MARS at this time?"
            | Some angle ->
                printfn "[instrumentProjectionComparison] HSH: t-vs-spice angle = %.2f degrees" angle
                Expect.isFalse (Double.IsNaN angle) "angle must not be NaN"
                Expect.isGreaterThanOrEqual angle 0.0 "angle must be non-negative"
                Expect.isLessThanOrEqual angle 180.0 "angle must be a valid rotation-difference angle"
        }

        // hera_ops.tm only ships Milani's cruise-phase kernels, so at this fixture's real
        // 2027-03-23 Didymos epoch there's no ephemeris/attitude data at all yet. If a
        // kernel update adds that coverage, this starts failing -- replace it with a real
        // angle comparison like the HSH test above.
        test "ASPECT (Milani/Didymos): current SPICE kernel set has no coverage at this fixture's epoch" {
            let tResolves, spiceResolves = computeAspectResolves ()
            Expect.isFalse tResolves "projectOntoQuat unexpectedly resolved -- Milani kernel coverage may have been extended past cruise"
            Expect.isFalse spiceResolves "projectOnto unexpectedly resolved -- same note as above"
        }

        // Real geometric sanity check: does the image's center pixel, unprojected back into
        // the world through each method's actual trafo, land on the target body at all --
        // not just "some angle came out", but a real ray-sphere hit against Mars's real
        // radius. Mars sits at IAU_MARS's origin, so this needs no extra position lookup.
        test "HSH (Hera/Mars): center ray from both t and spice hits round Mars" {
            let mbi = parseMbi hshFixture
            let p = hshProjection mbi
            let referenceFrame = "IAU_MARS"
            let observer = "MARS"
            let instruments = Map.ofList [ p.instrumentName, placeholderFrustum ]
            let position = -mbi.targetPos * 1000.0
            let marsRadius = 3389.5 * 1000.0

            match InstrumentProjection.projectOntoQuat referenceFrame observer instruments p position mbi.sc_quat,
                  InstrumentProjection.projectOnto referenceFrame observer instruments p with
            | None, _ -> failtest "projectOntoQuat (t) returned None"
            | _, None -> failtest "projectOnto (spice) returned None"
            | Some t, Some spice ->
                let tHit = hitsBody marsRadius (centerRay t)
                let spiceHit = hitsBody marsRadius (centerRay spice)
                printfn "[instrumentProjectionComparison] HSH center-ray hit distance: t=%A spice=%A (meters along ray)" tHit spiceHit
                Expect.isSome tHit "quaternion-based (t) center ray should hit round Mars"
                Expect.isSome spiceHit "spice-based center ray should hit round Mars"
        }

        // Same check for ASPECT/Didymos is currently impossible to run for real: it needs
        // the same projectOntoQuat/projectOnto call that the "no coverage" test above already
        // shows returns None for this fixture's epoch. Once that's fixed, replace this with a
        // real hit test against Didymos's real mean radius (409.5m, from CooTransformation's
        // own LatLonAlt2Xyz("didymos", 0, 0, 0) -- see HeraSpiceTests.fs), same shape as HSH.
        test "ASPECT (Milani/Didymos): center-ray hit test is blocked by the same kernel gap" {
            let tResolves, spiceResolves = computeAspectResolves ()
            if tResolves || spiceResolves then
                failtest "projectOntoQuat/projectOnto unexpectedly resolved for ASPECT -- write the real Didymos hit test now (see comment above)"
            else
                skiptest "no SPICE coverage for MILANI/DIDYMOS at this fixture's epoch yet (see the dedicated 'no coverage' test) -- cannot cast a real ray"
        }

        // Reproduces the original race (HSH + ASPECT interleaved across threads) and checks
        // every concurrent HSH result still matches a fresh sequential baseline.
        test "concurrent projectOntoQuat/projectOnto calls no longer corrupt each other's results" {
            let baseline =
                match computeHshAngle () with
                | Some angle -> angle
                | None -> failtest "baseline HSH computation returned None -- cannot run the concurrency check"

            let work =
                Array.append
                    (Array.init 25 (fun _ -> async { return Choice1Of2 (computeHshAngle ()) }))
                    (Array.init 25 (fun _ -> async { return Choice2Of2 (computeAspectResolves ()) }))

            let results = work |> Async.Parallel |> Async.RunSynchronously
            let hshAngles = results |> Array.choose (function Choice1Of2 a -> a | _ -> None)
            Expect.isGreaterThan hshAngles.Length 0 "expected at least one concurrent HSH result to resolve"

            for angle in hshAngles do
                Expect.floatClose Accuracy.high angle baseline
                    (sprintf "concurrent HSH result %.6f drifted from baseline %.6f -- SPICE calls are racing again" angle baseline)
        }
    ]
