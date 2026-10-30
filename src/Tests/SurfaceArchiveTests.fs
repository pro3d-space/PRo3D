module SurfaceArchiveTests

open System
open System.IO

open Expecto
open ICSharpCode.SharpZipLib.Zip

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Data.Opc
open Aardvark.GeoSpatial.Opc
open FSharp.Data.Adaptive

open OpcViewer.Base
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.MapProjection
open PRo3D.Tests
open PRo3D.Tool.SunAnglesVerb
// after PRo3D.Core.Surface, whose Sg module would hide the scene graph combinators
open Aardvark.SceneGraph

/// Surfaces packed into stored zip archives (*.opc, *.zip), see docs/PackedSurfaces.md.
/// The archives are built at test time: synthetic entry lists for the layout rules, and the
/// Didymos_ASPECT test-data surface for loading a real OPC through Prinziple.
module private Fixtures =

    let tempDir () =
        let dir = Path.Combine(Path.GetTempPath(), "pro3d-surface-archive-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory dir |> ignore
        dir

    let private source (bytes : byte[]) =
        { new IStaticDataSource with member _.GetSource() = new MemoryStream(bytes) :> Stream }

    /// writes a zip with the given entries
    let zip (path : string) (method : CompressionMethod) (entries : list<string * byte[]>) =
        use z = ZipFile.Create path
        z.BeginUpdate()
        for (name, bytes) in entries do
            z.Add(source bytes, name, method)
        z.CommitUpdate()
        path

    let bytes (s : string) = Text.Encoding.UTF8.GetBytes s

    /// entries of a minimal OPC folder `<prefix><opc>/`
    let opc (prefix : string) (opc : string) (withCache : bool) (withKdTree : bool) =
        [
            yield $"{prefix}{opc}/Patches/patchhierarchy.xml", bytes "<xml/>"
            yield $"{prefix}{opc}/Images/Texture/0_0_0.tif", bytes "tif"
            if withCache then yield $"{prefix}{opc}/Patches/hierarchy.cache", bytes "cache"
            if withKdTree then yield $"{prefix}{opc}/Patches/0_0_0/0_0_0-0.aakd", bytes "kd"
        ]

    /// entries of all files below `folder`, named `<prefix><relative path>`
    let ofFolder (folder : string) (prefix : string) (exclude : string -> bool) =
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        |> Seq.filter (exclude >> not)
        |> Seq.map (fun f -> prefix + Path.GetRelativePath(folder, f).Replace('\\', '/'), File.ReadAllBytes f)
        |> Seq.toList

    /// Didymos_ASPECT: one OPC with a hierarchy cache, a per-patch KdTree and a lazy KdTree cache
    let didymos =
        TestUtils.Roots.firstExisting [
            Path.Combine(__SOURCE_DIRECTORY__, "resources", "HERA", "Didymos_ASPECT")
            TestUtils.Roots.testDataDir None [ "HERA"; "Didymos_ASPECT" ] |> Option.defaultValue ""
        ]

    let expectOk (r : Result<SurfaceArchive.SurfaceArchive, SurfaceArchive.ArchiveError>) =
        match r with
        | Result.Ok a -> a
        | Result.Error e -> failtestf "expected the archive to open: %s" (SurfaceArchive.ArchiveError.message e)

    let expectRejected (r : Result<SurfaceArchive.SurfaceArchive, SurfaceArchive.ArchiveError>) =
        match r with
        | Result.Error (SurfaceArchive.Rejected _) -> ()
        | Result.Error (SurfaceArchive.NotASurfaceArchive m) -> failtestf "expected Rejected, got NotASurfaceArchive: %s" m
        | Result.Ok a -> failtestf "expected the archive to be rejected, it opened as %s" a.surfacePath

open Fixtures

let layoutTests () =
    testList "layout" [

        test "a surface folder packed into the archive appears at <dir>\\<Name>\\<Name>" {
            let dir = tempDir ()
            let archive =
                zip (Path.Combine(dir, "Gale.opc")) CompressionMethod.Stored [
                    yield "Gale/Gale.opcx", bytes "<Aardvark/>"
                    yield! opc "Gale/" "Gale_000_000" true true
                    yield! opc "Gale/" "Gale_000_001" true true
                ]

            let a = SurfaceArchive.tryOpen archive |> expectOk
            let surfacePath = Path.Combine(dir, "Gale", "Gale")

            Expect.equal a.name "Gale" "surface name is the archive name"
            Expect.equal a.surfacePath surfacePath "virtual surface folder"
            Expect.equal (List.sort a.opcNames) [ "Gale_000_000"; "Gale_000_001" ] "OPC folders"
            Expect.isEmpty a.warnings "no warnings"

            Expect.isTrue (SurfaceArchive.isInArchive surfacePath) "surface folder lies in the archive"
            Expect.isTrue (Files.isSurfaceFolder surfacePath) "surface folder is recognised"
            Expect.isTrue (Files.surfaceFolderExists surfacePath) "surface folder exists"
            Expect.isTrue (Files.isOpcFolder (Path.Combine(surfacePath, "Gale_000_000"))) "OPC folder is recognised"
            Expect.equal (Files.getOPCNames surfacePath |> List.sort) [ "Gale_000_000"; "Gale_000_001" ] "OPC names from the archive"
            Expect.isTrue (Prinziple.fileExists (Path.Combine(surfacePath, "Gale.opcx"))) "opcx is readable"
        }

        test "an archive with the surface content at its top level appears at <dir>\\<Name>" {
            let dir = tempDir ()
            let archive =
                zip (Path.Combine(dir, "Flat.zip")) CompressionMethod.Stored [
                    yield "Flat.opcx", bytes "<Aardvark/>"
                    yield! opc "" "Flat_000_000" true true
                ]

            let a = SurfaceArchive.tryOpen archive |> expectOk
            Expect.equal a.surfacePath (Path.Combine(dir, "Flat")) "virtual surface folder is the root"
            Expect.equal a.opcNames [ "Flat_000_000" ] "OPC folder"
            Expect.isTrue (Files.isSurfaceFolder a.surfacePath) "surface folder is recognised"
        }

        test "opening an archive twice returns the first opening" {
            let dir = tempDir ()
            let archive = zip (Path.Combine(dir, "Twice.opc")) CompressionMethod.Stored (opc "Twice/" "Twice_000_000" true true)
            let a = SurfaceArchive.tryOpen archive |> expectOk
            let b = SurfaceArchive.tryOpen archive |> expectOk
            Expect.equal b a "same archive"
        }

        test "a stored surface path opens its archive (scene load)" {
            let dir = tempDir ()
            let archive = zip (Path.Combine(dir, "Stored.opc")) CompressionMethod.Stored (opc "Stored/" "Stored_000_000" true true)
            let surfacePath = Path.Combine(dir, "Stored", "Stored")

            match SurfaceArchive.tryOpenForPath surfacePath with
            | Some (Result.Ok a) ->
                Expect.equal a.archivePath archive "archive found from the surface path"
                Expect.equal a.surfacePath surfacePath "same virtual surface folder"
            | Some (Result.Error e) -> failtestf "archive did not open: %s" (SurfaceArchive.ArchiveError.message e)
            | None -> failtest "no archive found for the surface path"

            Expect.isNone (SurfaceArchive.tryOpenForPath dir) "an existing folder is no archive path"
        }
    ]

let rejectionTests () =
    testList "rejection" [

        test "compressed entries are rejected" {
            let dir = tempDir ()
            let archive = zip (Path.Combine(dir, "Deflated.opc")) CompressionMethod.Deflated (opc "Deflated/" "Deflated_000_000" true true)
            SurfaceArchive.tryOpen archive |> expectRejected
        }

        test "an OPC without hierarchy.cache is rejected" {
            let dir = tempDir ()
            let archive = zip (Path.Combine(dir, "NoCache.opc")) CompressionMethod.Stored (opc "NoCache/" "NoCache_000_000" false true)
            SurfaceArchive.tryOpen archive |> expectRejected
        }

        test "an OPC without KdTrees opens with a warning" {
            let dir = tempDir ()
            let archive = zip (Path.Combine(dir, "NoKd.opc")) CompressionMethod.Stored (opc "NoKd/" "NoKd_000_000" true false)
            let a = SurfaceArchive.tryOpen archive |> expectOk
            Expect.equal a.warnings.Length 1 "one warning about missing KdTrees"
        }

        test "a zip without OPC is no surface archive" {
            let dir = tempDir ()
            let archive = zip (Path.Combine(dir, "Calibration.zip")) CompressionMethod.Stored [ "cal/camera.json", bytes "{}" ]
            match SurfaceArchive.tryOpen archive with
            | Result.Error (SurfaceArchive.NotASurfaceArchive _) -> ()
            | other -> failtestf "expected NotASurfaceArchive, got %A" other
        }

        test "an archive next to its unpacked folder is rejected" {
            let dir = tempDir ()
            let archive = zip (Path.Combine(dir, "Both.opc")) CompressionMethod.Stored (opc "Both/" "Both_000_000" true true)
            Directory.CreateDirectory(Path.Combine(dir, "Both")) |> ignore
            SurfaceArchive.tryOpen archive |> expectRejected
        }

        test "an archive next to a folder starting with its name is rejected" {
            let dir = tempDir ()
            let archive = zip (Path.Combine(dir, "Prefix.opc")) CompressionMethod.Stored (opc "Prefix/" "Prefix_000_000" true true)
            Directory.CreateDirectory(Path.Combine(dir, "Prefix2")) |> ignore
            SurfaceArchive.tryOpen archive |> expectRejected
        }
    ]

let discoveryTests () =
    testList "discovery" [

        test "discovery finds archives and skips zips without OPC" {
            let dir = tempDir ()
            let surface = zip (Path.Combine(dir, "Found.opc")) CompressionMethod.Stored (opc "Found/" "Found_000_000" true true)
            let junk = zip (Path.Combine(dir, "Junk.zip")) CompressionMethod.Stored [ "readme.txt", bytes "no opc" ]

            let discovered = Files.superDiscoveryMultipleSurfaceFolder dir
            Expect.containsAll discovered [ surface; junk ] "both archives are candidates"

            let surfaces, problems = SurfaceUtils.mkFromPaths 0.0 [ dir ] discovered
            Expect.equal (surfaces |> List.map (fun s -> s.name)) [ "Found" ] "one surface named after the archive"
            Expect.isEmpty problems "a discovered zip without OPC is skipped silently"

            let s = surfaces |> List.head
            Expect.equal s.importPath (Path.Combine(dir, "Found", "Found")) "import path is the virtual surface folder"
            Expect.equal s.opcPaths [ Path.Combine(dir, "Found", "Found", "Found_000_000") ] "OPC paths below it"
        }

        test "a chosen zip without OPC is reported" {
            let dir = tempDir ()
            let junk = zip (Path.Combine(dir, "Chosen.zip")) CompressionMethod.Stored [ "readme.txt", bytes "no opc" ]
            let surfaces, problems = SurfaceUtils.mkFromPaths 0.0 [ junk ] [ junk ]
            Expect.isEmpty surfaces "no surface"
            Expect.equal problems.Length 1 "the chosen zip is reported"
        }
    ]

/// Loads a real OPC from a stored archive the way the viewer does and checks that nothing is
/// written into or next to the archive.
let realOpcTests () =
    let load (withLazyCache : bool) =
        match didymos with
        | None -> skiptest "Didymos_ASPECT test data not found"
        | Some folder ->
            let dir = tempDir ()
            let isLazyCache (f : string) = f.EndsWith("-0.cache", StringComparison.OrdinalIgnoreCase)
            let archive =
                ofFolder folder "Didymos_ASPECT/" (fun f -> not withLazyCache && isLazyCache f)
                |> zip (Path.Combine(dir, "Didymos_ASPECT.opc")) CompressionMethod.Stored

            let surfaces, problems = SurfaceUtils.mkFromPaths 0.0 [ archive ] [ archive ]
            Expect.isEmpty problems "no problems"
            let s = surfaces |> List.exactlyOne
            Expect.equal s.name "Didymos_ASPECT" "surface name"
            Expect.isTrue (Files.surfaceFolderExists s.importPath) "surface folder exists"

            for opcPath in s.opcPaths do
                let h = PatchHierarchy.load Serialization.binarySerializer.Pickle Serialization.binarySerializer.UnPickle (OpcPaths opcPath)
                let leaves = h.tree |> QTree.getLeaves |> Seq.length
                Expect.isGreaterThan leaves 0 "hierarchy has patches"

                let kd = KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ Serialization.binarySerializer false false DebugKdTreesX.loadTriangles' true
                Expect.isGreaterThan (HashMap.count kd) 0 "KdTrees load from the archive"

                for (_, tree) in kd do
                    match tree with
                    | KdTrees.Level0KdTree.LazyKdTree l ->
                        Expect.isTrue (SurfaceArchive.isInArchive l.kdtreePath) "KdTree path points into the archive"
                        let t = KdTrees.loadKdtree l.kdtreePath
                        Expect.isTrue t.KdIntersectionTree.BoundingBox3d.IsValid "KdTree reads from the archive"
                    | KdTrees.Level0KdTree.InCoreKdTree _ -> ()

            use texture = Prinziple.openRead (Path.Combine(s.opcPaths |> List.head, "Images", "Checkerboard", "0_0_0.tif"))
            Expect.isTrue texture.CanSeek "texture streams from a stored archive can seek"

            Expect.equal (Directory.GetFileSystemEntries dir |> Array.toList) [ archive ] "nothing was written next to the archive"

    testList "real OPC" [
        test "loads hierarchy and KdTrees with the packed lazy KdTree cache" { load true }
        test "loads hierarchy and KdTrees without a lazy KdTree cache, and does not write one" { load false }
    ]

/// Renders the OPC through Aardvark.GeoSpatial.Opc (MapSg, the map projection panel's scene
/// graph) once from the folder and once from the archive: geometry and textures must decode
/// from archive streams exactly as from files.
let renderTests () =
    let render (runtime : IRuntime) (hierarchies : string[]) =
        let size = V2i(512, 256)
        let kind = MapProjectionKind.Equirectangular
        let viewProj = Projection.viewProj kind Projection.defaultMaxColatitude V2d.Zero 1.0 size
        let target = FloatTarget.create runtime size
        try
            let runner = PRo3D.Core.Surface.Sg.loadRunnerFor runtime
            let cfg = { OpcSg.defaultConfig target.signature runner MapSg.finestLod "DIDYMOS" with asyncLoading = false }
            let view : MapSg.MapView =
                {
                    kind          = AVal.constant kind
                    viewProj      = AVal.constant viewProj
                    maxColatitude = AVal.constant Projection.defaultMaxColatitude
                    radiusRange   = AVal.constant (Projection.radiusRange (MapSg.maxRadius hierarchies))
                }
            let surface : MapSg.MapSurface =
                { hierarchies = hierarchies; placement = AVal.constant Trafo3d.Identity; visible = AVal.constant true }
            MapSg.surfaces cfg MapSg.surfaceEffects view (ASet.single surface)
            |> Sg.viewTrafo (AVal.constant Trafo3d.Identity)
            |> Sg.projTrafo (AVal.constant Trafo3d.Identity)
            |> FloatTarget.render target 4
        finally
            FloatTarget.dispose target

    testList "render" [
        test "a packed OPC renders exactly like its folder" {
            match PRo3D.Tests.Render.context.Value, didymos with
            | None, _ -> skiptest "no OpenGL runtime in this environment"
            | _, None -> skiptest "Didymos_ASPECT test data not found"
            | Some (runtime, _), Some folder ->

            Startup.init ()
            let dir = tempDir ()
            let archive =
                ofFolder folder "Didymos_ASPECT/" (fun _ -> false)
                |> zip (Path.Combine(dir, "Didymos_ASPECT.opc")) CompressionMethod.Stored
            let a = SurfaceArchive.tryOpen archive |> expectOk

            let packed   = render runtime (a.opcNames |> List.map (fun n -> Path.Combine(a.surfacePath, n)) |> List.toArray)
            let unpacked = render runtime (a.opcNames |> List.map (fun n -> Path.Combine(folder, n)) |> List.toArray)

            let channels (img : PixImage<float32>) =
                [ Col.Channel.Red; Col.Channel.Green; Col.Channel.Blue; Col.Channel.Alpha ] |> List.map img.GetChannel
            let mutable differing = 0
            let mutable covered = 0
            let levels = Collections.Generic.HashSet<int>()
            let alpha = unpacked.GetChannel Col.Channel.Alpha
            let red = unpacked.GetChannel Col.Channel.Red
            for (p, u) in List.zip (channels packed) (channels unpacked) do
                for y in 0 .. unpacked.Size.Y - 1 do
                    for x in 0 .. unpacked.Size.X - 1 do
                        if p.[x, y] <> u.[x, y] then differing <- differing + 1
            for y in 0 .. unpacked.Size.Y - 1 do
                for x in 0 .. unpacked.Size.X - 1 do
                    if alpha.[x, y] > 0.5f then
                        covered <- covered + 1
                        levels.Add(int (red.[x, y] * 255.0f)) |> ignore

            printfn "packed vs unpacked render: %d covered pixels, %d red levels, %d differing values" covered levels.Count differing
            Expect.isGreaterThan covered 1000 "the surface covers the map"
            Expect.isGreaterThan levels.Count 1 "the texture shows up, not a flat colour"
            Expect.equal differing 0 "the packed surface renders exactly like its folder"
            Expect.equal (Directory.GetFileSystemEntries dir |> Array.toList) [ archive ] "nothing was written next to the archive"
        }
    ]

let tests () =
    testList "surface archives" [
        layoutTests ()
        rejectionTests ()
        discoveryTests ()
        realOpcTests ()
        renderTests ()
    ]
