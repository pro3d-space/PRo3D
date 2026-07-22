open Expecto
open NUnit


let allTests () : Test =
    testList "all tests" [
        GeoJsonRework.Tests.tests()
        TriangleSetTests.tests()

        // requires spice kernels
        HeraSpiceTests.tests()

        // skipped automatically when fixtures under C:\pro3ddata are absent
        SbmtImportAlignmentTest.tests()

        ProjectedImageMetadataTest.tests()

        // relies on HeraSpiceTests.tests() above having already loaded hera_ops.tm
        InstrumentProjectionComparisonTest.tests()
        DidymosProjectionSpiceTest.tests()
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