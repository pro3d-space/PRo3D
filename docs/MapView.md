# MapView Navigation

MapView is the third navigation mode next to FreeFly and ArcBall. It turns the
viewer into a top-down map of the body: **the camera always looks straight at
the frame origin, with north pointing up the screen**, and every navigation step
is scaled by how high the camera is above the ground.

Pick it from the [tool strip](ToolStrip.md) on the right edge of the main view (the blue map
icon, third in the navigation group).

For the frame and camera state MapView shares with the other two modes, see
[Camera & Navigation](Navigation.md).

**Small bodies switch to MapView by themselves** ([#898](https://github.com/pro3d-space/PRo3D/issues/898)):
when the scene body becomes Phobos, Deimos, Didymos or Dimorphos -- picked in the top bar,
picked as the observed body in the GIS view, or found in the first imported OPC
([SceneBody.md](SceneBody.md)) -- the main view switches to MapView, and the top-right notice
says so (*Scene body Dimorphos: switched to MapView*). It does not switch when the body stays
the same, when a scene is loaded or a bookmark played (they keep their saved mode), or for
Mars, Earth and the Moon. Switch back with the tool strip as usual. After an import the camera
also moves out to an overview -- the same direction from the body centre, 3.5 body radii out --
since the import's own framing leaves it a few tens of metres above a small body; picking the
body keeps the camera where it is.

MapView needs a body: it is unavailable while the planet is `None`. The planet is the
[scene body](SceneBody.md), so choosing the observed body in the GIS view enables MapView
just as choosing the planet in the top bar does. A GIS scene observed in a frame other
than the body's fixed frame (e.g. `J2000`, saved before the scene body existed) keeps the
planet it was saved with, but its world is not body-fixed: MapView's pole (+Z) is then the
wrong axis. The GIS view says so and switches the frame on request.

| File | Role |
|------|------|
| [`src/PRo3D.Viewer/MapViewCameraController.fs`](../src/PRo3D.Viewer/MapViewCameraController.fs) | The controller: map frame, camera constraint, pan/zoom integration |
| [`src/PRo3D.Viewer/Navigation.fs`](../src/PRo3D.Viewer/Navigation.fs) | Dispatch, per-message configuration, mode switching |
| [`src/PRo3D.Base/CooTransformation.fs`](../src/PRo3D.Base/CooTransformation.fs) | `getUpVector`, `tryGetBodyRadius` |

## Controls

| Input | Effect |
|-------|--------|
| Left-drag | Move over the ground (grab-and-drag: the terrain follows the cursor) |
| Middle-drag | **Pan**: shift the view rigidly, without turning the camera |
| `Shift` + right-drag | Pan, as middle-drag — in Direct Tool Mode too |
| Right-drag | Zoom in/out |
| Mouse wheel | Zoom in/out |
| `W` `A` `S` `D` | Move north / west / south / east |
| `R` | Reset: drop the pan — straight down at the body centre, north up, same distance |

The WASD axes can be inverted per computer under *User Preferences* — see
`UserPreferences.remapMapViewKey`. Those are machine-local settings and are not
stored in the scene.

## The camera constraint

After every mouse move, key release and animation tick,
`updateCameraForMapView` re-derives the camera orientation from its position:

```
centred = cameraLocation + pan                // un-pan (see below)
frame   = mapFrame planet centred             // local east / north / up
camera  = CameraView.lookAt centred V3d.OOO frame.north
camera  = camera moved by -pan in its own right/up plane
```

So the camera's position and the pan are the only free state; orientation is a
function of them. Navigating therefore means *moving the camera over the body*,
never turning it — which is what makes the view stay map-like.

### Pan

A pan is a rigid translation of the view: the camera moves sideways in its own
image plane and does not turn, so the whole picture shifts on screen and the body
centre is no longer in the middle. The ground under the cursor follows it (one
window width of drag = `2 · tan(hfov / 2) · height` of ground).

The pan is **not stored separately** — it is encoded in the view itself.
`panOffset` reads it back as the position of the frame origin in the camera's
right/up plane; `updateCameraForMapView` un-pans, re-aims and re-pans by that
offset, and orbiting (left-drag, WASD) rotates the camera *rigidly* about the
centre (forward included) instead of re-aiming it, so the pan survives both. Since
it lives in the `CameraView`, bookmarks and scenes keep it without any format
change. Entering MapView from another mode starts centred (`switchToMapViewController`).

When a pan starts from the centred view, the top-right overlay (the one that says
"scene saved") shows *Map View panned - press R to reset* for a few seconds: once
after entering MapView and again after each reset, not on every drag.

`R` (`recentre`) drops the pan: it moves the camera back to the un-panned
position, so the body keeps its size, and aims it straight down, north up — the
Google Earth / Cesium "reset view" convention. Like those viewers, PRo3D does not
force other camera changes back into a nadir view: a view set by a gizmo snap, a
fly-to or the remote API while in MapView keeps its direction (it only gets north
turned up, since any view with the origin ahead reads as a panned map view).
Press `R` to get the map view back.

Pan speed uses the camera's height above the ground *along the view axis*
(`depthAboveSurface`), not its distance from the centre, which grows as you pan.
The pole guard (`blocksPole`) checks the view direction for the same reason: once
panned, it differs from the direction to the centre, and it is the view direction
whose north-up frame degenerates at the pole. It also rejects a step whose
horizontal view direction reverses: a fast drag can jump right over the pole and
land on the far side below the threshold. Wheel zoom, like right-drag zoom,
never moves the camera past the plane through the centre.

Wheel and right-drag zoom move along the view direction, i.e. towards the
screen centre, not towards the body centre. The vertical axis lock does not apply
during a pure pan (a translation has no rotation to constrain); a pan held
together with an orbit is still locked.

### The map frame

`mapFrame` returns the local east/north/up triple, plus the `polarAxis` the
frame is derived from:

- **Planetary bodies** (Mars, Earth, Moon, Phobos, Deimos, Didymos, Dimorphos) —
  `up` is the body-relative up at the camera position
  (`CooTransformation.getUpVector`), and the body-fixed **+Z** axis is the pole:
  `east = Z × up`, `north = up × east`.
- **Non-planetary frames** — `up` is a fixed axis convention rather than a
  function of position, so the `Z × up` construction degenerates everywhere.
  These frames name their axes directly, each matching what
  `ReferenceSystem.updateCoordSystemAt` defines for that planet:
  `Planet.JPL` is NED (north `+X`, up `−Z`), `Planet.ENU` is ENU
  (north `+Y`, up `+Z`), and `Planet.None` takes JPL's north (`+X`) with ENU's
  up (`+Z`). None used to share the ENU branch, putting its north 90° out from
  the reference system, the cross and the gizmo. TC-2.5 asserts the agreement
  against `ReferenceSystemApp` directly, so neither side can drift.

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

The [navigation axis gizmo](NavigationGizmo.md) keeps its four horizontal snaps in
MapView but disables the vertical (Up / Down) ones: looking along the polar axis is
the north-up singularity `blocksPole` guards against.

Its [axis lock](NavigationGizmo.md#lock-a-navigation-axis) is available in MapView
for the **vertical edge only**: clicking it constrains a drag to a constant-latitude
orbit about the body spin axis (`mapFrame.polarAxis`), i.e. longitude-only motion.
The two horizontal edges are not lockable in MapView. The lock is a post-filter on
the camera in `Navigation.update` and is transient (see NavigationGizmo.md for the
clearing rules).

`Shift` + right is the pan gesture in both mouse schemes because the right button
always belongs to the camera (zoom normally, look in Direct Tool Mode), so Shift only
switches it to pan. `Shift` + left is left alone: in Direct Tool Mode it is the tool's
click, and with *Pick Annotation* active Shift+click multi-selects.

Middle-drag used to repeat left-drag (in a view always aimed at the centre,
"pan" and "orbit" were the same motion); it now pans.

## Tests

`src/Tests/Features/Section02_ViewerActionsNavigation.fs`, TC-2.5.
