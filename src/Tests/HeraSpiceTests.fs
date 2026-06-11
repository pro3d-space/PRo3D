module HeraSpiceTests

#nowarn "9"

open System
open System.Globalization
open System.IO

open Expecto

open FSharp.NativeInterop

open Aardvark.Base

open PRo3D.Extensions
open PRo3D.Extensions.FSharp

let private logDir = Path.Combine(".", "logs")
let private spiceRoot = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..")
let private heraKernel =
    Path.GetFullPath(Path.Combine(spiceRoot, "spice", "kernels", "mk", "hera_ops.tm"))

// HERA tests are the special case: they need the (non-public) HERA mission
// kernels. They self-skip when those kernels are absent (e.g. in CI), or when
// --skip-hera is passed on the command line (runTests uses this for a
// deterministic kernel-free run). The kernel-independent SPICE coverage lives
// in SpiceTests.fs and always runs.
let private skipHeraRequested =
    Environment.GetCommandLineArgs() |> Array.contains "--skip-hera"
let private hasHera = File.Exists heraKernel && not skipHeraRequested

/// init CooTransformation and load the HERA meta-kernel exactly once, and keep
/// it loaded: SPICE's kernel pool is global, so the sequenced HERA tests share
/// one initialised context (re-init/de-init per test loses loaded frames). cd's
/// into the kernel dir while furnishing as the .tm uses relative paths.
let private heraInit =
    lazy (
        if not (Directory.Exists logDir) then Directory.CreateDirectory logDir |> ignore
        let r = CooTransformation.Init(true, Path.Combine(logDir, "CooTrafo.log"), 4, 4)
        if r <> 0 then failwith "init failed."
        // the HERA meta-kernel references sibling kernels by relative path, some
        // resolved lazily at call time -- so stay in the kernel dir (as the
        // original did) rather than restoring the previous working directory.
        Environment.CurrentDirectory <- Path.GetDirectoryName heraKernel
        CooTransformation.AddSpiceKernel(heraKernel) |> ignore
    )

let private heraTests () =
    [
        test "GetRelState" {
            heraInit.Force()
            let t = "2026-12-03 08:15:00.00"
            let p : double[] = Array.zeroCreate 3
            let m : double[] = Array.zeroCreate 9
            let pdPosVec = fixed &p[0]
            let pdRotMat = fixed &m[0]
            let result = CooTransformation.GetRelState("EARTH", "SUN", "MOON", t, "J2000", NativePtr.toNativeInt pdPosVec, NativePtr.toNativeInt pdRotMat)
            Expect.equal result 0 "GetRelState"
        }

        test "GetPositionTransformationMatrix" {
            heraInit.Force()
            let t = "2026-12-03 08:15:00.00"
            let m : double[] = Array.zeroCreate 9
            let pdMat = fixed &m[0]
            let result = CooTransformation.GetPositionTransformationMatrix("IAU_EARTH", "J2000", t, pdMat)
            Expect.equal 0 result "GetPositionTransformationMatrix"
        }

        test "spacecraft in J2000" {
            heraInit.Force()
            let time = DateTime.Parse("2025-03-12 10:30:20.482190Z", CultureInfo.InvariantCulture)
            Expect.isSome (CooTransformation.getRelState "mars" "sun" "hera" time "J2000") "could get hera relstate (J2000)"
        }

        test "spacecraft in HERA_SPACECRAFT" {
            heraInit.Force()
            let time = DateTime.Parse("2025-03-12 10:30:20.482190Z", CultureInfo.InvariantCulture)
            Expect.isSome (CooTransformation.getRelState "mars" "sun" "hera" time "HERA_SPACECRAFT") "could get hera relstate (HERA_SPACECRAFT)"
        }

        test "latlon for phobos" {
            heraInit.Force()
            let mutable x, y, z = 0.0, 0.0, 0.0
            let result = CooTransformation.LatLonAlt2Xyz("phobos", 0.0, 0.0, 0.0, &x, &y, &z)
            Expect.equal 0 result "LatLonAlt2Xyz result code"
        }

        test "transform phobos to eclipj2000" {
            heraInit.Force()
            let time = DateTime.Parse("2025-03-12 10:30:20.482190Z", CultureInfo.InvariantCulture)
            Expect.isSome (CooTransformation.getRotationTrafo "IAU_PHOBOS" "ECLIPJ2000" time) "could transform phobos to eclipj2000"
        }

        test "dimorphos" {
            heraInit.Force()
            let time = DateTime.Parse("2025-03-12 10:30:20.482190Z", CultureInfo.InvariantCulture)
            let trafo = CooTransformation.getRotationTrafo "IAU_DIMORPHOS" "ECLIPJ2000" time
            let mutable x, y, z = 0.0, 0.0, 0.0
            CooTransformation.LatLonAlt2Xyz("dimorphos", 0.0, 0.0, 0.0, &x, &y, &z) |> ignore
            Expect.isSome trafo "could transform dimorphos to eclipj2000"
        }
    ]

let tests () =
    testSequenced <| testList "heraSpice" [
        if hasHera then
            yield! heraTests()
        else
            yield test "heraKernelsAvailable" {
                skiptest (sprintf "HERA spice kernels not found at %s - skipping HERA-specific tests" heraKernel)
            }
    ]
