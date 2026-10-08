# Image Inspector (experimental)

Shows the selected projected instrument image in its own panel and links it to the 3D view in
both directions. It is the first step towards measuring crater depths from shadows; the design is
in [dev/shadowEstimation.md](dev/shadowEstimation.md).

## Opening it

The panel is part of the **GIS** window layout, and can be added to any layout as
**Image Inspector**. It shows the image selected in *GIS View → projected images*.
Without a selected image, an observed body, or a surface bound to a SPICE body, the panel says
which one is missing.

## What it shows

- **Hover the image** — the pixel under the pointer is sent through the image's camera, and where
  that ray meets the surface is marked in the main view: a small red/green/blue axis cross, plus a
  yellow stretch of the camera ray leading to it. The bottom line reads out the pixel
  (0-based, origin top-left) and whether the ray hit a surface.
- **Hover the 3D view** — the surface point under the 3D cursor is projected into the image and
  marked with a cyan crosshair.

## What it proves — and what not

The image is drawn exactly the way the projection samples it, so the marker always lands on the
texture of the pixel you hover. That checks that **panel and projection agree** (orientation,
flips). It does **not** check that the projection matches reality: for that, compare the projected
texture with the mesh's own relief — boulders and rims should sit on the boulders and rims of the
geometry.

## Limits

- Experimental; nothing is saved with the scene.
- No zoom/pan in the panel yet.
- Picking happens on the UI thread; the first hover can stall while kd-trees load.
- The projection surface is the first surface bound to a SPICE body (the same choice as
  *fly to image*).
