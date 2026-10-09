# Shadow-based crater depth — design

Status: **design + experimental spike** (branch `features/shadow-measurement`, no issue yet).

> Original note: find crater depths using shadows on instrument images. The images are projected
> onto a rather low-quality mesh. Mark shadows in the instrument image, use tan and the sun
> incidence angle at the projected 3D location to compute the height, and create a scale bar
> there. Needs the selected instrument image in a new dockable window, "Instrument View".

## Idea

Crater depths from the shape model are unreliable where the mesh is coarse, but the instrument
images projected onto it are much sharper. Shadows in those images carry height information:

    depth ≈ L · tan(e)        e = sun elevation above the local horizontal = 90° − incidence
                              L = horizontal shadow length, measured along the sun azimuth

The user marks a shadow in the instrument image; PRo3D computes the depth with the sun direction
at the image's acquisition time and shows the result in 3D (e.g. as a scale bar).

## Key decisions

### 1. Measure in the image, not on the mesh

Clicking the shadow tip on the textured 3D surface picks the *mesh* point under the cursor. That
point is displaced by the mesh's height error (amplified by the viewing angle) — the very error
the method is meant to avoid. So a click is treated as a **pixel**, never as a surface point:

- the **rim point** comes from pixel → camera ray → mesh hit (the rim is where the mesh is best);
- the **shadow tip** is *triangulated*: the camera ray through the tip pixel intersected with the
  sun ray grazing the rim point. The mesh is not used at the tip at all.

Degenerate when camera and sun directions are nearly parallel (phase angle ≈ 0).

### 2. A 2D instrument view, not 3D-first

A 3D-first version would still need the full world ↔ pixel round trip and buys nothing but a
skipped panel — the cheap part. The 2D panel removes several problems at once:

- clicks hit the image's own pixels, not a texture draped over a bad mesh;
- no other projected image can be on top (the stack blends several);
- the sun ray from the rim is a plain 2D line in the image (its projection), and snapping the tip
  to it is nearest-point-on-a-line — after which the camera ray meets the sun ray *exactly*.

The 3D view shows the results: rim, triangulated tip, sun line, scale bar.

### 3. New tool, new persisted data

