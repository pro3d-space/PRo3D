# Validating the image projection

Does a projected instrument image land exactly on the terrain, and if not, is
the fault in the projection or in the image's metadata?

This page answers that with a dataset we generate ourselves, so that "correct"
is something we can construct rather than assume. Everything here is
reproducible from [`pro3d-tool`](./Pro3DTool-SimulateImage.md) and the
Playwright harness in `tests-ui/`; the numbers are what those runs printed.

**Where things stand.** The projection *shader* is proven correct (step 1
below, to a mean of 0.077 DN). The production viewer is **not** yet validated,
and currently does not project our own dataset correctly. Two further things
make images look wrong independently of any of that:

1. **Orientation Source** defaults to *SPICE*, which reads no pointing from the
   image at all — it aims the camera at the body centre with a fixed roll. Real
   frames then land on the body with their features out of place.
2. A sidecar can state the pointing wrongly, and the projector will follow it
   faithfully. The HERA COP delivery does exactly this.

---

## The ladder

The projection is validated in steps, each with a number rather than an
impression, and each only meaningful once the one below it holds:

| step | what it isolates | status |
|---|---|---|
| **1** | the projection shader itself (`stableImageProjection`, offscreen — what `sun-angles` and `ProjectionTestbed` compose) | **passed**, below |
| 2 | the stack shader (`stableImageProjectionStack`), offscreen, same image and camera — must equal step 1 | **passed**, below |
| 3 | the production viewer, same image and camera — must equal step 2 | **passed**, below |

All three pass. Step 1 and 2 compare the render against the source image DN for
DN; step 3 cannot (the viewer has its own field of view and standoff) and
compares the projection against the terrain underneath it instead, which turns
out to be the sharpest test of the three.

Both prerequisites for step 3 are now in: a *Transfer Function* toggle (off =
the image's own RGB, so the render is comparable to the source rather than to a
colour-mapped version of it), and the viewer's missing `NormalFlip` binding.

## 1. The projection shader reproduces the image it was given

`pro3d-tool simulate-image --write-mbi` renders the body from SPICE and writes
an `.mbi.json` describing **the camera it actually used**.
`simulate-image --project` then feeds that image back through PRo3D's own
single-image projection shader, rendering from the same camera:

```
pro3d-tool simulate-image --opc <TestData>/HERA/Dimorphos \
    --project HERA_AFC_0005_20270304_140000_SIM.png \
    --body DIMORPHOS --frame DIMORPHOS_FIXED --observer DIMORPHOS \
    --out reprojected.png
```

The output must *be* the input. The transfer function is switched off for this
(`UseFalseColor` off, `DataType` float, range 0..1 makes `ColorMapping.remap`
the identity) and the exposure is pinned to 1, so the comparison is DN for DN
rather than through a colour map.

| source image | projected back onto the body | \|difference\|, stretched 0–8 DN |
|---|---|---|
| ![source](images/projectionValidation/step1-source.png) | ![reprojected](images/projectionValidation/step1-reprojected.png) | ![difference](images/projectionValidation/step1-diff.png) |

(AFC-1 frames are **1020 × 1020** — square, per `hera_afc_v06.ti`. These three
are the same square crop around the body, at the same scale.)

| | |
|---|---|
| body pixels rendered / source non-zero | 54,662 / 54,707 |
| mean \|ΔDN\| | **0.077** |
| median \|ΔDN\| | **0** |
| bit-identical pixels | **94.24%** |
| within ±1 DN | **99.49%** |
| worst | 51 DN |

The residual is the silhouette, not the geometry: of the 75 pixels past 4 DN
(0.137% of the body), **90.7% lie within 3 px of the limb**, where the source's
local gradient is **141 DN/px** against 4.5 DN/px over the body as a whole —
bilinear resampling across a hard edge, which no projection can avoid.

So the projection shader, the sidecar convention and the camera model are
correct together. Anything that misregisters downstream is introduced after
this point.

**What this step cannot show.** Source and reprojection share one camera, so a
wrong *absolute* roll would rotate both together and cancel. Roll is pinned
elsewhere: against the real ASPECT frame it comes out at **−2.03°** (IoU 93.9%),
and for AFC the same code lands within **11.9°** of the COP delivered frame at
the same epoch — different by an amount the delivery's own 34° body-orientation
error covers, and nowhere near the 90° a wrong `specialTrafos` entry would give.
An independent absolute-roll check for AFC would need a real AFC frame with
trustworthy metadata, which we do not have.

