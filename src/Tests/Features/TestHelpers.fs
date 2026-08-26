/// Shared helpers for the feature tests (one file per protocol section under
/// docs/Test_Protocol/PRo3D_TestProtocol.tex).
///
/// TC references use the hierarchical numbering printed by \testcase{} in the LaTeX
/// document: TC-<section>.<subsection-within-section>.
///
/// Scope: model computation. A few sections need a real surface bounding box or a
/// scene graph, which only exists with a GL runtime; those use the Render fixture
/// below, the same OpenGlApplication PRo3D.exe creates, just without a window.
namespace PRo3D.Tests

open System
open System.Collections.Concurrent
open System.IO
open System.Threading

open Aardvark.Base
open Aardvark.Application                 // MouseButtons, Keys
open Aardvark.Rendering                   // IRuntime, TextureFormat
open Aardvark.Application.Slim            // OpenGlApplication
open Aardvark.UI                          // ColorInput
open Aardvark.UI.Primitives               // NumericInput, ColorPicker, FreeFlyController, ArcBallController
open Aardvark.UI.Animation                // AnimationApp
open Aardvark.UI.Animation.Deprecated     // AnimationAction (fly-to animations)

open FSharp.Data.Adaptive

open Expecto

open Aardvark.GeoSpatial.Opc.Load         // Runtime.CreateLoadRunner
open OpcViewer.Base
open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.Viewer

/// Small model-level helpers used across sections.
[<AutoOpen>]
module Helpers =

    /// Root-group path (empty list = root node in updateNodeAt).
    let rootPath : list<Index> = []

    /// Minimal Annotation leaf for use in GroupsModel tests.
    let makeLeaf (geometry : Geometry) (projection : Projection) (color : C4b) : Leaf =
        let ann =
            Annotation.make
                projection
                None
                geometry
                None
                ({ c = color } : ColorInput)
                Annotation.Initial.thickness
                "test-surface"
        Leaf.Annotations ann

    let initGroups  () = GroupsModel.initial
    let initDrawing () = DrawingModel.initialdrawing

    let addLeaf (leaf : Leaf) (groups : GroupsModel) =
        GroupsApp.addLeafToActiveGroup leaf false groups

    let flatCount (groups : GroupsModel) = groups.flat |> HashMap.count

    let inRootLeaves (id : Guid) (groups : GroupsModel) =
        groups.rootGroup.leaves |> IndexList.toList |> List.contains id

    let surfacesOf (groups : GroupsModel) : list<Surface> =
        groups.flat |> Leaf.toSurfaces |> HashMap.toList |> List.map snd

    /// A surface record built exactly as ViewerAction.ImportSurface builds it, but
    /// pointed at an arbitrary (possibly non-existent) path. Enough for the many
    /// model-level tests that only need a surface leaf with a guid and a name.
    let makeSurface (name : string) : Surface =
        SurfaceUtils.mk SurfaceType.SurfaceOPC MeshLoaderType.Unkown 100.0 name


/// The startup work PRo3D.exe does before any model is touched. Aardvark.Init sets up
/// native-dependency resolution; the pickler factories are what let Serialization read
/// the recent-scenes list and the OPC kd-trees back — without them .save/.loadAs hit a
/// null serializer. Both are idempotent, and the other test lists call Aardvark.Init
/// the same way (see TriangleSetTests, ProfileAttributeExtractionTest).
module Startup =

    let init () =
        Aardvark.Init()
        Serialization.init()
        Serialization.registry.RegisterFactory (fun _ -> KdTrees.level0KdTreePickler)
        Serialization.registry.RegisterFactory (fun _ -> Surface.Init.incorePickler)


