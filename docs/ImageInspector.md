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
- **Zoom and pan** — the mouse wheel zooms about the pointer (up to 64x; the pixel under the
  pointer stays put), dragging pans, double-click or *Reset* returns to the whole image.
- **Hover the 3D view** — the surface point under the 3D cursor is projected into the image and
  marked with a cyan crosshair.

## Measuring a shadow (crater depth)

1. Click **Measure shadow** in the panel's bottom bar.
2. Click the crater **rim** where the shadow starts (orange ring). The pixel's ray meets the mesh
   there. A dashed yellow **sun line** appears: the direction a shadow cast from that rim point
   runs in this image, from the sun direction at the image's acquisition time.
3. Click the shadow **tip**. The click snaps onto the sun line (magenta ring).

The tip is not taken from the mesh: it is where the camera ray through the tip pixel meets the sun
ray grazing the rim. So a coarse mesh does not enter at the tip. The bottom bar shows

- **depth** below the rim, with how much it changes per pixel of tip position (± m/px),
- **shadow** length, horizontal,
- **sun** elevation above the local horizon,
- **mesh … off**: how far the mesh under the tip pixel is from the triangulated tip — a direct
  read of the mesh's local error.

"Up" is radial from the body centre. In 3D the measurement shows the rim (orange), the tip
(magenta), the sun ray between them (yellow), the depth (cyan) and the mesh point under the tip
(gray). **Create scale bar** places a vertical scale bar at the tip, as tall as the depth. A third
click starts a new measurement; **Clear** resets it. Clicks that end a drag are ignored.

Caveats: the depth is to the shadow tip, not necessarily the crater floor's deepest point; a
shadow that does not reach the centre underestimates bowl-shaped craters. The sun is treated as
a point; at low sun the edge of the shadow blurs.

## What it proves — and what not

The image is drawn exactly the way the projection samples it, so the marker always lands on the
texture of the pixel you hover. That checks that **panel and projection agree** (orientation,
flips). It does **not** check that the projection matches reality: for that, compare the projected
texture with the mesh's own relief — boulders and rims should sit on the boulders and rims of the
geometry.

## Limits

- Experimental; nothing but a created scale bar is saved with the scene.
- The scale bar's direction is the scene planet's up at the tip; with no planet set it is +Z.
- The image is sampled with linear filtering, so at high zoom pixel edges look soft.
- Picking happens on the UI thread; the first hover can stall while kd-trees load.
- The projection surface is the first surface bound to a SPICE body (the same choice as
  *fly to image*).
