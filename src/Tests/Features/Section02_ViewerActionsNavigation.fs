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
open MapViewCameraController             // MapViewController

module private Nav =

    /// The real config the viewer passes to Navigation.update.
    let viewConfig = ViewConfigModel.initial
    let refSystem  = ReferenceSystem.initial
    let navConf    = ViewerApp.navConf

    /// Defaults rather than UserPreferences.load(): these only affect MapView WASD
    /// axis inversion, and the tests must not depend on the developer's config file.
    let userPrefs  = UserPreferences.initial

    /// Navigation.update against a specific reference system (i.e. a specific
    /// planet), matching how the NavigationMessage handler calls it (Viewer.fs).
    let runOn (refSystem       : ReferenceSystem)
              (switchToArcball : bool)
              (pick            : option<unit -> option<V3d>>)
              (ctrlFlag        : bool)
              (model           : NavigationModel)
              (act             : Navigation.Action) =
        Navigation.update viewConfig refSystem navConf userPrefs switchToArcball pick model act ctrlFlag

    /// Navigation.update with the viewer's own config, matching how the
    /// NavigationMessage handler calls it (Viewer.fs).
    let run (switchToArcball : bool)
            (pick            : option<unit -> option<V3d>>)
            (ctrlFlag        : bool)
            (model           : NavigationModel)
            (act             : Navigation.Action) =
        runOn refSystem switchToArcball pick ctrlFlag model act

    /// A navigation model with the camera at a known pose.
    let modelLookingAt (location : V3d) (target : V3d) =
        let view = CameraView.lookAt location target V3d.OOI
        { NavigationModel.initial with
            camera = { NavigationModel.initial.camera with view = view } }

    /// A point on the Mars surface (the same one TC-2.4 uses) and the body
    /// radius beneath it.
    let marsSurface = V3d(693177.21, -3147511.67, 1070879.15)
    let marsRadius  = marsSurface.Length

    /// The camera 500 km above that point, looking horizontally — the pose a
    /// user is typically in when they switch to MapView.
    ///
    /// The altitude is deliberately large. MapView's body radius used to be
    /// estimated as |cameraLocation| — i.e. radius + altitude — so a test flying
    /// low cannot tell the correct value from the broken one.
    let marsAltitude = 500000.0

    let aboveMars =
        let location = marsSurface * (1.0 + marsAltitude / marsRadius)
        modelLookingAt location (location + V3d.IOO)

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

        // TC-2.5 MapView Navigation
        //
        // MapView pins the camera to "look at the body centre, north up" and
        // scales every pan/zoom step by the camera's height above the surface.
        // Both of those had failure modes that only showed up on some paths
        // into the mode, which is what these lock down.

        test "TC-2.5 entering MapView looks at the body centre with north up" {
            let nav, _ =
                Nav.run false None false Nav.aboveMars
                    (Navigation.Action.SetNavigationMode NavigationMode.MapView)
            let view = nav.camera.view
            let radial = Vec.normalize view.Location
            Expect.floatClose Accuracy.medium (Vec.dot view.Forward radial) -1.0
                "the camera should look straight down at the body centre"
            // screen-up is a compass direction: perpendicular to the local up...
            let up = CooTransformation.getUpVector view.Location Planet.Mars |> Vec.normalize
            Expect.floatClose Accuracy.low (Vec.dot view.Up up) 0.0
                "screen-up should be horizontal, i.e. a compass direction"
            // ...and it is north, not south: the test point is in the northern
            // hemisphere, where north tilts toward the +Z pole.
            Expect.isGreaterThan (Vec.dot view.Up V3d.OOI) 0.0
                "screen-up should point north, not south"
        }

        test "TC-2.5 MapView scales navigation by the body radius, on every entry" {
            // Regression: the radius used to be estimated from exploreCenter, and
            // fell back to |cameraLocation| when that was zero — which is the
            // default, so the *first* switch into MapView got radius + altitude.
            // Switching to ArcBall and back repaired it, because ArcBall's
            // pickOrbitCenter writes a real surface point into exploreCenter.
            // The tolerance must stay well below Nav.marsAltitude for this to
            // distinguish the correct radius from the old fallback.
            let enter m = fst (Nav.run false None false m (Navigation.Action.SetNavigationMode NavigationMode.MapView))
            let first  = enter Nav.aboveMars
            let second = enter (fst (Nav.run false None false first (Navigation.Action.SetNavigationMode NavigationMode.FreeFly)))
            for nav in [ first; second ] do
                Expect.isTrue (abs (nav.camera.rotationFactor - Nav.marsRadius) < 5000.0)
                    "MapView should scale by the Mars radius, not by camera state"
            Expect.floatClose Accuracy.medium
                second.camera.rotationFactor first.camera.rotationFactor
                "re-entering MapView should give the same body scale"
        }

        test "TC-2.5 the very first switch into MapView leaves it navigable" {
            // The reported symptom, asserted directly: MapView was frozen the
            // first time it was entered and only came alive after a detour
            // through ArcBall. The old radius estimate made height above the
            // surface exactly zero, which collapsed the drag angle to zero.
            Expect.equal Nav.aboveMars.exploreCenter V3d.Zero
                "precondition: a fresh FreeFly model has no explore center"
            let inMapView, _ =
                Nav.run false None false Nav.aboveMars
                    (Navigation.Action.SetNavigationMode NavigationMode.MapView)
            let before = inMapView.camera.view.Location
            let step m msg = fst (Nav.run false None false m (Navigation.Action.MapViewControllerAction msg))
            let dragged =
                [ MapViewController.Message.Down (MouseButtons.Left, V2i(100, 100))
                  MapViewController.Message.Move (V2i(101, 100)) ]
                |> List.fold step inMapView
            Expect.isGreaterThan (Vec.distance dragged.camera.view.Location before) 1000.0
                "a drag on the first MapView entry should move the camera over the body"
        }

        test "TC-2.5 a MapView action repairs the body scale on a restored camera" {
            // Bookmark restore and scene load enter MapView without ever running
            // SetNavigationMode, so they arrive with the FreeFly default (0.01)
            // as the "body radius" — which made pan and zoom wildly oversensitive.
            let restored =
                { Nav.aboveMars with
                    navigationMode = NavigationMode.MapView
                    updatePerFrame = true }
            Expect.isLessThan restored.camera.rotationFactor 1.0
                "precondition: a restored camera carries the FreeFly default"
            let nav, _ =
                Nav.run false None false restored
                    (Navigation.Action.MapViewControllerAction MapViewController.Message.StepTime)
            Expect.isTrue (abs (nav.camera.rotationFactor - Nav.marsRadius) < 50000.0)
                "the first MapView action should recompute the body radius"
        }

        test "TC-2.5 entering MapView leaves the ArcBall explore center alone" {
            // MapView orbits the frame origin regardless, so zeroing exploreCenter
            // only destroyed ArcBall's state (and the pink dot) for no gain.
            let centre = V3d(10.0, 0.0, 0.0)
            let start  = { Nav.aboveMars with exploreCenter = centre }
            let nav, _ =
                Nav.run false None false start
                    (Navigation.Action.SetNavigationMode NavigationMode.MapView)
            Expect.equal nav.exploreCenter centre "the explore center must survive a MapView switch"
            Expect.equal nav.camera.orbitCenter (Some V3d.OOO) "MapView orbits the frame origin"
        }

        test "TC-2.5 the ENU map frame agrees with the ENU reference system" {
            // Deriving east/north from the generic "east = pole x up" rule
            // degenerates when up is a fixed axis, and used to land ENU's north
            // on its east axis — a map view rotated by 90 degrees.
            let frame = MapViewController.mapFrame Planet.ENU (V3d(1.0, 2.0, 3.0))
            Expect.equal frame.up V3d.OOI "ENU up should point along +Z"
            Expect.equal frame.east V3d.IOO "ENU east should point along +X"
            Expect.equal frame.north V3d.OIO "ENU north should point along +Y (see TC-2.4)"
        }

        test "TC-2.5 the Mars map frame is a right-handed east/north/up triple" {
            let frame = MapViewController.mapFrame Planet.Mars Nav.marsSurface
            Expect.floatClose Accuracy.low (Vec.dot frame.up frame.north) 0.0 "up and north should be perpendicular"
            Expect.floatClose Accuracy.low (Vec.dot frame.up frame.east) 0.0 "up and east should be perpendicular"
            Expect.floatClose Accuracy.low (Vec.dot frame.north frame.east) 0.0 "north and east should be perpendicular"
            Expect.floatClose Accuracy.low (Vec.distance (Vec.cross frame.east frame.north) frame.up) 0.0
                "east x north should give up"
            Expect.isSome frame.polarAxis "a planetary frame has a pole to guard against"
        }
    ]
