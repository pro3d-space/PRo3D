module HeraSpiceTests

open System.Globalization

#nowarn "9"

open System
open System.IO

open Expecto

open FSharp.NativeInterop

open Aardvark.Base

open PRo3D.Extensions
open PRo3D.Extensions.FSharp
open PRo3D.SPICE

module Coo = PRo3D.Base.CooTransformation

let logDir = Path.Combine(".", "logs")

/// The `kernels` directory of a HERA dataset -- the one holding `mk`, `ck`, `spk`, ... .
///
/// From $PRO3D_SPICE_KERNELS, which may name either the dataset root or `kernels` itself
/// (as PRo3D.Tool reads it); otherwise a `spice` mirror next to the PRo3D clone. Not
/// found means `hasHera` is false and the kernel tests skip.
/// See docs/tests/SpiceKernels.md.
let kernelsDir =
    let sibling = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "spice", "kernels")
    let fromEnv =
        match Environment.GetEnvironmentVariable "PRO3D_SPICE_KERNELS" with
        | null | "" -> []
        | value -> [ value; Path.Combine(value, "kernels") ]
    fromEnv @ [ sibling ]
    |> List.tryFind (fun dir -> Directory.Exists(Path.Combine(dir, "mk")))
    |> Option.defaultValue sibling

/// Directory holding the meta-kernels (`hera_ops.tm`, `hera_plan.tm`, `former_versions/...`).
let mkDir = Path.Combine(kernelsDir, "mk")

let spiceFileName = Path.Combine(mkDir, "hera_ops.tm")

// The HERA mission kernels are ~1.3 GB, too large to commit. These tests self-skip
// without them, and on --skip-hera even with them (runTests passes it, for a
// deterministic kernel-free run). Kernel-independent SPICE coverage is in SpiceTests.fs.
// CI gets them via scripts/fetch-spice-kernels.sh; see docs/tests/SpiceKernels.md.
/// Public so other kernel-using tests can honour the flag too: --skip-hera promises a
/// deterministic kernel-free run even on a machine that has the kernels.
let skipHeraRequested =
    Environment.GetCommandLineArgs() |> Array.contains "--skip-hera"
let hasHera = File.Exists spiceFileName && not skipHeraRequested

do Aardvark.Base.Aardvark.UnpackNativeDependencies(typeof<CooTransformation.RelState>.Assembly)

let private spiceLock = obj()
let private activeKernel : string option ref = ref None

let init () =
    if not (Directory.Exists(logDir)) then
        Directory.CreateDirectory(logDir) |> ignore

    let r = CooTransformation.Init(true, Path.Combine(logDir, "CooTrafo.log"), 4, 4)
    if r <> 0 then failwith "init failed."
    { new IDisposable with
        member x.Dispose() =
            CooTransformation.DeInit()
            // DeInit empties the pool, so the record of what is loaded has to go too --
            // otherwise the next ensureKernelAt for that kernel decides it need not act.
            lock spiceLock (fun () -> activeKernel.Value <- None) }

// One meta-kernel at a time: there is no per-kernel unload, and layering two of them
// makes conflicting CK segments for the same frame silently win over each other. So track
// which one is active and reset the pool (DeInit + Init + furnsh) when a scenario needs a
// different one. An mbi sidecar's SPICE_MK field says which that is. Every caller has to
// re-request its kernel immediately before its SPICE calls, since anyone may have swapped
// it since.
//
// Switching goes through CooTransformation.switchKernel, as the viewer does, for two
// reasons documented in docs/tests/SpiceKernels.md: it rewrites PATH_VALUES to an absolute
// path before the furnsh, so nothing SPICE stores depends on the working directory (this
// used to chdir instead, which broke once a swap crossed from mk/ to mk/former_versions/),
// and the swap has to hold the projection lock, because it empties the pool.
let private cooTrafoInitialized =
    lazy (
        // switchKernel reinitializes with the log directory initCooTrafo recorded, so the
        // suite has to have been through initCooTrafo at least once -- it has, unless
        // someone runs only these tests.
        let appData =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pro3D")
        Coo.initCooTrafo None appData)

let ensureKernelAt (candidates : string list) : unit =
    lock spiceLock (fun () ->
        match candidates |> List.tryFind File.Exists with
        | None -> ()
        | Some path ->
            let fullPath = Path.GetFullPath(path)
            if activeKernel.Value <> Some fullPath then
                cooTrafoInitialized.Force()
                // The projection lock, not just this module's: a swap during another
                // thread's SPICE call mixes two kernel sets, or crashes it.
                InstrumentProjection.withSpiceLock (fun () ->
                    match Coo.switchKernel (Path.GetDirectoryName fullPath) (Path.GetFileName fullPath) with
                    | Some _ -> ()
                    | None -> printfn "[HeraSpiceTests] failed to load kernel %s" fullPath)
                activeKernel.Value <- Some fullPath)

