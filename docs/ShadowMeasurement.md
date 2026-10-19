# Shadow Measurement (experimental)

Measures the **depth of a crater** or the **height of a boulder** from the length of its shadow in
a projected instrument image:

> height = shadow length × tan(sun elevation)

The sun direction comes from SPICE at the image's acquisition time, the geometry from the image's
camera and the surface mesh. The tool lives in the [Image Inspector](ImageInspector.md) panel.
The design and its verification are in [dev/shadowEstimation.md](dev/shadowEstimation.md).

Why from the image and not from the mesh: a shadow shows relief the mesh may not resolve at all —
a boulder smaller than the mesh's triangles, a crater the shape model smoothed out. Only one end of
the measurement is taken from the mesh (the end on terrain a coarse mesh gets right); the other is
triangulated from the image alone.

## What you need

- A surface (OPC) **bound to a SPICE body**, and an **observed body** set (*GIS View*).
- **SPICE kernels** covering the image's time.
- A **projected image** with its `.mbi.json` sidecar (the acquisition time comes from there),
  **selected** under *GIS View → projected images*.
- The **Image Inspector** panel, switched on: it is part of the **GIS** window layout, or add it to
  any layout. It opens behind an *experimental feature* notice; click **Open anyway**.

If something is missing, the panel's bottom bar says what: *no image selected*, *no observed body
set*, *no surface bound to a SPICE body*, *the image has no mbi sidecar*, *the image's projector
did not resolve (SPICE coverage?)*.

Good images have the sun **low** (long shadows) but not grazing, and a **phase angle well above
15°** (camera and sun rays far from parallel).

## Measuring a crater's depth

1. Zoom in on the crater (mouse wheel; drag pans). Light borders appear around image pixels when
   zoomed in far enough — that is the precision of a click.
2. Click **Measure shadow** in the bottom bar. The button next to it reads **Crater depth**.
3. **First click: the rim, where the shadow begins.** With **Snap** on (the default) the click
   moves along the sun direction onto the rim's dark end (see [Snapping](#snapping)). An orange
   dot marks it, and a dashed yellow **sun line** appears: where the shadow can lie.
4. **Second click: the end of the shadow**, on the crater floor. The click moves onto the sun
   line, then snaps onto the shadow's edge (magenta dot).
5. Read the result in the **result box** at the panel's top-left. The next click starts a new
   measurement; **Clear** discards the current one.

## Measuring a boulder's height

1. Click **Crater depth** to switch it to **Boulder height**.
2. **First click: the tip of the boulder's shadow, on the ground.** It snaps onto the shadow's
   end. The sun line now points from there towards the sun.
3. **Second click: the boulder's top edge** that casts the shadow tip. It moves onto the sun line
   but is not snapped — there is no reliable image edge at a boulder's top.

The boulder itself need not exist in the mesh: its top is triangulated from the image.

## Reading the result

| Row | Meaning |
|---|---|
| **Depth** / **Height** | below the rim / above the ground, in metres |
| per pixel | how much the result changes when the second click moves one pixel along the sun line — your resolution |
| shadow | horizontal length of the shadow |
| sun (up: …) | sun elevation above the horizon of the chosen [up](#which-up) |
| sun (radial up), up tilt from radial | the same for the radial horizon, and how far the chosen up tilts from it |
| phase | angle between sun and camera; below 15° the box warns that the result is unreliable |
| pixel on ground | the image's ground resolution at the second point |
| snapped | how far snapping moved the two clicks, in pixels |
| mesh offset | distance between the triangulated point and the mesh under the second pixel |

**The mesh offset is the quality check.** Where the mesh resolves the crater, a correct
measurement lands on it: the offset stays well below a pixel (on a test frame: median 0.2 m at
1.6 m pixels). Above 0.5 px the box warns that the clicks are probably not on the shadow's edges.
For a boulder the mesh may not contain it — a large offset there is expected.

## Which "up"

Depth and height depend directly on the sun's elevation above *some* horizon. On a small body the
radial direction can tilt far from the local surface (25° and more on Didymos), and near the
terminator the sun can sit almost on the radial horizon: radial depths then come out near zero.
The **Up** button cycles:

- **Up: local** (default) — the surrounding terrain: a plane through the mesh under a ring of
  pixels 24 px around the first click.
- **Up: radial** — from the body centre.
- **Up: plane** — your own reference plane: after the second click, click two or more points on
  the crater rim (or on the ground around a boulder; blue dots). The result updates with every
  point; **Clear** starts over.

Switching up re-measures the current clicks. If the box shows *the sun is … below this horizon*,
try another up.

## Snapping

**Snap** moves a click along the sun line onto the nearest shadow edge in the image, within 6 px,
to sub-pixel precision.

- A **crater rim** is not an edge but a ramp: the brightness falls over several pixels as the
  rounded crest turns away from the sun. The point that casts the shadow is where it has become
  dark — the snap lands there.
- A **shadow's end** is a sharp edge; the snap lands halfway between lit and dark.

At grazing light this matters: on a rendered Didymos frame, clicks 3 px off the edges moved the
depth by up to 3 m without snapping and by under 0.5 m with it. Without Snap, click at the dark end
of the rim's ramp.

## In the 3D view

The measurement is drawn as real, depth-tested geometry:

- a small axis cross at each end in the local surface frame (red = horizontal towards the sun,
  green = across, blue = up),
- yellow: the sun ray between the two ends,
- cyan: the depth or height, vertical from the lower end,
- gray: the mesh under the second click (compare with the triangulated point).

The markers show only while **Measure shadow** is on and the measured image is selected.

**Create scale bar** puts the result into the scene as two ordinary scale bars, as tall as the
result and oriented along the camera's sky: one at each end. These are the only thing the tool
saves with the scene.

## Tips and pitfalls

- **Zoom in.** With the sun a few degrees up, one pixel along the shadow is ~10× that in depth;
  check *per pixel*.
- **Keep Snap on** and watch the *mesh offset*.
- **Near the terminator, avoid radial up.**
- **A crater's depth is to the shadow tip**, not necessarily to the deepest point: a shadow that
  ends before the centre underestimates a bowl-shaped crater.
- **The sun is treated as a point;** at very low sun the shadow's edge blurs.
- **Low phase** (< 15°): shadows are short and seen nearly end-on; do not trust the result.
- **Simulated test images** from `pro3d-tool simulate-image`: render them with
  `--shadow-bias 0.006`; the default 0.002 produces false shadow speckles (acne) on grazing
  slopes.

If a click fails, the bottom bar says why, for example *that pixel does not hit the surface*,
*the tip must lie on the shadow side of the rim*, or *the top must lie towards the sun from the
shadow tip* (the clicks are in the wrong order, or crater/boulder is set wrong).

## Limits

- Experimental. The measurement itself is not saved; create scale bars to keep it.
- A measurement belongs to the image it was made on: selecting another image hides it, and the
  next click starts a new one. Its 3D markers are placed for the scene time it was made at; after
  changing the scene time, measure again.
- *per pixel* and *pixel on ground* assume square image pixels (true for Hera AFC).
- The scale bars follow the camera's sky at the moment they are created, not the up used for the
  measurement.
- The projection surface is the first surface bound to a SPICE body.
