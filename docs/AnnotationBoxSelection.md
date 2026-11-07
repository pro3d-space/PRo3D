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
| Ctrl+click on an annotation | click on an annotation | select **only** it: a multi-selection is cleared |
| Ctrl+Shift+click on an annotation | Shift+click on an annotation | toggle it in the selection; the one selected by a plain click stays selected |
| Esc while dragging | Esc while dragging | cancel the rectangle, selection unchanged |

**Why not Ctrl for toggle?** Ctrl is already taken: in the classic scheme it arms the tool, and
in Direct Tool Mode it hands the left button back to the camera. Alt is the toggle key instead.

A release within 4 px of the press counts as a click. If both Alt and Shift are held, Alt wins.

On macOS, use **Cmd** where this page says Ctrl (Ctrl+click is a right-click there), and
**Option** for Alt: Cmd+drag replaces, Cmd+Option+drag toggles. See the modifier key in
[DirectToolMode.md](DirectToolMode.md#the-modifier-key).
A click on empty space leaves the selection alone.

The camera is unaffected. In the classic scheme a held Ctrl already switches the camera off, and
in Direct Tool Mode the camera never uses the left button, so the rectangle takes no navigation
gesture away.

In the classic scheme, releasing Ctrl before the mouse button **cancels** the rectangle,
selection unchanged: without Ctrl the left button drives the camera again, and the rectangle
would no longer match what is under it. The rectangle disappears on the key event itself
(`ViewerApp.dropBoxIfDisarmed`), not on the next mouse move. In Direct Tool Mode, pressing Ctrl
mid-drag cancels it the same way.

## What counts as touching

An annotation is selected when **any of it** touches the rectangle: a vertex inside it, or a
segment crossing it, even with both ends outside. Long polylines that run off-screen can
therefore be picked up by any visible part.

- Draped annotations are tested along their draped path, not the straight chord between their
  control points. Ellipses are tested along their sampled outline, which is what they store.
- Hidden annotations, either toggled invisible or in a hidden group, are never selected.
- Geometry behind the camera is clipped away; it cannot land on screen.
- **Annotations hidden behind the surface are not selected.** Only the part of an annotation
  inside the rectangle counts, and it must be visible: a box over the visible end of an
  annotation that runs behind a hill selects it, and a box over only its hidden end does not.
  See [Occlusion](#occlusion).
- A filled polygon is selected by its outline only. A rectangle drawn entirely inside the
  polygon, touching no edge, does not select it.

## Occlusion

On screen, annotations behind a surface are hidden by the GPU depth test: the surfaces are
drawn first, then the annotation lines are drawn against their depth. That depth buffer lives
inside the render control and is not available to the selection, so selection repeats the test
on the CPU with the surfaces' picking KdTrees.

- **Box.** Samples are taken along the part of each annotation inside the rectangle: its
  vertices there, plus a point about every 8 px along segments crossing it. Each sample is
  tested with a ray from the camera. The first visible sample selects the annotation. At most 32
  rays are spent per annotation (`DrawingApp.maxVisibilityRaysPerAnnotation`), spread evenly
  over its samples so a hidden first stretch cannot use them all up; an annotation with no
  visible sample among them counts as hidden.
- **Ctrl+click.** The click is tested at the annotation point under the cursor
  (`DrawingApp.annotationPointUnderCursor`), with the camera of that click. Clicking the visible
  part of a partly hidden annotation selects it; clicking a hidden part does nothing. This
  applies in every mode that picks annotations, including Edit Annotation re-selection.
  Grabbing a control-point handle is unaffected, since handles are drawn on top of everything.
- **Nearest surface.** The ray takes the nearest hit over all occluding surfaces, whatever
  their priority (`Picking.castToSurfaces` via `SurfaceIntersection.doKdTreeIntersectionNearest`).
  Surface picking (`doKdTreeIntersection`) prefers the highest priority instead, which would
  let a nearer low-priority surface fail to hide what is behind it. Both share one pass over
  the surfaces per ray, so a ray costs the same as a pick ray, whatever the surface count.

The comparison is the depth test itself (`DrawingApp.pointVisible`). The annotation's depth is
offset toward the camera exactly as `depthOffsetFS` offsets it on the GPU, with the same
constant (`Shader.DepthOffset.annotationWorld`, converted by `Shader.DepthOffset.toClip`), so
an annotation lying on the terrain counts as visible, just as it is drawn.

**Cost.** Nothing per frame; rays are cast only on release or click. The first test on a surface
patch loads its KdTree from disk, as the first surface pick does, so the first box selection in
a fresh scene can pause briefly.

**Where it differs from the screen:**

- Every visible surface occludes. On screen, annotations are depth-tested only against the last
  surface priority group drawn.
- Surfaces without KdTrees never occlude.
- **Hover highlighting** still comes from the annotation pick pass, which contains no terrain.
  A hidden annotation can still light up under the cursor, but clicking it does nothing.

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
| Hit test: project in double on the CPU, clip against w > 0, samples inside the rectangle (Liang–Barsky, perspective-correct) checked by a visibility predicate | `DrawingApp.annotationsTouchingRect`, [Drawing-App.fs](../src/PRo3D.Core/Drawing/Drawing-App.fs) |
| Occlusion: the depth test on the CPU; the point a click lands on | `DrawingApp.pointVisible`, `DrawingApp.annotationPointUnderCursor` |
| KdTree ray cast against the visible surfaces | `Picking.castToSurfaces`, [Picking.fs](../src/PRo3D.Viewer/Viewer/Picking.fs) |
| Ctrl+click check: the pick pass sends `PickAnnotation (sceneHit, id)`, the viewer forwards `PickDirectly` only when the clicked part is visible | packed pick handler in `DrawingApp.view`; `clickedPartVisible` and the `PickAnnotation` case in [Viewer.fs](../src/PRo3D.Viewer/Viewer/Viewer.fs) |
| The depth offset shared by the shaders and the CPU test | `Shader.DepthOffset.annotationWorld`, `Shader.DepthOffset.toClip`, [Utilities.fs](../src/PRo3D.Base/Utilities.fs) |
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
- **Clicks.** A click runs no box hit test; a click on an annotation is handled by the
  existing pick, now with the occlusion check. An earlier version cleared the selection on a
  click on empty space, deciding "empty" with a second hit test on a small square. That square
  could not match the pick pass's widened lines and points, so a click near the edge of a thick
  line selected it on press and cleared it again on release. Clearing on empty space was
  dropped rather than duplicating the pick pass's footprint.
- **Selection paths.** Viewport picks store the root path in their `TreeSelection`. The box
  selection stores the real group path, and drops any older entry for the same id, so nothing is
  ever selected twice.
- **Old body-level handlers.** `StartDragging` / `Dragging` / `EndDragging` used to clear
  `multiSelectBox`. They no longer touch it: the body's mousedown arrives right after the render
  control's pointerdown and would wipe the rectangle just started.

## Tests

`src/Tests/Features/Section24_BoxSelection.fs` covers:

- **TC-24.1, the hit test:**
  - a vertex inside the rectangle;
  - a segment crossing it with no vertex inside;
  - a polyline whose bend passes through the rectangle;
  - hidden annotations;
  - geometry behind the camera, and a segment partly behind it;
  - draped segments versus the straight chord;
  - every geometry.
- **TC-24.2, the selection modes:**
  - Replace;
  - Replace with nothing;
  - Add, without duplicating a viewport pick;
  - Toggle, including the green annotation;
  - a sub-group path;
  - unknown ids.
- **TC-24.3:** box-select, Delete, then one Undo.
- **TC-24.4, occlusion in the box**, against a flat test terrain:
  - a point on, below and above the terrain;
  - a fully buried annotation;
  - a partly buried one, boxed over its visible and over its hidden end;
  - the ray cap.
- **TC-24.5, the click point:**
  - the visible and the buried part of a half-buried line;
  - perspective-correct mapping back to the world.
- **TC-24.6, real terrain:** the MSL OPC from `src/Tests/resources`, through
  `HeadlessPicking`. A point on the surface is visible, 1 m into it hidden, 1 m above it
  visible. Skipped without the data.

`tests-ui/tests/box-selection.spec.ts` (Playwright) runs it in the app, on the Dimorphos OPC
with 5000 generated polylines (`PRO3D_BOX_SCALE` scales the count) whose visibility is known
by construction:

- **A**: 2000 inside the body (on a 30 m sphere), never visible;
- **B**: 1000 floating between the camera and the body (120 m, within 40 deg of the view
  direction), always visible;
- **C**: 2000 behind the body (120 m, within 20 deg of the far side), all hidden by it.

In order, it checks that:

- a Ctrl+click on the edge of a B line selects it, and it stays selected after the release;
- a Ctrl+drag over the whole view selects exactly B: A 0, B 1000, C 0, read per annotation from
  the annotations list;
- a Ctrl+click on empty space changes nothing;
- releasing Ctrl mid-drag hides the rectangle at once and changes nothing;
- a Ctrl+drag over empty space clears the selection.

It prints the viewer's own measurement (`[BoxSelection] ... visibility rays, ... ms`). A
hidden annotation is tested at every sample up to the ray budget before it counts as hidden,
so the hidden ones dominate. Measured at 5000
annotations: 129,000 rays (4000 hidden x 32, plus one per visible annotation) took 1.3 to
4.8 s over several runs, roughly 10 to 40 microseconds each. The first box also loads the
KdTree patches the rays touch: four here, 3 to 11 s in all depending on the disk cache. Picking loads the same patches
lazily too, so a later box costs only the rays.

While it runs, the busy indicator reads `selecting annotations` ([BusyIndicator](BusyIndicator.md)),
and the spec checks that `/busy` reports it.

A second test in the same spec checks the modifier keys on 100 annotations
([DirectToolMode.md](DirectToolMode.md#the-modifier-key)). At 5000 annotations the main page is
too busy building its annotation list (#900) to take the Direct Tool Mode click. It checks that:

- Alt+click without Ctrl picks nothing;
- in Ctrl+Alt+drag, letting go of Alt keeps the rectangle (Ctrl still holds it), and the
  gesture toggles;
- in Direct Tool Mode, Alt+drag toggles, and the camera does not move;
- with Ctrl held, moving focus away and letting go there leaves Ctrl released: a plain drag
  then moves the camera.

The manual steps are in the test protocol as TC-62 (Box Selection of Annotations).