## 2. The stack shader agrees with it

The viewer does not render through `stableImageProjection`; it renders the
projection **stack**. `--project-shader stack` sends the same image, the same
projector and the same camera through `stableImageProjectionStack` as a
one-layer stack — the per-patch matrices coming from `projectionUniformMap`
exactly as in the viewer:

```
pro3d-tool simulate-image --opc <TestData>/HERA/Dimorphos     --project HERA_AFC_0005_20270304_140000_SIM.png --project-shader stack ...
```

| | |
|---|---|
| body pixels drawn, single / stack | 54,662 / **54,662** |
| drawn by one shader only | **0** |
| mean \|ΔDN\| between the two | 0.072 |
| identical pixels | 94.75% |

**Coverage is bit-for-bit the same** — not one pixel differs in which fragments
the two shaders accept — so the geometry, the projector composition and the
facing test agree exactly. The two paths are the same projection.

Where they differ in *value*, it is filtering, and the stack is the better of
the two. Against the source image:

| shader | mean \|ΔDN\| | identical | within ±1 DN |
|---|---|---|---|
| single-image | 0.0771 | 94.24% | 99.49% |
| **stack** | **0.0054** | **99.46%** | **100.00%** |

The 75 differing pixels (0.137%) sit exactly where step 1's residual sat: 90.7%
within 3 px of the limb, median source gradient 141 DN/px against 4.5 over the
body. The single-image path samples a mip-mapped `sampler2d`, the stack an
un-mipmapped `sampler2dArray`, so the single path blurs slightly where the
projected texel density falls below 1:1. Nothing here is geometric.

## 3. The viewer reproduces the terrain it projects onto

The viewer looks at the body through its own field of view from its own
standoff, so it cannot be compared with the source image DN for DN the way
steps 1 and 2 were. It can be compared with **the terrain underneath it** —
and that is the sharper test, provided both show the same thing.

They can be made to. `--texture-only` renders the OPC's own DRACO mosaic as this
camera sees it — no lighting, and no de-shading fit — and the surface is viewed
with DRACO as its primary texture, so the *same features* are on both sides and
a misregistration is feature doubling rather than a judgement call:

```
pro3d-tool simulate-image --opc <Dimorphos_0_Meridian> --time 2027-03-21T14:00:00Z     --texture-only --no-shadows --write-mbi ...
```

![the mosaic as the instrument sees it](images/projectionValidation/step3-source-texture.png)

Deliberately **not** `--deshade`: that fits a light direction (r = 0.38 here),
divides it out, clamps the result and falls back to a constant albedo where its
confidence drops — an approximation with no place in an image being used as
evidence. Nor plain shaded relief: this shape model is smooth, so an unlit
constant-albedo render is a featureless disk at any phase angle. The detail
lives in the texture, so the texture is what the reference image has to carry.

Camera on that image's own axis at 250 m, *Orientation Source* = MBI,
*Transfer Function* off:

| terrain (DRACO mosaic) | the image projected onto it |
|---|---|
| ![terrain, DRACO](images/projectionValidation/step3-terrain-draco.png) | ![the image projected](images/projectionValidation/step3-projected-draco.png) |

Over the mosaic the two are indistinguishable. Measured by cross-correlating the
high-pass filtered frames — which finds the shift that best aligns them, so a
misregistration would show as a non-zero peak:

| | |
|---|---|
| region | 600 × 630 px over the mosaic |
| best correlation | **1.0000** |
| at shift | **(0, 0) px** |
| mean \|ΔDN\|, 342,635 lit pixels | **0.000** |
| bit-identical | **99.99%** |

Zero shift, zero difference. The production viewer's projection is exact.

### Reading the right-hand side

Two things there are easy to mistake for defects.

**The flat bright area** is the de-shading falling back to a constant albedo
where the DRACO mosaic has no data: its confidence weight goes to zero and the
shader returns `AlbedoConst`. It is not lighting — `--ambient 1.0` removes the
lighting term outright (`iOverF = albedo`). The source image is measurably flat
there: std 16 DN, and stretching 152→240 DN reveals no structure.

**The smeared, stair-stepped structure** is because we are *not looking through
the AFC*. The camera is on the instrument's axis but at 250 m, where the
instrument was at 8.0 km. From 32× closer it sees terrain the AFC saw at grazing
incidence, or could not see at all behind ridges — so a handful of source texels
stretch across hundreds of screen pixels (the radial smearing) and the
projector's own horizon cuts across the relief (the hard edge).

