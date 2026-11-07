# Follow cursor in the map projection view (#772)

The map centres on the 3D view's preview-cursor hit point, so that moving the mouse over the
terrain moves the map under it. On a planet this is the feature that makes the map a companion
view rather than a thing you have to navigate separately: a Jezero OPC is 0.045° across, so
without help you are panning a 360° map to find a speck.

**The toggle belongs in the map panel's own toolbar** (next to *Equirectangular / Polar north /
Polar south / Zoom to data / Reset view*). Nothing is added to the global toolbar, and the map
project keeps its property of touching no shared UI.

## What already exists

| | |
|---|---|
| `Model.surfaceIntersection : Option<SurfaceIntersection>` | `{ surfaceName; hitPoint : V3d; normal }`, set by the background preview pick (`Viewer.fs:1592`), exposed by Adaptify as `aval<Option<SurfaceIntersection>>` |
| Frame | `hitPoint` is in the same body-fixed frame the map projects (OPC global coordinates), like the camera position the marker already uses — no conversion beyond `Projection.lonLatR` + `forward` |
| `MapInputs` | already carries `planet`, `surfaces`, `annotations`, `camera`; built only by `MapProjectionHost` |
| `MapSg.cameraMarker` | a constant-screen-size marker; the cursor marker is the same function with another colour |

`DrawingModel.hoverPosition` looks like the obvious source but is dead — nothing sends
`DrawingAction.Move` any more, and the code says so. Use `surfaceIntersection`.

## Measurements taken before designing (2026-09-19, RTX 500 Ada laptop)

**1. Re-centring is input-bound, not compute-bound.** `tests-ui/src/probe-map-recenter.ts`, real
Jezero tiles as Mars, after *Zoom to data*, centre moved as fast as the app accepts:

| surfaces | idle | re-centring continuously | worst frame gap |
|---|---|---|---|
| 4 | 0 fps | 29.9 fps / 300 moves | 51 ms |
| 24 | 0 fps | 29.9 fps / 300 moves | 52 ms |
| 43 | 0 fps | 29.9 fps / 300 moves | 44 ms |

29.9 fps is exactly the probe's input rate: **one frame per centre change at every surface count**,
and surface count does not matter. Idle is 0 fps because the render control only pushes a frame
when something changed — a stationary cursor costs nothing.

*Conclusion: no dead-zone, no throttling.* Preview picks cannot arrive faster than mouse moves,
and the map already keeps up with those. An earlier claim that this would be costly was an
assumption, and the measurement contradicts it.

**2. A panel that is not the selected tab does nothing.**
`tests-ui/src/probe-map-background-tab.ts`, 58-surface Jezero scene, built-in default layout where
Map Projection is the last tab of a stack whose first tab is selected:

```
untouched:       iframes = [..., mapprojection, ...]   [map] log lines = 0
after selecting: iframes = [..., mapprojection, ...]   [map] log lines = 116
```

Golden Layout creates the iframe for every panel eagerly, but the map evaluates nothing until its
tab is shown. The detector proves itself in the same run: silent before, 116 lines after.

## Design

### Data in
`MapInputs` gains one field, built in `MapProjectionHost.inputs` beside `camera`:

```fsharp
cursor : aval<Option<V3d>>   // m.surfaceIntersection |> AVal.map (Option.map (fun s -> s.hitPoint))
```

The standalone app passes `AVal.constant None` (no 3D view). Optionally `--cursor x,y,z` later,
mirroring `--camera`, if a test wants the marker without a viewer.

### State
`MapProjectionModel` gains `follow : bool` (session-only, like the rest of the map state — it lives
on `Model`, never on `Scene`, so nothing is persisted and no scene version moves).
`MapProjectionAction` gains `SetFollow of bool`.

### Centre
The centre stays a plain model field; following is resolved **in the view**, not by messages:

```fsharp
let effectiveCenter =
    adaptive {
        let! follow = m.follow
        if not follow then return! m.center
        else
            let! cursor = inputs.cursor
            match cursor with
            | Some p -> let! kind = m.kind
                        let ll = Projection.lonLatR p
                        return Projection.forward kind ll.X ll.Y |> clampCenter kind
            | None -> return! m.center          // cursor off the surface: stay where we are
    }
```

used by `viewProj` and `unitsPerPixel`. No action is dispatched per pick, so there is no message
traffic and no re-entrancy with drag/zoom.

**Turning follow off keeps the view where it is:** the toolbar handler forces `effectiveCenter` and
sends `SetFollow false` together with that centre (`FitTo`-style), the same pattern *Zoom to data*
already uses — forcing in a UI callback is allowed.

**Dragging while following** switches follow off (a drag is an explicit "I want to steer"), again
resolved in `update`, where `DragStart` clears the flag. Wheel-zoom keeps following: zooming about
the pointer while the centre tracks the cursor is coherent.

### Marker
The cursor gets its own marker via `MapSg.cameraMarker` with a second colour (e.g. yellow-green),
so the camera and the cursor stay distinguishable. It is drawn regardless of `follow`, because
"where is my mouse on the map" is useful even without recentring.

## Why a closed panel cannot regress

Measured above, and structurally:

- `MapProjectionHost.inputs` — and therefore the new `cursor` aval — is built only in the
  `mapprojection` page route. A panel whose tab is not selected evaluates none of it (0 `[map]`
  lines on a 58-surface scene).
- Nothing is added to the viewer's update loop, scene graph, or main render control. The new field
  is one `AVal.map` over an existing model field, created inside `inputs`.
- The preview pick that produces `surfaceIntersection` **already runs** for its own reasons (the
  cursor panel and annotation drawing). Following it adds a reader, not a producer: no extra
  picking, no extra rays, no change to its cadence.
- `follow` lives on `Model`, not `Scene`: no persistence, no version bump, no compatibility risk.

Residual risk, bounded: a panel left *visible but behind another window* is still a live iframe. A
hidden iframe gets no rAF from the browser, so it cannot render; and the measured idle cost is 0
fps. Worst case is bounded by the same input rate as a pan.

## Testing ladder

1. **Expecto, CPU** (`MapProjectionMathTests`): `effectiveCentre`-style pure helper — following a
   body-fixed point yields the map-space point the projection puts it at; `None` keeps the previous
   centre; the centre is clamped; switching follow off preserves the current centre.
2. **Expecto, headless GPU** (`MapProjectionRenderTest`): with follow on and a cursor at a known
   lon/lat, that point lands at the centre pixel of the render, within a pixel.
3. **Perf**: extend `probe-map-recenter.ts` to drive the real follow path (cursor updates instead
   of drags) and confirm the numbers above hold; record them in `docs/MapProjectionView.md`.
4. **Playwright** (`map-projection.spec.ts`): in PRo3D, with the panel open and follow on, moving
   the mouse over the 3D view moves the map — assert the graticule shifts and the cursor marker
   stays at the centre; with follow off it does not move.
5. **Regression guard**: the background-tab probe becomes a spec — with the panel present but not
   selected, the PRo3D log has no `[map]` lines after the scene settles.

## Effort

Half a day for the feature (inputs field, model flag, toolbar toggle, effective centre, cursor
marker), plus half a day for rungs 1, 2, 4 and 5 and the docs page.

## Not in scope

- Following the **camera** instead of the cursor (trivial variant once this exists: same centre
  resolution, different source).
- Rover-scale zoom. Following the cursor invites zooming deeper, which runs into the float32 body
  positions (0.25 m grid at Mars radius, ~1 px at the current 32768 cap). That needs the per-patch
  double anchor described in `docs/MapProjectionView.md`, and is a separate piece of work.
