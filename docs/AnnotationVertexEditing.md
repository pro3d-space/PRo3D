# Annotation vertex editing

Move the individual points of an annotation after it has been drawn.

Until this feature, an annotation's points were fixed the moment you finished it. A vertex clicked
a few metres off meant deleting the whole annotation and drawing it again. This adds an editing
mode in which the points you originally clicked come back as handles you can pick up and put down
somewhere else on the surface.

This is the "move" half of [#639](https://github.com/pro3d-space/PRo3D/issues/639). Adding and
removing points build on the same machinery and are not implemented yet — see
[Not yet](#not-yet).

## Using it

1. Choose **Edit Annotation** (the green move icon) in the [tool strip](ToolStrip.md) on the right edge of the main view.
2. Select an annotation — Ctrl+click it in the 3D view, or pick it in the annotation tree. Its
   control points appear as white discs.
3. **Ctrl+click a handle** to pick it up. It turns green, and a green line runs from the cursor to
   its two neighbours showing where the annotation would go.
4. Move the cursor over the surface. The point follows the 3D cursor's live surface hit.
5. **Ctrl+click again** to put it down. The annotation's length, area and dip/strike are
   recomputed.

**Escape** at any time puts a picked-up point back where it was. **Ctrl+Z** undoes a completed
move.

The hint line in the top toolbar says which of the two states you are in.

### Notes

- **Ctrl is what separates picking from navigating**, here as everywhere else in PRo3D. Without it
  the mouse drives the camera. If you have the "invert drawing" toggle on, it is the other way
  round.
- **The 3D cursor is switched on for you** while you are in this mode, whatever the scene's
  "preview intersection" setting says — the drop needs a live surface hit. Your setting is read,
  never overwritten, and applies again as soon as you leave the mode.
- **Clicking an annotation's body selects it** instead of grabbing anything, so you can move from
  one annotation to the next without leaving the mode.
- **You have to move the cursor between picking a point up and putting it down.** A pick-up
  followed by an immediate click on the same spot leaves the point in hand.
- **Clicking where there is no surface does nothing** — the point stays in hand until you click
  somewhere the cursor actually finds ground. An annotation vertex can never end up off-surface.
- **A point may be moved onto a different surface** than the annotation was drawn on. This is
  allowed; a message appears at the top right saying which surface it landed on. The annotation
  keeps recording the surface it was drawn on.

### Which annotations can be edited

Points, lines, polylines and polygons.

Ellipses cannot. Their stored points are the *sampled outline* computed when the ellipse was
finished, not the two or four points you clicked to define it — there are no control points left
to move.

Dip-and-strike and true-thickness annotations are also excluded: their points mean "samples of a
fitted plane" rather than "corners of a polyline", so moving one individually is not a meaningful
operation.

## What happens on drop

The moved point is written into the annotation, and then:

- **The segments either side of it are re-sampled** onto the terrain, at the annotation's sampling
  distance. For a closed polygon, moving the first or last point also re-samples the segment that
  closes the ring.
- **Annotations that carry no segments keep none.** Every tool except the two ellipse ones defaults
  to `Projection.Linear`, which draws straight lines between points and generates no terrain
  samples at all. An edit does not silently promote such an annotation to a terrain-following one —
  it keeps the shape it was drawn with. (Set the projection to Viewpoint, Sky or Bookmark *before*
  drawing to get terrain-following segments.)
- **Measurements are recomputed** — length, area, height, dip and strike.
- **One undo entry is pushed** for the whole drop.

While a point is in hand nothing is written to the annotation at all, which is why cancelling is
instant and free.

## How it works

### Why the handles are drawn the way they are

Each handle is **six vertices expanded on the CPU** — two triangles at the same world position,
told apart by a corner offset that the vertex shader turns into a screen-space quad. That looks
roundabout for what is visually a dot, and the alternatives were each tried and rejected:

- **`gl_PointSize` + `gl_PointCoord`** depends on `GL_PROGRAM_POINT_SIZE`, and every point draw in
  the codebase that actually renders composes `DefaultSurfaces.pointSprite` alongside it.
- **`DefaultSurfaces.pointSprite`** cannot be used, because it does not carry `ObjId`/`SubId`
  through — and carrying those is the entire purpose of this draw.
- **A hand-written point→quad geometry shader** works in principle (`LineShader.thickLine` is one
  for lines) but adds a stage for no gain once the expansion is this cheap: 14 handles is 84
  vertices.

Two more constraints worth knowing before editing these shaders:

- The shader bodies are **written out inline** — no `[<ReflectedDefinition>]` helper, no
  module-level colour constants. FShade has to inline the whole body.
- The pick pass uses **one** fragment shader (`handlePickFragment`), not `handleFragment` followed
  by `Picking.pickVertexId`. Chaining two fragment shaders makes FShade carry the whole
  `HandleVertex` record — including its `[<Position>]` — between them.
- `uniform.ViewportSize` is **not** the pick target's size. The handles are drawn into two
  different targets, so the viewport is passed explicitly per pass as `HandleViewport`.
- `Id` and `SubId` are declared `Flat`: integers cannot be interpolated, and `Picking.SubVertex`
  already declared them that way.

### Handles and picking

PRo3D picks annotations by rendering them into an offscreen `Rgba32f` buffer with an object id in
the alpha channel, then downloading the single pixel under the cursor
(`PackedRendering.pickRenderTarget`). The handles ride in that same pass rather than in one of
their own:

| channel | meaning |
|---|---|
| alpha | packed object id — index into `PackedRendering.orderedAnnotations`. `-1` means nothing was hit. |
| red | control point index for a handle fragment, `-1` for everything else |

So one 1×1 download tells the readback both *which annotation* is under the cursor and *whether it
is one of its handles*. Red ≥ 0 is the whole discriminator. Green and blue still carry the
fragment colour, which nothing reads except the debug lens overlay in OpcViewer's
`AnnotationViewer`.

Handles are drawn last in the pick pass and with a larger depth offset than fills or lines, so
grabbing a control point beats picking the outline running through it. They are also fattened in
the pick pass only, the same forgiveness the line picking already applies.

### Why a grab needs a mouse move before it can be dropped

`Interactions.EditAnnotation` is the first mode with *both* pick systems live: annotation picking
(the offscreen id buffer) and surface picking (kd-tree rays, which produce the live 3D cursor). The
feature needs both — one to know which handle you clicked, the other to know where you are pointing.

The consequence is that the click which grabs a handle also reaches the surface, and would read as
a drop. Which of the two messages the update loop happens to process first would then decide the
outcome. `VertexGrab.movedSinceGrab` removes the ambiguity: a drop is only accepted once the
preview cursor has produced a fresh hit after the grab. Both orderings then behave identically, and
"move it before you drop it" is what the gesture means anyway.

### Model

`DrawingModel.vertexGrab : Option<VertexGrab>` holds the annotation id, the control point index,
the original position (so cancelling is free) and that `movedSinceGrab` flag. It lives on `Model`
rather than `Scene`, so it is session state and is never written to a scene file — no scene version
bump.

The *live* position is deliberately not in the model. It is read from `Model.surfaceIntersection`,
which the background picking thread already maintains; writing it into the drawing model on every
mouse move would dirty the model and rebuild the packed line geometry at cursor rate. Only the
small rubber-band scene graph depends on it.

## Not yet

- **Adding and removing points**, the rest of #639. Removing is straightforward on this base — drop
  the point, merge the two segments either side and re-sample. Adding needs picking a *segment*
  rather than a vertex, which means either a second sub-index in the pick buffer or a
  nearest-segment test on the CPU.
- **Editing an annotation while you are still drawing it.** Handles appear only on finished
  annotations. Backspace still removes the last point mid-draw.
- **Dragging** (press, move, release) rather than click-to-grab, click-to-drop.

## See also

- [AdvancedAnnotations.md](AdvancedAnnotations.md) — the annotation tools themselves
- [KdTrees.md](KdTrees.md) — the surface intersection the 3D cursor and the re-sampling both use
