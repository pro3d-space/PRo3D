open Expecto
open NUnit
open Aardvark.Base
open Aardvark.Data.Opc


// Feature tests, one list per protocol section (docs/Test_Protocol). New section
// files are registered here as they are added.
let featureTests () : Test =
    PRo3D.Tests.Startup.init()
    testList "PRo3D feature tests" [
        PRo3D.Tests.Section01_StartingPRo3D.tests
        PRo3D.Tests.Section02_ViewerActionsNavigation.tests
        PRo3D.Tests.Section03_DrawingAnnotations.tests
        PRo3D.Tests.Section04_SurfaceProperties.tests
        PRo3D.Tests.Section05_AnnotationProperties.tests
        PRo3D.Tests.Section06_Scalebars.tests
        PRo3D.Tests.Section07_Bookmarks.tests
        PRo3D.Tests.Section08_SequencedBookmarks.tests
        PRo3D.Tests.Section09_ViewerConfiguration.tests
        PRo3D.Tests.Section10_Grouping.tests
        PRo3D.Tests.Section12_GisView.tests
        PRo3D.Tests.Section12_SceneBody.tests
        PRo3D.Tests.Section13_ContourMultitexturing.tests
        PRo3D.Tests.Section14_SurfaceComparison.tests
        PRo3D.Tests.Section16_CommandLine.tests
        PRo3D.Tests.Section18_KeyboardShortcuts.tests
        PRo3D.Tests.Section19_UndoRedoGroupColor.tests
        PRo3D.Tests.Section20_BooleanOperations.tests
        PRo3D.Tests.Section21_OutcropTraces.tests
    ]

