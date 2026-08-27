# Multi-Image Projection (GIS tab) — Requirements

Projecting a single instrument image onto a surface works today (GIS tab →
*Projected Images*). Users need to project **multiple images at once**. This page
captures the agreed requirements; the implementation plan lives in
[plans/multiImageProjection.md](../plans/multiImageProjection.md).

## Scope and scale

- **Up to a few dozen images projected simultaneously** (e.g. a flyby sequence).
  All simultaneously projected images come from the **same instrument**, so they
  share dimensions and intrinsics.
- Rendering approach: **texture array + bounded uniform array of projector
  matrices**. Storage buffers are ruled out — macOS is capped at OpenGL 4.1
  (no SSBOs), and Vulkan/MoltenVK is no alternative because the OPC pipeline
  needs geometry shaders. A `sampler2DArray` and a fixed-size matrix array are
  core features well below GL 4.1, so the feature works on all platforms with a
  compile-time cap on stack size.

## Compositing

- The projected images form an **ordered stack**. Where images overlap, the
  image **higher in the stack wins outright** (painter's order, opaque
  overwrite — no inter-image blending).
- The **global opacity slider stays**, orthogonal to ordering: it blends the
  *result* of the stack with the underlying surface texture.
- Per-image display settings (channel, min/max, false color) keep working per
  image.

## UI: library + projection stack

- The imported image list becomes a **library**: sortable (date, distance),
  filterable, browse-oriented. Sorting the library never changes projection
  order.
- A separate **projection stack** lists only the projected images in draw
  order, with reordering and removal. Adding from the library puts the image on
  top of the stack.
- **Hovering a library image live-previews it in 3D as if it were on top of the
  stack** — this is the workflow for finding good images. No 2D thumbnail
  popup; the preview is the 3D view itself.
- Hovering an image that is **already in the stack** must react too: highlight
  its entry/projection and make clear it is already stacked (no duplicate
  preview entry).
- Each image gets a **fly-to button** that steers the camera to view that
  image's projected footprint on the surface.
- Each image shows **curated header fields** (obs date, instrument, distance to
  target, sun position, …) — not a raw metadata dump.
- UI space is constrained: **restructure the GIS tab** to reduce accordion
  nesting while adding the stack UI.

## Footprints

- On hover, show the image's footprint **as an outline on the surface** *and*
  as a **frustum wireframe in 3D** from the spacecraft position.
- Footprints are hover-only for now; a persistent per-image/global footprint
  toggle ("footprints showable in general") is a cheap follow-up and stays on
  the list as future work.

## Out of scope (for this effort)

- **Persistence** of the projected-image state in the scene file (today nothing
  survives save/load). Deliberately deferred to a separate task.
- Persistent (non-hover) footprint toggles — future work, see above.
- Occlusion / self-shadowing of projections (terrain between projector and
  surface) — unchanged from single-image behavior.