/// A headless GL runtime plus the machinery to drive the real viewer update loop.
/// Needed by any section that requires a scene-graph surface (import, fly-to, load).
module Render =

    let dataDir       = Path.Combine(__SOURCE_DIRECTORY__, "..", "data")
    let surfaceName   = "1087_004779_MSLMST_0011"
    let opcName       = "1087_004779_MSLMST_0011_000_000"

    /// The large, binary fixtures live in the PRo3D.Resources.TestData submodule
    /// mounted at src/Tests/resources, kept out of the main repo at ~254 MB.
    let resourcesDir = Path.Combine(__SOURCE_DIRECTORY__, "..", "resources")

    /// Absent unless the clone used --recurse-submodules; see `available` / `skipReason`.
    let opcSurfaceDir = Path.Combine(resourcesDir, surfaceName)

    /// The OPC scene graph — and with it every surface bounding box — cannot be
    /// built without a GL runtime: Sg.createSgSurfaces fails with "GL runner was
    /// not initialized". This is the same setup PRo3D.exe performs on startup,
    /// minus the window. Where no GL context can be had, dependent lists self-skip.
    let context : Lazy<option<IRuntime * IFramebufferSignature>> =
        lazy (
            try
                let app = new OpenGlApplication()
                Aardvark.Rendering.GL.RuntimeConfig.SuppressSparseBuffers <- true
                PRo3D.Core.Surface.Sg.hackRunner <- app.Runtime.CreateLoadRunner 1 |> Some

                let signature =
                    app.Runtime.CreateFramebufferSignature(
                        [
                            DefaultSemantic.Colors,       TextureFormat.Rgba8
                            DefaultSemantic.DepthStencil, TextureFormat.Depth24Stencil8
                        ],
                        samples = ViewerApp.dataSamples)

                Some (app.Runtime :> IRuntime, signature)
            with e ->
                Log.warn "[TestHelpers] no GL runtime: %s" e.Message
                None
        )

    /// True when the OPC test data and a GL context are both available.
    let available () =
        Directory.Exists opcSurfaceDir && (context.Value |> Option.isSome)

    let skipReason () =
        if not (Directory.Exists opcSurfaceDir) then
            Some (sprintf "no OPC test data at %s — run: git submodule update --init src/Tests/resources"
                          (Path.GetFullPath opcSurfaceDir))
        elif context.Value |> Option.isNone then
            Some "no OpenGL runtime in this environment"
        else
            None

    /// Builds a fresh-viewer factory and an update function bound to the runtime,
    /// exactly as PRo3D wires ViewerApp.updateViewer.
    let makeViewer () =
        let runtime, signature = context.Value |> Option.get
        let sendQueue = new BlockingCollection<string>()
        let cts       = new CancellationTokenSource()
        let mailbox   = MailboxProcessor.Start(Viewer.initMessageLoop cts, cts.Token)

        let freshModel () =
            Viewer.initial
                mailbox
                StartupArgs.initArgs
                ""                      // renderingUrl
                1                       // numberOfSamples
                dataDir                 // screenshotDirectory
                ViewerLenses._animator
                "tests"

        let update (m : Model) (msg : ViewerAction) =
            ViewerApp.updateViewer runtime signature sendQueue mailbox m msg

        freshModel, update

    /// FlyTo and other camera animations do not move the camera directly; they push
    /// a 2 s animation the viewer drives from a clock thread. This stands in for that
    /// thread and ticks it to completion against a real clock, as the viewer would.
    let runAnimationToCompletion update (m : Model) =
        let clock = System.Diagnostics.Stopwatch.StartNew()
        let mutable current = m
        while AnimationApp.shouldAnimate current.animations && clock.Elapsed.TotalSeconds < 30.0 do
            current <- update current (ViewerAction.AnimationMessage (AnimationAction.Tick clock.Elapsed.TotalSeconds))
            Thread.Sleep 10
        current


/// Drives the real annotation drawing pipeline (DrawingApp.update) the way the
/// viewer's matchPickingInteraction does: choose a geometry, start drawing, then
/// feed each Ctrl+click as an AddPointAdv. The mouse -> 3D-point pick is the
/// renderer's job, so callers supply the picked points and an identity surface-hit.
module Draw =

    /// Non-ellipse tests use Planet.None so the measurement maths (heights,
    /// altitudes) stay pure. The geographical ellipse projection needs a real
    /// planet, so ellipse tests use Mars, whose CooTransformation the suite
    /// initialises before these tests run (GeoJsonRework.Tests).
    let refSystemFlat = { ReferenceSystem.initial with planet = Planet.None }
    let refSystemMars = ReferenceSystem.initial

    let drawConfig = ViewerApp.drawingConfig
    let bc         = new BlockingCollection<string>()
    let view       = CameraView.lookAt (V3d(0.0, 0.0, 10.0)) V3d.Zero V3d.OOI

    /// the surface hit a pick would return: identity means the click lands exactly
    /// on the point we chose
    let identityHit : V3d -> option<V3d> = Some

    let run (refSystem : ReferenceSystem) (model : DrawingModel) (act : DrawingAction) =
        DrawingApp.update refSystem drawConfig None bc view false model act

    /// choose a geometry and start drawing — selecting a tool, then enabling draw
    let startTool (refSystem : ReferenceSystem) (geom : Geometry) =
        DrawingModel.initialdrawing
        |> fun m -> run refSystem m (DrawingAction.SetGeometry geom)
        |> fun m -> run refSystem m DrawingAction.StartDrawing

    /// feed one Ctrl+click at p
    let click (refSystem : ReferenceSystem) (p : V3d) (m : DrawingModel) =
        run refSystem m (DrawingAction.AddPointAdv(p, identityHit, None, "test-surface", None))

    /// every annotation currently in the flat map
    let annotations (m : DrawingModel) =
        m.annotations.flat |> Leaf.toAnnotations |> HashMap.toList |> List.map snd

    /// the single annotation, or a failing test if there isn't exactly one
    let theAnnotation (label : string) (m : DrawingModel) =
        match annotations m with
        | [ a ] -> a
        | other -> failtestf "%s: expected exactly one annotation, got %d" label (List.length other)

    /// draw a whole annotation of the given geometry from the given points; the
    /// geometries that don't auto-complete get a Finish (the Enter key)
    let drawFull (refSystem : ReferenceSystem) (geom : Geometry) (autoFinishes : bool) (points : V3d list) =
        let drawn = points |> List.fold (fun m p -> click refSystem p m) (startTool refSystem geom)
        if autoFinishes then drawn else run refSystem drawn DrawingAction.Finish
