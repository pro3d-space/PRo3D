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
- Hover state is transient (not persisted); hover picks run on a background worker, latest wins; the
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
  → `ImageInspector.hoverSg`. Every move is sent; `ImageInspector.HoverWorker` picks off the UI
  thread, latest wins, and posts `HoverResult` back through the mailbox; stale results (the
  pointer moved on or left) are dropped by request number. A client-side throttle tried
  earlier lost the final move and was removed.
- 3D → 2D: `m.surfaceIntersection` (the existing preview pick) projected into the image → cyan
  crosshair.

- Zoom/pan: transient `Model.imageView` (centre + scale over projector NDC) drives the panel's
  ortho camera; pointer positions arrive as panel NDC and go through `ImageView.toImage`.

Open in the spike: nearest-neighbour sampling when zoomed in; measurement clicks still pick on the UI thread;
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
- Not yet: persistence of the measurement itself, bowl correction, uncertainty from the solar
  disc.

### First real test: Didymos (2026-10-09)

Scene `workshop3/didyShadow.pro3d`, frame `AFC1_COP_20270205_170000` (rendered by
`simulate-image` from the same OPC, `Didymos_SK_OPC__texture/Didymos_ASPECT_texture`,
`DIDYMOS_FIXED`). Re-rendering it with today's tool reproduces it (r = 0.91, same shadows,
phase 61.5° vs. our 61.0°): the sun direction is consistent.

- The crater sits next to the terminator. With **radial** up the sun is 0–1° above the horizon
  and depths came out near zero (1.4 m, 0.04 m) against ~9 m of mesh relief. The local surface
  there tilts **25.8°** from radial; above the **local** horizon the sun is 5.9° and the depth
  6.0 m (±0.19 m/px), with the triangulated tip 0.77 m from the mesh — below a pixel.
  → "up" is the dominant error on small bodies; local up is now the default, radial a toggle.
- **Verified with SPICE alone.** PRo3D's own pixel rays, intersected with the Didymos DSK
  (`dskxv`, same shape model as the OPC) and lit/shadowed by `illumf` (its own sun, DSK ray-cast):
  DSK intercepts match PRo3D's kd-tree hits to 1.4 cm (median); PRo3D's kd-tree + sun occlusion
  agrees with `illumf` on 99.4 % of 2422 pixels. Replaying the measurement with ideal clicks
  (rim = last lit, tip = last shadowed pixel per column, 77 columns): mesh offset median 0.22 m
  at 1.56 m pixels. The chain is right.
- **simulate-image's crater shadow agrees with SPICE.** It has shadow-map acne on a grazing slope
  (extra shadow pixels a median 28 px away from any SPICE shadow) -- a renderer artefact, not the
  crater. A first "13° sun rotation" fit was explaining that acne and was wrong.
- The ray checks (green: the sun ray from the rim to the mesh; white: the first terrain towards
  the sun from the tip) were dropped: at grazing sun the ray barely clears the terrain and the
  first hit lands up to 50 m away even with ideal clicks. The **mesh offset** is the robust check
  (ideal clicks 0.0-0.2 px, off-edge clicks 0.9-1.2 px; warning above 0.5 px).
- **Click placement dominates at grazing light.** Clicks 3 px off the edges moved the depth from
  4.4 m to 3.3-6.3 m. The rim is a gradual brightness ramp (~8 px) as the rounded crest turns
  away; its casting point is the ramp's dark end, not its middle. **Edge snapping** along the sun
  line (rim: where the ramp reaches the dark level; shadow end: halfway across the sharp cast
  edge) brought the same clicks to 4.1-4.6 m.
- In planned Hera frames the sun line is always vertical: the attitude keeps the sun in the
  spacecraft x-z plane (sun y-component <= 0.0012 in every sidecar checked).
- UI bug found on the way: a newline inside a text/attribute value froze the whole panel's
  incremental update. Never put `\n` into DOM text or attributes.

Kinds: crater (anchor = rim, triangulate the tip away from the sun) and boulder (anchor = shadow
tip on the ground, triangulate the top towards the sun). Up: local = plane by Newell's method
through mesh hits of a 24 px ring around the anchor pixel; radial = from the body centre;
plane = Newell's method through the anchor and >= 2 further clicked points, in angular order.

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