A new interaction (`MeasureShadow`) with its own model — **not** a new `Annotation.Geometry`
value: that enum is persisted per annotation and older releases would fail on an unknown value
(scene compatibility is absolute). Persist inputs under a new key — image id, rim pixel(s), tip
pixel, reference choice — and *recompute* results, so a projection fix (e.g. #801) corrects old
measurements. Optionally also emit an ordinary `ScaleBar` so older releases still show something.

## Hidden complexity (checklist)

1. **Image orientation.** The panel must show the image exactly as the projection samples it. A
   transpose/flip (cf. the AFC detector transpose in #801) makes every pixel → ray silently wrong.
2. **Sun at image time.** The sun direction comes from the image's acquisition time, never from
   the scene's current SPICE time.
3. **One image per measurement.** Bind to the image id; pixel → ray uses that image's camera.
4. **Local vertical.** On small irregular bodies "up" is ambiguous (radial, gravity, fitted
   plane). Default: plane fitted through a few rim clicks; make it explicit and selectable.
5. **Error budget.** Rim height still comes from the mesh; solar disc (≈0.25–0.5° at Didymos)
   blurs the edge at low sun; depth error ≈ GSD · tan(e); shadows not reaching the crater centre
   need a bowl-shape correction (Pike / Chappelow).

## Milestone 1 — round trip, no shadow logic

Prove that pixel ↔ world is right before measuring anything.

- **Instrument View** dock panel: the selected projected image, zoom/pan.
- **Hover in 2D → marker in 3D.** Every mouse move unprojects the hovered pixel through the
  image's camera and intersects the surfaces; a constant-screen-size marker sits at the hit, with
  a short segment of the camera ray leading to it. Marker changes colour / hides on a miss.
- **Hover in 3D → crosshair in 2D** (reverse direction), so both directions are checked live.
- Hover state is transient (not persisted); hover updates are throttled (one per frame); the
  marker is built in local space and placed with `Sg.trafo` (precision).

What it proves and what not: the marker always lands on the texture pixel being hovered, because
the texture uses the same projection. So it validates **panel ↔ projection** consistency (risk 1),
not projection ↔ reality. For the latter compare texture against the mesh's own relief (boulders,
rims) — e.g. with texturing off / shaded relief.

The ray segment also previews the measurement geometry: camera ray and sun ray meeting at the tip.

### Spike status (implemented)

`src/PRo3D.Viewer/Viewer/ImageInspector.fs`, user page [../ImageInspector.md](../ImageInspector.md).

- Panel id **`imageinspector`** ("Image Inspector") — `instrumentview` is already the View
  Planner's rover camera, so the name from the original note could not be reused.
- The panel draws a full-screen quad sampled at `tc = 0.5 + 0.5·ndc`, the projection shader's
  own convention, so **panel NDC = projector NDC**: the pointer fraction converts to the projector
  NDC directly, without any pixel convention in between. Pixels (`InstrumentObservation.ndcToPixel`,
  Image convention) are only a read-out.
- Projector: `Visualization.projectDirect` with the viewer's boresight composition, memoized
  per (image, method, boresight, observer, frame) like the projection stack. Surface placement
  `fullTrafo' * preTransform` takes body-fixed → render space; the projection surface is chosen as
  in `flyToImageCamera`.
- 2D → 3D: `ImageInspectorHover ndc` → ray → `Picking.pickRay` → `Model.imageHover` (transient)
  → `ImageInspector.hoverSg` in the depth-cleared overlay pass. Client throttles moves to ~30/s.
- 3D → 2D: `m.surfaceIntersection` (the existing preview pick) projected into the image → cyan
  crosshair.

- Zoom/pan: transient `Model.imageView` (centre + scale over projector NDC) drives the panel's
  ortho camera; pointer positions arrive as panel NDC and go through `ImageView.toImage`.

Open in the spike: nearest-neighbour sampling when zoomed in; picking on the UI thread (move to the background pick thread);
the projection-surface choice duplicates `flyToImageCamera` (extract one helper); the existing
"Selected Image" preview renders png/jpg white (`createInstrumentScene`), the inspector does not.

### Milestone 2 spike (implemented, first version)

- `ImageInspector.click`: rim click → mesh hit; `sunLine` projects the shadow ray
  (`rim − t·sun`, two points) into the image; the tip click is `snap`ped onto it and
  `triangulate`d (closest approach of camera ray and sun ray, `t > 0`).
- Sun: `InstrumentObservation.sunDirection` in the surface's frame at the image's `obs_date`,
  cached per (image, frame, body). Up: radial from the body-fixed origin.
- `measure`: depth, horizontal length, sun elevation, depth per pixel (re-triangulated one pixel
  further along the line), mesh hit under the tip.
- State `Model.shadowMeasure` is transient; **Create scale bar** emits an ordinary
  `ScaleVisualization` (Sky_planet, Pivot.Left, metres) — persisted the old way, so older releases
  show it.
- Not yet: plane fit for up, persistence of the measurement itself, `docs/ShadowMeasurement.md`
  split-out, bowl correction, uncertainty from the solar disc.

## Milestone 2 — measurement

- Rim click (pixel → mesh), sun line drawn in the panel from the rim along the projected sun ray.
- Tip click snapped to that line → triangulated tip. In 3D show both the mesh hit and the
  triangulated tip; their distance is a direct read of local mesh error.
- Reference plane from rim clicks; depth, sun elevation, uncertainty; scale bar in 3D.
- Persistence under a new key; `docs/ShadowMeasurement.md` user page.

## Related code

- `src/PRo3D.Base/InstrumentProjection.fs` — instrument cameras (frustums, look-at, boresight).
- `src/PRo3D.Core/ProjectedImageList-Model.fs` — `selectedImage`, stack.
- `src/PRo3D.Tool` `unproject` verb — existing pixel → surface code path (see
  `docs/Pro3DTool-Unproject.md`).
- `src/PRo3D.GIS/SunAngles.fs`, `PRo3D.SPICE` — sun direction.
- `src/PRo3D.Core/ScaleBarsApp.fs`, `src/PRo3D.Viewer/DockConfigs.fs`.
