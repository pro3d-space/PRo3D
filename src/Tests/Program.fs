open Expecto
open NUnit

// Initialise Aardvark up front: this sets up native-dependency resolution
// (incl. the PRo3D.SPICE natives CooTransformation + cspice) into its managed
// temp directory for every assembly, so the SPICE P/Invokes resolve.
do Aardvark.Base.Aardvark.Init()

// Same serializer setup PRo3D.exe does on startup: the recent-scenes list and the
// OPC kd-trees are read back through these picklers. Without it Serialization
// .save/loadAs hit a null serializer.
do PRo3D.Base.Serialization.init()
do PRo3D.Base.Serialization.registry.RegisterFactory (fun _ -> OpcViewer.Base.KdTrees.level0KdTreePickler)
do PRo3D.Base.Serialization.registry.RegisterFactory (fun _ -> PRo3D.Core.Surface.Init.incorePickler)


// Feature tests, one list per protocol section (docs/Test_Protocol). New section
// files are registered here as they are added.
let featureTests () : Test =
    testList "PRo3D feature tests" [
        PRo3D.Tests.Section01_StartingPRo3D.tests
        PRo3D.Tests.Section02_ViewerActionsNavigation.tests
        PRo3D.Tests.Section03_DrawingAnnotations.tests
        PRo3D.Tests.Section04_SurfaceProperties.tests
        PRo3D.Tests.Section05_AnnotationProperties.tests
        PRo3D.Tests.Section06_Scalebars.tests
        PRo3D.Tests.Section07_Bookmarks.tests
        PRo3D.Tests.Section09_ViewerConfiguration.tests
        PRo3D.Tests.Section10_Grouping.tests
        PRo3D.Tests.Section13_ContourMultitexturing.tests
        PRo3D.Tests.Section14_SurfaceComparison.tests
        PRo3D.Tests.Section18_KeyboardShortcuts.tests
        PRo3D.Tests.Section19_UndoRedoGroupColor.tests
    ]

let allTests () : Test =
    testList "all tests" [
        // default: kernel-independent tests (use only the default SPICE kernels)
        GeoJsonRework.Tests.tests()
        SpiceTests.tests()

        // special case: HERA tests self-skip when their kernels are absent or
        // when --skip-hera is passed (see HeraSpiceTests).
        HeraSpiceTests.tests()
        featureTests ()
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
    // --skip-hera is our own flag (consumed by HeraSpiceTests via the process
    // command line); strip it so Expecto doesn't reject it as an unknown arg.
    let expectoArgs = args |> Array.filter (fun a -> a <> "--skip-hera")
    runTestsWithCLIArgs [] expectoArgs (allTests ())