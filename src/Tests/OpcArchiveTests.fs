module OpcArchiveTests

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

/// OPCs packed into stored zip archives (`<surface>\<Opc>.opc`) inside a plain surface folder
/// that keeps its sidecars (.opcx, metadata), see docs/PackedSurfaces.md. The archives are built
/// at test time: synthetic entry lists for the layout rules, and the Didymos_ASPECT test-data
/// surface for loading a real OPC through Prinziple.
module private Fixtures =

    let tempDir () =
        let dir = Path.Combine(Path.GetTempPath(), "pro3d-opc-archive-tests", Guid.NewGuid().ToString("N"))
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

    /// entries of a minimal OPC, at the archive's top level
    let opc (withCache : bool) (withKdTree : bool) =
        [
            yield "Patches/patchhierarchy.xml", bytes "<xml/>"
            yield "Images/Texture/0_0_0.tif", bytes "tif"
            if withCache then yield "Patches/hierarchy.cache", bytes "cache"
            if withKdTree then yield "Patches/0_0_0/0_0_0-0.aakd", bytes "kd"
        ]

    /// a surface folder `<dir>\<name>` with its .opcx sidecar
    let surfaceFolder (dir : string) (name : string) =
        let s = Path.Combine(dir, name)
        Directory.CreateDirectory s |> ignore
        File.WriteAllText(Path.Combine(s, name + ".opcx"), "<Aardvark/>")
        s

    /// a packed OPC `<surface>\<opcName>.opc`; returns its OPC path
    let packed (surface : string) (opcName : string) (method : CompressionMethod) (entries : list<string * byte[]>) =
        zip (Path.Combine(surface, opcName + ".opc")) method entries |> ignore
        Path.Combine(surface, opcName)

    /// a minimal plain OPC folder `<surface>\<opcName>`; returns its path
    let plain (surface : string) (opcName : string) =
        let o = Path.Combine(surface, opcName)
        Directory.CreateDirectory(Path.Combine(o, "Images", "Texture")) |> ignore
        Directory.CreateDirectory(Path.Combine(o, "Patches")) |> ignore
        File.WriteAllText(Path.Combine(o, "Patches", "patchhierarchy.xml"), "<xml/>")
        o

    /// entries of all files below `folder`, named by their relative path
    let ofFolder (folder : string) =
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        |> Seq.map (fun f -> Path.GetRelativePath(folder, f).Replace('\\', '/'), File.ReadAllBytes f)
        |> Seq.toList

    let filesBelow (dir : string) =
        Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories) |> Array.sort |> Array.toList

    /// Didymos_ASPECT: a surface folder with .opcx and .opc.json sidecars and one OPC with a
    /// hierarchy cache, a per-patch KdTree and a lazy KdTree cache
    let didymos =
        TestUtils.Roots.firstExisting [
            Path.Combine(__SOURCE_DIRECTORY__, "resources", "HERA", "Didymos_ASPECT")
            TestUtils.Roots.testDataDir None [ "HERA"; "Didymos_ASPECT" ] |> Option.defaultValue ""
        ]

    /// Didymos_ASPECT repacked as the processing pipeline serves it: the sidecars copied, each OPC
    /// folder packed into `<Opc>.opc`. Returns the surface folder and the OPC names.
    let packDidymos (folder : string) (dir : string) (exclude : string -> bool) =
        let surface = Path.Combine(dir, "Didymos_ASPECT")
        Directory.CreateDirectory surface |> ignore
        for f in Directory.GetFiles folder do
            File.Copy(f, Path.Combine(surface, Path.GetFileName f))
        let names =
            Directory.GetDirectories folder |> Array.toList |> List.map (fun o ->
                let name = Path.GetFileName o
                ofFolder o
                |> List.filter (fst >> exclude >> not)
                |> zip (Path.Combine(surface, name + ".opc")) CompressionMethod.Stored
                |> ignore
                name
            )
        surface, names

    let expectOk (r : Result<OpcArchive.OpcArchive, OpcArchive.ArchiveError>) =
        match r with
        | Result.Ok a -> a
        | Result.Error e -> failtestf "expected the archive to open: %s" (OpcArchive.ArchiveError.message e)

    let expectRejected (r : Result<OpcArchive.OpcArchive, OpcArchive.ArchiveError>) =
        match r with
        | Result.Error (OpcArchive.Rejected _) -> ()
        | Result.Error (OpcArchive.NotAnOpcArchive m) -> failtestf "expected Rejected, got NotAnOpcArchive: %s" m
        | Result.Ok a -> failtestf "expected the archive to be rejected, it opened as %s" a.opcPath

