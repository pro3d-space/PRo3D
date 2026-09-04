# Camera & Navigation

How the camera works in PRo3D: the frame it is expressed in, the state it lives
in, the three modes that drive it, and the widgets that display it.

This is the hub page. The two pieces that have their own detailed docs are linked
rather than repeated:

- [MapView](MapView.md) — the map-view controller in depth
- [Navigation Gizmo](NavigationGizmo.md) — the interactive axis gizmo
- [Tool Strip](ToolStrip.md) — where the mode buttons live

| File | Role |
|------|------|
| [`src/PRo3D.Base/Navigation-Model.fs`](../src/PRo3D.Base/Navigation-Model.fs) | `NavigationMode`, `NavigationModel` |
| [`src/PRo3D.Viewer/Navigation.fs`](../src/PRo3D.Viewer/Navigation.fs) | the dispatcher: one `update` for all three modes |
| [`src/PRo3D.Viewer/MapViewCameraController.fs`](../src/PRo3D.Viewer/MapViewCameraController.fs) | PRo3D's own MapView controller |
| [`src/PRo3D.Core/ReferenceSystem.fs`](../src/PRo3D.Core/ReferenceSystem.fs) | derives up / north / northO from a position and a planet |
| [`src/PRo3D.Base/CooTransformation.fs`](../src/PRo3D.Base/CooTransformation.fs) | `Planet`, `getUpVector`, `tryGetBodyRadius` |
| [`src/PRo3D.Core/Sg.fs`](../src/PRo3D.Core/Sg.fs) | the in-scene reference cross |
| [`src/PRo3D.Core/TransformationApp.fs`](../src/PRo3D.Core/TransformationApp.fs) | the numeric reference-system basis used by transformations |

FreeFly and ArcBall are **not** in this repository — they are
`FreeFlyController` / `ArcBallController` from the `Aardvark.UI.Primitives`
package. PRo3D only wraps them.

---

## 1. The reference system defines the frame

