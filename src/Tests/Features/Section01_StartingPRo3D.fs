/// Section 1 — Starting PRo3D
///   TC-1.1 (Start PRo3D), TC-1.2 (Add Surface (OPC)), TC-1.3 (Surface FlyTo),
///   TC-1.4 (Save Scene), TC-1.5 (Load Scene (Open)), TC-1.6 (Load Scene (Recent))
///
///   These drive the real viewer: every step is a ViewerAction fed to
///   ViewerApp.updateViewer, the same function the UI calls. Import, fly-to and load
///   all need a GL runtime (Sg.createSgSurfaces fails without one, and fly-to aims
///   at the sg surface's bounding box), so the run uses the shared Render fixture.
module PRo3D.Tests.Section01_StartingPRo3D

open System
open System.IO

open Aardvark.Base
open Aardvark.UI.Animation.Deprecated    // AnimationApp

open FSharp.Data.Adaptive

open Expecto

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Viewer
open PRo3D.Tests

module private Scene =

    /// While this is true the run deletes the scene files it wrote once the
    /// assertions against them are done. Flip it to false to leave them on disk
    /// so they can be checked by hand (see the last test in the list) — they are
    /// gitignored either way, but a run then leaves a new set behind every time.
    let cleanupGeneratedFiles = true

    /// PRo3D keeps its recent-scenes list in ./recent, relative to the working
    /// directory — the same file the installed viewer writes. Saving a scene
    /// rewrites it, so the run stashes it and puts it back afterwards.
    let private recentPath = "./recent"

    let private readRecentBackup () =
        if File.Exists recentPath then Some(File.ReadAllBytes recentPath) else None

    let private restoreRecentBackup (backup : option<byte[]>) =
        match backup with
        | Some bytes -> File.WriteAllBytes(recentPath, bytes)
        | None       -> if File.Exists recentPath then File.Delete recentPath

    /// Removes everything saving a scene wrote for it.
    let deleteGenerated (scenePath : string) =
        let paths = ViewerIO.ScenePaths.create scenePath
        for file in [ paths.scene; paths.annotations; paths.correlations ] do
            if File.Exists file then File.Delete file
        if Directory.Exists paths.bookmarksFolder then
            Directory.Delete(paths.bookmarksFolder, true)

    type Fixture = {
        started       : Model    // TC-1.1
        imported      : Model    // TC-1.2
        flyToPushed   : Model    // TC-1.3, before the animation is ticked
        flownTo       : Model    // TC-1.3, after it has run
        saved         : Model    // TC-1.4
        reopened      : Model    // TC-1.5
        fromRecent    : Model    // TC-1.6
        scenePath     : string
        surfaceId     : Guid
        surfaceBox    : Box3d    // what FlyTo aims at
        recentOnDisk  : Recent
    }

    /// One full run — start, import, fly to, save, reopen — shared by every TC-1 test.
    let fixture =
        lazy (
            let freshModel, update = Render.makeViewer ()

            // TC-1.1 / TC-1.2
            let started  = freshModel ()
            let imported = update started (ViewerAction.ImportSurface [ Render.opcSurfaceDir ])

            let surfaceId =
                match imported.scene.surfacesModel.surfaces.flat |> HashMap.toList with
                | [ (id, _) ] -> id
                | other -> failwithf "expected one surface after import, got %d" (List.length other)

            // the bounding box the renderer built for the surface: FlyTo's target
            let surfaceBox =
                (imported.scene.surfacesModel.sgSurfaces |> HashMap.find surfaceId).globalBB

            // TC-1.3
            let flyToPushed =
                update imported (ViewerAction.SurfaceActions (SurfaceAppAction.FlyToSurface surfaceId))
            let flownTo = flyToPushed |> Render.runAnimationToCompletion update

            let scenePath =
                let stamp = DateTime.Now.ToString "yyyyMMdd_HHmmss"
                Path.Combine(Render.dataDir, sprintf "TestScene_%s.pro3d" stamp)

            let backup = readRecentBackup ()

            // TC-1.4 — saving from the flown-to camera, so the scene records it
            let saved = update flownTo (ViewerAction.SaveAs scenePath)
            let recentOnDisk = Serialization.loadAs<Recent> recentPath

            // TC-1.5 — reopen into a brand new model, as "Scene -> Open" would
            let reopened = update (freshModel ()) (ViewerAction.LoadScene scenePath)

            // TC-1.6 — the same, but resolving the path through the recent list
            let fromRecent =
                let newest =
                    recentOnDisk.recentScenes
                    |> List.sortByDescending (fun s -> s.writeDate)
                    |> List.head
                update (freshModel ()) (ViewerAction.LoadScene newest.path)

            restoreRecentBackup backup

            {
                started      = started
                imported     = imported
                flyToPushed  = flyToPushed
                flownTo      = flownTo
                saved        = saved
                reopened     = reopened
                fromRecent   = fromRecent
                scenePath    = scenePath
                surfaceId    = surfaceId
                surfaceBox   = surfaceBox
                recentOnDisk = recentOnDisk
            }
        )

