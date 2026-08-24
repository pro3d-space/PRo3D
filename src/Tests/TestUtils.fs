module TestUtils

open System
open System.IO

type TestParameters = {
    testDataSource : string option
}

/// The two roots the data-backed tests resolve their fixtures from.
///
/// They are deliberately separate. `testData` is a clone of
/// PRo3D.Resources.TestData - public, versioned, and the place a fixture belongs
/// unless there is a reason it cannot. `privateData` is whatever local pile the
/// non-redistributable data sits in; nothing there can be assumed to exist on any
/// other machine, so every test reading from it self-skips.
module Roots =

    let private existingDir (path : string) =
        if String.IsNullOrWhiteSpace path then None
        elif Directory.Exists path then Some path
        else None

    /// First of `paths` that exists, ignoring null/blank entries.
    let firstExisting (paths : seq<string>) =
        paths |> Seq.tryPick existingDir

    /// Root of a PRo3D.Resources.TestData checkout. PRO3D_TEST_DATA is the
    /// documented way in; the suite-wide --testdatasource is honoured second so
    /// run-tests.cmd keeps working.
    let testData (testDataSource : Option<string>) =
        firstExisting [
            Environment.GetEnvironmentVariable "PRO3D_TEST_DATA"
            testDataSource |> Option.defaultValue ""
        ]

    /// Fallback for `privateData` when PRO3D_PRIVATE_TESTDATA is unset. Only a
    /// convenience for the machine the fixtures were captured on - not a path any
    /// test may depend on.
    let defaultPrivateRoot = @"C:\pro3ddata"

    /// Root of the non-redistributable fixtures: large catalogs, vendor data and
    /// captures that cannot be committed. Set PRO3D_PRIVATE_TESTDATA to it.
    ///
    /// Setting the variable wins outright - pointing it somewhere that does not
    /// exist yields None rather than quietly reverting to `defaultPrivateRoot`,
    /// so a typo shows up as skipped tests instead of tests that read whatever
    /// happens to sit at the old hardcoded path.
    ///
    /// Returns None when the root does not exist, which is the normal case away
    /// from that machine - callers skip rather than fail.
    let privateData () =
        match Environment.GetEnvironmentVariable "PRO3D_PRIVATE_TESTDATA" with
        | null | "" -> existingDir defaultPrivateRoot
        | path      -> existingDir path

    /// A directory below the private root, or None if either part is missing.
    /// `segments` are joined onto the root, e.g.
    /// `privateDir [ "HERA"; "OPCUpdate" ]`.
    let privateDir (segments : list<string>) =
        privateData ()
        |> Option.bind (fun root ->
            Path.Combine(root :: segments |> Array.ofList) |> existingDir)

let outputDir (parameters : TestParameters) (subFolder : string) =
    // Exports land next to the fixtures they were produced from.
    let root =
        Roots.testData parameters.testDataSource
        |> Option.defaultValue (Path.GetTempPath())
    let dir = Path.Combine(root, "outputs", subFolder)
    if not (Directory.Exists dir) then
        Directory.CreateDirectory dir |> ignore
    dir