Everything below is relative to the **`Reference System:`** dropdown in the top
toolbar ([`ViewerGUI.fs:961-963`](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs#L961-L963)).
It is a dropdown over the `Planet` enum
([`CooTransformation.fs:13-23`](../src/PRo3D.Base/CooTransformation.fs#L13-L23))
and writes `scene.referenceSystem.planet` through `ReferenceSystemAction.SetPlanet`.

There is no separate "coordinate system" setting: the planet **is** the frame
selector. What it selects:

| Reference system | `up` | `north` | has a body radius | MapView |
|---|---|---|---|---|
| Mars, Moon, Phobos, Deimos, Didymos, Dimorphos, Earth | body-relative up at the point, via SPICE (the "+100 m altitude trick"), falling back to `p.Normalized` | `northVector up`, i.e. `east = Z × up`, `north = up × east` | yes | enabled |
| `ENU` | `+Z` | `+Y` | no | enabled |
| `JPL` | `−Z` | `+X` | no | enabled |
| `None` | `+Z` | `+X` | no | **disabled** |

Sources: `up` from
[`CooTransformation.getUpVector`](../src/PRo3D.Base/CooTransformation.fs#L523-L538),
`north` from
[`ReferenceSystem.updateCoordSystemAt`](../src/PRo3D.Core/ReferenceSystem.fs#L86-L103),
the MapView gate from
[`ViewerGUI.fs:1121-1122`](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs#L1121-L1122)
("MapView orients to the planet centre with up = north, so it needs a reference body").

Two further pieces of the reference-system model matter downstream:

- **`noffset`** rotates `north` about `up` by a user-set angle, producing **`northO`**
  ([`ReferenceSystem.fs:94-95`](../src/PRo3D.Core/ReferenceSystem.fs#L94-L95)).
  Almost every consumer reads `northO`, **not** `north` — the exception is
  `Sg.view`'s `styleXZYnorth` ([`Sg.fs:283`](../src/PRo3D.Core/Sg.fs#L283)), which
  still reads `north.value`.
- **`origin`** is where the cross is drawn and what gets persisted. Navigating
  re-derives up/north but must *not* move `origin` — see §5.

### The one invariant that holds everywhere

```
east = north × up
```

giving a **right-handed (east, north, up)** triad. Confirmed in
[`Sg.fs:171`](../src/PRo3D.Core/Sg.fs#L171),
[`TransformationApp.fs:114`](../src/PRo3D.Core/TransformationApp.fs#L114),
[`TransformationApp.fs:328`](../src/PRo3D.Core/TransformationApp.fs#L328) and
[`MapViewCameraController.fs:114-117`](../src/PRo3D.Viewer/MapViewCameraController.fs#L114-L117),
and asserted by TC-2.5
([`Section02_ViewerActionsNavigation.fs:383-391`](../src/Tests/Features/Section02_ViewerActionsNavigation.fs#L383-L391)):
`east × north = up`.

Note this is self-consistent with `northVector`, which goes the other way round
(`east₀ = Z × up`, `north = up × east₀`): recomputing `north × up` gives `east₀`
back exactly.

### Handedness is our choice, not the body's

Worth stating plainly, because it is easy to assume otherwise: **handedness is a
property of the labelling, not of the reference system.** The three directions
east, north and up form a right-handed triad in that order for *every* body,
including the non-planetary frames. Whether the resulting XYZ frame is left- or
right-handed depends only on which letter we staple onto which direction:

- `X = east, Y = north, Z = up` → right-handed (`X × Y = Z`)
- `X = north, Y = east, Z = up` → left-handed (`X × Y = −Z`)

So there is no body that *forces* one or the other, and no reason for the choice
to vary with the dropdown. Every reference system can be presented either way; the
convention should be picked once and applied uniformly. The only thing genuinely
fixed per body is the underlying body-fixed cartesian frame the OPC data is stored
in, which is upstream of all of this and is not what the letters describe.

### What switching the reference system actually does

`SetPlanet` is not just a relabelling. The
[`ReferenceSystemMessage` handler](../src/PRo3D.Viewer/Viewer/Viewer.fs#L1817-L1855)
does four things:

1. recomputes up / north / northO (`ReferenceSystemApp.update`);
2. **re-aims the camera** through `SceneLoader.updateCameraUp`
   ([`Scene.fs:339-348`](../src/PRo3D.Viewer/Scene.fs#L339-L348));
3. on `SetPlanet` only, rebuilds each surface's local reference system
   (`TransformationApp.Action.UpdatePlanetInLocalRefSys`);
4. recalculates every annotation's angular results (bearing, dip and strike, …)
   and refits the Color-by-Category ramp.

Step 2 is the one that surprises. `updateCameraUp` rebuilds the camera as

```fsharp
ReferenceSystem.bodyAwareLookAt referenceSystem cam.view.Location (cam.view.Location + cam.view.Forward)
```

Position and viewing direction are preserved, but the **sky vector is replaced**,
so the camera is *rolled* about its own view axis. The OPC data does not move —
the view rolls under it, which looks the same at a glance.

`bodyAwareSky` ([`ReferenceSystem-Model.fs:179-188`](../src/PRo3D.Core/ReferenceSystem-Model.fs#L179-L188))
returns world `+Z` for small bodies and the reference up otherwise, and
`isSmallBody` ([`CooTransformation.fs:360-363`](../src/PRo3D.Base/CooTransformation.fs#L360-L363))
covers Phobos, Deimos, Didymos and Dimorphos. Combined with the `up` column in the
table above, the resulting camera sky splits the dropdown into exactly two groups:

| Reference system | camera sky after the switch |
|---|---|
| Earth, Mars, Moon | the **local** reference up at the camera position |
| Phobos, Deimos, Didymos, Dimorphos | world `+Z` (forced by `isSmallBody`) |
| `None`, `ENU` | world `+Z` (their reference up *is* `+Z`) |
| `JPL` | world `−Z` |

which is why Earth / Mars / Moon behave as one group and
None / ENU / Phobos / Deimos / Didymos / Dimorphos as another.

**`updateCameraUp` runs for every `ReferenceSystemAction`, not just `SetPlanet`**
([`Viewer.fs:1826-1829`](../src/PRo3D.Viewer/Viewer/Viewer.fs#L1826-L1829)) — so
nudging the north-offset slider, toggling the cross's visibility or changing its
text size also re-rolls the camera. See open question 3.

---

## 2. What X, Y and Z mean — and where the code disagrees

The triad above is unambiguous. **Which letter is attached to which direction is
not.** Two conventions are live in the codebase at the same time:

| Consumer | X | Y | Z | Handedness |
|---|---|---|---|---|
| [`TransformationApp.getReferenceSystemBasis_global`](../src/PRo3D.Core/TransformationApp.fs#L110-L119) (planetary bodies) | north | east | up | **left** |
| [`TransformationApp.getLocalRefSys`](../src/PRo3D.Core/TransformationApp.fs#L322-L329) | north | east | up | **left** |
| [`Sg.view` → `xyzSystem`](../src/PRo3D.Core/Sg.fs#L216-L253) — the in-scene cross with "X"/"Y"/"Z" labels | north | east | up | **left** |
| [`ScaleBarsApp.viewScaleCoordinateFrame`](../src/PRo3D.Core/ScaleBarsApp.fs#L721) (red/green/blue through the basis above) | north | east | up | **left** |
| [`NavigationGizmo.axisDir`](../src/PRo3D.Viewer/NavigationGizmo.fs#L59-L63) | **east** | **north** | up | right |
| [`MapViewController.mapFrame`](../src/PRo3D.Viewer/MapViewCameraController.fs#L106-L108), `ENU` / `None` † | east | north | up | right |
| [`MapViewController.mapFrame`](../src/PRo3D.Viewer/MapViewCameraController.fs#L104-L105), `JPL` † | north | east | **down** | right |

† **MapView is known to be buggy and is not a source of truth for this.** Its rows
are listed for completeness only — do not treat them as evidence for a convention.
MapView is to be fixed in its own right (open question 1); whatever convention is
settled here should then be applied to it, not read out of it.

So the convention actually in force is **X = North, Y = East, Z = Up** — a
left-handed frame (`X × Y = −Z`) — held by the four non-MapView consumers, and the
navigation gizmo is the outlier.

### Colours

The colour mapping *is* consistent (X red, Y green, Z blue), which is what makes
the disagreement visible on screen: the same red arrow means **north** in the
scene and **east** in the gizmo.

Which cross you actually see depends on the planet
([`Sg.fs:338-348`](../src/PRo3D.Core/Sg.fs#L338-L348)):

| Reference system | cross drawn | red | green | blue | labels |
|---|---|---|---|---|---|
| Mars, Earth, Moon, Phobos, Deimos, Didymos, Dimorphos | `refsystem` ([`Sg.fs:305-313`](../src/PRo3D.Core/Sg.fs#L305-L313)) | north | east | up | `N` arrow only |
| `ENU` | `refsystem2` ([`Sg.fs:315-323`](../src/PRo3D.Core/Sg.fs#L315-L323)) | north | east | up | `N`, `E` |
| `None`, `JPL` | `xyzSystem` ([`Sg.fs:328-333`](../src/PRo3D.Core/Sg.fs#L328-L333)) | north | east | up | `X`, `Y`, `Z` |

Red = north, green = east, blue = up in **all three** variants. Only the labelling
differs.

### Comments that contradict their own code

- [`TransformationApp.fs:520`](../src/PRo3D.Core/TransformationApp.fs#L520) — the
  Transformations panel tooltip reads *"East(X)/North(Y)/Up(Z) is defined by this
  reference system"*, but the basis it describes
  ([`:118`](../src/PRo3D.Core/TransformationApp.fs#L118)) is
  `Trafo3d.FromOrthoNormalBasis(north, east, up)`, i.e. North(X)/East(Y)/Up(Z).
  The comment at [`:167`](../src/PRo3D.Core/TransformationApp.fs#L167)
  ("translation along north, east, up directions") is the correct one.
- [`NavigationGizmo.fs:51-52`](../src/PRo3D.Viewer/NavigationGizmo.fs#L51-L52) and
  [`NavigationGizmo.md:12-14`](NavigationGizmo.md) claim the gizmo uses "the same
  basis used by the in-scene reference cross". It does not — it swaps X and Y.

### Non-planetary bodies get a different basis entirely

`getReferenceSystemBasis_global` only builds the local (north, east, up) triad for
the planetary bodies. The others get fixed global axes plus the north offset
([`TransformationApp.fs:105-125`](../src/PRo3D.Core/TransformationApp.fs#L105-L125)):

| Reference system | basis |
|---|---|
| Mars, Moon, Phobos, Deimos, Didymos, Dimorphos | `(north, east, up)` — local, position-dependent |
| Earth | `(+X, +Y, +Z)` — the **global identity**, not a local triad |
| `ENU` | `(+Y, +X, +Z)` — a plain X/Y swap |
| `JPL` | `(−X, +Y, −Z)` |
| `None` | `(+X, +Y, +Z)` |

Earth is the surprise: it is a body with a perfectly good local up and north, but
transformations on Earth are expressed in global axes.

---

## 3. Camera state and the three modes

All three modes share **one** `CameraControllerState`
([`Navigation-Model.fs:9-20`](../src/PRo3D.Base/Navigation-Model.fs#L9-L20)):

```fsharp
type NavigationMode = FreeFly = 0 | ArcBall = 1 | MapView = 2

[<ModelType>]
type NavigationModel = {
    camera         : CameraControllerState
    navigationMode : NavigationMode
    exploreCenter  : V3d
    updatePerFrame : bool
}
```

There is no per-mode camera state. The mode only decides **which controller's
`update` runs** — the camera pose carries across a mode switch unchanged unless
the switch explicitly rewrites it.

[`Navigation.update`](../src/PRo3D.Viewer/Navigation.fs#L85-L220) is the single
dispatcher:

| Mode | Behaviour | Where |
|---|---|---|
| **FreeFly** | delegates to `FreeFlyController.update`, then overwrites `freeFlyConfig` from the scene's `navigationSensitivity` | [`Navigation.fs:110-126`](../src/PRo3D.Viewer/Navigation.fs#L110-L126) |
| **ArcBall** | remaps ctrl+right-drag to left-drag, delegates to `ArcBallController.update`, then forces `orbitCenter = exploreCenter` | [`Navigation.fs:87-108`](../src/PRo3D.Viewer/Navigation.fs#L87-L108) |
| **MapView** | PRo3D's own controller; see [MapView.md](MapView.md) | [`Navigation.fs:127-185`](../src/PRo3D.Viewer/Navigation.fs#L127-L185) |

### MapView re-purposes shared fields

While in MapView, three `CameraControllerState` fields carry non-standard meanings
([`Navigation.fs:141-150`](../src/PRo3D.Viewer/Navigation.fs#L141-L150)):
`targetPhiTheta` = window size, `panFactor` = horizontal FOV, `rotationFactor` =
body radius. A camera state that has been through MapView must not be handed
straight to FreeFly. See [MapView.md](MapView.md#interaction-with-the-other-modes).

### Switching modes

[`SetNavigationMode`](../src/PRo3D.Viewer/Navigation.fs#L187-L220):

- → **ArcBall**: picks an orbit centre with a centre-of-screen ray; falls back to
  FreeFly if nothing is hit. `updatePerFrame = false`.
- → **FreeFly**: rebuilds the view via
  `CameraView.lookAt location center (ReferenceSystem.bodyAwareSky planet up)`.
- → **MapView**: `switchToMapViewController`, sets the body radius,
  `updatePerFrame = true`, and deliberately leaves `exploreCenter` alone.

ArcBall is also entered **implicitly** when the user picks an explore centre
([`Navigation.fs:87-93`](../src/PRo3D.Viewer/Navigation.fs#L87-L93)).

### Persistence

`navigationMode` is stored on `Scene`
([`Viewer-Model.fs:219`](../src/PRo3D.Viewer/Viewer-Model.fs#L219)) and on
bookmarks ([`Bookmark-Model.fs:26`](../src/PRo3D.Core/Bookmark-Model.fs#L26)).
Both restore paths — [`Scene.fs:329-337`](../src/PRo3D.Viewer/Scene.fs#L329-L337)
and [`Bookmarks.fs:203-213`](../src/PRo3D.Viewer/Bookmarks.fs#L203-L213) — rebuild
`NavigationModel` **directly**, without going through `SetNavigationMode`. That is
why MapView recomputes its body radius on every message rather than only on the
mode switch.

---

## 4. Input routing

[`Viewer.fs:2417-2439`](../src/PRo3D.Viewer/Viewer/Viewer.fs#L2417-L2439) picks the
active controller's DOM attributes by mode:

```fsharp
match state, inverseFlag = ctrlFlag with
| NavigationMode.FreeFly, true -> yield! FreeFlyController.extractAttributes …
| NavigationMode.ArcBall, true -> yield! ArcBallController.extractAttributes …
| NavigationMode.MapView, true -> yield! MapViewController.extractAttributes …
| _, false                     -> yield! AMap.empty
```

The `inverseFlag = ctrlFlag` gate is the **camera-vs-drawing switch**: by default
the camera is live only while no modifier is held, and `Invert Drawing:` in the
top toolbar reverses that. When the gate is false the render control gets *no*
camera attributes at all, and the mouse belongs to the active tool.

The gizmo and tool strip both stop propagation of
`mousedown mouseup click dblclick contextmenu wheel` so that clicking them never
starts a camera drag on the render body underneath.

---

## 5. Where the frame is re-derived while navigating

- **Navigation does not touch the reference system at all.** `up` / `north` /
  `northO` always describe the frame at `origin` — the point the cross is drawn at,
  the panel reports and the scene persists. Every action that sets them computes
  them there: `UpdateUpNorth` and `InferCoordSystem` at the point being placed
  (which becomes `origin`), `SetPlanet` at the existing `origin`
  ([`ReferenceSystem.fs:148`](../src/PRo3D.Core/ReferenceSystem.fs#L148)).
  `SetUp` / `SetNorth` / `SetNOffset` are deliberate manual overrides and are left
  alone.

  Until recently the `NavigationMessage` handler re-derived them at the **camera**
  on every mouse event, which drew the cross in one place carrying the orientation
  of another and wiped manual overrides on the next click — the unfixed half of
  [issue #662](https://github.com/pro3d-space/PRo3D/issues/662). See open question 3.
- **Small bodies** substitute world `+Z` for the reference up as the camera sky,
  to avoid FreeFly gimbal lock when the view direction runs near-parallel to a
  radial up: `ReferenceSystem.bodyAwareSky`
  ([`ReferenceSystem-Model.fs:179-188`](../src/PRo3D.Core/ReferenceSystem-Model.fs#L179-L188)),
  applied to Phobos, Deimos, Didymos and Dimorphos. Consumers:
  `SetNavigationMode FreeFly`, `SceneLoader.updateCameraUp` and the fly-to paths.
  The gizmo snap deliberately bypasses it
  ([`Viewer.fs:673-674`](../src/PRo3D.Viewer/Viewer/Viewer.fs#L673-L674)).

---

## 6. The navigation gizmo

The bottom-left axis gizmo is a pure function of the camera orientation and the
reference system — it holds no state. Full description in
[NavigationGizmo.md](NavigationGizmo.md); the parts that matter here:

- It draws six labelled circles by projecting the axis directions onto the camera
  plane with three dot products against `CameraView.Right / Up / Forward`
  ([`NavigationGizmo.fs:96-111`](../src/PRo3D.Viewer/NavigationGizmo.fs#L96-L111)).
  That projection is correct; the axis→direction assignment above it is what
  disagrees with the rest of the codebase.
- Clicking a circle animates the camera to look along that axis onto the centre of
  the multi-selected surfaces' bounding box
  ([`Viewer.fs:649-680`](../src/PRo3D.Viewer/Viewer/Viewer.fs#L649-L680)). Both the
  drawing and the snap go through the same `axisDir`, so they are at least
  consistent with each other.
- `gizmoCameraUp` ([`NavigationGizmo.fs:74-79`](../src/PRo3D.Viewer/NavigationGizmo.fs#L74-L79))
  currently returns *north* as the screen-up for the top/bottom (±Z) view, and the
  reference up otherwise.

---

## 7. Open questions and known inconsistencies

The backlog this document was written to establish. Nothing here has been changed yet;
the order is roughly the order we intend to work through them.

1. **MapView is buggy and needs fixing in its own right.** Until it is, it is *not*
   a source of truth for frame conventions — see the † note in §2. Symptoms and
   scope still to be collected; [MapView.md](MapView.md) describes the intended
   design, not necessarily the current behaviour.

2. **The gizmo's X and Y are swapped** relative to every non-MapView consumer (§2).
   Red points east in the gizmo and north everywhere else. Affects both the drawing
   and the click-snap.

   *Proposed resolution (§5 of the discussion):* make the gizmo's **labels follow
   the reference system** instead of fixing one convention for all of them —
   `N`/`E`/`U` where the reference system is presented as a compass frame, `X`/`Y`/`Z`
   where it is presented as an axis frame. The in-scene cross already makes exactly
   this split ([`Sg.fs:338-348`](../src/PRo3D.Core/Sg.fs#L338-L348)):

   | Reference system | in-scene cross labels | proposed gizmo labels |
   |---|---|---|
   | Mars, Earth, Moon, Phobos, Deimos, Didymos, Dimorphos | `N` arrow only | `N` / `E` / `U` |
   | `ENU` | `N`, `E` | `N` / `E` / `U` |
   | `None`, `JPL` | `X`, `Y`, `Z` | `X` / `Y` / `Z` |

   This dissolves the letter ambiguity for every real body — a circle labelled `N`
   cannot be read as the wrong axis — and confines the X/Y question to `None` and
   `JPL`, where it can simply follow the `xyzSystem` cross (X = north, Y = east).
   It also leaves `getReferenceSystemBasis_global` untouched, so no persisted
   transformation values are reinterpreted.

3. ~~**The coordinate cross is oriented at the camera but drawn at the origin.**~~
   **FIXED.** The `NavigationMessage` handler used to refresh up / north / northO at
   `nav.camera.view.Location` on every navigation message, while the cross is
   rendered at `referenceSystem.origin`
   ([`Sg.fs:177-253`](../src/PRo3D.Core/Sg.fs#L177-L253)) — two different points, so
   the cross was drawn in one place with the orientation of another. The refresh
   call and the now-unused `refreshUpNorthForPosition` helper were removed; nothing
   in the navigation path needs a camera-tracking `up`.

   *Symptom (before the fix):* the cross was correct on load, then visibly jumped
   the first time you clicked anything at all — every mouse down/up and wheel event
   is a `NavigationMessage` — and afterwards drifted slowly with the camera, so it
   looked stable again. It also silently discarded any manual `SetUp` / `SetNorth`.

   *Why it shows up on small bodies:* the angle between up-at-origin and
   up-at-camera is roughly `groundDistance / bodyRadius`. On Dimorphos (R ≈ 77 m)
   a camera a few hundred metres out gives a completely different up; on Mars
   (R ≈ 3390 km) the same offset is a fraction of a degree. The bug is present
   everywhere and only *visible* on the small bodies.

   *History:* this was the unfixed half of
   [#662](https://github.com/pro3d-space/PRo3D/issues/662). Before commit
   `156d2c34` navigation ran `UpdateUpNorth cameraLocation`, which moved **both**
   the cross's position and its orientation onto the camera — the cross vanished
   into the near plane. That fix split out `RefreshUpNorth` and stopped `origin`
   moving, but left the *orientation* still derived from the camera.

   *Why dropping the refresh is safe:* nothing in the navigation path wants a
   camera-tracking `up`. The cross and the gizmo both want origin-up; annotation
   results are recalculated only in the `ReferenceSystemMessage` handler;
   `TransformationApp`'s basis wants up at the surface or pivot; MapView derives its
   own frame and never reads the reference system. Two subsystems had already had to
   route around the old behaviour and say so in their comments —
   `ProfileAttributeExtraction.sampleAt` and `ColorByCategoryApp.stampOf`. The one
   consumer that does want up near the camera is `bodyAwareSky` via
   `updateCameraUp`, which is not called from this path and is item 4.

   Anchoring the refresh at `origin` instead of removing it would have been
   equivalent for the cross (recomputing at an unchanged origin is idempotent) but
   would still have clobbered manual `SetUp` / `SetNorth` overrides, and it ran two
   SPICE round-trips per mouse event.

   *Still missing:* a regression test. It belongs at viewer level
   (`Render.makeViewer` in `Features/TestHelpers.fs`), since `Navigation.update`
   alone never sees the reference system — drive one `NavigationMessage` and assert
   `up` / `north` / `origin` are unchanged.

4. **`updateCameraUp` runs on every reference-system action, not just `SetPlanet`**
   ([`Viewer.fs:1826-1829`](../src/PRo3D.Viewer/Viewer/Viewer.fs#L1826-L1829)).
   Toggling the cross's visibility, changing its text size or colour, or dragging
   the north-offset slider all re-roll the camera about its view axis. Only the
   actions that actually change the frame (`SetPlanet`, `SetUp`, `SetNorth`,
   `SetNOffset`, `InferCoordSystem`) have any business doing that, and arguably not
   even all of those. This is the mechanism behind "switching the reference system
   reorients the OPC" — the OPC does not move, the camera rolls (§1).

5. **`getReferenceSystemBasis_local` throws for Moon, Phobos, Deimos, Didymos and
   Dimorphos.** Its match
   ([`TransformationApp.fs:127-146`](../src/PRo3D.Core/TransformationApp.fs#L127-L146))
   handles only Earth, ENU, Mars, JPL and None, and ends in `| _ -> failwith ""` —
   whereas the `_global` variant
   ([`:105-125`](../src/PRo3D.Core/TransformationApp.fs#L105-L125)) covers all the
   bodies. It is reached from
   [`getReferenceSystemBasisAndOriginTrafo`](../src/PRo3D.Core/TransformationApp.fs#L197-L201)
   when a surface has a local reference system **and** pivot mode is `BBCenter` or
   `PickPivot`. Defaults (`refSys = None`, `pivotMode = NoPivot`,
   [`Transformation-Model.fs:143`](../src/PRo3D.Core/Transformation-Model.fs#L143),
   [`:154`](../src/PRo3D.Core/Transformation-Model.fs#L154)) keep it out of reach on
   a fresh scene, so this is a latent crash, not an everyday symptom — but it is a
   real hole and the five bodies it misses are the same five where item 3 is most
   visible, which is what made it look related.

6. **`Planet.None`'s north disagrees between two subsystems.**
   [`ReferenceSystem.updateCoordSystemAt`](../src/PRo3D.Core/ReferenceSystem.fs#L88-L92)
   groups `None` with `JPL` and gives north `+X`;
   [`MapViewController.mapFrame`](../src/PRo3D.Viewer/MapViewCameraController.fs#L106-L108)
   groups `None` with `ENU` and gives north `+Y`. [MapView.md](MapView.md) asserts
   the two match. Folds into item 1.

7. **Earth's transformation basis is the global identity**, not its local
   (north, east, up) triad, unlike every other body
   ([`TransformationApp.fs:106-107`](../src/PRo3D.Core/TransformationApp.fs#L106-L107)).
   Intentional or historical is unclear.

8. **What should be screen-up after a ±Z gizmo snap** — entangled with item 2. The
   preference expressed so far is the CAD view-cube convention, "Y up". Under
   X = north / Y = east that puts *east* up the screen in a top view, which
   contradicts the north-up map convention; under X = east / Y = north it means
   north-up and `gizmoCameraUp` needs no change. Settle after item 2.

9. **East is computed with the wrong sign in two coordinate crosses.**
   [`TraverseApp.fs:647`](../src/PRo3D.Viewer/TraverseApp.fs#L647) and
   [`ViewPlanApp.fs:1068`](../src/PRo3D.SimulatedViews/Viewplanner/ViewPlanApp.fs#L1068)
   both use `up × north`, which is `−east`. Those green lines point west.

10. **The orientation cube is dead code.**
   [`Viewer.fs:2839`](../src/PRo3D.Viewer/Viewer/Viewer.fs#L2839) binds it, but the
   list returned at
   [`:2851-2857`](../src/PRo3D.Viewer/Viewer/Viewer.fs#L2851-L2857) omits it — so
    the `drawOrientationCube` toggle in the view config, its scene serialisation and
    the `resources/rotationcube*` assets are all inert. Either wire it back up or
    remove it.

11. **`ViewPlanApp.fs:1052-1053`** paints both the `right` and the `tilt` platform
    marker `C4b.Red`.

12. **[`NavigationGizmo.md`](NavigationGizmo.md) links `images/navigation-gizmo.png`**,
    which does not exist in `docs/images/`.

---

## Tests

[`src/Tests/Features/Section02_ViewerActionsNavigation.fs`](../src/Tests/Features/Section02_ViewerActionsNavigation.fs):

- **TC-2.1 / TC-2.2** — FreeFly and ArcBall through PRo3D's dispatcher
- **TC-2.3** — picking the explore centre
- **TC-2.4** — placing the coordinate system; asserts ENU gives `up = +Z`,
  `north = +Y`, and that Mars up/north are orthonormal
- **TC-2.5** — MapView: body scale, the first-entry and restore paths, and that the
  map frame is a right-handed east/north/up triple

There are no tests for the navigation gizmo.
