module TestUtils

open System.IO

type TestParameters = {
    testDataSource : string option
}

let outputDir (parameters : TestParameters) (subFolder : string) =
    // Same root resolution the data-backed tests use, so exports land next to
    // the fixtures they were produced from: PRO3D_TEST_DATA, then
    // --testdatasource, then the temp directory.
    let root =
        match System.Environment.GetEnvironmentVariable "PRO3D_TEST_DATA" with
        | null | "" -> parameters.testDataSource |> Option.defaultValue (Path.GetTempPath())
        | path -> path
    let dir = Path.Combine(root, "outputs", subFolder)
    if not (Directory.Exists dir) then
        Directory.CreateDirectory dir |> ignore
    dir