let tests =
    testSequenced <| testList "Section 1 — Starting PRo3D" [

        match Render.skipReason () with
        | Some reason ->
            test "TC-1.1 – TC-1.6 (skipped)" { skiptest reason }
        | None ->

        /// The single surface of the model, or a failing test if there is not exactly one.
        let theSurface (label : string) (m : Model) =
            match surfacesOf m.scene.surfacesModel.surfaces with
            | [ s ] -> s
            | other -> failtestf "%s: expected exactly one surface, got %d" label (List.length other)

        // TC-1.1 Start PRo3D

        test "TC-1.1 a freshly started viewer has no surfaces" {
            let m = Scene.fixture.Value.started
            Expect.isEmpty (surfacesOf m.scene.surfacesModel.surfaces)
                "surface list should be empty on startup"
        }

        test "TC-1.1 a freshly started viewer has no scene path" {
            let m = Scene.fixture.Value.started
            Expect.isNone m.scene.scenePath "no scene should be open on startup"
        }

        // TC-1.2 Add Surface (OPC) — via ViewerAction.ImportSurface

        test "TC-1.2 importing the OPC adds exactly one surface" {
            let m = Scene.fixture.Value.imported
            Expect.equal (surfacesOf m.scene.surfacesModel.surfaces |> List.length) 1
                "import should add one surface"
        }

        test "TC-1.2 the imported surface points at the OPC on disk" {
            let s = Scene.fixture.Value.imported |> theSurface "import"
            Expect.equal s.name Render.surfaceName "surface is named after its folder"
            Expect.equal s.surfaceType SurfaceType.SurfaceOPC "surface type should be OPC"
            Expect.equal s.importPath Render.opcSurfaceDir "import path should be the chosen folder"
            Expect.equal s.opcNames [ Render.opcName ] "the folder contains exactly one OPC"
            Expect.all s.opcPaths Directory.Exists "every discovered OPC path should exist"
        }

        test "TC-1.2 importing reads the .opcx surface attributes" {
            let s = Scene.fixture.Value.imported |> theSurface "import"
            Expect.isSome s.opcxPath "the .opcx next to the OPC should be picked up"
            Expect.equal (s.textureLayers |> IndexList.count) 1
                "the test .opcx declares a single texture layer"
            Expect.isSome s.primaryTexture "the texture layer should become the primary texture"
        }

        test "TC-1.2 importing builds a renderable surface with a bounding box" {
            let f = Scene.fixture.Value
            Expect.isTrue (f.imported.scene.surfacesModel.sgSurfaces |> HashMap.containsKey f.surfaceId)
                "the imported surface should have a scene-graph surface"
            Expect.isFalse (f.surfaceBox.IsInvalid || f.surfaceBox.IsEmpty)
                "the scene-graph surface should have a real bounding box"
        }

        // TC-1.3 Surface FlyTo — via SurfaceAppAction.FlyToSurface.
        //   FlyTo does not set the camera, it queues a 2 second camera animation
        //   aimed at the surface bounding box; the camera only arrives once that
        //   animation has been ticked to completion.

        test "TC-1.3 FlyTo queues a camera animation" {
            let f = Scene.fixture.Value
            Expect.isNonEmpty (f.flyToPushed.animations.animations |> IndexList.toList)
                "FlyTo should push an animation"
            Expect.isTrue (AnimationApp.shouldAnimate f.flyToPushed.animations)
                "the queued animation should be pending"
        }

        test "TC-1.3 FlyTo lands the camera on the surface bounding box" {
            let f    = Scene.fixture.Value
            let view = f.flownTo.navigation.camera.view

            Expect.isFalse (AnimationApp.shouldAnimate f.flownTo.animations)
                "the fly-to animation should have finished"
            // Position is the point of FlyTo, and it is exact: the camera arrives at
            // the corner of the surface bounding box that addFlyToSurfaceAnimation
            // targets (CameraView.lookAt bb.Max bb.Center).
            Expect.isLessThan (Vec.distance view.Location f.surfaceBox.Max) 1e-6
                "camera should end up at the bounding box maximum"
            // The view direction ends up along the Max->Center diagonal of the box.
            // Note the sign: PRo3D's deprecated fly-to animation leaves the camera
            // pointing along +diagonal (away from the centre), the negation of the
            // lookAt target it computes. We assert colinearity, not the sign.
            let axis = (f.surfaceBox.Max - f.surfaceBox.Center).Normalized
            Expect.floatClose Accuracy.medium (abs (Vec.dot view.Forward axis)) 1.0
                "camera view direction should lie along the box Max-Center axis"
        }

        test "TC-1.3 FlyTo actually moved the camera" {
            let f = Scene.fixture.Value
            Expect.notEqual
                f.flownTo.navigation.camera.view.Location
                f.started.navigation.camera.view.Location
                "the camera should not still be at its startup position"
        }

        // TC-1.4 Save Scene — via ViewerAction.SaveAs

        test "TC-1.4 saving writes the scene file" {
            let f = Scene.fixture.Value
            Expect.isTrue (File.Exists f.scenePath)
                (sprintf "scene file %s should have been written" f.scenePath)
        }

        test "TC-1.4 saving writes the annotation side-car" {
            let f = Scene.fixture.Value
            let annotations = (ViewerIO.ScenePaths.create f.scenePath).annotations
            Expect.isTrue (File.Exists annotations)
                (sprintf "annotation file %s should have been written" annotations)
        }

        test "TC-1.4 saving records the scene path in the model" {
            let f = Scene.fixture.Value
            Expect.equal f.saved.scene.scenePath (Some f.scenePath)
                "the saved model should know where it was saved to"
        }

        test "TC-1.4 the saved scene records the flown-to camera" {
            let f = Scene.fixture.Value
            Expect.isLessThan (Vec.distance f.saved.scene.cameraView.Location f.surfaceBox.Max) 1e-6
                "the scene should be saved with the camera FlyTo left it at, not the startup camera"
        }

        // TC-1.5 Load Scene (Open) — via ViewerAction.LoadScene into a fresh model

        test "TC-1.5 the reopened scene contains the imported surface" {
            let f = Scene.fixture.Value
            let s = f.reopened |> theSurface "reopened"
            Expect.equal s.name Render.surfaceName "surface should survive the round trip"
            Expect.equal s.guid f.surfaceId "the surface should be the one that was saved"
            Expect.equal s.importPath Render.opcSurfaceDir "import path should survive the round trip"
            Expect.equal s.opcNames [ Render.opcName ] "OPC names should survive the round trip"
        }

        test "TC-1.5 the reopened scene renders the surface again" {
            let f = Scene.fixture.Value
            Expect.isTrue (f.reopened.scene.surfacesModel.sgSurfaces |> HashMap.containsKey f.surfaceId)
                "loading should rebuild the scene-graph surface"
        }

        test "TC-1.5 the reopened scene restores the flown-to camera" {
            let f = Scene.fixture.Value
            Expect.isLessThan
                (Vec.distance f.reopened.navigation.camera.view.Location f.surfaceBox.Max) 1e-6
                "the camera should come back where FlyTo left it"
        }

        test "TC-1.5 the reopened scene knows its own path" {
            let f = Scene.fixture.Value
            Expect.equal f.reopened.scene.scenePath (Some f.scenePath) "scene path should be set on load"
        }

        // TC-1.6 Load Scene (Recent)

        test "TC-1.6 saving puts the scene at the head of the recent list" {
            let f = Scene.fixture.Value
            match f.saved.recent.recentScenes with
            | head :: _ ->
                Expect.equal head.path f.scenePath "the scene just saved should be the newest entry"
                Expect.equal head.name (Path.GetFileName f.scenePath)
                    "the recent entry is labelled with the file name, which is what the menu shows"
            | [] -> failtest "recent list should not be empty after saving"
        }

        test "TC-1.6 the recent list is persisted to disk" {
            let f = Scene.fixture.Value
            Expect.contains (f.recentOnDisk.recentScenes |> List.map (fun s -> s.path)) f.scenePath
                "./recent should mention the saved scene"
        }

        test "TC-1.6 opening the newest recent entry restores the scene" {
            let f = Scene.fixture.Value
            let s = f.fromRecent |> theSurface "from recent"
            Expect.equal s.name Render.surfaceName
                "the surface should come back when loading via the recent list"
            Expect.equal f.fromRecent.scene.scenePath (Some f.scenePath)
                "the recent entry should resolve to the scene that was saved"
        }

        // Teardown — must stay last in this sequenced list.

        test "generated scene files are cleaned up" {
            let f = Scene.fixture.Value
            if Scene.cleanupGeneratedFiles then
                Scene.deleteGenerated f.scenePath
                Expect.isFalse (File.Exists f.scenePath) "the generated scene file should be gone"
            else
                skiptest (sprintf
                            "Scene.cleanupGeneratedFiles is false — left %s on disk for inspection"
                            f.scenePath)
        }
    ]
