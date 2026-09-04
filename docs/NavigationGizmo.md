# Navigation Axis Gizmo

A small always-visible gizmo in the **bottom-left corner of the main render view**.
It shows the three reference-system axes and lets you snap the camera to a clean,
axis-aligned view of your selection with a single click — the same affordance a
view-cube gives in CAD / DCC tools.

For the reference frame these axes are expressed in, see
[Camera & Navigation](Navigation.md). **Note:** the axis→direction mapping below
disagrees with the rest of the codebase — see
[Navigation.md, open question 2](Navigation.md#7-open-questions-and-known-inconsistencies).

![gizmo](images/navigation-gizmo.png)

## What it shows

- Three coloured lines from the centre to labelled circles: **X = East** (red),
  **Y = North** (green), **Z = Up** (blue) — the reference-system frame
  (`east = north × up`), the same basis used by the in-scene reference cross.
- The three opposite directions **-X / -Y / -Z** as toned-down lines and circles.
- The gizmo rotates live with the camera. When two circles overlap, the one nearer
  the viewer is drawn on top (painter's algorithm on the projected depth), and
  circles pointing away from the viewer are dimmed.

## What a click does

Clicking a circle animates the camera (2 s) to **look straight along that axis onto
the centre of the combined bounding box of the currently multi-selected surfaces**,
at a distance that frames that bounding box.

- Multi-selection is the green cube-icon selection in the Surfaces panel
  (`GroupsModel.selectedLeaves`).
- **With nothing multi-selected the circles render disabled and do nothing** — there
  is no target to frame.
- Camera *up* after the snap: for the top / bottom view (±Z) North points up the
  screen (map convention); for ±X / ±Y the reference Up axis stays vertical.

## Implementation

| File | Role |
|------|------|
| [`src/PRo3D.Viewer/NavigationGizmo.fs`](../src/PRo3D.Viewer/NavigationGizmo.fs) | `GizmoAxis`, the SVG overlay `view`, and the `resolveAxisWorldDir` / `gizmoCameraUp` helpers |
| [`src/PRo3D.Viewer/Viewer-Model.fs`](../src/PRo3D.Viewer/Viewer-Model.fs) | `ViewerAction.OrientCameraToGizmoAxis of NavigationGizmo.GizmoAxis` |
| [`src/PRo3D.Viewer/Viewer/Viewer.fs`](../src/PRo3D.Viewer/Viewer/Viewer.fs) | `updateViewer` handler: bounding box of the multi-selection → framing distance → push a `CameraAnimations.animateForwardAndLocation` animation |
| [`src/PRo3D.Viewer/Viewer/ViewerGUI.fs`](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs) | yields the gizmo into the `"render"` page's overlay `alist`, next to the [tool strip](ToolStrip.md) |

Design notes:

- **Overlay, not an `Sg`.** The gizmo is an SVG overlay driven only by the camera
  orientation (`m.navigation.camera.view`) and the reference system. It has no model
  state and is not persisted. The geometry-dependent maths (selection bounding box,
  framing distance from the current frustum FOV) runs in `updateViewer`, where
  reading plain `Model` values is fine.
- **Click isolation.** The wrapper `div` stops propagation of
  `mousedown mouseup click dblclick contextmenu wheel` (same guard as
  `ViewerGUI.ToolStrip.view`), so interacting with the gizmo never starts a camera
  drag, selection rectangle, or context menu on the render body underneath. Only the
  circles take pointer events; the lines/labels/gaps are click-through.
- **Framing.** `dist = 1.25 · max(r / tan(hfov/2), r / tan(vfov/2))` with
  `r = ½·|bb.Size|`; `hfov` from `Frustum.horizontalFieldOfViewInDegrees`, `vfov`
  derived via `Frustum.aspect`.
- **Small bodies.** The handler uses `CameraView.lookAt` with the chosen up directly
  rather than `ReferenceSystem.bodyAwareLookAt`, because an axis snap is a
  deliberately radial view; the small-body gimbal work-around does not apply.

## Relation to the Orientation Cube

Unrelated to the textured [orientation cube](../src/PRo3D.Core/OrientationCube.fs)
(`config.drawOrientationCube`, top-right, non-interactive). The gizmo is a separate,
interactive, bottom-left widget.
