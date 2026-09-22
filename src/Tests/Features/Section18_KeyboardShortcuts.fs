/// Section 18 — Keyboard Shortcuts
///   TC-18.1 (Keyboard Shortcuts)
///
///   Drives the real ViewerApp.updateViewer with KeyDown actions and checks the
///   interaction the shortcut selects. The KeyDown -> interaction mapping needs no
///   GL runtime (it only mutates model state), so a headless model is built with
///   Viewer.initial and updateViewer is called with a placeholder runtime, exactly
///   as PRo3D's RemoteApi does for non-rendering actions.
module PRo3D.Tests.Section18_KeyboardShortcuts

open System.Collections.Concurrent
open System.Threading

open Aardvark.Application                // Keys
open Aardvark.Rendering                  // IRuntime, IFramebufferSignature

open Expecto

open PRo3D
open PRo3D.Core                          // Interactions
open PRo3D.Viewer
open PRo3D.Tests

module private Head =

    /// A headless viewer model plus an update bound to a placeholder runtime. Safe
    /// for actions (KeyDown shortcuts, SetInteraction) that do not touch the renderer.
    let make () =
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

    /// Fire a sequence of key presses.
    let press (keys : Keys list) =
        let model, update = make ()
        keys |> List.fold (fun m k -> update m (ViewerAction.KeyDown k)) model

let tests =
    testList "Section 18 — Keyboard Shortcuts" [

        // TC-18.1 Keyboard Shortcuts — the F-key action shortcuts

        test "TC-18.1 F1 selects the PickExploreCenter interaction" {
            let m = Head.press [ Keys.F1 ]
            Expect.equal m.interaction Interactions.PickExploreCenter "F1 should select PickExploreCenter"
        }

        test "TC-18.1 F2 selects the DrawAnnotation interaction" {
            // prime with F1 first so the change to DrawAnnotation is observable
            let m = Head.press [ Keys.F1; Keys.F2 ]
            Expect.equal m.interaction Interactions.DrawAnnotation "F2 should select DrawAnnotation"
        }

        test "TC-18.1 F3 selects the PickAnnotation interaction" {
            let m = Head.press [ Keys.F3 ]
            Expect.equal m.interaction Interactions.PickAnnotation "F3 should select PickAnnotation"
        }

        test "TC-18.1 F4 selects the PlaceCoordinateSystem interaction" {
            let m = Head.press [ Keys.F4 ]
            Expect.equal m.interaction Interactions.PlaceCoordinateSystem "F4 should select PlaceCoordinateSystem"
        }

        test "TC-18.1 the shortcuts switch between interactions" {
            // F2 then F3 lands on PickAnnotation, proving the last shortcut wins
            let m = Head.press [ Keys.F2; Keys.F3 ]
            Expect.equal m.interaction Interactions.PickAnnotation "the most recent shortcut should win"
        }

        test "TC-18.1 SetInteraction (actions menu) shares the interaction state" {
            let model, update = Head.make ()
            let m = update model (ViewerAction.SetInteraction Interactions.PickSurface)
            Expect.equal m.interaction Interactions.PickSurface
                "selecting an action in the menu sets the same interaction the shortcuts do"
        }
    ]