let allTests (parameters : TestUtils.TestParameters) : Test =
    // SPICE kernel state is process-global: exactly one metakernel is active, swapped
    // via DeInit+Init (see HeraSpiceTests.ensureKernelAt). Under parallel execution
    // another test can swap the kernel between a test's ensure and its SPICE calls,
    // which shows up as spurious None results depending on schedule. Sequence the
    // whole suite; per-list testSequenced is not enough, lists still interleave.
    testSequenced <| testList "all tests" [
        // kernel-independent tests (use only the default SPICE kernels)
        GeoJsonRework.Tests.tests()
        AnnotationExportTest.tests()
        ColorByCategoryPersistence.Tests.tests()
        ColorByCategoryColor.Tests.tests()
        SpiceTests.tests()
        TriangleSetTests.tests()
        BulkAnnotationRoseTest.tests()
        BulkAnnotationRoseTest.largeTests()
        OutcropTraceAttitudeTest.tests()
        OutcropTraceShaderTest.tests()
        SurfaceEffectVariantTest.tests()
        PRo3D.Tests.SurfaceEffectSwitchTest.tests()
        PolygonFillTests.tests()
        RegionOpsTests.tests()
        RegionFixtureTests.tests()
        AnnotationRegionOpsTests.tests()
        AdaptiveNestingTests.tests()
        VertexEditingTests.tests()

        // requires the (non-public) HERA kernels; self-skips without them
        HeraSpiceTests.tests()

        // resolves its fixtures from PRO3D_TEST_DATA / --testdatasource; self-skips
        SbmtImportAlignmentTest.tests parameters

        ProjectedImageMetadataTest.tests()
        ProjectedImageStackTest.tests()

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

        // pro3d-tool verbs. Placed after the kernel-sensitive tests for the same reason
        // they are ordered above: the sun-angles case needs the plan kernel, and every
        // swap degrades DAF handles. It reuses HeraSpiceTests' kernel tracking rather
        // than doing its own Init/DeInit, so it adds no swap of its own. Its kdtree
        // cases need neither kernels nor a GPU and always run.
        Pro3DToolTests.tests()
        MbiSidecarTest.tests()

        // unproject: the pixel addressing and table cases need no data; the shape-model
        // cross-check reuses the same kernel tracking and self-skips without kernels.
        UnprojectTest.tests()

        // end-to-end batch rendering with sun lighting; self-skips without the
        // C:\pro3ddata workshop fixture, $PRO3D_SPICE_KERNELS, a GPU, or a built
        // PRo3D.Snapshots.exe. Uses its own kernel tree (the env var), not the suite's.
        PRo3D.Tests.SnapshotSunLightingTest.tests()

        // Sections whose OPC-backed lists self-skip when the test-data submodule
        // (src/Tests/resources) or a GL context is unavailable.
        featureTests ()
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
        // No CLI here, so the data-backed lists fall back to PRO3D_TEST_DATA.
        let parameters : TestUtils.TestParameters = { testDataSource = None }
        let r = allTests parameters |> runTests Impl.ExpectoConfig.defaultConfig 
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
    // Fixture generation, not a test run: writes the view-plan footprint scene for
    // issue #733 and exits. Defaults into the test-data submodule, which is where
    // the committed fixture lives; see PRo3D.Resources.TestData/cases/viewplan-footprint.
    // Is a GL context obtainable at all in this process, and on which thread? On macOS
    // GLFW drives an NSApplication run loop, so where the context is created decides
    // whether the process makes progress or sits in the Cocoa event loop forever.
    // Short-circuits before Expecto so the main thread is still ours.
    if args |> Array.contains "--gl-probe" then
        printfn "[probe] thread=%d isMain(approx)=%b"
            System.Threading.Thread.CurrentThread.ManagedThreadId
            (System.Threading.Thread.CurrentThread.ManagedThreadId = 1)
        printfn "[probe] Aardvark.Init()..."
        Aardvark.Base.Aardvark.Init()
        printfn "[probe] new OpenGlApplication()..."
        let app = new Aardvark.Application.Slim.OpenGlApplication()
        printfn "[probe] runtime = %s | %s" app.Runtime.Context.Driver.vendor app.Runtime.Context.Driver.renderer
        printfn "[probe] OK"
        exit 0

    // macOS: GLFW drives an NSApplication run loop, so a context created on an Expecto
    // worker thread sits in the Cocoa event loop forever instead of returning. Forcing the
    // lazy here, while the main thread is still ours, makes the GL-dependent tests runnable
    // on macOS at all; they otherwise hang rather than skip.
    if args |> Array.contains "--gl-init-main" then
        match PRo3D.Tests.Render.context.Value with
        | Some _ -> printfn "[gl-init-main] GL runtime created on the main thread"
        | None -> printfn "[gl-init-main] WARNING: no GL runtime"

    // Performance measurement, not a correctness test: runs on the main thread, outside
    // Expecto, and exits. See SurfaceEffectBenchmark.
    // Why did #719 measure "drop generateNormal alone" SLOWER than composing both
    // geometry shaders (146 ms vs 64 ms)? Hypothesis: with no writer for LocalNormal, the
    // stages that read it turn it into a vertex INPUT, which the OPC patches do not
    // provide. Prints the inputs so the claim is checkable rather than asserted.
    // What would computing normals on the CPU at patch load actually cost? Loads every
    // patch of an OPC and times the two candidate strategies against the load itself, so
    // the trade-off is measured rather than guessed. PRO3D_BENCH_OPC selects the dataset.
    if args |> Array.contains "--normal-cost" then
        Aardvark.Base.Aardvark.Init()
        let root =
            match System.Environment.GetEnvironmentVariable "PRO3D_BENCH_OPC" with
            | null | "" -> failwith "set PRO3D_BENCH_OPC"
            | d -> d
        let opcs =
            if System.IO.Directory.Exists (System.IO.Path.Combine(root, "patches")) then [ root ]
            else System.IO.Directory.GetDirectories root |> Array.toList
                 |> List.filter (fun d -> System.IO.Directory.Exists (System.IO.Path.Combine(d, "patches")))
        let ser = MBrace.FsPickler.FsPickler.CreateBinarySerializer()
        let mutable totalVerts = 0L
        let mutable totalTris = 0L
        let mutable loadMs = 0.0
        let mutable faceMs = 0.0
        let mutable vertMs = 0.0
        let sw = System.Diagnostics.Stopwatch()
        for opc in opcs do
            let h = PatchHierarchy.load ser.Pickle ser.UnPickle (OpcPaths.OpcPaths opc)
            let rec patches t =
                match t with
                | QTree.Node (p, cs) -> p :: (cs |> Array.toList |> List.collect patches)
                | QTree.Leaf p -> [ p ]
            let ps = patches h.tree
            printfn "[normals] %s: %d patches" (System.IO.Path.GetFileName opc) ps.Length
            for p in ps do
                sw.Restart()
                let (g, _) = Aardvark.Data.Opc.Patch.load h.opcPaths ViewerModality.XYZ p.info
                sw.Stop(); loadMs <- loadMs + sw.Elapsed.TotalMilliseconds
                let pos = g.IndexedAttributes.[Aardvark.Rendering.DefaultSemantic.Positions] |> unbox<V3f[]>
                let index = match g.IndexArray with | :? (int[]) as a -> a | _ -> [||]
                let triCount = index.Length / 3
                totalVerts <- totalVerts + int64 pos.Length
                totalTris <- totalTris + int64 triCount

                // (a) per-FACE normals: one cross product per triangle
                sw.Restart()
                let faceN : V3f[] = Array.zeroCreate triCount
                for t in 0 .. triCount - 1 do
                    let a = pos.[index.[3*t]]
                    let b = pos.[index.[3*t+1]]
                    let c = pos.[index.[3*t+2]]
                    faceN.[t] <- Vec.cross (b - a) (c - a) |> Vec.normalize
                sw.Stop(); faceMs <- faceMs + sw.Elapsed.TotalMilliseconds

                // (b) per-VERTEX normals: accumulate face normals, then normalize
                sw.Restart()
                let vertN : V3f[] = Array.zeroCreate pos.Length
                for t in 0 .. triCount - 1 do
                    let i0, i1, i2 = index.[3*t], index.[3*t+1], index.[3*t+2]
                    let n = Vec.cross (pos.[i1] - pos.[i0]) (pos.[i2] - pos.[i0])
                    vertN.[i0] <- vertN.[i0] + n
                    vertN.[i1] <- vertN.[i1] + n
                    vertN.[i2] <- vertN.[i2] + n
                for i in 0 .. vertN.Length - 1 do vertN.[i] <- Vec.normalize vertN.[i]
                sw.Stop(); vertMs <- vertMs + sw.Elapsed.TotalMilliseconds
        printfn "[normals] vertices %d  triangles %d" totalVerts totalTris
        printfn "[normals] patch load (geometry only) %8.1f ms" loadMs
        printfn "[normals] per-face   normals         %8.1f ms  (%+.1f%% of load)  %.1f MB" faceMs (100.0*faceMs/loadMs) (float totalTris * 12.0 / 1048576.0)
        printfn "[normals] per-vertex normals         %8.1f ms  (%+.1f%% of load)  %.1f MB" vertMs (100.0*vertMs/loadMs) (float totalVerts * 12.0 / 1048576.0)
        exit 0

    if args |> Array.contains "--effect-inputs" then
        Aardvark.Base.Aardvark.Init()
        let show name (e : FShade.Effect) =
            let ins = e.Inputs |> Map.toList |> List.map fst |> List.sort
            printfn "[inputs] %-34s %s" name (String.concat ", " ins)
            printfn "[inputs] %-34s LocalNormal present: %b" "" (ins |> List.contains "LocalNormal")
        let trafo = FShade.Effect.ofFunction PRo3D.ViewerUtils.Shader.stableTrafo
        let sun   = FShade.Effect.ofFunction PRo3D.SPICE.Shaders.solarShadingLS
        let gen   = FShade.Effect.ofFunction PRo3D.Core.ImageProjection.Shaders.generateNormal
        let noFN  = FShade.Effect.ofFunction PRo3D.Core.ImageProjection.Shaders.noFaceNormal
        show "reader alone (no writer)"        (FShade.Effect.compose [ trafo; sun ])
        show "reader + generateNormal (GS)"    (FShade.Effect.compose [ trafo; gen; sun ])
        show "reader + noFaceNormal (no GS)"   (FShade.Effect.compose [ trafo; noFN; sun ])
        exit 0

    if args |> Array.contains "--bench" then
        // unpacks the native deps (glvm et al) that OpenGlApplication needs; Render.context
        // swallows the failure and reports "no GL runtime" if this has not run
        Aardvark.Base.Aardvark.Init()
        match PRo3D.Tests.Render.context.Value with
        | Some _ -> printfn "[bench] GL runtime created on the main thread"
        | None -> printfn "[bench] WARNING: no GL runtime"
        exit (PRo3D.Tests.SurfaceEffectBenchmark.run ())

    match args |> Array.tryFindIndex ((=) "--make-footprint-scene") with
    | Some i ->
        let outDir =
            match args |> Array.tryItem (i + 1) with
            | Some d when not (d.StartsWith "--") -> d
            | _ -> System.IO.Path.Combine(__SOURCE_DIRECTORY__, "resources", "cases", "viewplan-footprint")
        PRo3D.Tests.FootprintSceneFixture.build outDir |> ignore
        0
    | None ->

    // --skip-hera is consumed by HeraSpiceTests via the process command line;
    // strip it so Expecto doesn't reject it as an unknown argument.
    let config = parseArgs (args |> Array.filter (fun a -> a <> "--skip-hera"))

    let parameters : TestUtils.TestParameters = { testDataSource = config.testDataSource }

    // The profile tests resolve their fixtures from PRO3D_TEST_DATA first and
    // --testdatasource second, and skip when neither points at a
    // PRo3D.Resources.TestData checkout -- so they are always registered.
    match config.testDataSource, System.Environment.GetEnvironmentVariable "PRO3D_TEST_DATA" with
    | Some path, _ -> printfn "Test data source: %s" path
    | None, (null | "") ->
        printfn "No test data source specified (set PRO3D_TEST_DATA or use --testdatasource <path>)"
    | None, path -> printfn "Test data source (PRO3D_TEST_DATA): %s" path

    let tests = testList "all" [ allTests parameters; profileTests parameters ]

    runTestsWithCLIArgs [] (Array.ofList config.expectoArgs) tests