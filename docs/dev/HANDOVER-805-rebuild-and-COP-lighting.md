# Handover: rebuilding #805 (simulated image series) and the COP lighting question

For an agent picking up two related jobs. Written 2026-10-09 at the end of the shadow-measurement
work (branch `features/shadow-measurement`, worktree `..\pro3d-shadow`), which used simulated
frames as ground truth and ran into both.

## The two jobs

1. **Rebuild #805 cleanly.** PR #805 (`features/801_simulate-series`) is a concept spike: do not
   review or merge it, rebuild the functionality in a new, clean PR from `develop`, then close #805
   (closing superseded PRs needs no extra question). The spec already exists — read it first:
   - [`REBUILD-simulated-image-series.md`](REBUILD-simulated-image-series.md) — commands that
     define the acceptance target, every measurement, the four load-bearing decisions, the dead
     weight not to carry (merge/relabel scripts, variant names in filenames, one-off scripts).
   - [`HANDOVER-simulated-image-series.md`](HANDOVER-simulated-image-series.md) — the narrative.
   - [`AFC-image-orientation.md`](AFC-image-orientation.md) — read before touching `specialTrafos`.
   - [`../ShapeModelCrosscheck.md`](../ShapeModelCrosscheck.md) — the SPICE ray-cast and
     Cosmographia cross-checks.
2. **Explain the COP frames' lighting.** The delivered COP set renders Didymos lit everywhere,
   where its own geometry has a night side (details below). Find out why, and decide what it
   means for using COP frames as test data.

## What the shadow work established (new since the rebuild doc)

### simulate-image's shadows: the crater is right, the default bias is not

- The shadow-measurement tool was verified against **SPICE alone**: PRo3D's own pixel rays
  intersected with the Didymos DSK (`dskxv`; `g_01165mm_spc_obj_didy_0000n00000_v003`, the same
  shape as the OPC `Didymos_SK_OPC__texture/Didymos_ASPECT_texture`), lit flag from `illumf`.
  DSK intercepts vs PRo3D kd-tree hits: median 1.4 cm. Occlusion: 99.4 % agreement on 2422 pixels.
- simulate-image's **crater shadow agrees with SPICE**. Its extra shadow pixels are a patch of
  **shadow-map acne** on a grazing slope (median 28 px from any SPICE shadow), rendered with the
  `develop` default `--shadow-bias 0.002`. The rebuild doc's sweep already says 0.002 has acne and
  **0.006** is right — **make 0.006 the default in the rebuild** (and re-render the two older
  eclipse series the rebuild doc lists as acne-affected).
- A **trap** worth knowing: a pixel-wise "renderer shadow vs mesh + sun" comparison over a region
  with acne disagreed on 59 % of the shadow pixels, and a fitted ~13° sun rotation "fixed" it.
  That fit was explaining the acne, not a wrong sun. Before fitting a cause to a disagreement
  count, look at where the disagreeing pixels are.
- **Grazing light is the hard case** for any renderer comparison: at a few degrees of sun, one
  pixel along a shadow is ~10x that in height, and a rounded crest is an ~8 px brightness ramp.
  Compare masks at shadow *edges* with that in mind; compare silhouettes for geometry.

### The independent cross-check, as code

This is the most useful tool from the session — it needs only `spiceypy` and the metakernel, and
checks a renderer's shadows pixel by pixel without sharing any code with it:

```python
import os, numpy as np, spiceypy as sp
mk = r'C:\Users\haral\Desktop\pro3d\spice2\kernels\mk\hera_plan.tm'
os.chdir(os.path.dirname(mk)); sp.furnsh(mk)          # PATH_VALUES in the mk are relative
et = sp.str2et('2027-02-05T17:00:00Z')
# O: ray origins (km), D: unit directions, body-fixed DIDYMOS_FIXED -- e.g. from
# InstrumentObservation.pixelRay (PRo3D.GIS/InstrumentObservation.fs), or SPICE's own rays:
#   sp.sincpt('DSK/UNPRIORITIZED','DIDYMOS',et,'DIDYMOS_FIXED','NONE','HERA','HERA_AFC-1',[x,y,1])
X, found = sp.dskxv(False, 'DIDYMOS', [], et, 'DIDYMOS_FIXED', O.tolist(), D.tolist())
for p, f in zip(X, found):
    if f:
        trgepc, srfvec, phase, inc, emi, visible, lit = sp.illumf(
            'DSK/UNPRIORITIZED', 'DIDYMOS', 'SUN', et, 'DIDYMOS_FIXED', 'NONE', 'HERA', list(p))
        # lit == False: the surface faces away or the body shadows itself there
```

