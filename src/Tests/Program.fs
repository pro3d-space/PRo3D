open Expecto
open NUnit


let allTests () : Test =
    // SPICE kernel state is process-global: exactly one metakernel is active, swapped
    // via DeInit+Init (see HeraSpiceTests.ensureKernelAt). Under parallel execution
    // another test can swap the kernel between a test's ensure and its SPICE calls,
    // which shows up as spurious None results depending on schedule. Sequence the
    // whole suite; per-list testSequenced is not enough, lists still interleave.
    testSequenced <| testList "all tests" [
        GeoJsonRework.Tests.tests()
        TriangleSetTests.tests()

        // requires spice kernels
        HeraSpiceTests.tests()

        // skipped automatically when fixtures under C:\pro3ddata are absent
        SbmtImportAlignmentTest.tests()

        ProjectedImageMetadataTest.tests()

        // Kernel-swap count matters: the native DeInit does not fully clear CSPICE's
        // binary-kernel (DAF) state, so handles go stale after repeated metakernel swaps
        // (SPICE(DAFNOSUCHHANDLE) on SPK/CK reads; text-kernel frames keep working).
        // Order the plan-kernel tests before the comparison tests, whose concurrency
        // test churns several extra swaps -- and report the DeInit issue upstream.
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
    let config = parseArgs args

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