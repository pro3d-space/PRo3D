# MapView Navigation

MapView is the third navigation mode next to FreeFly and ArcBall. It turns the
viewer into a top-down map of the body: **the camera always looks straight at
the frame origin, with north pointing up the screen**, and every navigation step
is scaled by how high the camera is above the ground.

Pick it from the [tool strip](ToolStrip.md) on the right edge of the main view (the blue map
icon, third in the navigation group).

| File | Role |
|------|------|
| [`src/PRo3D.Viewer/MapViewCameraController.fs`](../src/PRo3D.Viewer/MapViewCameraController.fs) | The controller: map frame, camera constraint, pan/zoom integration |
| [`src/PRo3D.Viewer/Navigation.fs`](../src/PRo3D.Viewer/Navigation.fs) | Dispatch, per-message configuration, mode switching |
| [`src/PRo3D.Base/CooTransformation.fs`](../src/PRo3D.Base/CooTransformation.fs) | `getUpVector`, `tryGetBodyRadius` |

## Controls

| Input | Effect |
|-------|--------|
| Left-drag | Move over the ground (grab-and-drag: the terrain follows the cursor) |
| Middle-drag | Same as left-drag |
| Right-drag | Zoom in/out |
| Mouse wheel | Zoom in/out |
| `W` `A` `S` `D` | Move north / west / south / east |

The WASD axes can be inverted per computer under *User Preferences* — see
`UserPreferences.remapMapViewKey`. Those are machine-local settings and are not
stored in the scene.

## The camera constraint

After every mouse move, key release and animation tick,
`updateCameraForMapView` re-derives the camera pose from its position alone:

```
frame  = mapFrame planet cameraLocation      // local east / north / up
camera = CameraView.lookAt cameraLocation V3d.OOO frame.north
```

So the camera's position is the only free state; orientation is a function of
it. Navigating therefore means *moving the camera over the body*, never turning
it — which is what makes the view stay map-like.

### The map frame

`mapFrame` returns the local east/north/up triple, plus the `polarAxis` the
frame is derived from:

- **Planetary bodies** (Mars, Earth, Moon, Phobos, Deimos, Didymos, Dimorphos) —
  `up` is the body-relative up at the camera position
  (`CooTransformation.getUpVector`), and the body-fixed **+Z** axis is the pole:
  `east = Z × up`, `north = up × east`.
- **Non-planetary frames** — `up` is a fixed axis convention rather than a
  function of position, so the `Z × up` construction degenerates everywhere.
  These frames name their axes directly:
  `Planet.JPL` is NED (X north, Y east, Z **down**), `Planet.ENU` and
  `Planet.None` are ENU (X east, Y north, Z up). This matches the reference
  system's own definition of north (see TC-2.4).

Right over a pole, north is genuinely undefined. `blocksPole` rejects camera
motion that would drive the view direction into the polar axis, so the camera
never reaches the singularity; motion *away* from it always passes, so the
camera cannot get stuck there either. Non-planetary frames have no singularity
and are not guarded.

## Navigation scale

Pan and zoom steps are scaled by the camera's **height above the reference
surface**, so the terrain moves under the cursor at roughly the same screen
speed whether you are 10 m or 100 km up:

```
height      = |cameraLocation| − bodyRadius        (floored to a small positive value)
halfPatch   = tan(hfov / 2) · height               // half the visible ground width
visibleArc  = 2 · atan(halfPatch / bodyRadius)     // angle it subtends at the body centre
```

A drag across the full window then rotates the camera about the body centre by
`visibleArc`, which is what makes the ground track the cursor.

`bodyRadius` comes from `CooTransformation.tryGetBodyRadius`, i.e. from the
body's reference surface rather than from scene state, and
`Navigation.mapViewRadius` recomputes it on **every** MapView message rather than
only on the mode switch. Both properties matter, and the history explains why.

The radius used to be estimated from `NavigationModel.exploreCenter`, falling
back to `|cameraLocation|` when that was zero. `exploreCenter` defaults to
`V3d.Zero` and only becomes a real surface point when something writes one:
ArcBall's `pickOrbitCenter`, or the `firstImport` branch in
[`Viewer.fs`](../src/PRo3D.Viewer/Viewer/Viewer.fs). So the **first** switch into
MapView normally took the fallback, which makes

```
height = |cameraLocation| − bodyRadius = 0     exactly
```

and a zero height collapses `halfPatch`, hence `visibleArc`, hence the drag
angle — the map view was frozen. Detouring through ArcBall and back appeared to
fix it, because ArcBall had meanwhile written a picked surface point into
`exploreCenter`. MapView is also entered by restoring a bookmark and by loading a
scene, and neither of those runs `SetNavigationMode` at all, so a radius computed
only on the mode switch was absent there and left the FreeFly default (`0.01`) in
place — the opposite failure, wildly oversensitive pan and zoom.

Non-planetary frames (JPL/ENU) have no body, so the only scale available is where
the data sits relative to the origin; `mapViewRadius` falls back to the distance
from the origin to the explore centre for those. This is also why MapView no
longer zeroes `exploreCenter` — see below.

The height is floored to a small positive value. Terrain below the reference
surface is routine — common in Martian basins, and permanent on a body like
Dimorphos whose mean radius (77 m) exceeds its polar radius (57.5 m) — and a
non-positive height would freeze pan and zoom outright.

## Interaction with the other modes

MapView orbits `V3d.OOO` and stores that in `CameraControllerState.orbitCenter`.
It deliberately does **not** touch `NavigationModel.exploreCenter`, which belongs
to ArcBall and drives the magenta explore-centre dot.

`CameraControllerState.rotationFactor` carries the body radius while in MapView.
That field means "rotation sensitivity" to the FreeFly controller, so a camera
state that has been through MapView should not be handed to FreeFly without
resetting it — `SetNavigationMode FreeFly` rebuilds the view for exactly this
kind of reason.

## Known limitation

MapView re-aims the camera in place; it does not move it. Switching to MapView
from a ground-level FreeFly pose therefore gives a nadir view from a couple of
metres up — correct, but rarely what the user wants. Framing the loaded surface
on entry would need the scene bounding box, which the navigation layer currently
has no access to.

## Tests

`src/Tests/Features/Section02_ViewerActionsNavigation.fs`, TC-2.5.
