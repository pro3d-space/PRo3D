# Keyboard shortcuts

## Ctrl shortcuts work everywhere

| Keys | Action |
|---|---|
| **Ctrl+Z** | undo the last drawing action |
| **Ctrl+Y** or **Ctrl+Shift+Z** | redo it |
| **Ctrl+S** | save the scene (only once it has a path; otherwise use *Save* in the menu) |

On macOS, Cmd works in place of Ctrl.

These keys work in whichever part of the window has keyboard focus: the 3D view, the
instrument view, any dock panel (annotation properties, annotation tree, surfaces, config, …)
or the top menu. A panel gets focus when you click into it; the 3D view takes it as soon as
the cursor enters it. The one exception is a focused **text or number field**. There, Ctrl+Z / Ctrl+Y undo and redo your
typing as usual, and Ctrl+S does nothing (not even the browser's "save page"). Click outside the
field (or press Tab to leave it) to undo drawing actions again.

Ctrl+Shift+Z is redo as on macOS (Cmd+Shift+Z) and in many Windows applications; Ctrl+Shift+S
saves like Ctrl+S, there is no "save as" shortcut. Holding a key down does not repeat the
shortcut. Ctrl+Alt+Z and the like are ignored, and so is
Alt+Z on its own: earlier versions treated Alt like Ctrl in the 3D view, so Alt+Z used to undo.
Alt+click still picks like Ctrl+click.

## Delete: only where annotations are

**Delete** deletes the selected annotations, in one undo step
([AnnotationDeletion.md](AnnotationDeletion.md)). Unlike the Ctrl shortcuts it is taken only by
the pages about annotations: the 3D view, the **Annotations** tree and the annotation
**Properties** panel. In any other panel (Surfaces, say, where Delete would mean the selected
surface) it does nothing, and in a text field it deletes text. Modifiers turn it off: Shift+Delete,
Ctrl+Delete and the like are ignored. On a Mac keyboard the key labelled *delete* is Backspace;
Delete is fn+delete there.

## Keys handled only by the 3D view

Everything else (F1–F4 interaction modes, Enter / Backspace / Escape while drawing, 0–3
semantics, C, PageUp / PageDown, MapView WASD, …) is still handled by the render control.
These keys only work while the 3D view has keyboard focus, which it takes as soon as the cursor
enters it. Ctrl+click picking also depends on that focus, because the 3D view tracks whether
Ctrl is held. When the view loses focus, for example because you click a dock panel, it
forgets any Ctrl or Shift it saw go down. A modifier released elsewhere therefore cannot stay
"stuck" and leave picking armed.

## Implementation

| What | Where |
|---|---|
| `pro3dCtrlShortcut(event)`: the client-side filter (Ctrl/Cmd, no Alt, not repeated, key Z/Y/S, not in a text-entry field) and `preventDefault` (Ctrl+S also inside text fields) | [utilities.js](../src/PRo3D.Viewer/resources/utilities.js) |
| `Events.onCtrlShortcut`: the body `onkeydown` that asks it and sends the key code and Shift |
| `pro3dDeleteKey(event)`: Delete, no modifier, not repeated, not in a text-entry field | [utilities.js](../src/PRo3D.Viewer/resources/utilities.js) |
| `Events.onCtrlShortcutOrDelete`: the same handler plus Delete, on the render, annotations and properties pages; `Gui.annotationShortcut` turns Delete into `DeleteSelectedAnnotations` | [Utilities.fs](../src/PRo3D.Viewer/Utilities.fs), [ViewerGUI.fs](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs) | [Utilities.fs](../src/PRo3D.Viewer/Utilities.fs) |
| `ViewerAction.CtrlShortcut` | [Viewer-Model.fs](../src/PRo3D.Viewer/Viewer-Model.fs) |
| Update case: undo / redo / save | [Viewer.fs](../src/PRo3D.Viewer/Viewer/Viewer.fs) |
| The handler on every page body | `Gui.Pages.pageRouting` in [ViewerGUI.fs](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs) |
| `releaseModifiersOnBlur` on both render controls | [Viewer.fs](../src/PRo3D.Viewer/Viewer/Viewer.fs) |

Every dock panel is a separate document (`?page=annotations`, `?page=properties`, …). Key
events do not cross document boundaries, so the handler sits on each page's `<body>`, where
key events from anywhere in that page bubble up. The render control's own `onkeydown` bubbles
to the render page's body too. That is why the `KeyDown` update no longer handles Z/Y/S:
handling them in both places would undo twice.

The shortcut reads `event.ctrlKey` / `event.metaKey` from the key event instead of the model's
`ctrlFlag`. `ctrlFlag` is only maintained by key events that reach the render control, and it
stays what decides whether Ctrl+click picks.

Before this change, the Ctrl shortcuts lived in the `KeyDown` update and checked `ctrlFlag`.
They did nothing over a dock panel, did nothing if Ctrl was pressed before the cursor entered
the 3D view, and fired on a plain Z/S if Ctrl had been released outside the view.
