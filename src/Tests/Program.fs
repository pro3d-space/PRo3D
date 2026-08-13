open Expecto
open NUnit


let allTests () : Test =
    // SPICE kernel state is process-global: exactly one metakernel is active, swapped
    // via DeInit+Init (see HeraSpiceTests.ensureKernelAt). Under parallel execution
    // another test can swap the kernel between a test's ensure and its SPICE calls,
    // which shows up as spurious None results depending on schedule. Sequence the
    // whole suite; per-list testSequenced is not enough, lists still interleave.
    testSequenced <| testList "all tests" [
        // kernel-independent tests (use only the default SPICE kernels)
        GeoJsonRework.Tests.tests()
        SpiceTests.tests()
        TriangleSetTests.tests()

        // requires the (non-public) HERA kernels; self-skips without them
        HeraSpiceTests.tests()

        // skipped automatically when fixtures under C:\pro3ddata are absent
        SbmtImportAlignmentTest.tests()

        ProjectedImageMetadataTest.tests()

        // *.opc.json sidecars; the fixture-backed case self-skips without the data
        OpcSidecarTests.tests()

        // Kernel-swap count matters: the native DeInit does not fully clear CSPICE's
        // binary-kernel (DAF) state, so handles go stale after repeated metakernel swaps
        // (SPICE(DAFNOSUCHHANDLE) on SPK/CK reads; text-kernel frames keep working).
        // Order the plan-kernel tests before the comparison tests, whose concurrency
        // test churns several extra swaps -- and report the DeInit issue upstream.
        if HeraSpiceTests.hasHera then
            DidymosProjectionSpiceTest.tests()
            InstrumentProjectionComparisonTest.tests()
    ]

let profileTests (parameters : TestUtils.TestParameters) : Test =
    testList "profile tests" [
        ProfileAttributeExtractionTest.tests(parameters)
    ]


module NunitEntry =

    open NUnit.Framework
    open FsUnit

    [<Test>]
    let ``[expecto tests]``() =
        let r = allTests () |> runTests Impl.ExpectoConfig.defaultConfig 
        r |> should equal 0

type TestConfig = {
    testDataSource : string option
    expectoArgs    : string list
}

let parseArgs (args : string array) =
    let rec parse config = function
        | "--testdatasource" :: path :: rest ->
            parse { config with testDataSource = Some path } rest
        | arg :: rest ->
            parse { config with expectoArgs = config.expectoArgs @ [arg] } rest
        | [] -> config

    parse { testDataSource = None; expectoArgs = [] } (Array.toList args)

[<EntryPoint>]
let main args =
    // --skip-hera is consumed by HeraSpiceTests via the process command line;
    // strip it so Expecto doesn't reject it as an unknown argument.
    let config = parseArgs (args |> Array.filter (fun a -> a <> "--skip-hera"))

    let parameters : TestUtils.TestParameters = { testDataSource = config.testDataSource }

    let tests =
        match config.testDataSource with
        | Some path ->
            printfn "Test data source: %s" path
            testList "all" [ allTests(); profileTests parameters ]
        | None ->
            printfn "No test data source specified (use --testdatasource <path>)"
            allTests()

    runTestsWithCLIArgs [] (Array.ofList config.expectoArgs) tests