open Fixtures

let layoutTests () =
    testList "layout" [

        test "a packed OPC is read at <surface>\\<Opc>" {
            let surface = surfaceFolder (tempDir ()) "Gale"
            let opcPath = packed surface "Gale_000_000" CompressionMethod.Stored (opc true true)

            Expect.isTrue (Files.isSurfaceFolder surface) "the surface folder is recognised by its packed OPC"
            Expect.equal (Files.getOPCNames surface) [ "Gale_000_000" ] "OPC name from the archive file"

            let a = OpcArchive.tryOpen opcPath |> expectOk
            Expect.equal a.opcPath opcPath "registered at the OPC path"
            Expect.equal a.archivePath (opcPath + ".opc") "archive file"
            Expect.isEmpty a.warnings "no warnings"

            Expect.isTrue (OpcArchive.isInArchive opcPath) "the OPC lies in the archive"
            Expect.isTrue (OpcArchive.isInArchive (Path.Combine(opcPath, "Patches"))) "and everything below it"
            Expect.isFalse (OpcArchive.isInArchive surface) "the surface folder does not"
            Expect.isTrue (Files.isOpcFolder opcPath) "the OPC reads as a folder"
            Expect.isTrue (Prinziple.fileExists (Path.Combine(opcPath, "Patches", "hierarchy.cache"))) "entries are readable"
        }

        test "a surface may mix plain and packed OPCs" {
            let surface = surfaceFolder (tempDir ()) "Mixed"
            let folderOpc = plain surface "Mixed_000_000"
            let packedOpc = packed surface "Mixed_000_001" CompressionMethod.Stored (opc true true)

            let surfaces, problems = SurfaceUtils.mkFromPaths 0.0 [ surface ]
            Expect.isEmpty problems "no problems"
            let s = surfaces |> List.exactlyOne
            Expect.equal s.name "Mixed" "the surface is named after its folder"
            Expect.equal (s.opcPaths |> List.sort) [ folderOpc; packedOpc ] "both OPCs"
            Expect.isTrue (OpcArchive.isInArchive packedOpc) "the packed OPC is opened on import"
            Expect.isFalse (OpcArchive.isInArchive folderOpc) "the plain one is not an archive"
        }

        test "opening an archive twice returns the first opening" {
            let surface = surfaceFolder (tempDir ()) "Twice"
            let opcPath = packed surface "Twice_000_000" CompressionMethod.Stored (opc true true)
            let a = OpcArchive.tryOpen opcPath |> expectOk
            let b = OpcArchive.tryOpen opcPath |> expectOk
            Expect.equal b a "same archive"
        }

        test "another spelling of the OPC path reads through the registered form" {
            // Prinziple matches the registered path by plain string prefix: a path spelled
            // differently (8.3 short names, '.' segments, a trailing separator) misses the archive
            let dir = tempDir ()
            let surface = surfaceFolder dir "Spelling"
            let opcPath = packed surface "Spelling_000_000" CompressionMethod.Stored (opc true true)
            let spelled = Path.Combine(dir, ".", "Spelling", "Spelling_000_000") + string Path.DirectorySeparatorChar

            match OpcArchive.prepare spelled with
            | Some readPath ->
                Expect.equal readPath (OpcArchive.normalize opcPath) "read through the registered form"
                Expect.isTrue (Prinziple.fileExists (Path.Combine(readPath, "Patches", "patchhierarchy.xml"))) "readable there"
            | None -> failtest "the archive did not open"

            let surfaces, _ = SurfaceUtils.mkFromPaths 0.0 [ Path.Combine(dir, ".", "Spelling") ]
            Expect.equal (surfaces |> List.map (fun s -> s.importPath)) [ OpcArchive.normalize surface ] "import stores the registered form"
        }

        test "an OPC without archive is prepared as it is" {
            let surface = surfaceFolder (tempDir ()) "Plain"
            let o = plain surface "Plain_000_000"
            Expect.equal (OpcArchive.prepare o) (Some o) "plain OPC folder"
        }
    ]