let ensureOpsKernel () : unit =
    ensureKernelAt [ spiceFileName ]

let loadKernelForMbiContent (mbiJsonContent : string) : unit =
    let mkName =
        FSharp.Data.JsonValue.TryParse(mbiJsonContent)
        |> Option.bind PRo3D.Core.InstrumentMetadata.Tiff_Mbi_Json.tryExtractSpiceMk
    match mkName with
    | None -> ensureOpsKernel ()
    | Some mkName ->
        ensureKernelAt
            [ Path.Combine(mkDir, mkName + ".tm")
              Path.Combine(mkDir, "former_versions", mkName + ".tm")
              spiceFileName ]


let heraSpecificTests () = 
    testList "heraSpecificCases" [
        test "spacecraft in J2000" {
            ensureOpsKernel ()
            let time = DateTime.Parse("2025-03-12 10:30:20.482190Z", CultureInfo.InvariantCulture)
            Expect.isSome (CooTransformation.getRelState "mars" "sun" "hera" time "J2000") "could get hera relstate for hera"
        }
        test "spacecraft in HERA_SPACECRAFT" {
            ensureOpsKernel ()
            let time = DateTime.Parse("2025-03-12 10:30:20.482190Z", CultureInfo.InvariantCulture)
            Expect.isSome (CooTransformation.getRelState "mars" "sun" "hera" time "HERA_SPACECRAFT") "could get hera relstate for hera"
        }

        test "latlon for phobos" {
            ensureOpsKernel ()
            let mutable x,y,z = 0.0,0.0,0.0
            let result = CooTransformation.LatLonAlt2Xyz("phobos", 0.0, 0.0, 0.0, &x, &y, &z)
            let pos = V3d(x,y,z)
            let dFromCenter = pos.Length
            Expect.equal 0 result "Xyz2LatLonAlt result code"
        }

        test "transform phobos to eclipj2000" {
            ensureOpsKernel ()
            let time = DateTime.Parse("2025-03-12 10:30:20.482190Z", CultureInfo.InvariantCulture)
            let trafo = CooTransformation.getRotationTrafo "IAU_PHOBOS" "ECLIPJ2000" time
            Expect.isSome trafo "could transform phobos to eclipj2000"
        }

        test "dimorphos" {
            ensureOpsKernel ()
            let time = DateTime.Parse("2025-03-12 10:30:20.482190Z", CultureInfo.InvariantCulture)
            // IAU_DIMORPHOS is not a frame in the current kernels -- retired in favour of
            // DIMORPHOS_FIXED, exactly like IAU_DIDYMOS (see DidymosProjectionSpiceTest).
            // This test used to pass only because getRotationTrafo returned Some Identity
            // on failure; now that failure is an honest None, assert both directions.
            let retired = CooTransformation.getRotationTrafo "IAU_DIMORPHOS" "ECLIPJ2000" time
            let mutable x,y,z = 0.0,0.0,0.0
            let r = CooTransformation.LatLonAlt2Xyz("dimorphos", 0.0, 0.0, 0.0, &x, &y, &z)

            Expect.isNone retired "IAU_DIMORPHOS is a retired frame name and must not resolve"
            // the positive DIMORPHOS_FIXED check lives in DidymosProjectionSpiceTest,
            // which has the plan kernel (with the Didymos-system data) loaded
        }

        test "latlonalt to xyz for dimorphos (Spherical convention)" {
            ensureOpsKernel ()
            // Dimorphos routes through the F# Spherical (LATREC) path because
            // it is tri-axial and has no PCK rotation model.
            // In the Spherical convention, SphericalCoo.altitude stores the
            // radial distance from the body centre (matching SPICE reclat),
            // so altitude=0 would mean the origin; use a non-zero radius.
            let sc : Coo.SphericalCoo =
                { latitude = 0.0; longitude = 0.0; altitude = 89.5; radian = 0.0 }
            let xyz = Coo.tryGetXYZFromLatLonAlt sc PRo3D.Base.Planet.Dimorphos
            Expect.isSome xyz "tryGetXYZFromLatLonAlt should return Some for Dimorphos"
            let pos = xyz.Value
            Expect.isGreaterThan pos.Length 0.0 "Dimorphos surface point should be non-zero distance from center"
        }

        test "roundtrip xyz <-> latlonalt for dimorphos (Spherical)" {
            ensureOpsKernel ()
            // Spherical (LATREC) should round-trip to numerical precision.
            let original = V3d(50.0, 30.0, 40.0)
            let sc = Coo.tryGetLatLonAlt PRo3D.Base.Planet.Dimorphos original
            Expect.isSome sc "tryGetLatLonAlt should return Some for Dimorphos"
            let recovered = Coo.tryGetXYZFromLatLonAlt sc.Value PRo3D.Base.Planet.Dimorphos
            Expect.isSome recovered "tryGetXYZFromLatLonAlt should return Some for Dimorphos"
            let drift = (recovered.Value - original).Length
            Expect.isLessThan drift 1e-9 (sprintf "Spherical round-trip drift = %g should be < 1e-9" drift)
        }

        test "roundtrip xyz <-> latlonalt for mars (Planetographic)" {
            // Native PGRREC path; round-trip within native numerical precision.
            let original = V3d(3500000.0, 100000.0, 200000.0)
            let sc = Coo.tryGetLatLonAlt PRo3D.Base.Planet.Mars original
            Expect.isSome sc "tryGetLatLonAlt should return Some for Mars"
            let recovered = Coo.tryGetXYZFromLatLonAlt sc.Value PRo3D.Base.Planet.Mars
            Expect.isSome recovered "tryGetXYZFromLatLonAlt should return Some for Mars"
            let drift = (recovered.Value - original).Length
            Expect.isLessThan drift 1.0 (sprintf "Mars round-trip drift = %g m should be < 1 m" drift)
        }

        test "latlonalt to xyz for didymos" {
            let mutable x,y,z = 0.0,0.0,0.0
            let result = CooTransformation.LatLonAlt2Xyz("didymos", 0.0, 0.0, 0.0, &x, &y, &z)
            let pos = V3d(x,y,z)
            Expect.equal 0 result "LatLonAlt2Xyz for didymos (geographical model available?)"
            Expect.isGreaterThan pos.Length 0.0 "didymos surface point should be non-zero distance from center"
        }
    ]


