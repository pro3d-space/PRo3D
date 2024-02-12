#nowarn "9"
open System
open Microsoft.FSharp.NativeInterop  
open Expecto
open JR
open System.IO

let config = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "..", "PRo3D.Base/resources"))
let logDir = Path.Combine(".", "logs")

let spiceFileName = @"F:\pro3d\hera-kernels\kernels\mk\hera_crema_2_0_LPO_ECP_PDP.tm"

let init () =
    if not (Directory.Exists(logDir)) then 
        Directory.CreateDirectory(logDir) |> ignore

    if Directory.Exists config then printfn "config exists"


    let r = JR.CooTransformation.Init(true, Path.Combine(logDir, "CooTrafo.log"), 4, 4)
    if r <> 0 then failwith "init failed."
    { new IDisposable with member x.Dispose() = JR.CooTransformation.DeInit()}

let tests () =
    testSequenced <| testList "init" [
        test "InitDeInit" {
            let i = init()
            i.Dispose()
        }
        test "CorrectVersion" {
            use _ = init()
            let v = JR.CooTransformation.GetAPIVersion()
            Expect.equal v 4u "returned wrong version"
        }

        use _ = init()
        System.Environment.CurrentDirectory <- Path.GetDirectoryName(spiceFileName)
        let init = JR.CooTransformation.AddSpiceKernel(spiceFileName)
        Expect.equal 0 init "spice adding"

        test "GetRelState" {
            let t = "2026-12-03 08:15:00.00"
            let p : double[] = Array.zeroCreate 3
            let m : double[] = Array.zeroCreate 9
            let pdPosVec = fixed &p[0]
            let pdRotMat = fixed &m[0]
            let result = JR.CooTransformation.GetRelState("EARTH", "SUN", "MOON", t, "J2000", NativePtr.toNativeInt pdPosVec, NativePtr.toNativeInt pdRotMat)
            Expect.equal result 0 "GetRelState" // returns -1
        }

        test "LatLonToXyz" {
            let mutable lat,lon,alt = 0.0,0.0,0.0
            let result = JR.CooTransformation.Xyz2LatLonAlt("mars", 1.0, 1.0, 1.0, &lat, &lon, &alt)
            Expect.equal 0 result "Xyz2LatLonAlt result code"
        }
        test "XyzToLatLon" {
            let mutable px,py,pz = 0.0,0.0,0.0
            let result = JR.CooTransformation.LatLonAlt2Xyz("MARS", 18.447, 77.402, 0, &px, &py, &pz)
            printfn "%A" (py, py, pz)
            Expect.equal 0 result "LatLonAlt2Xyz result code"
        }

        test "GetPositionTransformationMatrix" {
            let t = "2026-12-03 08:15:00.00"
            let m : double[] = Array.zeroCreate 9
            let pdMat = fixed &m[0]
            let result = JR.CooTransformation.GetPositionTransformationMatrix("IAU_EARTH", "J2000", t, NativePtr.toNativeInt pdMat)
            Expect.equal 0 result "GetPositionTransformationMatrix_AFC"
        }

    ]


[<EntryPoint>]
let main args =
    Solarsytsem.run args
    runTestsWithCLIArgs [] args (tests ())