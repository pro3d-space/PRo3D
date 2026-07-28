module TestUtils

open System.IO

type TestParameters = {
    testDataSource : string option
}

let outputDir (parameters : TestParameters) (subFolder : string) =
    let root =
        parameters.testDataSource
        |> Option.defaultValue (Path.GetTempPath())
    let dir = Path.Combine(root, "outputs", subFolder)
    if not (Directory.Exists dir) then
        Directory.CreateDirectory dir |> ignore
    dir
