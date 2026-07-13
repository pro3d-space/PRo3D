open Expecto
open NUnit

// Initialise Aardvark up front: this sets up native-dependency resolution
// (incl. the PRo3D.SPICE natives CooTransformation + cspice) into its managed
// temp directory for every assembly, so the SPICE P/Invokes resolve.
do Aardvark.Base.Aardvark.Init()


let allTests () : Test =
    testList "all tests" [
        // default: kernel-independent tests (use only the default SPICE kernels)
        GeoJsonRework.Tests.tests()
        SpiceTests.tests()

        // special case: HERA tests self-skip when their kernels are absent or
        // when --skip-hera is passed (see HeraSpiceTests).
        HeraSpiceTests.tests()
        FeatureTests.tests()
    ]


module NunitEntry =

    open NUnit.Framework
    open FsUnit

    [<Test>]
    let ``[expecto tests]``() =
        let r = allTests () |> runTests Impl.ExpectoConfig.defaultConfig 
        r |> should equal 0

[<EntryPoint>]
let main args =
    // --skip-hera is our own flag (consumed by HeraSpiceTests via the process
    // command line); strip it so Expecto doesn't reject it as an unknown arg.
    let expectoArgs = args |> Array.filter (fun a -> a <> "--skip-hera")
    runTestsWithCLIArgs [] expectoArgs (allTests ())