let tests () =
    if not hasHera then
        testList "init" [
            test "heraKernelsAvailable" {
                skiptest (sprintf "HERA spice kernels not found at %s (or --skip-hera) -- skipping HERA-specific tests" spiceFileName)
            }
        ]
    else
    testList "init" [
        test "InitDeInit" {
            let i = init()
            i.Dispose()
        }
        test "CorrectVersion" {
            use _ = init()
            let v = CooTransformation.GetAPIVersion()
            Expect.equal v 7u "returned wrong version"
        }

        // No top-level `use _ = init()` here: this is a plain list literal, so
        // any `use` binding placed directly in it disposes (= DeInit, which now
        // actually clears the kernel pool) as soon as the list finishes being
        // built -- i.e. immediately, before any test body below actually runs.
        // That's harmless only as long as DeInit() is a no-op. Each test below
        // already calls ensureOpsKernel() itself, and ensureKernelAt() already
        // does its own Init() on first use (activeKernel starts at None), so
        // nothing here needs a separate top-level Init() at all.

        test "GetRelState" {
            ensureOpsKernel ()
            let t = "2026-12-03 08:15:00.00"
            let p : double[] = Array.zeroCreate 3
            let m : double[] = Array.zeroCreate 9
            let pdPosVec = fixed &p[0]
            let pdRotMat = fixed &m[0]
            let result = CooTransformation.GetRelState("EARTH", "SUN", "MOON", t, "J2000", NativePtr.toNativeInt pdPosVec, NativePtr.toNativeInt pdRotMat)
            Expect.equal result 0 "GetRelState" // returns -1
        }

        test "LatLonToXyz" {
            ensureOpsKernel ()
            let mutable lat,lon,alt = 0.0,0.0,0.0
            let result = CooTransformation.Xyz2LatLonAlt("mars", 1.0, 1.0, 1.0, &lat, &lon, &alt)
            Expect.equal 0 result "Xyz2LatLonAlt result code"
        }
        test "XyzToLatLon" {
            ensureOpsKernel ()
            let mutable px,py,pz = 0.0,0.0,0.0
            let result = CooTransformation.LatLonAlt2Xyz("MARS", 18.447, 77.402, 0, &px, &py, &pz)
            printfn "%A" (py, py, pz)
            Expect.equal 0 result "LatLonAlt2Xyz result code"
        }

        test "GetPositionTransformationMatrix" {
            ensureOpsKernel ()
            let t = "2026-12-03 08:15:00.00"
            let m : double[] = Array.zeroCreate 9
            let pdMat = fixed &m[0]
            let result = CooTransformation.GetPositionTransformationMatrix("IAU_EARTH", "J2000", t, pdMat)
            Expect.equal 0 result "GetPositionTransformationMatrix"
        }

        heraSpecificTests()

    ]
