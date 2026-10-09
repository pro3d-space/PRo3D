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
  (0-based, origin top-left) and whether the ray is on or off the surface.
- **Zoom and pan** — the mouse wheel zooms about the pointer (up to 64x; the pixel under the
  pointer stays put), dragging pans, double-click or *Reset* returns to the whole image.
  Zoomed in, image pixels show as flat squares with light borders (from about 5 screen pixels
  per image pixel) — the precision a click really has.
- **Hover the 3D view** — the surface point under the 3D cursor is projected into the image and
  marked with a cyan crosshair.

## Measuring a shadow: crater depth, boulder height

1. Click **Measure shadow** in the panel's bottom bar. **Crater depth / Boulder height** switches
   what is measured, **Up** what "up" is (below), **Snap** edge snapping (on by default).
2. First click, on terrain the mesh gets right:
   - crater: the **rim**, where the shadow begins;
   - boulder: the **tip of its shadow, on the ground**.

   With **Snap** the click moves along the sun direction onto the nearest shadow edge in the
   image (within 6 px, sub-pixel). At a crater rim the brightness falls gradually as the rounded
   crest turns away from the sun; the snap lands where it reaches the dark level — where the sun
   grazes the crest, the point that casts the shadow. A shadow's end is a sharp edge; there it
   lands halfway between lit and dark. On a rendered Didymos frame, clicks 3 px off either edge
   moved the depth by up to 3 m without snapping and by under 0.5 m with it. Its ray meets
   the mesh there (orange ring). A dashed yellow **sun line** appears: the sun ray from that
   point projected into the image, at the image's acquisition time — away from the sun for a
   crater, towards it for a boulder.
3. Second click: the **end of the shadow** (crater) or the boulder's **top edge** that casts it.
   The click moves onto the sun line (magenta ring); for a crater, Snap then moves it onto the
   shadow's end. A boulder's top edge is not snapped — there is no reliable image edge there.

The second point is not taken from the mesh: it is where the camera ray through its pixel meets
the sun ray. So a coarse mesh — which may not contain the boulder at all — does not enter there.
The bottom bar gives the result in one line; a **result box** at the panel's top-left lists

- **Depth** below the rim / **Height** above the ground, and **per pixel**: how much it changes
  when the second click moves one pixel along the sun line,
- **shadow**: horizontal length,
- **sun** elevation above the horizon of the chosen up, and above the radial one, and how far
  the chosen up tilts from radial,
- **phase** — below 15° camera and sun rays are nearly parallel and the box says the result is
  unreliable,
- **pixel on ground**: the image's ground resolution at the second point,
- **snapped**: how far snapping moved the two clicks,
- **mesh offset**: how far the mesh under the second pixel is from the triangulated point, in
  metres and pixels. When image and mesh agree and the clicks sit on the shadow's edges this is
  below a pixel (measured on a frame rendered from the same mesh: median 0.2 m at 1.6 m pixels);
  above 0.5 px the box warns that the clicks are probably off the edges.

A failed second click shows why in the bar and ends the attempt; the next click starts over.

### Which "up"

On a small body the radial direction can be tilted far from the local surface — on Didymos'
slopes by 25° and more — and depth or height depend directly on the sun's elevation above the
horizon used. Near the terminator the sun can sit almost on the radial horizon, and radial
depths then come out near zero.

- **Up: local** (default) — a plane through the mesh under a ring of pixels 24 px around the
  first click, i.e. the surrounding terrain.
- **Up: radial** — from the body centre.
- **Up: plane** — a plane through the first click and further clicked points: after the second
  click, click two or more points on the crater rim (or the ground around a boulder; blue
  rings). The result updates with every point; **Clear** starts over.

### 3D

Real geometry, depth-tested. Crosses in the local surface frame (red = horizontal towards the sun,
green = across, blue = up) at both ends; yellow = the sun ray between them; cyan = the depth or
height, vertical from the lower end; gray = the mesh under the second click. The hover marker uses
the instrument frame: red = image right, green = image up, yellow = the camera ray.

**Create scale bar** places two scale bars as tall as the result, along the camera's sky
(orientation *Sky*): one at the first click, one at the second. The lower end's bar points up to
the upper end's height, the upper end's bar reaches down to the lower end's.
**Clear** resets; clicks that end a drag are ignored.

Caveats: a crater's depth is to the shadow tip, not necessarily the floor's deepest point; a
shadow that does not reach the centre underestimates bowl-shaped craters. The sun is treated as
a point; at low sun the edge of the shadow blurs.

## Pitfalls

- **Grazing light magnifies click errors.** With the sun a few degrees up, one pixel along the
  shadow is ~10x that in depth. Zoom in, keep **Snap** on, and watch the *mesh offset*: well
  below a pixel means the clicks sit on the shadow's edges.
- **Near the terminator, radial up gives depths near zero.** The sun is then almost on the
  radial horizon; use **Up: local** or **Up: plane**.
- **A crater rim is a ramp, not an edge.** The brightness fades over several pixels as the
  crest turns away from the sun; the point that casts the shadow is where it has become dark.
  Snap takes care of this; without it, click at the dark end.
- **Simulated test frames** (`pro3d-tool simulate-image`) show shadow-map acne on grazing slopes
  with the default `--shadow-bias 0.002`; render them with `--shadow-bias 0.006`.

## What it proves — and what not

The image is drawn exactly the way the projection samples it, so the marker always lands on the
texture of the pixel you hover. That checks that **panel and projection agree** (orientation,
flips). It does **not** check that the projection matches reality: for that, compare the projected
texture with the mesh's own relief — boulders and rims should sit on the boulders and rims of the
geometry.

## Limits

- Experimental; nothing but a created scale bar is saved with the scene.
- The scale bars follow the camera's sky at the moment they are created, not the local up used
  for the measurement.
- Hover picking runs on a background thread, newest position wins; the first hovers can lag
  while kd-trees load. Measurement clicks pick on the UI thread.
- The projection surface is the first surface bound to a SPICE body (the same choice as
  *fly to image*).