The same effect is why a projection never repaints the *whole* visible body from
a close viewpoint. Measured on the same image: at the instrument's own pose the
tool covers **99.92%** of it (54,662 of 54,707 pixels); from 500 m, where the
camera's visible cap is 80°, **86.2%**; from 220 m, cap 67°, **92.1%**. Less
coverage when the camera sees more of the body, because the shortfall lives at
the limb. Nothing here is a projection error — it is the geometry of looking
from somewhere the instrument was not.

### A caution about the coverage number

The probe reports "% of the body repainted", which counts pixels whose colour
*changed*. This test deliberately makes the projection reproduce the terrain, so
almost nothing changes and it reports **22.8%** — its lowest reading yet, on its
best result. Low coverage here is evidence of good registration, not of a
failure. Coverage answers "did the projection land at all"; correlation answers
"did it land in the right place", and only the second one is meaningful once the
first is settled.

## Supporting evidence: a self-made AFC dataset

`pro3d-tool simulate-image --write-mbi` renders the body from SPICE and writes
an `.mbi.json` describing **the camera it actually used**. Project that image
back onto the same shape model and it must land on itself — there is no
metadata to doubt, because the render and the sidecar are the same camera.

Eight AFC-1 frames of Dimorphos are committed to the test data repository as
`HERA/SimulatedAFC` (see the README there), rendered against the OPC sitting
next to them so the set is self-contained:

![a frame from the simulated AFC dataset](images/projectionValidation/afcset-frame.png)

Three independent checks, all on the committed files:

| check | result |
|---|---|
| sidecar read back through `Visualization.projectDirect` vs the render camera | boresight **0.000000°**, worst frustum corner **0.000 px** (all 8) |
| `unproject --method mbi`, 5 pixels per frame | **40 of 40** hit the shape model |
| `TRG_POS` transformed into the spacecraft frame | `(0.00225, 0.00118, 0.999997)` — +Z, as the convention requires |

The residual `(0.00225, 0.00118)` is not error: it is AFC-1's real 0.145°
offset from the direction the spacecraft tracks.

### In the viewer

The same dataset on an Earth-textured OPC, camera on the image's axis at 220 m
(this OPC ships no DRACO mosaic, hence the placeholder colouring):

| terrain only | the frame projected onto it |
|---|---|
| ![terrain, nothing projected](images/projectionValidation/viewer-closeup-terrain.png) | ![the frame projected, raw RGB](images/projectionValidation/viewer-closeup-rawrgb.png) |

| | body repainted | painted off the body |
|---|---|---|
| before the `NormalFlip` fix, 500 m | 17.1% | 0.01% |
| after, 500 m | 86.2% | 0.08% |
| after, 220 m | 92.1% | 0.23% |

## Supporting evidence: real data, ASPECT at Didymos

The self-made set proves the chain is self-consistent. A *real* observation
proves the convention is the one real deliveries use.

`simulate-image --mbi` takes the camera from an existing image's own sidecar —
through the viewer's projection code, not a look-at — and renders with it. If
the convention were misread, the render would not line up with the image it
came from:

```
pro3d-tool simulate-image --opc <Didymos_ASPECT> \
    --mbi "ASP_000000_270323T060000_2B_NIR1_0.tif" \
    --body DIDYMOS --frame DIDYMOS_FIXED --observer DIDYMOS --write-mbi
```

| the real ASPECT frame | rendered through *its own* sidecar |
|---|---|
| ![real ASPECT frame](images/projectionValidation/aspect-real.png) | ![simulated from the ASPECT sidecar](images/projectionValidation/aspect-simulated.png) |

Same place, same size, same orientation. (Dimorphos is missing on the right
only because that OPC contains Didymos alone.) Both files are in the test data
repository under `HERA/Instrument Data`; the sidecar is used exactly as it
ships.

## The setting that decides whether any of this is used

*Projection Settings → Orientation Source*:

| mode | where the pointing comes from |
|---|---|
| **SPICE** (default) | Nowhere in the image. The spacecraft position is SPICE's, but the camera is then aimed at the **body centre** with a fixed up-vector — `InstrumentProjection.getLookAt` computes an attitude and discards it. Every image is painted as if shot dead-centre with one roll. |
| **MBI** | The image's measured attitude (`SC_QUAT`) and range (`TRG_POS`). Correct when the sidecar is, and faithfully wrong when it is not. |

So the symptom tells you where to look: *"lands on the body but the features do
not line up"* is the first mode; *"does not land at all"* is the second plus a
sidecar problem.

