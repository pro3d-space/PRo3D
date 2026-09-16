/// Surface-effect variants switched at runtime must reach the OPC patches and draw the
/// same pixels (#719). Renders the test-data OPC through the viewer's own scene graph
/// (SurfaceEffectHarness) and changes only the model. Needs a GL context and the test
/// data (PRO3D_TEST_DATA or the src/Tests/resources submodule); skips otherwise.
module PRo3D.Tests.SurfaceEffectSwitchTest

open System
open System.IO

open Expecto

open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Viewer

open PRo3D.Tests

/// long enough for patch loading and LoD refinement to converge
let private settle = TimeSpan.FromSeconds 5.0

/// Mean absolute difference per channel, in [0, 255].
let private meanDifference (a : PixImage<byte>) (b : PixImage<byte>) =
    let ma, mb = a.GetMatrix<C4b>(), b.GetMatrix<C4b>()
    let mutable sum = 0L
    ma.ForeachIndex(fun i ->
        let x, y = ma.[i], mb.[i]
        sum <- sum + int64 (abs (int x.R - int y.R) + abs (int x.G - int y.G) + abs (int x.B - int y.B)))
    float sum / float (3L * ma.SX * ma.SY)

/// Fraction of the lit (surface) pixels of `a` that changed noticeably in `b`.
let private changedFraction (a : PixImage<byte>) (b : PixImage<byte>) =
    let ma, mb = a.GetMatrix<C4b>(), b.GetMatrix<C4b>()
    let mutable lit = 0L
    let mutable changed = 0L
    ma.ForeachIndex(fun i ->
        let x, y = ma.[i], mb.[i]
        if int x.R + int x.G + int x.B > 24 then
            lit <- lit + 1L
            if max (abs (int x.R - int y.R)) (max (abs (int x.G - int y.G)) (abs (int x.B - int y.B))) > 16 then
                changed <- changed + 1L)
    if lit = 0L then 0.0 else float changed / float lit

let private withSurfaces (f : Surface -> Surface) (m : Model) =
    let flat =
        m.scene.surfacesModel.surfaces.flat
        |> HashMap.map (fun _ leaf -> match leaf with Leaf.Surfaces s -> Leaf.Surfaces (f s) | other -> other)
    { m with scene = { m.scene with surfacesModel = { m.scene.surfacesModel with surfaces = { m.scene.surfacesModel.surfaces with flat = flat } } } }

let private triangleFilter (maxSize : float) =
    withSurfaces (fun s -> { s with filterByTriangleSize = true; triangleSize = { s.triangleSize with value = maxSize } })

