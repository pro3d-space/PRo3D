module InstrumentProjectionComparisonTest

open System
open System.IO

open Expecto
open Aardvark.Base
open Aardvark.Rendering

open PRo3D.SPICE
open PRo3D.Core

// The orientation sources (ProjectorPose) against the real HERA fixtures. All of them go
// through InstrumentProjection.resolve; these tests compare the Sidecar pose (the
// measured SC_QUAT + TRG_POS) with SpiceAimed (SPK position + spacecraft CK attitude,
// turned onto the target).
//
// History: SpiceAimed replaced a "look at the origin" camera whose up vector was the
// instrument boresight itself -- degenerate whenever the attitude pointed at the target --
// and whose depth range came from the sidecar's TRG_POS even though it never used it
// otherwise. A sidecar with TRG_POS = 0 got a far plane of 100 m and projected nothing.
//
// Also: found a real concurrency bug while writing these tests -- running HSH and
// ASPECT cases in parallel silently changed the HSH answer (122.98 vs 104.23,
// no crash, just wrong). Real cause: CooTransformation locks each native call
// individually, but one projection needs several calls, so another thread's SPICE call
// can interleave between them and corrupt CSPICE's internal state. Fixed by locking
// InstrumentProjection.resolve as one unit; the last test below checks it holds.

let private hshFixture = Path.Combine(__SOURCE_DIRECTORY__, "data", "HSH_0CR7B2_250312T062000_1B.mbi.json")
let private aspectFixture = Path.Combine(__SOURCE_DIRECTORY__, "data", "ASP_000000_270323T060000_2B.mbi.json")

let private parseMbi (fixture : string) : InstrumentMetadata.Tiff_Mbi_Json.Mbi =
    if not (File.Exists fixture) then
        skiptest (sprintf "missing fixture: %s" fixture)
    let content = File.ReadAllText fixture
    // load the exact kernel this fixture's own SPICE_MK field names; a missing pinned
    // kernel fails the test with setup instructions (see HeraSpiceTests.fs)
    HeraSpiceTests.loadKernelForMbiContent content
    match InstrumentMetadata.Tiff_Mbi_Json.tryParseJson content with
    | Result.Error e -> failtestf "expected the MBI metadata to parse, but got error: %A" e
    | Result.Ok mbi -> mbi

let private rotationOf (p : ResolvedProjector) : Rot3d =
    Rot3d.FromM33d(p.view.Forward.UpperLeftM33(), 1e-6)

let private angleBetweenDeg (a : Rot3d) (b : Rot3d) : float =
    let rel = a.Inverse * b
    let q : QuaternionD = Rot3d.op_Explicit(rel)
    let w = max -1.0 (min 1.0 (abs q.[0]))
    2.0 * acos w * 180.0 / Math.PI

// ImageProjection.fs's shader uses these trafos as `projectedPos = MVP * worldPos`
// (Forward = world -> clip), so Backward unprojects clip/NDC back to world -- same
// technique Viewer.fs's own pickRayNdc uses for mouse picking.
let private centerRay (full : Trafo3d) : Ray3d =
    let near = full.Backward.TransformPosProj(V3d(0.0, 0.0, -1.0))
    let far = full.Backward.TransformPosProj(V3d(0.0, 0.0, 1.0))
    Ray3d(near, Vec.normalize (far - near))

// The surface body sits at the origin frame's origin, so a body-radius sphere there is
// the right "round body" stand-in regardless of frame.
let private hitsBody (radiusMeters : float) (ray : Ray3d) : Option<float> =
    let mutable t = 0.0
    if ray.HitsSphere(V3d.Zero, radiusMeters, &t) then Some t else None

let private request (instrument : string) (spacecraft : string) (origin : string) (frame : string)
                    (mbi : InstrumentMetadata.Tiff_Mbi_Json.Mbi) (pose : ProjectorPose) : ProjectorRequest =
    {
        instrument  = instrument
        spacecraft  = spacecraft
        target      = mbi.target |> Option.defaultValue origin
        origin      = origin
        originFrame = frame
        time        = mbi.obs_date
        pose        = pose
        boresight   = None
        nearFar     = None
        fov         = None
        flipRows    = false
    }

let private hsh (mbi : InstrumentMetadata.Tiff_Mbi_Json.Mbi) pose = request "HERA_HSH" "HERA" "MARS" "IAU_MARS" mbi pose
let private aspect (mbi : InstrumentMetadata.Tiff_Mbi_Json.Mbi) pose = request "MILANI_ASPECT_NIR1" "MILANI" "DIDYMOS" "ECLIPJ2000" mbi pose
let private sidecar (mbi : InstrumentMetadata.Tiff_Mbi_Json.Mbi) = ProjectorPose.Sidecar (mbi.sc_quat, mbi.targetPos)