`dskxv` is vectorised (2400 rays in well under a second). Using PRo3D's own pixel rays sidesteps
the detector-axis transpose between the SPICE ray-tracer and the IK layout that
`ShapeModelCrosscheck.md` discusses. A renderer's *cast* shadow mask is the difference between a
render with and without `--no-shadows` at the same `--gain`.

### The sun line is vertical in every planned frame — by attitude

In all planned HERA AFC sidecars checked (the COP-derived frame and the four 2027-03-21 test
frames), the sun's y-component in the spacecraft frame is ≤ 0.0012 and the target sits on +Z.
The planned attitude keeps the sun in the spacecraft x-z plane, so in the image the sun direction
— and every shadow — runs along one image axis. Not a bug; worth stating in the rebuild's docs,
and worth not "fixing".

## The COP lighting question

**Data.** `C:\pro3ddata\HERA\workshop3\COP\COP\<date>\HERA_AFC_*_COP.png` + `.mbi.json`
(generator comment "JR (c) 2026", metakernel note `hera_plan.tm 2026-08-19`). Sidecar defects
are documented in [`../COP-sidecar-issues.md`](../COP-sidecar-issues.md) and quantified in
REBUILD §2 "Sidecar conventions": metres not km, `TRG_POS` negated and always Didymos-centred,
`SC_QUAT` conjugated, `DATE-OBS` = `"AFC1-Synthetic"` (time only in the file name).

**Measured today, frame `HERA_AFC_0065_20270205_170000_COP.png`** against simulate-image through
the same camera (`workshop3\shadowProjectionTest\AFC1_COP_20270205_170000.mbi.json`, which is the
COP sidecar converted to our convention; render with
`--body DIDYMOS --frame DIDYMOS_FIXED --kernel ...\spice2\kernels\mk\hera_plan.tm`):

| | |
|---|---|
| silhouette vs simulate-image `--no-lighting` | **IoU 0.986**, centroids 0.6 px apart, no alignment; transposed: 0.906 |
| phase angle at Didymos (SPICE, and simulate-image's log) | **61.5°** |
| COP body pixels that are lit (> 25 % of the 99th percentile) | **100 %** |
| simulate-image body pixels lit | 75 % — the rest is the night side and the terminator |

So **geometry and pose agree; the lighting does not.** The COP frame shows the full disk lit,
lunar-mosaic texture visible right across where a 61.5° phase must leave a night side. (I first
misread this as a different orientation — the night side had hidden part of our silhouette. It
is not; the silhouettes match.)

**Hypotheses to separate** (cheapest first):

1. The COP renderer outputs **texture/albedo only** (no illumination). Test: correlate the COP
   frame with simulate-image `--texture-only` through the same camera (same OPC; the texture is
   the lunar stand-in, see REBUILD §2 "two shape models"). A high correlation and no shading
   gradient means albedo-only.
2. It lights from the **camera** (headlight, zero phase). Test: brightness vs emission angle — a
   headlight darkens toward the limb with μ, independent of the sun; check the limb on the sun
   side vs the anti-sun side.
3. It uses a **different sun** (wrong frame/time; `DATE-OBS` carries no time). Test: fit the
   light direction from brightness vs mesh normals (simulate-image `--deshade` has a least-squares
   light fit — `fitBakedLight` in `PRo3D.Tool/SimulateImage.fs`) and compare with SPICE's sun at
   the file-name time.

Whatever the answer: **COP frames are not usable as shadow/illumination ground truth** until it
is explained; their geometry (silhouette, pose) is. The shadow-measurement docs currently say to
test on simulate-image renders — keep it that way. If the answer is a generator defect, add it to
`COP-sidecar-issues.md` (it is addressed to the data generators).

## Practical notes

- Kernels: `C:\Users\haral\Desktop\pro3d\spice2\kernels\mk\hera_plan.tm` was used throughout
  today; for kernel *tests* the pinned set lives under `..\spice` (see the project memory /
  `scripts/spice-kernels.pins`).
- simulate-image is built in either worktree: `dotnet build src/PRo3D.Tool -c Debug`, exe at
  `bin\Debug\net9.0\PRo3D.Tool.exe`. It renders 1020² in ~1 s.
- Useful frames: `workshop3\shadowSimTest\AFC1_SIM_DIDYMOS_20270205_170000.png` (simulate-image,
  same camera, `--micro-amplitude 0`, gain 3.690, bias 0.002 — has the acne patch top-left of
  the crater region x 300-520, y 520-720).
- The Didymos crater used for the shadow tests: image column ~400, rim row ~581, shadow end ~610;
  ideal-click depth with local up 4.1-4.6 m.
- `dotnet-stack` is installed if a viewer hangs; Playwright UI specs live in `tests-ui/` (see its
  README; ~10 min per run, mostly startup).
