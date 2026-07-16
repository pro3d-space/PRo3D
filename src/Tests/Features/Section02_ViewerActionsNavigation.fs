/// Section 2 — Viewer Actions and Navigation
///   TC-2.1 (FreeFly Navigation), TC-2.2 (ArcBall Navigation),
///   TC-2.3 (Pick Explore Center), TC-2.4 (Place Coordinate System)
///
///   These exercise PRo3D's own navigation dispatcher (Navigation.update) and
///   reference-system logic (ReferenceSystemApp.update) — the exact functions the
///   viewer delegates to. The per-frame WASD/mouse integration lives in Aardvark's
///   FreeFly/ArcBall controllers and is driven by the render loop; here we drive
///   those controllers through PRo3D's wiring and assert the invariants that hold
///   regardless of frame timing.
module PRo3D.Tests.Section02_ViewerActionsNavigation

open System.Threading

open Aardvark.Base
open Aardvark.Rendering                 // CameraView
open Aardvark.Application               // MouseButtons
open Aardvark.UI.Primitives             // FreeFlyController, ArcBallController

open Expecto

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Navigation2                   // NavigationModel
open PRo3D.Viewer
open PRo3D.Tests

module private Nav =

    /// The real config the viewer passes to Navigation.update.
    let viewConfig = ViewConfigModel.initial
    let refSystem  = ReferenceSystem.initial
    let navConf    = ViewerApp.navConf

    /// Navigation.update with the viewer's own config, matching how the
    /// NavigationMessage handler calls it (Viewer.fs).
    let run (switchToArcball : bool)
            (pick            : option<unit -> option<V3d>>)
            (ctrlFlag        : bool)
            (model           : NavigationModel)
            (act             : Navigation.Action) =
        Navigation.update viewConfig refSystem navConf switchToArcball pick model act ctrlFlag

    /// A navigation model with the camera at a known pose.
    let modelLookingAt (location : V3d) (target : V3d) =
        let view = CameraView.lookAt location target V3d.OOI
        { NavigationModel.initial with
            camera = { NavigationModel.initial.camera with view = view } }