Measured in the viewer, one image in the stack, same scene and camera
throughout:

| | **SPICE** (default) | **MBI** |
|---|---|---|
| COP frame, as delivered | ![COP with SPICE](images/projectionValidation/viewer-cop-spice.png)<br>87.9% of the body repainted — but offset | ![COP with MBI](images/projectionValidation/viewer-cop-mbi.png)<br>**0.0% — misses entirely** |

`pro3d-tool unproject` says the same without a GPU: that frame's centre pixel
finds no surface with `--method mbi`, and hits at 8557 m with `--method spice`.

## The HERA COP delivery

Its sidecars state the pointing three ways wrong at once — each documented with
its evidence and its fix in
[COP-sidecar-issues.md](./COP-sidecar-issues.md#pointing--issues-5-6-and-7):

| | field | delivered | should be |
|---|---|---|---|
| 5 | `SC_QUAT0..3` | J2000 → spacecraft | spacecraft → J2000 (the conjugate) |
| 6 | `TRG_POSX/Y/Z` | the camera's location | target **minus** spacecraft |
| 7 | `TRG_POSX/Y/Z` | measured from Didymos | measured from `TARGET` (Dimorphos) |

Correcting all three by hand makes the centre pixel land on Dimorphos at
8561.9 m, 0.15° from the delivery's own ground-truth boresight. Correcting only
5 and 6 still misses.

### The images themselves do not match our shape model either

Rendering the same epoch with `simulate-image --no-lighting` (a flat disk, so
the image *is* the silhouette) and overlaying the outlines — red = delivered,
green = ours:

![silhouette overlay, delivered vs simulated](images/projectionValidation/afc-overlay.png)

| | |
|---|---|
| centroid offset | **1.80 px** of 1020 — the boresight and ephemeris agree to ~0.01° |
| silhouette area | ours **1.149×** larger |
| principal-axis roll | **+11.9°** |
| best fit allowing rotation **and** scale | IoU 90.4% at −12°, scale 0.94 |

A pure roll does not explain it: rotating our silhouette to best fit only
raises IoU from 84.5% to 85.5%. The outlines are genuinely different shapes, so
the body is presented differently — and indeed, checked against the delivery's
own `PRo3D.json`, its per-image body **orientation** disagrees with SPICE's
body-fixed frames:

| epoch | Didymos vs `DIDYMOS_FIXED` | Dimorphos vs `DIMORPHOS_FIXED` |
|---|---|---|
| 2027-02-05T01:00 | 6.2° | 34.6° |
| 2027-03-01T04:00 | 15.6° | 34.3° |
| 2027-04-30T01:00 | 2.2° | 13.9° |

Body *positions* agree with SPICE to about a metre, so this is orientation
only, and it is not a constant frame offset or a single clock error. Our two
Dimorphos OPCs agree with each other, which puts the disagreement between the
delivery and SPICE rather than between our shape models.

These frames were produced through PRo3D's own sequenced-bookmark rendering
([PR #716](https://github.com/pro3d-space/PRo3D/pull/716)), which is the first
place to look for how the body orientation was set per snapshot. Not chased
further here.

## What was fixed along the way

- **The transfer function was not a property.** `UseFalseColor` was bound in
  `ColorMapping.fs` as `p.colorMapping |> AVal.map Option.isSome`, and
  `getProjectionVisualizationProperties` always supplies a colour map for the
  selected image — so it was effectively always on. Now a real
  `useTransferFunction` flag on `ProjectedImageListModel`, with a checkbox in
  *Projection Settings*; off paints the layer's own RGB, skipping both the
  min/max remap and the colour map. Instrument data still defaults to the
  transfer function; an RGB image does not need it, and a projection cannot be
  *checked* against its source through a colour map.
- **The viewer never bound `NormalFlip`.** The offscreen tools estimate each
  dataset's winding and bind it; the viewer did not, and worked around it for
  *lighting* by orienting the face normal toward the viewer. That cannot work
  for the projection, whose facing test is relative to the **projector**. On an
  inward-wound OPC the test was inverted and the projection survived only near
  the limb. The heuristic now lives in `PRo3D.Core.NormalWinding.estimate` (one
  implementation for the tools and the viewer), `Surface.Sg` binds it per
  hierarchy, and the surface effect composes `applyNormalFlip`. Measured:
  `Dimorphos_0_Meridian` votes 98 outward (flip 0), the test-repo
  `HERA/Dimorphos` 26 inward (flip 1).

  This governed *coverage*, not alignment — it explained a crescent, not a shift.

- **The fly-to landed the camera in the right place pointing the wrong way.**
  *Fly to this image* (the location arrow on an image's row) put the camera at
  exactly the instrument's position — position residual 0 m against the
  sidecar — and aimed it 180 degrees away, into empty space. The frame came
  back entirely empty, which is indistinguishable from a broken projection, and
  it cost most of a day: every check on the *position* passed, because at the
  instrument's own focal length `standoff` equals `pc.distance`, so the sign
  error cancels out of `posB = projPos + fwd * (distance - standoff)` and
  survives only in the orientation.

  The camera handed to the animation was correct — logged
  `dot(boresight, direction-to-body) = 1.0000` — so the mangling was in
  `CameraAnimations.animateForwardAndLocation`, the deprecated animation path
  (`transformLocationForwardUp` rotates forward and up out of the state it is
  given while setting the location absolutely). The target up here is the
  instrument's, which through the improper mounting comes out nearly opposite
  the current one. The fly-to now **sets** the camera instead of animating it:
  landing correctly matters more than the 3.5 s glide.

  Measured before/after at the AFC's 8.041 km, viewer fov set to AFC-1's
  5.5307 degrees: lit fraction 0.00% → 3.23%, matching a reference render
  whose camera was written straight into the scene file (3.23%); apparent body
  width 22.6% of frame width against 22.8% predicted from the sidecar's range.
  `tests-ui/tests/looking-at-dimorphos.spec.ts` asserts exactly that, so this
  cannot regress silently.

  Three plausible-looking fixes were falsified before this one, each by
  measurement rather than inspection: correcting the sign where the axis is
  extracted (`camToBody.TransformDir(-V3d.OOI)`), correcting it again in render
  space, and seeding the animation state with the current camera. All three
  were no-ops — the bearing never moved — which is what finally pointed at
  the animation rather than at the pose.

## Still open

Nothing in the ladder. The projection is correct in the shader (step 1), in the
stack path (step 2) and in the production viewer (step 3).

## Reproducing this page

```
# 1: the self-made dataset (already committed to the test data repo)
pro3d-tool simulate-image --opc <TestData>/HERA/Dimorphos \
    --time 2027-03-04T14:00:00Z --body DIMORPHOS --frame DIMORPHOS_FIXED \
    --observer HERA --instrument HERA_AFC-1 --gain 3.7 \
    --out HERA_AFC_0005_20270304_140000_SIM.png --write-mbi

pro3d-tool unproject --opc <TestData>/HERA/Dimorphos \
    --images <TestData>/HERA/SimulatedAFC --input pixels.csv \
    --body DIMORPHOS --frame DIMORPHOS_FIXED --observer DIMORPHOS --method mbi

# 2: a real ASPECT observation through its own sidecar
pro3d-tool simulate-image --opc <TestData>/HERA/Didymos_ASPECT --mbi <the .tif> \
    --body DIDYMOS --frame DIDYMOS_FIXED --observer DIDYMOS --write-mbi

# 1 (viewer half): asserts; needs a scene on the same OPC
cd tests-ui && PRO3D_SIM_IMAGE_DIR=<dataset> npx playwright test projection-overlap

# the gate the projection tests stand on: can the viewer be put where the
# instrument was, and is the body then the right apparent size?
PRO3D_AFC_DIR=<folder> npx playwright test looking-at-dimorphos

# 3: look at one case, including the ones that fail (asserts nothing).
# Screenshots and logs lit-fraction after EVERY stage -- an empty frame three
# steps later cannot be attributed, and that is what made the fly-to bug expensive.
PRO3D_PROBE_IMAGE_DIR=<folder> PRO3D_PROBE_METHOD=MbiBased \
PRO3D_PROBE_LABEL=cop-mbi npx tsx src/probe-projection-landing.ts

# step 2: the same image and camera through the stack shader
pro3d-tool simulate-image --opc <opc> --project <image> --project-shader stack ...

# 4: flat silhouettes for an outline comparison
pro3d-tool simulate-image ... --no-lighting --no-shadows --out flat.png

# step 1: project an image back through PRo3D's single-image projection shader
pro3d-tool simulate-image --opc <opc> --project <image>     --body DIMORPHOS --frame DIMORPHOS_FIXED --observer DIMORPHOS     --out reprojected.png
```

The Playwright harness is machine-local by design (real GPU, local OPC and
image data); `tests-ui/src/pro3d.ts` lists the `PRO3D_*` variables.
