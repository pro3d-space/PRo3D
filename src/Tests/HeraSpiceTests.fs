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

module Coo = PRo3D.Base.CooTransformation

let logDir = Path.Combine(".", "logs")
let spiceRoot = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..")
let spiceFileName = Path.Combine(spiceRoot, "spice", "kernels", "mk", "hera_ops.tm")

do Aardvark.Base.Aardvark.UnpackNativeDependencies(typeof<CooTransformation.RelState>.Assembly)

let init () =
    if not (Directory.Exists(logDir)) then 
        Directory.CreateDirectory(logDir) |> ignore

    let r = CooTransformation.Init(true, Path.Combine(logDir, "CooTrafo.log"), 4, 4)
    if r <> 0 then failwith "init failed."
    { new IDisposable with member x.Dispose() = CooTransformation.DeInit()}


let heraSpecificTests () = 
    testList "heraSpecificCases" [
        test "spacecraft in J2000" {
            let time = DateTime.Parse("2025-03-12 10:30:20.482190Z", CultureInfo.InvariantCulture)
            Expect.isSome (CooTransformation.getRelState "mars" "sun" "hera" time "J2000") "could get hera relstate for hera"
        }
        test "spacecraft in HERA_SPACECRAFT" {
            let time = DateTime.Parse("2025-03-12 10:30:20.482190Z", CultureInfo.InvariantCulture)
            Expect.isSome (CooTransformation.getRelState "mars" "sun" "hera" time "HERA_SPACECRAFT") "could get hera relstate for hera"
        }

        test "latlon for phobos" {
            let mutable x,y,z = 0.0,0.0,0.0
            let result = CooTransformation.LatLonAlt2Xyz("phobos", 0.0, 0.0, 0.0, &x, &y, &z)
            let pos = V3d(x,y,z)
            let dFromCenter = pos.Length
            Expect.equal 0 result "Xyz2LatLonAlt result code"
        }

        test "transform phobos to eclipj2000" {
            let time = DateTime.Parse("2025-03-12 10:30:20.482190Z", CultureInfo.InvariantCulture)
            let trafo = CooTransformation.getRotationTrafo "IAU_PHOBOS" "ECLIPJ2000" time
            Expect.isSome trafo "could transform phobos to eclipj2000"
        }

        test "dimorphos" {
            let time = DateTime.Parse("2025-03-12 10:30:20.482190Z", CultureInfo.InvariantCulture)
            let trafo = CooTransformation.getRotationTrafo "IAU_DIMORPHOS" "ECLIPJ2000" time
            let mutable x,y,z = 0.0,0.0,0.0
            let r = CooTransformation.LatLonAlt2Xyz("dimorphos", 0.0, 0.0, 0.0, &x, &y, &z)

            Expect.isSome trafo "could transform phobos to eclipj2000"
        }

        test "latlonalt to xyz for dimorphos (Spherical convention)" {
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
    testSequenced <| testList "init" [
        test "InitDeInit" {
            let i = init()
            i.Dispose()
        }
        test "CorrectVersion" {
            use _ = init()
            let v = CooTransformation.GetAPIVersion()
            Expect.equal v 5u "returned wrong version"
        }

        use _ = init()
        let fullPath = Path.GetFullPath(spiceFileName)
        System.Environment.CurrentDirectory <- Path.GetDirectoryName(fullPath)
        let init = CooTransformation.AddSpiceKernel(fullPath)
        Expect.equal 0 init "spice adding"

        test "GetRelState" {
            let t = "2026-12-03 08:15:00.00"
            let p : double[] = Array.zeroCreate 3
            let m : double[] = Array.zeroCreate 9
            let pdPosVec = fixed &p[0]
            let pdRotMat = fixed &m[0]
            let result = CooTransformation.GetRelState("EARTH", "SUN", "MOON", t, "J2000", NativePtr.toNativeInt pdPosVec, NativePtr.toNativeInt pdRotMat)
            Expect.equal result 0 "GetRelState" // returns -1
        }

        test "LatLonToXyz" {
            let mutable lat,lon,alt = 0.0,0.0,0.0
            let result = CooTransformation.Xyz2LatLonAlt("mars", 1.0, 1.0, 1.0, &lat, &lon, &alt)
            Expect.equal 0 result "Xyz2LatLonAlt result code"
        }
        test "XyzToLatLon" {
            let mutable px,py,pz = 0.0,0.0,0.0
            let result = CooTransformation.LatLonAlt2Xyz("MARS", 18.447, 77.402, 0, &px, &py, &pz)
            printfn "%A" (py, py, pz)
            Expect.equal 0 result "LatLonAlt2Xyz result code"
        }

        test "GetPositionTransformationMatrix" {
            let t = "2026-12-03 08:15:00.00"
            let m : double[] = Array.zeroCreate 9
            let pdMat = fixed &m[0]
            let result = CooTransformation.GetPositionTransformationMatrix("IAU_EARTH", "J2000", t, pdMat)
            Expect.equal 0 result "GetPositionTransformationMatrix"
        }

        heraSpecificTests()

    ]
