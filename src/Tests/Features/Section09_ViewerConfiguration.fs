/// Section 9 — Viewer Configuration
///   TC-9.1 (Viewer Config Settings)
///   TC-9.2 (Screenshots): which render-server scene a screenshot captures - the
///   main view's, reported by its render control on boot (docs/Screenshots.md). The
///   capture itself renders and writes a file: tests-ui/tests/screenshot.spec.ts.
///
///   Config edits go through the real ConfigProperties.update.
module PRo3D.Tests.Section09_ViewerConfiguration

open Aardvark.Base
open Aardvark.UI.Primitives              // Numeric

open System.Collections.Concurrent
open System.Threading

open Aardvark.Rendering                  // IRuntime, IFramebufferSignature

open Expecto

open PRo3D                                // ConfigProperties
open PRo3D.Core                           // ViewConfigModel
open PRo3D.Base                           // ScreenshotUtilities
open PRo3D.Viewer
open PRo3D.Tests

module private Screenshots =

    let live (names : list<string>) : list<ScreenshotUtilities.Utilities.ClientStatistics> =
        names |> List.map (fun n ->
            { session = System.Guid.Empty; name = n; frameCount = 1
              invalidateTime = 0.0; renderTime = 0.0; compressTime = 0.0; frameTime = 0.0 })

    /// A headless viewer and its real update, as in Section 18: the actions here touch no renderer.
    let viewer () =
        let cts       = new CancellationTokenSource()
        let mailbox   = MailboxProcessor.Start(Viewer.initMessageLoop cts, cts.Token)
        let sendQueue = new BlockingCollection<string>()
        let model =
            Viewer.initial mailbox StartupArgs.initArgs "" 1 "." ViewerLenses._animator "tests"
        let update (m : Model) (msg : ViewerAction) =
            ViewerApp.updateViewer
                (Unchecked.defaultof<IRuntime>) (Unchecked.defaultof<IFramebufferSignature>)
                sendQueue mailbox m msg
        model, update

let tests =
    testList "Section 9 — Viewer Configuration" [

        // TC-9.1 Viewer Config Settings

        test "TC-9.1 SetNearPlane updates the near plane" {
            let m = ConfigProperties.update ViewConfigModel.initial
                        (ConfigProperties.Action.SetNearPlane (Numeric.SetValue 0.5))
            Expect.floatClose Accuracy.high m.nearPlane.value 0.5 "near plane should be 0.5"
        }

        test "TC-9.1 SetFarPlane updates the far plane" {
            let m = ConfigProperties.update ViewConfigModel.initial
                        (ConfigProperties.Action.SetFarPlane (Numeric.SetValue 5000.0))
            Expect.floatClose Accuracy.high m.farPlane.value 5000.0 "far plane should be 5000"
        }

        test "TC-9.1 SetNavigationSensitivity updates the sensitivity" {
            let m = ConfigProperties.update ViewConfigModel.initial
                        (ConfigProperties.Action.SetNavigationSensitivity (Numeric.SetValue 3.0))
            Expect.floatClose Accuracy.high m.navigationSensitivity.value 3.0 "sensitivity should be 3"
        }

        test "TC-9.1 ToggleLodColors flips the LoD colouring flag" {
            let m = ConfigProperties.update ViewConfigModel.initial ConfigProperties.Action.ToggleLodColors
            Expect.equal m.lodColoring (not ViewConfigModel.initial.lodColoring) "LoD colouring should toggle"
        }

        test "TC-9.1 ToggleOrientationCube flips the orientation-cube flag" {
            let m = ConfigProperties.update ViewConfigModel.initial ConfigProperties.Action.ToggleOrientationCube
            Expect.equal m.drawOrientationCube (not ViewConfigModel.initial.drawOrientationCube)
                "orientation cube should toggle"
        }

        test "TC-9.1 config edits are independent" {
            // setting the near plane leaves the far plane untouched
            let m = ConfigProperties.update ViewConfigModel.initial
                        (ConfigProperties.Action.SetNearPlane (Numeric.SetValue 0.25))
            Expect.floatClose Accuracy.high m.farPlane.value ViewConfigModel.initial.farPlane.value
                "far plane should be unchanged when only the near plane is set"
        }

        // TC-9.2 Screenshots - which scene the render server is asked for

        test "TC-9.2 the newest main view the server renders is captured" {
            let chosen =
                ScreenshotUtilities.Utilities.tryChooseScene [ "n813"; "n5" ] (Screenshots.live [ "n5"; "n674"; "n813" ])
            Expect.equal chosen (Some "n813") "the newest main view, not the first client the server lists"
        }

        test "TC-9.2 a main view that is gone is skipped" {
            // n900: a page that was closed or reloaded
            let chosen =
                ScreenshotUtilities.Utilities.tryChooseScene [ "n900"; "n5" ] (Screenshots.live [ "n674"; "n5" ])
            Expect.equal chosen (Some "n5") "the newest main view still rendered"
        }

        test "TC-9.2 other views are never captured in place of the main view" {
            // n674: say the instrument view - live, but no main view
            let chosen = ScreenshotUtilities.Utilities.tryChooseScene [ "n900" ] (Screenshots.live [ "n674" ])
            Expect.equal chosen None "no main view live: no screenshot rather than another view"
            Expect.equal (ScreenshotUtilities.Utilities.tryChooseScene [] (Screenshots.live [ "n674" ])) None
                "before the main view has booted"
        }

        test "TC-9.2 booted main views are kept newest first, each once" {
            let model, update = Screenshots.viewer ()
            let m =
                [ "n5"; "n674"; "n5" ]
                |> List.fold (fun m s -> update m (ViewerAction.MainViewBooted s)) model
            Expect.equal m.mainViewScenes [ "n5"; "n674" ] "a control booting again moves to the front"
        }

        test "TC-9.2 the list of booted main views stays short" {
            let model, update = Screenshots.viewer ()
            let m =
                [ 1 .. 40 ]
                |> List.fold (fun m i -> update m (ViewerAction.MainViewBooted (sprintf "n%d" i))) model
            Expect.equal m.mainViewScenes.Length 16 "page reloads must not grow it without bound"
            Expect.equal (List.tryHead m.mainViewScenes) (Some "n40") "the newest first"
        }

        test "TC-9.2 a new scene keeps the main views on screen" {
            let model, update = Screenshots.viewer ()
            let m = update model (ViewerAction.MainViewBooted "n5")
            let m = update m ViewerAction.NewScene
            Expect.equal m.mainViewScenes [ "n5" ] "the render control does not boot again for a new scene"
        }
    ]
