
open Expecto
open NUnit

let allTests () : Test = 
    testList "all tests" [
        // requires spice kernels
        HeraSpiceTests.tests()
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
    runTestsWithCLIArgs [] args (allTests ())