module DidymosProjectionSpiceTest

open System
open System.Globalization
open System.IO

open Expecto
open Aardvark.Base

open PRo3D.Extensions
open PRo3D.Base.Gis

// User-reported error when projecting an ASPECT image onto Didymos in the GUI:
//   "[SPICE] failed to transform body (body = Didymos, bodyFrame = IAU_DIDYMOS,
//    observer = Didymos, observerFrame = J2000, time = 03/03/2027 03:05:00."
// and, after switching the reference-frame dropdown to J2000, the same failure
// shape again (bodyFrame = J2000 this time). This breaks down every SPICE call
// involved in placing/projecting onto Didymos at that exact time, to find which
// one(s) actually fail and why.

let private reportedTime = DateTime.Parse("2027-03-03T03:05:00", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)

// The ASPECT mbi sidecar's own SPICE_MK field names "hera_plan_v180_20250616_001" --
// hera_ops.tm (the app's default) has no Milani proximity-phase data at all. Load the
// exact kernel the data was generated against.
//
// This used to be a module-level `do` block calling AddSpiceKernel directly, bypassing
// HeraSpiceTests's activeKernel tracking entirely -- a reasonable stopgap back when
// DeInit() was a no-op and kernels only ever accumulated anyway. Now that DeInit()
// actually clears SPICE state (see PRo3D-Extensions), an untracked load like that goes
// stale the moment any other test switches kernels through ensureKernelAt, silently
// corrupting whichever test happens to run afterward. Use the same tracked mechanism
// InstrumentProjectionComparisonTest.fs already uses instead.
let private planKernelPath =
    Path.Combine(HeraSpiceTests.spiceRoot, "spice", "kernels", "mk", "former_versions", "hera_plan_v180_20250616_001.tm")
    |> Path.GetFullPath

let private ensurePlanKernel () : unit =
    HeraSpiceTests.ensureKernelAt [ planKernelPath ]

