# Box selection of annotations

Drag a rectangle in the 3D view to select every annotation it touches. Before this, a
multi-selection had to be built one Shift+click at a time.

## Gestures

Box selection works in **Select Annotation** mode (F3), with the tool armed exactly as for a
click. In the classic mouse scheme that means **Ctrl held**; in
[Direct Tool Mode](DirectToolMode.md) no Ctrl is needed.

| Classic scheme | Direct Tool Mode | Effect |
|---|---|---|
| Ctrl+drag | drag | **replace** the selection with every annotation the rectangle touches |
| Ctrl+Shift+drag | Shift+drag | **add** the touched annotations |
| Ctrl+Alt+drag | Alt+drag | **toggle** each touched annotation: selected ones are deselected, the others selected |
| Ctrl+click on empty space | click on empty space | **clear** the selection |
| Ctrl+click on an annotation | click on an annotation | select it (unchanged) |
| Ctrl+Shift+click on an annotation | Shift+click on an annotation | toggle it in the multi-selection (unchanged) |
| Esc while dragging | Esc while dragging | cancel the rectangle, selection unchanged |

**Why not Ctrl for toggle?** Ctrl is already taken: in the classic scheme it arms the tool, and
in Direct Tool Mode it hands the left button back to the camera. Alt is the toggle key instead.

A release within 4 px of the press counts as a click. If both Alt and Shift are held, Alt wins.
Shift+click or Alt+click on empty space leaves the selection alone.

The camera is unaffected. In the classic scheme a held Ctrl already switches the camera off, and
in Direct Tool Mode the camera never uses the left button, so the rectangle takes no navigation
gesture away.

## What counts as touching

An annotation is selected when **any of it** touches the rectangle: a vertex inside it, or a
segment crossing it, even with both ends outside. Long polylines that run off-screen can
therefore be picked up by any visible part.

- Draped annotations are tested along their draped path, not the straight chord between their
  control points. Ellipses are tested along their sampled outline, which is what they store.
- Hidden annotations, either toggled invisible or in a hidden group, are never selected.
- Geometry behind the camera is clipped away; it cannot land on screen.
- **Occlusion is not considered.** An annotation hidden behind terrain is still selected if its
  projection touches the rectangle.
- A filled polygon is selected by its outline only. A rectangle drawn entirely inside the
  polygon, touching no edge, does not select it.

## Which selection it changes

The rectangle works on the multi-selection, shown as the coloured cube icon in the annotation
tree:

- **Replace** also drops the single (green-name) selection, so that a following
  [Delete](AnnotationDeletion.md) removes exactly what was boxed.
- **Add** leaves the green name as it is.
- **Toggle** treats the green annotation as selected, consistent with Delete: toggling it
  deselects it.

A box selection followed by **Delete** removes all boxed annotations in one undo step.
Selection changes themselves are not undoable, as with every other selection change.

## Implementation

| What | Where |
|---|---|
| Hit test: project in double on the CPU, clip against w > 0, vertex-in-rect and Liang–Barsky segment test | `DrawingApp.annotationsTouchingRect`, [Drawing-App.fs](../src/PRo3D.Core/Drawing/Drawing-App.fs) |
| Selection modes; rebuilds `TreeSelection`s with each leaf's real group path | `SelectionMode`, `DrawingAction.SelectAnnotations`, `DrawingApp.selectAnnotations` |
| Leaf → group path in one tree walk | `GroupsApp.leafPaths`, [GroupsApp.fs](../src/PRo3D.Core/GroupsApp.fs) |
| Pointer events (relative position, control size, button, Shift/Alt; pointer capture) | `BoxSelectEvents`, `renderControlAttributes`, [Viewer.fs](../src/PRo3D.Viewer/Viewer/Viewer.fs) |
| Gesture state and update (`BoxSelectStart/Move/End/Cancel`), pixel → NDC, click-vs-drag | `Model.multiSelectBox`, `ViewerApp.boxSelectionAction`, [Viewer.fs](../src/PRo3D.Viewer/Viewer/Viewer.fs) |
| The rectangle overlay (SVG over the render control) | `selectionRectangle`, [ViewerGUI.fs](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs) |

Notes:

- **Event traffic.** The move handler is attached only while a rectangle is being dragged, so an
  idle view sends no move traffic. Down, up and cancel stay attached: the handler set changes
  only after the server has processed the start of a drag, and a quick click would otherwise
  lose its release. The hit test runs once, on release.
- **Click on empty space.** On a click, the same hit test runs on a small square around the
  cursor, sized like the pick pass's widened lines (`3 + 5 × pickingTolerance` px). Only when it
  touches nothing is the selection cleared. A click on an annotation is still handled by the
  existing pick (`PickDirectly`).
- **Selection paths.** Viewport picks store the root path in their `TreeSelection`. The box
  selection stores the real group path, and drops any older entry for the same id, so nothing is
  ever selected twice.
- **Old body-level handlers.** `StartDragging` / `Dragging` / `EndDragging` used to clear
  `multiSelectBox`. They no longer touch it: the body's mousedown arrives right after the render
  control's pointerdown and would wipe the rectangle just started.

## Tests

`src/Tests/Features/Section22_BoxSelection.fs` covers:

- **TC-22.1, the hit test:**
  - a vertex inside the rectangle;
  - a segment crossing it with no vertex inside;
  - a polyline whose bend passes through the rectangle;
  - hidden annotations;
  - geometry behind the camera, and a segment partly behind it;
  - draped segments versus the straight chord;
  - every geometry.
- **TC-22.2, the selection modes:**
  - Replace;
  - Replace with nothing, as on a click on empty space;
  - Add, without duplicating a viewport pick;
  - Toggle, including the green annotation;
  - a sub-group path;
  - unknown ids.
- **TC-22.3:** box-select, Delete, then one Undo.

The manual steps are in the test protocol as TC-62 (Box Selection of Annotations).