let rejectionTests () =
    testList "rejection" [

        test "compressed entries are rejected" {
            let surface = surfaceFolder (tempDir ()) "Deflated"
            packed surface "Deflated_000_000" CompressionMethod.Deflated (opc true true)
            |> OpcArchive.tryOpen |> expectRejected
        }

        test "an OPC without hierarchy.cache is rejected" {
            let surface = surfaceFolder (tempDir ()) "NoCache"
            packed surface "NoCache_000_000" CompressionMethod.Stored (opc false true)
            |> OpcArchive.tryOpen |> expectRejected
        }

        test "an OPC without KdTrees opens with a warning" {
            let surface = surfaceFolder (tempDir ()) "NoKd"
            let a = packed surface "NoKd_000_000" CompressionMethod.Stored (opc true false) |> OpcArchive.tryOpen |> expectOk
            Expect.equal a.warnings.Length 1 "one warning about missing KdTrees"
        }

        test "a zip without OPC is no OPC archive" {
            let surface = surfaceFolder (tempDir ()) "Calibration"
            let p = packed surface "cal" CompressionMethod.Stored [ "cal/camera.json", bytes "{}" ]
            match OpcArchive.tryOpen p with
            | Result.Error (OpcArchive.NotAnOpcArchive _) -> ()
            | other -> failtestf "expected NotAnOpcArchive, got %A" other
            Expect.isFalse (Files.isSurfaceFolder surface) "a folder with only such a zip is no surface"
        }

        test "an archive next to its unpacked folder is rejected" {
            let surface = surfaceFolder (tempDir ()) "Both"
            let opcPath = packed surface "Both_000_000" CompressionMethod.Stored (opc true true)
            Directory.CreateDirectory opcPath |> ignore
            OpcArchive.tryOpen opcPath |> expectRejected
        }

        test "an archive next to a folder starting with its name is rejected" {
            let surface = surfaceFolder (tempDir ()) "Prefix"
            let opcPath = packed surface "Prefix_000_000" CompressionMethod.Stored (opc true true)
            Directory.CreateDirectory(opcPath + "_old") |> ignore
            OpcArchive.tryOpen opcPath |> expectRejected
        }

        test "two archives of one OPC are rejected" {
            let surface = surfaceFolder (tempDir ()) "Twins"
            let opcPath = packed surface "Twins_000_000" CompressionMethod.Stored (opc true true)
            zip (opcPath + ".zip") CompressionMethod.Stored (opc true true) |> ignore
            OpcArchive.tryOpen opcPath |> expectRejected
        }
    ]

let importTests () =
    testList "import" [

        test "discovery finds surface folders with packed OPCs and ignores other zips" {
            let dir = tempDir ()
            let surface = surfaceFolder dir "Found"
            packed surface "Found_000_000" CompressionMethod.Stored (opc true true) |> ignore
            zip (Path.Combine(surface, "readme.zip")) CompressionMethod.Stored [ "readme.txt", bytes "no opc" ] |> ignore

            Expect.equal (Files.superDiscoveryMultipleSurfaceFolder dir) [ surface ] "the surface folder"
            let surfaces, problems = SurfaceUtils.mkFromPaths 0.0 [ surface ]
            Expect.isEmpty problems "a zip without OPC is ignored silently"
            Expect.equal (surfaces |> List.collect (fun s -> s.opcNames)) [ "Found_000_000" ] "only the packed OPC"
        }

        test "a rejected OPC is left out of its surface and reported" {
            let surface = surfaceFolder (tempDir ()) "Partly"
            let good = packed surface "Partly_000_000" CompressionMethod.Stored (opc true true)
            packed surface "Partly_000_001" CompressionMethod.Deflated (opc true true) |> ignore

            let surfaces, problems = SurfaceUtils.mkFromPaths 0.0 [ surface ]
            let s = surfaces |> List.exactlyOne
            Expect.equal s.opcPaths [ good ] "only the loadable OPC"
            Expect.equal s.opcNames [ "Partly_000_000" ] "names match the paths"
            Expect.equal problems.Length 1 "the compressed one is reported"
        }

        test "a surface whose OPCs are all rejected is not imported" {
            let surface = surfaceFolder (tempDir ()) "Broken"
            packed surface "Broken_000_000" CompressionMethod.Stored (opc false true) |> ignore
            let surfaces, problems = SurfaceUtils.mkFromPaths 0.0 [ surface ]
            Expect.isEmpty surfaces "no surface"
            Expect.equal problems.Length 2 "the OPC and the surface are reported"
        }

        test "a single packed OPC chosen without its surface folder is reported" {
            let surface = surfaceFolder (tempDir ()) "Alone"
            let opcPath = packed surface "Alone_000_000" CompressionMethod.Stored (opc true true)
            let surfaces, problems = SurfaceUtils.mkFromPaths 0.0 [ opcPath + ".opc" ]
            Expect.isEmpty surfaces "no surface"
            Expect.equal problems.Length 1 "reported"
            Expect.isFalse (OpcArchive.isInArchive opcPath) "not opened"
        }
    ]