let tests () =
    testList "surface effect switching (#719)" [

        test "a variant switch at runtime reaches the OPC patches and draws the same pixels" {
            let opcDir =
                match SurfaceEffectHarness.testDataDir () with
                | Some dir when Directory.Exists (Path.Combine(dir, Render.surfaceName)) -> Path.Combine(dir, Render.surfaceName)
                | _ -> Render.opcSurfaceDir
            match Directory.Exists opcDir, Render.context.Value with
            | false, _ -> skiptest (sprintf "no OPC test data: set PRO3D_TEST_DATA or init the submodule (%s)" opcDir)
            | true, None -> skiptest "no OpenGL runtime in this environment"
            | true, Some (runtime, signature) ->

            Startup.init ()
            // load patches synchronously, like PRo3D.Snapshots: no LoD race with the settle time
            PRo3D.Core.Surface.Sg.useAsyncLoading <- false
            do Aardvark.Base.Aardvark.UnpackNativeDependencies(typeof<PRo3D.Extensions.FSharp.CooTransformation.RelState>.Assembly)
            CooTransformation.initCooTrafo None (Path.combine [ Environment.GetFolderPath Environment.SpecialFolder.ApplicationData; "Pro3D" ])

            let freshModel, update = Render.makeViewer ()
            let imported = update (freshModel ()) (ViewerAction.ImportSurface [ opcDir ])
            // Looking down at the OPC along the body's radial up. FlyToSurface looks from the
            // bounding box's max corner, which for this OPC lies under the terrain: black.
            let framed =
                match imported.scene.surfacesModel.sgSurfaces |> HashMap.toSeq |> Seq.tryHead with
                | Some (_, surface) ->
                    let bb = surface.globalBB
                    let c, up = bb.Center, bb.Center.Normalized
                    let north = up.Cross(V3d.OOI).Cross(up).Normalized
                    let view = CameraView.lookAt (c + up * 1.5 * bb.Size.Length) c north
                    { imported with navigation = { imported.navigation with camera = { imported.navigation.camera with view = view } } }
                | None -> failtest "the OPC import produced no surface"

            let am = AdaptiveModel.Create framed
            use renderer = new SurfaceEffectHarness.Renderer(runtime, signature, SurfaceEffectHarness.surfacesSg runtime am, V2i(800, 600))
            let show (m : Model) =
                transact (fun () -> am.Update m)
                renderer.Render settle

            // 1. default state: the lean variant (no geometry stage)
            let lean = show framed
            Expect.isGreaterThan (SurfaceEffectHarness.litFraction lean) 0.02 "the OPC is visible"

            // 2. a filter that filters nothing needs the geometry-stage variant: same pixels
            let full = show (triangleFilter 1e9 framed)
            Expect.isLessThan (meanDifference lean full) 2.0 "the geometry-stage variant draws the same surface"

            // 3. only that variant filters: a tiny maximum removes every triangle, proving
            //    step 2 did not simply keep drawing with the lean variant
            let filtered = show (triangleFilter 1e-9 framed)
            Expect.isLessThan (SurfaceEffectHarness.litFraction filtered) 0.001 "the switch reached the OPC patches"

            // 4. and back
            let back = show framed
            Expect.isLessThan (meanDifference lean back) 2.0 "switching back restores the lean rendering"
        }

        // The user's workflow: a surface starts without a projection (lean variant), then an
        // image goes onto the projection stack, which switches in the geometry stage for the
        // face normal the projection needs. The projection must appear, pixel for pixel as on
        // a surface that had the geometry stage from the start. Also with the opt-in winding
        // correction, which flips the projection's facing test.
        for asyncLoading in [ false; true ] do
        for winding in [ false; true ] do
        test (sprintf "a projection added at runtime appears (async loading %b, winding correction %b)" asyncLoading winding) {
            // the tests-ui scene: the Dimorphos OPC with SPICE set up (tests-ui writes it)
            let scene = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "tests-ui", "artifacts", "testdata-scene.pro3d")
            let images =
                SurfaceEffectHarness.testDataDir ()
                |> Option.map (fun d -> Path.Combine(d, "HERA", "Dimorphos_opc", "AFC_2027-03-21"))
            match File.Exists scene, images, Render.context.Value with
            | false, _, _ -> skiptest (sprintf "no tests-ui scene at %s (run a tests-ui spec once)" scene)
            | _, None, _ -> skiptest "PRO3D_TEST_DATA is not set"
            | _, _, None -> skiptest "no OpenGL runtime in this environment"
            | true, Some imageDir, Some (runtime, signature) ->

            Startup.init ()
            PRo3D.Core.Surface.Sg.useAsyncLoading <- asyncLoading
            let gis msg = ViewerAction.GisAppMessage (Gis.GisAppAction.ProjectedImageListMessage msg)

            /// (before, after) adding the first image to the stack. `geometryStageFromStart`:
            /// a triangle filter that filters nothing keeps the geometry stage composed.
            let project (geometryStageFromStart : bool) =
                let freshModel, update = Render.makeViewer ()
                let loaded = update (freshModel ()) (ViewerAction.LoadScene scene)
                let loaded = if winding then update loaded (gis PRo3D.ImageMapping.ProjectedImageListMessage.ToggleWindingCorrection) else loaded
                let loaded = if geometryStageFromStart then triangleFilter 1e9 loaded else loaded
                let withImages = update loaded (gis (PRo3D.ImageMapping.ProjectedImageListMessage.LoadImagesDir imageDir))

                let am = AdaptiveModel.Create withImages
                use renderer = new SurfaceEffectHarness.Renderer(runtime, signature, SurfaceEffectHarness.surfacesSg runtime am, V2i(800, 600))
                let show (m : Model) =
                    transact (fun () -> am.Update m)
                    renderer.Render settle

                let before = show withImages
                match withImages.scene.gisApp.projectedImageList.images |> IndexList.tryFirst with
                | None -> failtest "no image imported"
                | Some image ->
                    let after = show (update withImages (gis (PRo3D.ImageMapping.ProjectedImageListMessage.AddToStack image.id)))
                    before, after

            let before, switched = project false
            let _, fromStart = project true
            Expect.isGreaterThan (SurfaceEffectHarness.litFraction before) 0.02 "the OPC is visible"
            Expect.isGreaterThan (changedFraction before switched) 0.01 "the projected image shows on the surface"
            Expect.isLessThan (meanDifference switched fromStart) 1.0
                "switched in at runtime, the projection looks as with the geometry stage from the start"
        }
    ]
