# Image Inspector (experimental)

Shows the selected projected instrument image in its own panel and links it to the 3D view in
both directions, and hosts the [shadow measurement](ShadowMeasurement.md) tool. The design is in
[dev/shadowEstimation.md](dev/shadowEstimation.md).

## Opening it

The panel is part of the **GIS** window layout, and can be added to any layout as
**Image Inspector**. It shows the image selected in *GIS View → projected images*.
Without a selected image, an observed body, or a surface bound to a SPICE body, the panel says
which one is missing.

The panel opens behind a notice: *This is an experimental feature* with an **Open anyway** button.
Until then it does nothing (no texture, no rendering, no picking), so having it in a layout, or
opening it by accident, costs nothing.
**Close** in its bottom bar returns to the notice and drops its decoded images. This is not
saved; every session starts at the notice. Nothing of the panel is written to the scene —
only scale bars you create from a measurement, which are ordinary scale bars.

## What it shows

- **Hover the image** — the pixel under the pointer is sent through the image's camera, and where
  that ray meets the surface is marked in the main view: a small red/green/blue axis cross, plus a
  yellow stretch of the camera ray leading to it. The bottom line reads out the pixel
  (0-based, origin top-left) and whether the ray is on or off the surface.
- **Zoom and pan** — the mouse wheel zooms about the pointer (up to 64x; the pixel under the
  pointer stays put), dragging pans, double-click or *Reset* returns to the whole image.
  Zoomed in, image pixels show as flat squares with light borders (from about 5 screen pixels
  per image pixel) — the precision a click really has.
- **Hover the 3D view** — the surface point under the 3D cursor is projected into the image and
  marked with a cyan crosshair.

## Shadow measurement

**Measure shadow** in the bottom bar measures crater depths and boulder heights from shadows in
the image — see [ShadowMeasurement.md](ShadowMeasurement.md).

## What it proves — and what not

The image is drawn exactly the way the projection samples it, so the marker always lands on the
texture of the pixel you hover. That checks that **panel and projection agree** (orientation,
flips). It does **not** check that the projection matches reality: for that, compare the projected
texture with the mesh's own relief — boulders and rims should sit on the boulders and rims of the
geometry.

## Limits

- Experimental; nothing but a scale bar created from a measurement is saved with the scene.
- Hover picking runs on a background thread, newest position wins; the first hovers can lag
  while kd-trees load. Measurement clicks pick on the UI thread.
- The projection surface is the first surface bound to a SPICE body (the same choice as
  *fly to image*).