let tests =
    testList "Section 2 — Viewer Actions and Navigation" [

        // TC-2.1 FreeFly Navigation

        test "TC-2.1 the initial navigation mode is FreeFly" {
            Expect.equal NavigationModel.initial.navigationMode NavigationMode.FreeFly
                "a fresh viewer should start in FreeFly mode"
        }

        test "TC-2.1 a FreeFly action applies the configured navigation sensitivity" {
            let model = NavigationModel.initial
            let nav, _ =
                Nav.run false None false model
                    (Navigation.Action.FreeFlyAction FreeFlyController.Message.Rendered)
            // Navigation.update copies the nav-sensitivity config into the FreeFly
            // controller's move sensitivity (Navigation.fs).
            Expect.floatClose Accuracy.high
                nav.camera.freeFlyConfig.moveSensitivity Nav.viewConfig.navigationSensitivity.value
                "FreeFly move sensitivity should come from the navigation-sensitivity config"
        }

        test "TC-2.1 a FreeFly look-drag changes the camera orientation" {
            let model  = Nav.modelLookingAt V3d.Zero V3d.IOO
            let before = model.camera.view.Forward
            let step m msg = fst (Nav.run false None false m (Navigation.Action.FreeFlyAction msg))
            // press LMB and drag: the mouse-look gesture from the protocol. The
            // controller eases the rotation toward the drag target over render
            // ticks (lookAtDamping), so we pump Rendered ticks on a real clock.
            let dragging =
                model
                |> fun m -> step m (FreeFlyController.Message.Down (MouseButtons.Left, V2i(100, 100)))
                |> fun m -> step m (FreeFlyController.Message.Move (V2i(200, 100)))
            let clock = System.Diagnostics.Stopwatch.StartNew()
            let mutable current = dragging
            while Vec.distance current.camera.view.Forward before < 1e-4 && clock.Elapsed.TotalSeconds < 5.0 do
                current <- step current FreeFlyController.Message.Rendered
                Thread.Sleep 10
            let settled = step current (FreeFlyController.Message.Up MouseButtons.Left)
            Expect.isGreaterThan (Vec.distance settled.camera.view.Forward before) 1e-4
                "dragging with the left mouse button should rotate the camera"
        }

        // TC-2.2 ArcBall Navigation

        test "TC-2.2 switching to ArcBall with a valid pick sets the explore center" {
            let picked = V3d(3.0, 4.0, 5.0)
            let nav, _ =
                Nav.run true (Some (fun () -> Some picked)) false NavigationModel.initial
                    (Navigation.Action.SetNavigationMode NavigationMode.ArcBall)
            Expect.equal nav.navigationMode NavigationMode.ArcBall "mode should switch to ArcBall"
            Expect.equal nav.exploreCenter picked "explore center should be the picked point"
        }

        test "TC-2.2 switching to ArcBall with no pickable surface falls back to FreeFly" {
            // reproduces the viewer's guidance when the centre ray misses the surface
            let nav, feedback =
                Nav.run true (Some (fun () -> None)) false NavigationModel.initial
                    (Navigation.Action.SetNavigationMode NavigationMode.ArcBall)
            Expect.equal nav.navigationMode NavigationMode.FreeFly
                "without a pick, ArcBall cannot be entered and mode stays FreeFly"
            Expect.isSome feedback "the user should be told to centre the view first"
        }

        test "TC-2.2 entering ArcBall wires the orbit center onto the camera" {
            let centre = V3d(10.0, 0.0, 0.0)
            let start  = Nav.modelLookingAt (V3d(10.0, 0.0, 20.0)) centre
            let inArcBall, _ =
                Nav.run true None false start
                    (Navigation.Action.ArcBallAction (ArcBallController.Message.Pick centre))
            // Navigation.update binds the camera's orbitCenter to the explore
            // center, which is what makes subsequent drags orbit around it.
            Expect.equal inArcBall.camera.orbitCenter (Some centre)
                "the camera orbit center should be bound to the explore center"
        }

        test "TC-2.2 an ArcBall drag leaves the explore center fixed" {
            // The orbit integration itself lives in Aardvark's ArcBallController and
            // is driven by the render loop; what PRo3D guarantees at the model layer
            // is that a drag never moves the explore center the camera orbits.
            let centre = V3d(10.0, 0.0, 0.0)
            let start  = Nav.modelLookingAt (V3d(10.0, 0.0, 20.0)) centre
            let step m msg = fst (Nav.run false None false m (Navigation.Action.ArcBallAction msg))
            let inArcBall, _ =
                Nav.run true None false start
                    (Navigation.Action.ArcBallAction (ArcBallController.Message.Pick centre))
            let orbited =
                [ ArcBallController.Message.Down (MouseButtons.Left, V2i(100, 100))
                  ArcBallController.Message.Move (V2i(220, 140))
                  ArcBallController.Message.Up   MouseButtons.Left ]
                |> List.fold step inArcBall
            Expect.equal orbited.exploreCenter centre "the explore center must not move while orbiting"
        }

        // TC-2.3 Pick Explore Center

        test "TC-2.3 picking an explore center sets it and enters ArcBall" {
            // this is exactly what the PickExploreCenter interaction does (Viewer.fs)
            let picked = V3d(1.0, 2.0, 3.0)
            let nav, _ =
                Nav.run true None false NavigationModel.initial
                    (Navigation.Action.ArcBallAction (ArcBallController.Message.Pick picked))
            Expect.equal nav.exploreCenter picked "the pink-dot explore center should be the clicked point"
            Expect.equal nav.navigationMode NavigationMode.ArcBall
                "picking an explore center switches navigation to ArcBall"
        }

        test "TC-2.3 an explore-center pick is ignored when ArcBall switching is disabled" {
            // switchToArcball = false is the gate the viewer uses to keep FreeFly
            let nav, _ =
                Nav.run false None false NavigationModel.initial
                    (Navigation.Action.ArcBallAction (ArcBallController.Message.Pick (V3d(1.0, 2.0, 3.0))))
            Expect.equal nav.navigationMode NavigationMode.FreeFly
                "with switching disabled the mode should stay FreeFly"
        }

        // TC-2.4 Place Coordinate System

        test "TC-2.4 placing the coordinate system moves its origin to the clicked point" {
            let p = V3d(123.0, -45.0, 67.0)
            let model = { ReferenceSystem.initial with planet = Planet.ENU }
            let refSystem', _ =
                ReferenceSystemApp.update
                    Nav.viewConfig LenseConfigs.referenceSystemConfig model
                    (ReferenceSystemAction.UpdateUpNorth p)
            Expect.equal refSystem'.origin p "the coordinate system origin should be the clicked point"
        }

        test "TC-2.4 an ENU coordinate system has up = +Z and north = +Y" {
            let model = { ReferenceSystem.initial with planet = Planet.ENU }
            let refSystem', _ =
                ReferenceSystemApp.update
                    Nav.viewConfig LenseConfigs.referenceSystemConfig model
                    (ReferenceSystemAction.UpdateUpNorth (V3d(1.0, 2.0, 3.0)))
            // matches the protocol: blue up-arrow along +Z, red north-arrow along +Y
            Expect.equal refSystem'.up.value V3d.OOI "up vector should point along +Z"
            Expect.equal refSystem'.north.value V3d.OIO "north vector should point along +Y"
        }

        test "TC-2.4 on Mars the up and north vectors are orthonormal" {
            // the default planet; upVector goes through CooTransformation, which the
            // suite initialises before these tests run (GeoJsonRework.Tests).
            let p = V3d(693177.21, -3147511.67, 1070879.15)   // a point near the Mars surface
            let refSystem', _ =
                ReferenceSystemApp.update
                    Nav.viewConfig LenseConfigs.referenceSystemConfig ReferenceSystem.initial
                    (ReferenceSystemAction.UpdateUpNorth p)
            Expect.equal refSystem'.origin p "origin should be the clicked point"
            Expect.floatClose Accuracy.medium refSystem'.up.value.Length 1.0 "up should be a unit vector"
            Expect.floatClose Accuracy.medium refSystem'.north.value.Length 1.0 "north should be a unit vector"
            Expect.floatClose Accuracy.low
                (Vec.dot refSystem'.up.value refSystem'.north.value) 0.0
                "up and north should be perpendicular"
        }
    ]
