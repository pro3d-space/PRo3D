module SpiceTests

// Kernel-independent SPICE tests: these exercise the SPICE stack using only the
// default kernels embedded in PRo3D.SPICE (>= 1.0.9) -- they never touch the
// (non-public) HERA mission kernels. The HERA-specific tests live in
// HeraSpiceTests.fs and skip themselves when those kernels are absent.

#nowarn "9"

open System
open System.IO
open System.Runtime.InteropServices

open Expecto

open Aardvark.Base
open PRo3D.Extensions
open PRo3D.Extensions.FSharp

// Native SPICE libs (CooTransformation + cspice) are loaded via Aardvark.Init()
// (called once in Program.fs), which resolves the PRo3D.SPICE native.zip for all
// assemblies — so no per-module UnpackNativeDependencies is needed here.

/// Direct P/Invoke into the "cspice" native library re-exported by PRo3D.SPICE
/// (>= 1.0.9). Proves SPICE works without going through the CooTransformation
/// wrapper. tkvrsn_c / vnorm_c need no kernels; furnsh_c / str2et_c use the
/// embedded default leapseconds kernel.
module private CSpiceDirect =
    [<DllImport("cspice", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr tkvrsn_c(string item)

    [<DllImport("cspice", CallingConvention = CallingConvention.Cdecl)>]
    extern double vnorm_c(double[] v1)

    [<DllImport("cspice", CallingConvention = CallingConvention.Cdecl)>]
    extern void furnsh_c(string file)

    [<DllImport("cspice", CallingConvention = CallingConvention.Cdecl)>]
    extern void str2et_c(string str, double& et)

let private logDir = Path.Combine(".", "logs")

let private init () =
    if not (Directory.Exists logDir) then Directory.CreateDirectory logDir |> ignore
    let r = CooTransformation.Init(true, Path.Combine(logDir, "CooTrafo.log"), 4, 4)
    if r <> 0 then failwith "init failed."
    { new IDisposable with member _.Dispose() = CooTransformation.DeInit() }

let tests () =
    testSequenced <| testList "spice" [
        test "InitDeInit" {
            let i = init()
            i.Dispose()
        }

        test "CorrectVersion" {
            use _ = init()
            // 7 since PRo3D.SPICE 1.0.10 (this branch upgraded from 1.0.9 / API 5)
            Expect.equal (CooTransformation.GetAPIVersion()) 7u "returned wrong CooTransformation API version"
        }

        test "DefaultKernelsLatLonRoundtrip" {
            use _ = init()
            DefaultSpiceKernels.loadDefaults()

            // Jezero crater on Mars (lat 18.444, lon 77.451) -> XYZ -> back.
            let lat, lon, alt = 18.444, 77.451, 0.0
            let mutable px, py, pz = 0.0, 0.0, 0.0
            Expect.equal 0 (CooTransformation.LatLonAlt2Xyz("MARS", lat, lon, alt, &px, &py, &pz)) "LatLonAlt2Xyz failed"

            let mutable lat2, lon2, alt2 = 0.0, 0.0, 0.0
            Expect.equal 0 (CooTransformation.Xyz2LatLonAlt("MARS", px, py, pz, &lat2, &lon2, &alt2)) "Xyz2LatLonAlt failed"

            Expect.floatClose Accuracy.medium lat lat2 "latitude round-trip"
            Expect.floatClose Accuracy.medium lon lon2 "longitude round-trip"
            Expect.floatClose Accuracy.medium alt alt2 "altitude round-trip"
        }

        test "CSpiceDirectVersionAndVnorm" {
            let version = Marshal.PtrToStringAnsi(CSpiceDirect.tkvrsn_c "TOOLKIT")
            Expect.isNotNull version "tkvrsn_c returned null"
            Expect.stringStarts version "CSPICE" "unexpected CSPICE toolkit version string"
            Expect.floatClose Accuracy.high 5.0 (CSpiceDirect.vnorm_c [| 3.0; 4.0; 0.0 |]) "vnorm_c({3,4,0}) should be 5"
        }

        test "CSpiceDirectStr2Et" {
            // Uses the embedded leapseconds kernel (naif0012.tls) through cspice:
            // furnsh the LSK, then convert a UTC string to ephemeris time.
            use _ = init()
            DefaultSpiceKernels.loadDefaults()  // also extracts the kernels to disk
            let lsk = Path.Combine(DefaultSpiceKernels.defaultKernelDir, "naif0012.tls")
            CSpiceDirect.furnsh_c lsk
            let mutable et = 0.0
            CSpiceDirect.str2et_c("2020-01-01 00:00:00", &et)
            // ~20 years (minus 12h) after the J2000 epoch, in TDB seconds.
            Expect.isTrue (et > 6.0e8 && et < 6.4e8) "ET outside expected range"
        }
    ]
