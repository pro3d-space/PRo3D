/// Section 7 — Bookmarks
///   TC-7.1 (Add Bookmark), TC-7.2 (Bookmark FlyTo)
///
///   Exercises the real Bookmarks.update. AddBookmark captures the current camera;
///   flying to a bookmark (GroupsAppAction.UpdateCam) restores the saved camera into
///   the navigation model — the animation that eases there is the render loop's job.
///   A NavigationModel stands in for the outer model, wired through an identity lens
///   exactly as the viewer wires _navigation.
module PRo3D.Tests.Section07_Bookmarks

open Aardvark.Base
open Aardvark.Rendering                  // CameraView

open FSharp.Data.Adaptive

open Aether

open Expecto

open PRo3D.Base
open PRo3D.Core
open PRo3D.Navigation2                    // NavigationModel
open PRo3D.Viewer                         // BookmarkAction
open PRo3D.Bookmarkings                   // Bookmarks
open PRo3D.Tests

module private BM =
    /// The outer model is a NavigationModel, addressed through the identity lens.
    let navLens : Lens<NavigationModel, NavigationModel> = (id, (fun v _ -> v))

    let navAt (loc : V3d) (mode : NavigationMode) (explore : V3d) =
        { NavigationModel.initial with
            camera         = { NavigationModel.initial.camera with view = CameraView.lookAt loc V3d.Zero V3d.OOI }
            navigationMode = mode
            exploreCenter  = explore }

let tests =
    testList "Section 7 — Bookmarks" [

        // TC-7.1 Add Bookmark

        test "TC-7.1 AddBookmark captures the current camera as a bookmark" {
            let nav = BM.navAt (V3d(5.0, 6.0, 7.0)) NavigationMode.ArcBall (V3d(1.0, 1.0, 1.0))
            let _, bms = Bookmarks.update GroupsModel.initial Planet.None BookmarkAction.AddBookmark BM.navLens nav
            Expect.equal (bms.flat |> HashMap.count) 1 "one bookmark should be added"
            match bms.flat |> HashMap.toList |> List.head |> snd with
            | Leaf.Bookmarks b ->
                Expect.equal b.cameraView.Location nav.camera.view.Location
                    "the bookmark should capture the camera location"
                Expect.equal b.navigationMode NavigationMode.ArcBall
                    "the bookmark should capture the navigation mode"
                Expect.equal b.exploreCenter (V3d(1.0, 1.0, 1.0))
                    "the bookmark should capture the explore center"
            | _ -> failtest "expected a bookmark leaf"
        }

        test "TC-7.1 a second bookmark grows the list to two" {
            let nav = BM.navAt (V3d(5.0, 6.0, 7.0)) NavigationMode.FreeFly V3d.Zero
            let _, one = Bookmarks.update GroupsModel.initial Planet.None BookmarkAction.AddBookmark BM.navLens nav
            let _, two = Bookmarks.update one Planet.None BookmarkAction.AddBookmark BM.navLens nav
            Expect.equal (two.flat |> HashMap.count) 2 "a second bookmark should be added"
        }

        // TC-7.2 Bookmark FlyTo

        test "TC-7.2 UpdateCam restores the camera saved in a bookmark" {
            let navA = BM.navAt (V3d(5.0, 6.0, 7.0)) NavigationMode.ArcBall (V3d(1.0, 1.0, 1.0))
            let _, bms = Bookmarks.update GroupsModel.initial Planet.None BookmarkAction.AddBookmark BM.navLens navA
            let bmId = bms.flat |> HashMap.toList |> List.head |> fst

            // the camera has since moved elsewhere; "fly to" the bookmark
            let navB = BM.navAt (V3d(-9.0, -9.0, -9.0)) NavigationMode.FreeFly V3d.Zero
            let restored, _ =
                Bookmarks.update bms Planet.None
                    (BookmarkAction.GroupsMessage (GroupsAppAction.UpdateCam bmId)) BM.navLens navB

            Expect.equal restored.camera.view.Location navA.camera.view.Location
                "camera location should return to the bookmark"
            Expect.equal restored.navigationMode NavigationMode.ArcBall
                "navigation mode should return to the bookmark's"
            Expect.equal restored.exploreCenter (V3d(1.0, 1.0, 1.0))
                "explore center should return to the bookmark's"
        }
    ]
