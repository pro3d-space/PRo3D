# Navigation Axis Gizmo

A small always-visible gizmo in the **bottom-left corner of the main render view**.
It shows the three reference-system axes. Clicking a **circle** snaps the camera to a
clean, axis-aligned view of your selection (the affordance a view-cube gives in
CAD / DCC tools); clicking an **edge** locks that axis so navigation can only rotate
the camera about it (see [Lock a navigation axis](#lock-a-navigation-axis)).

For the reference frame these directions are expressed in, see
[Camera & Navigation](Navigation.md).

## What it shows

- Three coloured lines from the centre to labelled circles: **north** (red),
  **east** (green) and **up** (blue) — the reference-system frame
  (`east = north × up`), with the same colour assignment the in-scene reference
  cross uses.
- The three opposite directions as toned-down lines and circles.
- **The labels follow the selected reference system**, mirroring the split the
  in-scene cross already makes ([`Sg.fs`](../src/PRo3D.Core/Sg.fs)):

  | Reference system | labels |
  |---|---|
  | Mars, Earth, Moon, Phobos, Deimos, Didymos, Dimorphos, ENU | `N` / `E` / `U` (and `-N` / `-E` / `-U`) |
  | `None`, `JPL` | `X` / `Y` / `Z` (and `-X` / `-Y` / `-Z`) |

  Where axis letters are used the mapping is **X = north, Y = east, Z = up**, matching
  the in-scene `xyzSystem` cross and
  `TransformationApp.getReferenceSystemBasis_global`. On a real body the letters would
  be misleading — the local frame tilts as you move over the surface and is not the
  frame the vertex coordinates are in — so those get compass letters instead.
- The gizmo rotates live with the camera. When two circles overlap, the one nearer
  the viewer is drawn on top (painter's algorithm on the projected depth), and
  circles pointing away from the viewer are dimmed.

## What a click does

Clicking a circle **instantly** sets the camera (no animation) to **look straight
along that axis onto the centre of the combined bounding box of the currently
multi-selected surfaces**, at a distance that frames that bounding box.

- Multi-selection is the green cube-icon selection in the Surfaces panel
  (`GroupsModel.selectedLeaves`).
- **With nothing multi-selected the circles render disabled and do nothing** — there
  is no target to frame. Hovering the gizmo then shows a tooltip saying at least one
  surface must be selected.
- Camera *up* after the snap: for the top / bottom view North points up the screen
  (map convention, matching [MapView](MapView.md)); for the four side views the
  reference Up direction stays vertical.

### Per navigation mode

| Mode | Behaviour |
|---|---|
| **FreeFly** | All six circles set the camera view directly. |
| **ArcBall** | Same direct set. The orbit pivot (`exploreCenter`) is left where it is, so orbiting after a snap keeps its previous centre. |
| **MapView** | The four horizontal circles work. **Up / Down are disabled** — MapView locks the camera to a nadir, north-up pose and looking along the vertical (polar) axis is its gimbal-lock singularity (`MapViewCameraController.blocksPole`); the tooltip says so. |

## Lock a navigation axis

Clicking an **edge** — the full diameter line through the centre, e.g. `-N … +N` —
**locks that axis**. The whole edge is highlighted **yellow** and its two circles get a
yellow ring. While an axis is locked, mouse navigation may only **rotate the camera about
that world axis**: dragging revolves the view around the highlighted axle, the other two
rotational degrees of freedom are frozen, and **zoom / dolly stay free**. Dragging exactly
along the frozen direction does nothing (it "feels stuck").

The lock is **transient** — it is never written to the scene or a bookmark. It is cleared
by:

- switching the camera mode,
- clicking one of the gizmo circles (the axis snap),
- clicking the locked edge again (toggle off),
- loading a scene or restoring a bookmark.

### Per navigation mode

| Mode | Lockable edges | Effect |
|---|---|---|
| **FreeFly** | none | Not supported — FreeFly is in-place mouse-look with no orbit pivot. The edges are not clickable. |
| **ArcBall** | all three | Orbit around `exploreCenter` collapses to a 1-DOF rotation about the locked axis. |
| **MapView** | vertical only (`Up`/`Down`) | Constant-latitude orbit about the body spin axis → drag changes longitude only. The two horizontal edges are not clickable (MapView already forces look-at-origin + north-up). |

The constraint is a swing-twist decomposition of the frame-to-frame camera rotation
(`NavigationConstraint.constrainRotationToAxis`), applied as a post-filter inside
`Navigation.update` for the ArcBall and MapView branches.

The snap builds `CameraView.lookAt eye center camUp`, orthonormal by construction
(`forward = −axis`, `forward ⟂ camUp` for every axis and both frame types), so there
is no gimbal lock at the moment it is applied.

## Implementation

| File | Role |
|------|------|
| [`src/PRo3D.Viewer/NavigationGizmo.fs`](../src/PRo3D.Viewer/NavigationGizmo.fs) | `GizmoAxis` (named by direction: `North`/`South`/`East`/`West`/`Up`/`Down`), `labelOf`, the SVG overlay `view` (circles for the snap, transparent edge hit-lines + yellow highlight for the lock), and the `resolveAxisWorldDir` / `gizmoCameraUp` / `navAxisOf` helpers |
| [`src/PRo3D.Core/NavigationConstraint.fs`](../src/PRo3D.Core/NavigationConstraint.fs) | frame helpers shared with the gizmo (`frameOf` / `axisWorldDirection`) and `constrainRotationToAxis` — the swing-twist post-filter the axis lock applies |
| [`src/PRo3D.Base/Navigation-Model.fs`](../src/PRo3D.Base/Navigation-Model.fs) | `NavigationAxis` (`NorthSouth`/`EastWest`/`UpDown`) and `NavigationModel.lockedAxis : Option<NavigationAxis>` (transient) |
| [`src/PRo3D.Viewer/Viewer-Model.fs`](../src/PRo3D.Viewer/Viewer-Model.fs) | `ViewerAction.OrientCameraToGizmoAxis of NavigationGizmo.GizmoAxis`, `ViewerAction.ToggleNavigationAxisLock of NavigationAxis` |
| [`src/PRo3D.Viewer/Viewer/Viewer.fs`](../src/PRo3D.Viewer/Viewer/Viewer.fs) | `updateViewer` handlers: snap (bounding box → framing distance → `CameraView.lookAt` via `_view` + `_animationView`, also clears the lock); `ToggleNavigationAxisLock` toggles `lockedAxis` when the mode allows the axis |
| [`src/PRo3D.Viewer/Navigation.fs`](../src/PRo3D.Viewer/Navigation.fs) | `Navigation.update` applies `constrainRotationToAxis` in the ArcBall and MapView branches while `lockedAxis` is set; `SetNavigationMode` clears it |
| [`src/PRo3D.Viewer/Viewer/ViewerGUI.fs`](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs) | yields the gizmo into the `"render"` page's overlay `alist` next to the [tool strip](ToolStrip.md); builds `axisEnabled`, `edgeLockEnabled` (ArcBall any / MapView vertical / FreeFly none) and the `hint` tooltip text |

Design notes:

- **Overlay, not an `Sg`.** The gizmo is an SVG overlay driven only by the camera
  orientation (`m.navigation.camera.view`), the reference system, and
  `m.navigation.lockedAxis`. The `view` function itself holds no state; the axis
  lock lives on `NavigationModel` (transient, not persisted). The geometry-dependent
  maths (selection bounding box, framing distance from the current frustum FOV) runs
  in `updateViewer`, where reading plain `Model` values is fine.
- **Axis lock.** Clicking an edge sets `NavigationModel.lockedAxis`;
  `Navigation.update` then post-filters the camera through
  `NavigationConstraint.constrainRotationToAxis` (swing-twist: keep the rotation's
  twist about the locked world axis, drop the swing) in its ArcBall and MapView
  branches. ArcBall pivots on `exploreCenter` and allows all three edges; MapView
  pivots on the body centre and only the vertical edge (constant-latitude orbit
  about `mapFrame.polarAxis`); FreeFly is unsupported (`edgeLockEnabled` is `false`
  for every axis there). The lock clears on a mode switch (`SetNavigationMode`), a
  circle click (`OrientCameraToGizmoAxis`), a re-click of the locked edge, and
  scene / bookmark load (those rebuild `NavigationModel`).
- **Click isolation.** The wrapper `div` stops propagation of
  `mousedown mouseup click dblclick contextmenu wheel` (same guard as
  `ViewerGUI.ToolStrip.view`), so interacting with the gizmo never starts a camera
  drag, selection rectangle, or context menu on the render body underneath. Only the
  circles take pointer events; the lines/labels/gaps are click-through.
- **Instant, not animated.** The click writes the new `CameraView` straight to
  `_view` (and mirrors it into `_animationView`, as the navigation handler does),
  rather than pushing a `CameraAnimations` animation. A disabled circle keeps
  `pointer-events` so its hover still triggers the tooltip; only the `onClick` is
  dropped.
- **Tooltip.** `view` takes a `hint : aval<string>`, set as a semantic-ui `data-tooltip`
  on the wrapper `div` (pure CSS). `hint` is `""` while the gizmo is fully usable — then
  the wrapper is `pointer-events:none` and stays click-through. When `ViewerGUI` fills it
  with the "select a surface" / "not available in Map View" text the wrapper flips to
  `pointer-events:auto` so a hover anywhere over the gizmo raises the tooltip.
  `axisEnabled : aval<GizmoAxis -> bool>` gates each circle individually so Up/Down can
  be disabled without the other four.
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