/// Loads a real OPC from a stored archive the way the viewer does and checks that nothing is
/// written into the surface folder.
let realOpcTests () =
    let load (withLazyCache : bool) =
        match didymos with
        | None -> skiptest "Didymos_ASPECT test data not found"
        | Some folder ->
            let isLazyCache (f : string) = f.EndsWith("-0.cache", StringComparison.OrdinalIgnoreCase)
            let surface, names = packDidymos folder (tempDir ()) (fun f -> not withLazyCache && isLazyCache f)
            let before = filesBelow surface

            let surfaces, problems = SurfaceUtils.mkFromPaths 0.0 [ surface ]
            Expect.isEmpty problems "no problems"
            let s = surfaces |> List.exactlyOne
            Expect.equal s.name "Didymos_ASPECT" "surface name"
            Expect.equal s.opcNames names "OPC names"

            for opcPath in s.opcPaths do
                let readPath = OpcArchive.prepare opcPath |> Option.defaultWith (fun () -> failtest "OPC not readable")
                let h = PatchHierarchy.load Serialization.binarySerializer.Pickle Serialization.binarySerializer.UnPickle (OpcPaths readPath)
                let leaves = h.tree |> QTree.getLeaves |> Seq.length
                Expect.isGreaterThan leaves 0 "hierarchy has patches"

                let kd = KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ Serialization.binarySerializer false false DebugKdTreesX.loadTriangles' true
                Expect.isGreaterThan (HashMap.count kd) 0 "KdTrees load from the archive"

                for (_, tree) in kd do
                    match tree with
                    | KdTrees.Level0KdTree.LazyKdTree l ->
                        Expect.isTrue (OpcArchive.isInArchive l.kdtreePath) "KdTree path points into the archive"
                        let t = KdTrees.loadKdtree l.kdtreePath
                        Expect.isTrue t.KdIntersectionTree.BoundingBox3d.IsValid "KdTree reads from the archive"
                    | KdTrees.Level0KdTree.InCoreKdTree _ -> ()

                use texture = Prinziple.openRead (Path.Combine(readPath, "Images", "Checkerboard", "0_0_0.tif"))
                Expect.isTrue texture.CanSeek "texture streams from a stored archive can seek"

            Expect.equal (filesBelow surface) before "nothing was written into the surface folder"

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
                    unitsPerPixel = AVal.constant 1.0
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
            let surface, names = packDidymos folder (tempDir ()) (fun _ -> false)
            let before = filesBelow surface
            let readPaths =
                names |> List.map (fun n ->
                    OpcArchive.prepare (Path.Combine(surface, n)) |> Option.defaultWith (fun () -> failtest "OPC not readable")
                )

            let packed   = render runtime (readPaths |> List.toArray)
            let unpacked = render runtime (names |> List.map (fun n -> Path.Combine(folder, n)) |> List.toArray)

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
            Expect.equal differing 0 "the packed OPC renders exactly like its folder"
            Expect.equal (filesBelow surface) before "nothing was written into the surface folder"
        }
    ]

let tests () =
    testList "opc archives" [
        layoutTests ()
        rejectionTests ()
        importTests ()
        realOpcTests ()
        renderTests ()
    ]
