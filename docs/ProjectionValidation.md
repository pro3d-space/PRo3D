# Validating the image projection

Does a projected instrument image land exactly on the terrain, and if not, is
the fault in the projection or in the image's metadata?

This page answers that with a dataset we generate ourselves, so that "correct"
is something we can construct rather than assume. Everything here is
reproducible from [`pro3d-tool`](./Pro3DTool-SimulateImage.md) and the
Playwright harness in `tests-ui/`; the numbers are what those runs printed.

**Where things stand.** All three rungs pass, the production viewer included.
Projecting an AFC frame back onto the shape model it was rendered against, from
that frame's own camera, reproduces the frame **at zero pointing offset** in all
three — see [the proof](#the-proof) below.

Things that still make images look wrong independently of the projection itself:

1. **Orientation Source** set to *SPICE* reads no pointing from the image at all
   — it aims the camera at the body centre with a fixed roll, so real frames land
   on the body with their features out of place. It used to be the default, which
   made a correct projection look broken out of the box; **MBI** is the default
   now.
2. A sidecar can state the pointing wrongly, and the projector will follow it
   faithfully. The HERA COP delivery does exactly this.
3. **The scene has to be set up before any of it can work.** From an empty
   PRo3D, projection needs an observed body, a surface bound to a SPICE body,
   and a time inside the loaded kernels' coverage. PRo3D's default epoch is
   2025-03-10, where HERA has no ephemeris for Dimorphos — nothing resolves, and
   because SPICE calls serialise on a global lock the failing calls repeat every
   frame and the interface crawls. The fly-to now names whichever precondition
   is missing.
4. **The texture layer is not cosmetic.** `Earth` is a placeholder world map,
   and the two DRACO layers cover different hemispheres, so the wrong choice can
   leave a nearly black frame that any correlation will happily score well.
   See [the proof](#the-proof).

---

## The ladder

The projection is validated in steps, each with a number rather than an
impression, and each only meaningful once the one below it holds:

| step | what it isolates | status |
|---|---|---|
| **1** | the projection shader itself (`stableImageProjection`, offscreen — what `sun-angles` and `ProjectionTestbed` compose) | **passed**, below |
| 2 | the stack shader (`stableImageProjectionStack`), offscreen, same image and camera — must equal step 1 | **passed**, below |
| 3 | the production viewer, same image and camera — must equal step 2 | **passed**, below |

All three pass, and all three are now compared the same way: against the source
image itself.

Step 3 used to be excused from that ("the viewer has its own field of view and
standoff") and compared against the terrain underneath instead. It no longer
needs the excuse. Flying to the image puts the camera on the projector's own
axis, and setting *Focal (mm)* to the instrument's makes the viewer's field of
view the instrument's, so the render is the same gnomonic projection of the same
scene as the source frame — just on a different pixel grid. Resample by the
width ratio and the two must agree **at zero shift**. That is a stronger claim
than fitting each body's bounding box: a fit quietly absorbs a real pointing
error and then reports a meaningless few-pixel residual.

Prerequisites for step 3, all now in: the *Transfer Function* toggle (off = the
image's own RGB, so the render is comparable to the source rather than to a
colour-mapped version of it), the viewer's missing `NormalFlip` binding, a
fly-to that lands pointing at the body, and the projector matrix fix below.

<a name="the-proof"></a>
### The proof

One AFC-1 frame projected back onto the shape model it was rendered against,
from that frame's own camera. Every number below comes from a single run over a
single state, so the page cannot drift out of internal consistency:

- OPC `Dimorphos_opc` (the current export)
- texture layer **DRACO_2**, epoch **2027-03-21T20:00**, range 6 706.8 m
- viewer window 1100×1100, *Focal (mm)* 122.563 = AFC-1's 5.5307°
- *Orientation Source* MBI, *Transfer Function* off

| rung | correlation at zero shift | best shift found | mean ΔDN |
|---|---|---|---|
| **1** tool, single-image shader | **0.9999** | (0, 0) px | 0.0995 |
| **2** tool, stack shader | **0.9999** | (0, 0) px | 0.0077 |
| **3** viewer, production render | **0.9705** | (0, 0) px | — |

and for the viewer, the checks that a correlation alone cannot make:

| check | result |
|---|---|
| orientation — all eight square symmetries scored | **identity**, margin **0.8776** over the next best |
| coverage — from the shader's own coverage view | **99.2%** of 49 789 body pixels |
| silhouette overlap — lit frame | IoU **0.87**, centroid offset **6.0 m** |

Rungs 1 and 2, offscreen — source, single-image shader, stack shader:

![the tool reproduces the frame through both shaders](images/projectionValidation/ladder-tool.png)

Rung 3, the production viewer — source left, render right, same angular scale,
no fitting of any kind:

![the viewer reproduces the frame it projects](images/projectionValidation/ladder-viewer.png)

Silhouettes (yellow both, red source only, green render only) and the coverage
view (tint = how many stack layers cover a fragment):

| silhouette overlap | coverage |
|---|---|
| ![silhouettes](images/projectionValidation/ladder-silhouette.png) | ![coverage](images/projectionValidation/ladder-coverage.png) |

**Reading these honestly.** The registration and orientation numbers are
unambiguous: the shift search ranged ±24 px (±0.12°) and peaked at zero, and the
identity beat every flip and rotation by 0.88. The silhouette IoU is the weakest
of the four and should not be read as a 13% error: the source frame's unlit limb
is DN 0, indistinguishable from space, while the viewer's mask is "not the clear
colour" and so includes it. Across the four epochs in the dataset the disk fills
0.90–0.93 of its own bounding ellipse, which is the size of that bias. It is a
gate — *do the outlines coincide at all* — not a precision measurement.

Three choices in that setup are load-bearing:

- **DRACO_2, not DRACO_1 or Earth.** `Earth` is a placeholder world map. The two
  DRACO layers are different DART passes over different hemispheres, and at these
  epochs HERA looks at the DRACO_2 side. Measured at 20:00: DRACO_2 mean DN 5.80
  over 42 241 lit pixels, DRACO_1 mean 2.96 over 18 831, Earth mean 0.66 — two
  near-empty images correlate well and prove nothing.
- **A frame with texture, and a frame with light, for different checks.** The
  texture-only frame carries the surface detail the registration and orientation
  checks need; the lit frame fills the disk so its outline is the body's outline.
  Neither does both.
- **The viewer's field of view set to the instrument's**, and a squarish window.
  That is what makes the two frames the same gnomonic projection of the same
  scene, so they can be compared at zero shift instead of by fitting.

## 1. The projection shader reproduces the image it was given

`pro3d-tool simulate-image --write-mbi` renders the body from SPICE and writes
an `.mbi.json` describing **the camera it actually used**.
`simulate-image --project` then feeds that image back through PRo3D's own
single-image projection shader, rendering from the same camera:

```
pro3d-tool simulate-image --opc <test data>/HERA/Dimorphos_opc/Dimorphos \
    --project <test data>/HERA/Dimorphos_opc/AFC_2027-03-21/AFC1_DRACO2_20270321_200000.png \
    --texture-layer DRACO_2 --body DIMORPHOS --frame DIMORPHOS_FIXED \
    --observer HERA --instrument HERA_AFC-1 --out reprojected.png
```

(The tables in steps 1 and 2 are from an earlier run of the same procedure on an
earlier frame set; [the proof](#the-proof) has the numbers for the current one.)

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
pro3d-tool simulate-image ... --project-shader stack   # otherwise as in step 1
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

## 3. The viewer reproduces the image it projects

Project an image onto the body and look from the projector's own viewpoint. The
render must **be** the image again. The terrain's own texture does not enter
into it, which is what makes this the test to run: it needs no assumption about
what the surface is textured with.

Setup: one simulated AFC-1 frame of Dimorphos, projected onto
`Dimorphos_opc`. *Fly to image* puts the camera on that frame's projector
axis; *Focal (mm)* = 122.563 makes the viewer's field of view AFC-1's 5.5307°;
*Orientation Source* = MBI; *Transfer Function* off, so the layer is painted as
its own RGB. Window 1100×1100, square, because a 16:9 window would frame the
body by its shorter vertical fov instead of the instrument's.

Four checks, in this order. The order matters: every content-level number is
meaningless if the silhouettes do not coincide, and a shift search cannot see a
flip.

| # | check | result |
|---|---|---|
| **0** | geometric overlap — do the silhouettes coincide? | IoU **0.87**, centroid offset **6.0 m** (see the caveat above) |
| **1** | orientation — all eight square symmetries scored | **identity wins**, margin **0.8776** over the next best |
| **2** | registration against the source, shared angular grid | **0.9705** at **zero** shift (±24 px searched) |
| **3** | coverage, from the shader's own coverage view | **99.2%** |

The figures are [in the proof](#the-proof), which is the same run: source
against viewer render, the silhouette overlap, and the coverage view.

Two of those four checks deserve a note on how to read them.

**The silhouette check is a gate, not a measurement.** Its green band along the
bottom limb is not a projection error — it is where the source frame's unlit
limb reads as DN 0, indistinguishable from space, while the viewer's mask is
"not the clear colour" and includes it. Across the dataset's four epochs the
disk fills 0.90–0.93 of its own bounding ellipse, which is the scale of that
bias. It answers *do the outlines coincide at all*, which has to be true before
any content-level number means anything, and nothing finer.

**The orientation check exists because a shift search cannot see a flip.** A
mirrored image correlates poorly at every shift, and the search reports the
least-bad one — which reads as "slightly misregistered" rather than "mirrored".
So all eight square symmetries are scored and the identity has to win. Here it
wins by 0.88, which is not a close call.

Two frames because they test different things. The lit render fills the disk, so
its outline **is** the body's outline and check 0 means something. The
texture-only render carries the surface detail that checks 1 and 2 need — the
lit frame is nearly featureless at this phase angle (8.2° on a smooth shape
model), so its correlation would be carried partly by the disk shape.

This test does not show the image is glued to the *right* terrain, only that it
is reproduced from the viewpoint it was taken from. Comparing against the
terrain's own texture would show that; `simulate-image --texture-layer` now makes
it possible (see *Still open*).

## Supporting evidence: a self-made AFC dataset

`pro3d-tool simulate-image --write-mbi` renders the body from SPICE and writes
an `.mbi.json` describing **the camera it actually used**. Project that image
back onto the same shape model and it must land on itself — there is no
metadata to doubt, because the render and the sidecar are the same camera.

The set this page measures is committed to the test data repository
([PRo3D.Resources.TestData](https://github.com/pro3d-space/PRo3D.Resources.TestData)),
together with the OPC it was rendered from:

| path | content |
|---|---|
| `HERA/Dimorphos_opc/Dimorphos` | the Dimorphos OPC (DRACO_1/DRACO_2 layers, outward-wound) |
| `HERA/Dimorphos_opc/AFC_2027-03-21` | eight AFC-1 frames (texture-only and lit, four epochs), their sidecars, a README and the scene template |

Every sidecar passes the boresight invariant — `TRG_POS` transformed into the
spacecraft frame comes out as +Z — which `make-projection-test-data.py` checks
as it writes them; the round trip through the viewer's own reader is covered by
the `mbiSidecar` Expecto tests. `tests-ui` finds the set through `PRO3D_TEST_DATA`.

### In the viewer

Validated by the projector-viewpoint test in [step 3](#3-the-viewer-reproduces-the-image-it-projects):
silhouettes coincide, orientation is the identity, registration 0.9705 at zero
shift, coverage 99.2% — and, automated, by `projection-e2e.spec.ts` (see
[Reproducing this page](#reproducing-this-page)).

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
| **SPICE** | Nowhere in the image. The spacecraft position is SPICE's, but the camera is then aimed at the **body centre** with a fixed up-vector — `InstrumentProjection.getLookAt` computes an attitude and discards it. Every image is painted as if shot dead-centre with one roll. |
| **MBI** (default) | The image's measured attitude (`SC_QUAT`) and range (`TRG_POS`). Correct when the sidecar is, and faithfully wrong when it is not. |

So the symptom tells you where to look: *"lands on the body but the features do
not line up"* is the first mode; *"does not land at all"* is the second plus a
sidecar problem.

Measured in the viewer, one image in the stack, same scene and camera
throughout:

| | **SPICE** | **MBI** |
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
- **The body's own rotation was applied twice — the one defect that actually
  broke the viewer.** The per-patch projector matrices composed
  `vp.Forward * modelTrafo.Forward * Local2Global.Forward`. But these are applied
  to the raw *patch-local* position, and `Local2Global` already carries that to
  the surface's body-fixed frame — which is the frame `computeProjector` builds
  `vp` in ("the surface's reference frame — body-fixed, so the projection sticks
  to the terrain regardless of the scene's current time"). The model trafo then
  applied pxform, body-fixed → observer frame, a *second* time.

  Measured on one AFC frame projected back from its own camera: correlation with
  the source **0.028 → 0.976**. The offscreen tools were never affected: they
  place the OPC without a GIS transform, so their model trafo is identity.

- **The viewer never bound `NormalFlip`.** The projector-facing test needs
  outward normals, and OPC exports disagree on winding. The offscreen tools
  estimated each dataset's winding; the viewer did not. `NormalWinding.estimate`
  is now shared, and the viewer binds it per patch — lazily, only once something
  is projected or hovered, since the vote reads the root patch from disk.
  The test-data Dimorphos is wound outward (flip 0).

- **The fly-to landed the camera in the right place pointing the wrong way**,
  180° off, through the deprecated `CameraAnimations.animateForwardAndLocation`.
  The fly-to now **sets** the camera, after first setting the scene time to the
  image's epoch. `tests-ui/tests/looking-at-dimorphos.spec.ts` asserts the body is
  in frame at the right apparent size.

## Still open

Nothing in the ladder: the projection is correct in the shader (step 1), in the
stack path (step 2) and in the production viewer (step 3), for both OPC
windings.

- **Terrain comparison.** Step 3 shows the image reproduces itself from the
  projector's viewpoint, not that it is glued to the *right* terrain. With
  `simulate-image --texture-layer`, a render of the scene's own texture layer can
  now be compared against the viewer's terrain.
- **Absolute roll for AFC** needs a real AFC frame with trustworthy metadata
  (see step 1).

## Reproducing this page

Step 3, automated — generate a frame, project it through the UI, compare against
the source:

```
cd tests-ui
PRO3D_TEST_DATA=<PRo3D.Resources.TestData> PRO3D_SPICE_KERNELS=<kernel tree> npm run test:projection
```

Measured with `hera_plan_v182_20260820`: correlation **0.9949** at zero shift,
coverage 98.7%. The default epoch depends on the kernel version (the spec header
says why); `tests-ui/README.md` lists the other variables.

### The data set

[`scripts/make-projection-test-data.py`](../scripts/make-projection-test-data.py)
generates it — eight AFC-1 frames with sidecars and a `ProjectionTest.pro3d` set
up so that projection works the moment it opens:

```
python scripts/make-projection-test-data.py \
    --opc <test data>/HERA/Dimorphos_opc/Dimorphos --out <folder> \
    --texture-layer DRACO_2 \
    --scene-template <test data>/HERA/Dimorphos_opc/AFC_2027-03-21/ProjectionTest.pro3d

python scripts/make-projection-test-data.py --opc <...> --out <...> --list-layers
```

It checks every sidecar it writes against the boresight invariant before
returning, and resolves the texture layer's index from the OPC's `.opcx` rather
than assuming one — the ordering has differed between exports, and the viewer
needs the index while the tool takes the name.

The frames it produces are what this page measures: the numbers above were
re-derived from a freshly generated set, and reproduce to the digit
(registration 0.9705, identity, zero shift).

Shape model: `<test data>/HERA/Dimorphos_opc/Dimorphos`. The frames are only
meaningful on the shape model they were rendered against, so keep them together;
the README beside them covers the viewer settings that matter.

```
# the frames (--texture-layer matters: without it the tool draws the patch's
# DEFAULT layer, which is not necessarily the one a scene displays)
pro3d-tool simulate-image --opc <...>/Dimorphos_opc/Dimorphos \
    --time 2027-03-21T20:00:00Z --body DIMORPHOS --frame DIMORPHOS_FIXED \
    --observer HERA --instrument HERA_AFC-1 \
    --texture-only --texture-layer DRACO_2 --write-mbi \
    --out AFC1_DRACO2_20270321_200000.png

# rung 1 -- the single-image shader, what sun-angles and the testbeds compose
pro3d-tool simulate-image --opc <...> --project <that frame> \
    --texture-layer DRACO_2 --body DIMORPHOS --frame DIMORPHOS_FIXED \
    --observer HERA --instrument HERA_AFC-1 --out rung1.png

# rung 2 -- the same image and camera through the viewer's stack shader
pro3d-tool simulate-image --opc <...> --project <that frame> \
    --project-shader stack --texture-layer DRACO_2 ... --out rung2.png

# rung 3 -- the production viewer
cd tests-ui
PRO3D_SCENE=<...>/ProjectionTest.pro3d \
PRO3D_PROBE_IMAGE_DIR=<...> PRO3D_PROBE_IMAGE=AFC1_DRACO2_20270321_200000.png \
PRO3D_PROBE_METHOD=MbiBased PRO3D_PROBE_TRANSFER=off \
PRO3D_PROBE_FLYTO=1 PRO3D_PROBE_FOCAL=122.563 \
PRO3D_PROBE_VIEWPORT=1100x1100 PRO3D_PROBE_LABEL=rung3 \
  npx tsx src/probe-projection-landing.ts

# coverage rather than content: the same run with
PRO3D_PROBE_VISIBILITY=RelativeCount

# does the viewer see the body from the instrument's own viewpoint at all?
PRO3D_AFC_DIR=<...> npx playwright test looking-at-dimorphos
```

The other probes in `tests-ui/src/`, and why they exist:

| probe | question it answers |
|---|---|
| `probe-projection-landing.ts` | look at one case end to end, including the ones that fail; screenshots and logs lit-fraction after **every** step, because an empty frame three steps later cannot be attributed |
| `probe-accordion.ts` | **clicks** a UI control like a user and asserts the result is visible; the other probes reach into the DOM and so cannot see a broken widget |

The Playwright harness is machine-local by design (real GPU, local OPC and image
data); `tests-ui/src/pro3d.ts` lists the `PRO3D_*` variables, and
[`../ai/TESTING.md`](../ai/TESTING.md) records what these tests can and cannot
see — worth reading before trusting a green run.