let private computeHshAngle () : Option<float> =
    let mbi = parseMbi hshFixture
    match InstrumentProjection.resolve (hsh mbi (sidecar mbi)),
          InstrumentProjection.resolve (hsh mbi ProjectorPose.SpiceAimed) with
    | Ok t, Ok spice -> Some (angleBetweenDeg (rotationOf t) (rotationOf spice))
    | _ -> None

let tests () =
    testList "instrumentProjectionComparison" [

        test "HSH (Hera/Mars): Sidecar vs SpiceAimed orientation" {
            match computeHshAngle () with
            | None -> failtest "no SPICE coverage for HERA/MARS/IAU_MARS at this time?"
            | Some angle ->
                printfn "[instrumentProjectionComparison] HSH: sidecar-vs-spice angle = %.3f degrees" angle
                Expect.isFalse (Double.IsNaN angle) "angle must not be NaN"
                // Measured 0.005 degrees: the sidecar's attitude and SPICE's (CK, turned onto
                // Mars) are the same pointing. The old Spice mode's look-at camera, with the
                // boresight as its up vector, was 104-123 degrees off here.
                Expect.isLessThan angle 0.5 "the sidecar pose and the SPICE pose must agree on the attitude"
        }

        // The quaternion path takes attitude from the mbi sidecar and needs no spacecraft
        // CK, so with the fixture's own plan kernel loaded it must resolve. SpiceAimed
        // depends on Milani SPK coverage at this epoch; report it, don't assert.
        test "ASPECT (Milani/Didymos): the sidecar pose resolves from the mbi sidecar" {
            let mbi = parseMbi aspectFixture
            let t = InstrumentProjection.resolve (aspect mbi (sidecar mbi))
            let spice = InstrumentProjection.resolve (aspect mbi ProjectorPose.SpiceAimed)
            printfn "[instrumentProjectionComparison] ASPECT resolves: sidecar=%A spice=%s"
                (Result.isOk t) (match spice with Ok _ -> "ok" | Result.Error e -> e)
            Expect.isTrue (Result.isOk t)
                "the sidecar pose should resolve for ASPECT/Didymos -- attitude comes from the sidecar quaternion, no CK required"
        }

        // Real geometric sanity check: does the image's center pixel, unprojected back into
        // the world through each pose's actual trafo, land on the target body at all.
        test "HSH (Hera/Mars): center ray from the sidecar and the SPICE pose hits round Mars" {
            let mbi = parseMbi hshFixture
            let marsRadius = 3389.5 * 1000.0
            match InstrumentProjection.resolve (hsh mbi (sidecar mbi)),
                  InstrumentProjection.resolve (hsh mbi ProjectorPose.SpiceAimed) with
            | Result.Error e, _ -> failtestf "sidecar pose: %s" e
            | _, Result.Error e -> failtestf "SPICE pose: %s" e
            | Ok t, Ok spice ->
                let tHit = hitsBody marsRadius (centerRay t.full)
                let spiceHit = hitsBody marsRadius (centerRay spice.full)
                printfn "[instrumentProjectionComparison] HSH center-ray hit distance: t=%A spice=%A (meters along ray)" tHit spiceHit
                Expect.isSome tHit "sidecar center ray should hit round Mars"
                Expect.isSome spiceHit "SPICE center ray should hit round Mars"
        }

        // Unproject the image's centre pixel through the quaternion trafo and require it to
        // hit a round Didymos (mean radius ~409.5 m). The sidecar's boresight points at the
        // body centre to ~1e-4 rad, so the centre ray must strike near dead centre.
        test "ASPECT (Milani/Didymos): center ray from the sidecar pose hits round Didymos" {
            let mbi = parseMbi aspectFixture
            match InstrumentProjection.resolve (aspect mbi (sidecar mbi)) with
            | Result.Error e -> failtestf "sidecar pose for ASPECT: %s" e
            | Ok t ->
                let hit = hitsBody 409.5 (centerRay t.full)
                printfn "[instrumentProjectionComparison] ASPECT center-ray hit distance: %A (meters along ray)" hit
                Expect.isSome hit "sidecar center ray should hit round Didymos"
        }

        // Reproduces the original race: resolve calls, each several native calls, running
        // interleaved across threads. All 50 items use one fixture, so one metakernel stays
        // loaded and every result has to resolve.
        test "concurrent resolve calls do not corrupt each other's results" {
            let baseline =
                match computeHshAngle () with
                | Some angle -> angle
                | None -> failtest "baseline HSH computation returned None -- cannot run the concurrency check"

            let work = Array.init 50 (fun _ -> async { return computeHshAngle () })
            let results = work |> Async.Parallel |> Async.RunSynchronously

            let unresolved = results |> Array.filter Option.isNone |> Array.length
            Expect.equal unresolved 0
                "every concurrent HSH computation must resolve -- the kernel it needs stays loaded throughout"

            for angle in results |> Array.choose id do
                Expect.floatClose Accuracy.high angle baseline
                    (sprintf "concurrent HSH result %.6f drifted from baseline %.6f -- SPICE calls are racing again" angle baseline)
        }
    ]
