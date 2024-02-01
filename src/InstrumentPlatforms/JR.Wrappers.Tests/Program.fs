open System
open Expecto
open JR
open System.IO

let config = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "..", "PRo3D.Base/resources"))
let logDir = Path.Combine(".", "logs")
let spiceKernel = Path.GetFullPath(Path.Combine(config, "pck00010.tpc"))


let init () =
    if not (Directory.Exists(logDir)) then 
        Directory.CreateDirectory(logDir) |> ignore

    if Directory.Exists config then printfn "config exists"

    System.Environment.CurrentDirectory <- config

    let r = JR.CooTransformation.Init(true, logDir)
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
            let v = JR.CooTransformation.GetDllVersion()
            Expect.equal v 2u "returned wrong version"
        }

        test "GetRelState" {
            use _ = init()
            let mutable px,py,pz = 0.0,0.0,0.0
            let mutable vx,vy,vz = 0.0,0.0,0.0
            let result = JR.CooTransformation.GetRelState("MARS", "MARS", "1988 June 13, 3:29:48", "IAU_MARS", &px, &py, &pz, &vx, &vy, &vz)
            Expect.equal result 0 "GetRelState" // returns -1
        }

        use _ = init()
        let init = JR.CooTransformation.AddSpiceKernel(spiceKernel)
        Expect.equal 0 init "spice adding"

        test "LatLonToXyz" {
            let mutable lat,lon,alt = 0.0,0.0,0.0
            let result = JR.CooTransformation.Xyz2LatLonAlt("mars", 1.0, 1.0, 1.0, &lat, &lon, &alt)
            Expect.equal 0 result "Xyz2LatLonAlt result code"
        }
        test "xyzToLatLon" {
            let mutable px,py,pz = 0.0,0.0,0.0
            let result = JR.CooTransformation.LatLonAlt2Xyz("MARS", 18.447, 77.402, 0, &px, &py, &pz)
            printfn "%A" (py, py, pz)
            Expect.equal 0 result "LatLonAlt2Xyz result code"
        }

    ]


[<EntryPoint>]
let main args =
    //runTestsWithCLIArgs [] args (tests ())
    Solarsytsem.run args