let tests () =
    // sequencing comes from the suite-wide testSequenced in Program.fs
    testList "didymosProjectionSpice" [

        // GisModels.fs's transformBody calls this with body = observer = the
        // surface's own spice name whenever there's no separate spacecraft
        // context (e.g. viewing/projecting directly onto Didymos) -- it's how
        // the body's own render trafo gets placed at the observer's origin.
        test "getRelState DIDYMOS/SUN/DIDYMOS in J2000 (self-relative, as transformBody calls it)" {
            ensurePlanKernel ()
            let result = PRo3D.SPICE.CooTransformation.getRelState "DIDYMOS" "SUN" "DIDYMOS" reportedTime "J2000"
            printfn "[didymosProjectionSpice] getRelState DIDYMOS/SUN/DIDYMOS/J2000 = %A" result
            Expect.isSome result "expected Didymos's self-relative state (should be ~zero, not a SPICE failure) to resolve"
        }

        // Root cause found: "IAU_DIDYMOS" is not a bug in coverage or kernel
        // switching -- it simply doesn't exist as a frame name in the mission's
        // current kernels (hera_v14.tf and later; confirmed by direct kernel
        // inspection). It was retired at some point in favor of "DIDYMOS_FIXED"
        // (class 5, two-vector, body -658030 -- matches hera_didymos_v06.tpc's
        // BODY-658030_POLE_RA/DEC/PM). GisModels.fs's reference-frame dropdown
        // had this exact name hardcoded wrong too (as "IAU_DIDY", with a
        // "TODO: how is the correct reference frame name?" comment) -- fixed
        // there to "DIDYMOS_FIXED" alongside this test.
        test "getRotationTrafo IAU_DIDYMOS -> J2000 (obsolete frame name, documents the real bug)" {
            ensurePlanKernel ()
            let result = PRo3D.SPICE.CooTransformation.getRotationTrafo "IAU_DIDYMOS" "J2000" reportedTime
            printfn "[didymosProjectionSpice] getRotationTrafo IAU_DIDYMOS->J2000 = %A" result
            Expect.isNone result "IAU_DIDYMOS is not a frame defined by the current Hera kernels -- use DIDYMOS_FIXED instead"
        }

        test "getRotationTrafo DIDYMOS_FIXED -> J2000 (correct current frame name)" {
            ensurePlanKernel ()
            let result = PRo3D.SPICE.CooTransformation.getRotationTrafo "DIDYMOS_FIXED" "J2000" reportedTime
            printfn "[didymosProjectionSpice] getRotationTrafo DIDYMOS_FIXED->J2000 = %A" result
            Expect.isSome result "DIDYMOS_FIXED is the mission's actual body-fixed frame name for Didymos and should resolve"
        }

        test "getRotationTrafo DIMORPHOS_FIXED -> J2000 (secondary's body-fixed frame)" {
            ensurePlanKernel ()
            let result = PRo3D.SPICE.CooTransformation.getRotationTrafo "DIMORPHOS_FIXED" "J2000" reportedTime
            printfn "[didymosProjectionSpice] getRotationTrafo DIMORPHOS_FIXED->J2000 = %A" result
            Expect.isSome result "DIMORPHOS_FIXED should resolve with the plan kernel loaded"
        }

        test "getRotationTrafo J2000 -> J2000 (trivial identity, sanity check)" {
            ensurePlanKernel ()
            let result = PRo3D.SPICE.CooTransformation.getRotationTrafo "J2000" "J2000" reportedTime
            Expect.isSome result "J2000 to itself must always resolve"
        }

        // Reproduces the exact user-reported call shape, bodyFrame = IAU_DIDYMOS --
        // this is the root cause of the original bug report, not a coverage gap.
        test "transformBody DIDYMOS (bodyFrame IAU_DIDYMOS, obsolete name) observed from DIDYMOS in J2000" {
            ensurePlanKernel ()
            let result =
                CooTransformation.transformBody
                    (EntitySpiceName "DIDYMOS") (Some (FrameSpiceName "IAU_DIDYMOS"))
                    (EntitySpiceName "DIDYMOS") (FrameSpiceName "J2000") reportedTime
            printfn "[didymosProjectionSpice] transformBody (IAU_DIDYMOS) = %A" result
            Expect.isNone result "IAU_DIDYMOS is not a real frame name in the current kernels"
        }

        test "transformBody DIDYMOS (bodyFrame DIDYMOS_FIXED, correct name) observed from DIDYMOS in J2000" {
            ensurePlanKernel ()
            let result =
                CooTransformation.transformBody
                    (EntitySpiceName "DIDYMOS") (Some (FrameSpiceName "DIDYMOS_FIXED"))
                    (EntitySpiceName "DIDYMOS") (FrameSpiceName "J2000") reportedTime
            printfn "[didymosProjectionSpice] transformBody (DIDYMOS_FIXED) = %A" result
            Expect.isSome result "transformBody should resolve Didymos's own placement with the correct body-fixed frame name"
        }

        // Reproduces the user's second attempt, bodyFrame switched to J2000.
        test "transformBody DIDYMOS (bodyFrame J2000) observed from DIDYMOS in J2000" {
            ensurePlanKernel ()
            let result =
                CooTransformation.transformBody
                    (EntitySpiceName "DIDYMOS") (Some (FrameSpiceName "J2000"))
                    (EntitySpiceName "DIDYMOS") (FrameSpiceName "J2000") reportedTime
            printfn "[didymosProjectionSpice] transformBody (J2000) = %A" result
            Expect.isSome result "transformBody should resolve Didymos's own placement with an explicit J2000 bodyFrame too"
        }

        // The instrument side: same time, the SPICE calls projectOnto/projectOntoQuat
        // need to actually project an ASPECT image onto Didymos (see
        // InstrumentProjectionComparisonTest.fs for the ASPECT mbi fixture's own
        // 2027-03-23 epoch -- this is the user's actual in-GUI time, 2027-03-03).
        // Resolves now that this file loads the mbi-specified plan kernel (hera_ops.tm
        // alone, the app's default, has no Milani proximity-phase data at all).
        test "getRelState MILANI/SUN/DIDYMOS in ECLIPJ2000 (instrument position lookup)" {
            ensurePlanKernel ()
            let result = PRo3D.SPICE.CooTransformation.getRelState "MILANI" "SUN" "DIDYMOS" reportedTime "ECLIPJ2000"
            printfn "[didymosProjectionSpice] getRelState MILANI/SUN/DIDYMOS/ECLIPJ2000 = %A" result
            Expect.isSome result "Milani proximity-phase position should resolve with the plan kernel loaded"
        }

        test "getRotationTrafo ECLIPJ2000 -> MILANI_ASPECT_NIR1 (instrument attitude lookup)" {
            ensurePlanKernel ()
            let result = PRo3D.SPICE.CooTransformation.getRotationTrafo "ECLIPJ2000" "MILANI_ASPECT_NIR1" reportedTime
            printfn "[didymosProjectionSpice] getRotationTrafo ECLIPJ2000->MILANI_ASPECT_NIR1 = %A" result
            // This used to document a coverage gap (None). With the plan kernel loading
            // completely, the frame chain resolves at this epoch.
            Expect.isSome result "ECLIPJ2000 -> MILANI_ASPECT_NIR1 should resolve with the plan kernel loaded"
        }

        test "getRotationTrafo ECLIPJ2000 -> J2000 (reference-frame conversion, does not need Milani)" {
            ensurePlanKernel ()
            let result = PRo3D.SPICE.CooTransformation.getRotationTrafo "ECLIPJ2000" "J2000" reportedTime
            Expect.isSome result "ECLIPJ2000<->J2000 is a fixed inertial rotation and needs no spacecraft kernel"
        }
    ]
