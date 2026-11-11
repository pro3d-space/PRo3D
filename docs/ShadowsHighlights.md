# Shadows / Highlights

Brightens the darkest and darkens the brightest tones of a projected image, so that detail in
shadowed or overexposed parts of a frame becomes visible. Per image, on the 2D preview and on
the surface it is projected onto.

The method follows [LuciaKloos/PRo3D.ImageMapping](https://github.com/LuciaKloos/PRo3D.ImageMapping)
(branch `rgbRatiosComposite`); the controls are PRo3D's own.

## Use

*Projected Images* panel → *Selected Image* → *Properties*: below *Display*, two tables,
**Shadows** and **Highlights**, each with

| setting | range | what it does |
|---|---|---|
| **Amount** | 0–1, default 0 | how strongly the selected tones are pulled: shadows toward a brighter, highlights toward a darker gamma curve. 0 = off |
| **Tone** | 0–1, default 0.5 | which tones count: shadows from black up to *Tone* of the range, highlights from 1 − *Tone* up to white, with a soft edge |
| **Radius** | 0–100 px, default 30 | softens the selection across the image, so a dark area is lifted as a whole rather than pixel by pixel. 0 = sharp |

The undo icon in a table's header resets it. The settings belong to the image: every image in
the projection stack keeps its own, and the selected image's preview shows the result.

Shadows/highlights act on the image's normalised value (its *Display › Range*), before
the colour map. With **Transfer Function** off (*Projection Settings*) the projection shows the
image untouched, so the adjustment is not applied on the surface then.

## How it works

For each image with an amount above 0:

1. **Masks** — from the normalised value `t`: shadow mask `1 − smoothstep(0, tone, t)`,
   highlight mask `smoothstep(1 − tone, 1, t)`.
2. **Blur** — each mask through a separable 9-tap box blur (horizontal, then vertical) with
   taps `radius / 4` pixels apart.
3. **Adjust** — in the shader, `t` moves toward `t^2.8` by `highlightMask × amount` and toward
   `1 − (1 − t)^2.8` by `shadowMask × amount`.

Steps 1–2 run on the CPU, once per image, *Tone*, *Radius* and min/max, and are cached; the
preview and the surface sample the same mask, so they always agree. *Amount* is a shader
uniform: moving it recomputes nothing. A mask for a 1024² frame takes tens of milliseconds;
the busy indicator reads *adjusting shadows/highlights* meanwhile.

## Limitations

- Not saved with the scene, like the other per-image settings.
- The [Image Inspector](ImageInspector.md) shows the image without the adjustment.
- Single-band data only: the masks select on the band the colour map uses, not on the luminance
  of an RGB image.

## Code

| file | what |
|---|---|
| [`src/PRo3D.GIS/ShadowsHighlights.fs`](../src/PRo3D.GIS/ShadowsHighlights.fs) | masks, blur, cache, the shared `apply`, the stack's mask texture array |
| [`src/PRo3D.Core/ProjectedImageList-Model.fs`](../src/PRo3D.Core/ProjectedImageList-Model.fs) | `ToneAdjustment` (amount, tone, radius) on `ProjectedImageModel` |
| [`src/PRo3D.GIS/ProjectedImageApp.fs`](../src/PRo3D.GIS/ProjectedImageApp.fs) | the Properties tables and the preview shader |
| [`src/PRo3D.GIS/ImageProjection.fs`](../src/PRo3D.GIS/ImageProjection.fs) | the stack shader: per-layer amounts and mask slot |
| [`src/Tests/ShadowsHighlightsTest.fs`](../src/Tests/ShadowsHighlightsTest.fs) | masks, blur, adjustment, and the generated GLSL (no extension macOS lacks) |
