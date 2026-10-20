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
open PRo3D.Core                          // Interactions, GroupsModel
open PRo3D.Core.Drawing                  // DrawingApp, AnnotationsDelta
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

        // Ctrl+Z/Y arrive as CtrlShortcut from every page body (docs/KeyboardShortcuts.md)

        test "TC-18.1 CtrlShortcut Z undoes and Y redoes, with no ctrlFlag" {
            let model, update = Head.make ()
            let delta = AnnotationsDelta.SnapshotDelta(GroupsModel.initial, GroupsModel.initial)
            let m = { model with drawing = DrawingApp.pushUndo delta model.drawing }
            Expect.isFalse m.ctrlFlag "the shortcut must not depend on a tracked Ctrl-down"
            let undone = update m (ViewerAction.CtrlShortcut Keys.Z)
            Expect.isEmpty undone.drawing.undoStack "Ctrl+Z pops the undo stack"
            Expect.equal (List.length undone.drawing.redoStack) 1 "... onto the redo stack"
            let redone = update undone (ViewerAction.CtrlShortcut Keys.Y)
            Expect.equal (List.length redone.drawing.undoStack) 1 "Ctrl+Y pushes it back"
            Expect.isEmpty redone.drawing.redoStack "... and empties the redo stack"
        }

        test "TC-18.1 the render control's KeyDown no longer undoes (would undo twice)" {
            // the render control's keydown also bubbles to its page body, which sends CtrlShortcut
            let model, update = Head.make ()
            let delta = AnnotationsDelta.SnapshotDelta(GroupsModel.initial, GroupsModel.initial)
            let m = { model with drawing = DrawingApp.pushUndo delta model.drawing }
            let m = [ Keys.LeftCtrl; Keys.Z ] |> List.fold (fun m k -> update m (ViewerAction.KeyDown k)) m
            Expect.isTrue m.ctrlFlag "Ctrl is still tracked for picking"
            Expect.equal (List.length m.drawing.undoStack) 1 "KeyDown Z leaves the undo stack alone"
        }
    ]
