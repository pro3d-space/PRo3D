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

    /// A directory below the test-data root, or None if either part is missing.
    let testDataDir (testDataSource : Option<string>) (segments : list<string>) =
        testData testDataSource
        |> Option.bind (fun root ->
            Path.Combine(root :: segments |> Array.ofList) |> existingDir)

    /// The Dimorphos OPC every Dimorphos test uses: its patch hierarchy, with
    /// DRACO_1/DRACO_2/Earth texture layers, per-vertex *.aara attribute layers
    /// (LonLatRad, Normal, Slope, ...) and kd-trees. It replaced
    /// `Dimorphos_DRACO1/` and `HERA/Dimorphos_opc/Dimorphos`, which the test-data
    /// restructure removed; the geometry is identical.
    let dimorphosOpc (testDataSource : Option<string>) =
        testDataDir testDataSource [ "HERA"; "Dimorphos_opc"; "Dimorphos_DRACO1_DRACO2_Earth"; "Dimorphos" ]

    /// The MSL Stimson OPC (one patch of the Stimson_1087 dataset).
    let mslOpc (testDataSource : Option<string>) =
        testDataDir testDataSource [ "MSL"; "1087_004779_MSLMST_0011" ]

    /// Fallback for the legacy private root when PRO3D_PRIVATE_TESTDATA is unset.
    /// Only a convenience for the machine the fixtures were captured on - not a
    /// path any test may depend on.
    let defaultPrivateRoot = @"C:\pro3ddata"

    /// Roots of the non-redistributable fixtures - large catalogs, vendor data,
    /// captures that cannot be committed, and fixtures the public checkout dropped -
    /// in lookup order:
    ///
    ///   1. PRO3D_TEST_DATA_PRIVATE, the private root fixtures belong in from now on
    ///   2. PRO3D_PRIVATE_TESTDATA (default `C:\pro3ddata`), where the older private
    ///      fixtures still live
    ///
    /// Setting PRO3D_PRIVATE_TESTDATA wins over the default outright - pointing it
    /// somewhere that does not exist drops it rather than quietly reverting to
    /// `defaultPrivateRoot`, so a typo shows up as skipped tests instead of tests
    /// that read whatever happens to sit at the old hardcoded path.
    ///
    /// Empty when none exists, which is the normal case away from that machine -
    /// callers skip rather than fail.
    let privateRoots () =
        [ Environment.GetEnvironmentVariable "PRO3D_TEST_DATA_PRIVATE"
          match Environment.GetEnvironmentVariable "PRO3D_PRIVATE_TESTDATA" with
          | null | "" -> defaultPrivateRoot
          | path      -> path ]
        |> List.choose existingDir
        |> List.distinct

    /// A directory below the first private root that has it, or None. `segments`
    /// are joined onto the root, e.g. `privateDir [ "HERA"; "OPCUpdate" ]`.
    let privateDir (segments : list<string>) =
        privateRoots ()
        |> List.tryPick (fun root -> Path.Combine(root :: segments |> Array.ofList) |> existingDir)

/// Scenes derived from a committed template. A .pro3d stores absolute paths, so a
/// committed scene only opens on the machine that wrote it.
module Scenes =

    open System.Text.Json.Nodes

    /// PRO3D_SPICE_KERNELS as a kernel root (it may name the tree or its `kernels` dir).
    let private kernelRoot () =
        match Environment.GetEnvironmentVariable "PRO3D_SPICE_KERNELS" with
        | null | "" -> None
        | k when Directory.Exists(Path.Combine(k, "mk")) -> Some k
        | k -> Some (Path.Combine(k, "kernels"))

    /// `template` with its first surface re-pointed at `opc` and its SPICE meta-kernel at
    /// the same file name under PRO3D_SPICE_KERNELS, written to `out`. The F# twin of
    /// tests-ui's `sceneFor`, so tests need not wait for a tests-ui run to produce it.
    let fromTemplate (template : string) (opc : string) (out : string) =
        let d = JsonNode.Parse(File.ReadAllText template)
        let s = d.["surfaceModel"].["surfaces"].["flat"].[0].["Surfaces"]
        s.["importPath"] <- JsonValue.Create opc
        let opcPaths = JsonArray()
        for p in s.["opcPaths"].AsArray() do
            // Windows paths in the template, whatever the platform running the test
            let name = p.GetValue<string>().Split([| '\\'; '/' |]) |> Array.last
            opcPaths.Add(JsonValue.Create(Path.Combine(opc, name)))
        s.["opcPaths"] <- opcPaths
        match Directory.EnumerateFiles(opc, "*.opcx") |> Seq.tryHead with
        | Some opcx -> s.["opcxPath"] <- JsonValue.Create opcx
        | None      -> ()
        match kernelRoot (), d.["gisApp"] with
        | Some k, gis when not (isNull gis) && not (isNull gis.["spiceKernel"]) ->
            let name = gis.["spiceKernel"].GetValue<string>().Split([| '\\'; '/' |]) |> Array.last
            gis.["spiceKernel"] <- JsonValue.Create(Path.Combine(k, "mk", name))
        | _ -> ()
        d.["scenePath"] <- JsonValue.Create out
        Directory.CreateDirectory(Path.GetDirectoryName out) |> ignore
        File.WriteAllText(out, d.ToJsonString(Text.Json.JsonSerializerOptions(WriteIndented = true)))
        out

let outputDir (parameters : TestParameters) (subFolder : string) =
    // Exports land next to the fixtures they were produced from.
    let root =
        Roots.testData parameters.testDataSource
        |> Option.defaultValue (Path.GetTempPath())
    let dir = Path.Combine(root, "outputs", subFolder)
    if not (Directory.Exists dir) then
        Directory.CreateDirectory dir |> ignore
    dir
