# Multi-Image Projection (GIS tab)

Project an **ordered stack of up to 32 same-instrument images** onto a surface
at once — e.g. a flyby sequence over an asteroid. Lives in the GIS tab under
*Projected Images*. (The original requirements/plan: this page's git history
and [plans/multiImageProjection.md](../plans/multiImageProjection.md).)

## Workflow

1. **Import Directory** loads every image of a folder into the **library**
   (`.tif/.tiff/.png/.jpg/.jpeg/.exr`, each with its `.mbi.json` sidecar).
   The library is browse-oriented: sortable by observation date and distance,
   with a curated header per image (file name, instrument/camera, distance,
   observation date). Sorting the library never changes projection order.
2. **Hovering** a library or stack row live-previews that image in 3D as if it
   were on top of the stack, and shows its **footprint**: a green outline where
   the instrument frustum crosses the surface plus a frustum wireframe (cut at
   the target distance). Hovering an image already in the stack changes nothing
   in the rendering except the footprint highlight.
3. **+** in a row adds the image to the top of the **projection stack**
   (max 32); the panel above the library shows the stack top-first with
   ↑/↓ reorder, remove, and a count/cap indicator.
4. Where stacked images overlap, the image **higher in the stack wins
   outright** (painter's order, opaque). The global opacity slider blends the
   stack's result with the underlying surface texture.
5. The **fly-to** button (paper plane) animates the camera onto that image's
   projector axis — forward along the boresight, standing off far enough to
   frame the instrument footprint.
6. *Visibility → RelativeCount* colors the surface by **how many stack layers
   cover each fragment** (blue = few … red = many).

Per-image display settings (channel, min/max, false color preview) keep
working through each row's edit panel; the colormap and false-color toggle
apply to the whole stack (same instrument, one legend). Settings
(opacity/visibility/lighting/orientation source/registration) and the selected
image's 2D preview fold away into collapsible sections to keep import, stack
and library in view.

## How it renders

- One `sampler2DArray` (layer *i* = stack entry *i*) plus fixed-size uniform
  arrays of projector matrices and per-layer min/max, and a count. 32 × M44f =
  2 KB, far below GL 4.1's guaranteed 16 KB uniform-block minimum — **no
  storage buffers**, so the same shader runs everywhere including macOS
  (GL 4.1); the former `limitedShaderCapabilities` platform split is gone.
- Each layer's projector is computed by SPICE **in the surface's body-fixed
  reference frame at that image's own observation time**, so projections stick
  to the terrain regardless of the scene's current time. Projector matrices
  are memoized per (image, method, boresight, observer, frame) — SPICE is
  single-threaded, and reordering or hovering recomputes nothing.
- The fragment loop walks the stack top-down and stops at the first layer that
  covers the fragment with a projector-facing normal (early-out; back-facing
  and out-of-frustum fragments stay untouched).
- The texture array reallocates only when the layer set outgrows its
  allocation; reordering just re-uploads changed slices. Decoded bands are
  cached per (path, channel).

## Data expectations

Images are associated with their `.mbi.json` sidecars by the band file names
the sidecar declares, with a `<image>.mbi.json` naming fallback. The
observation time comes from `DATE-OBS`, falling back to the file-name
timestamp (`_yyyyMMdd_HHmmss_`) and then `DATE`; position vectors declared in
km are auto-corrected when they are actually metres (detected via the sun
distance); the projection target body comes from the sidecar's `TARGET`
header. See [COP-sidecar-issues.md](COP-sidecar-issues.md) for the concrete
data defects these fallbacks absorb.

The projection surface must have a GIS **entity and reference frame assigned**
(GIS tab → Surfaces), and a SPICE kernel with coverage for the observation
epochs must be loaded.

## Limits and future work

- Stack cap: 32 (`ProjectedImages.maxCount`; the shader's `Arr<N<32>, _>` size
  must change with it).
- Stack, hover and per-image settings are **not persisted** in the scene yet.
- Reordering by drag & drop (the arrows are the minimal interface), persistent
  footprint toggles, and projector-side occlusion/self-shadowing are future
  work.
- First start after a release that changed the surface shaders recompiles the
  effect once (surfaces can take a minute or more to appear).
