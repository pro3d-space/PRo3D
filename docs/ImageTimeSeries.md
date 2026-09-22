# Simulated image time series

A series of simulated HERA/AFC-1 frames of Dimorphos on a fixed cadence, with their
`.mbi.json` sidecars, a subset ready to import, and a scene set up to project them.

Where [`simulate-image`](./Pro3DTool-SimulateImage.md) renders *one* frame and
[`make-projection-test-data.py`](./ProjectionValidation.md) renders the four that prove
the projection is correct, this renders a *series*: the same body under changing
illumination and a changing visible face, which is what
[multi-image projection](./MultiImageProjection.md) exists to handle.

```
python scripts/make-image-time-series.py --out <folder>
```

With `$PRO3D_TEST_DATA` and `$PRO3D_SPICE_KERNELS` set, that is the whole command: the
OPC, the scene template and the kernels are all defaulted from them.

The lit frames come from [`pro3d-tool simulate-series`](./Pro3DTool-SimulateSeries.md) in
a **single invocation** — 429 frames in 50 s, because the OPC load, the de-shading fit and
the scene graph happen once instead of once per frame. What this script adds is the epoch
list and its pointing pre-flight, the independent SPICE reference, the validation, the
stack subset, the scene and the manifest.

## What it writes

| path | content |
|---|---|
| `<out>/delit/` | every epoch, de-lit DRACO texture — the realistic variant |
| `<out>/baked/` | the same texture **not** de-lit — the naive rendering, for testing whether de-lighting matters |
| `<out>/micro/` | every epoch, constant albedo + micro-structure |
| `<out>/smooth/` | every epoch, constant albedo, no micro-structure |
| `<out>/spice/` | optional: an independent SPICE ray-cast of a few epochs, for checking the others |
| `<out>/stack/` | the `--stack-count` subset, evenly spaced over the series |
| `<out>/ImageSeries.pro3d` | scene with body, reference frame, kernel, epoch and focal length set |
| `<out>/series.json` | what was rendered, against which kernels, at what gain |
| `<out>/README.md` | the same in short form, for whoever receives the folder |

## The default series is one rotation

Dimorphos turns in about **11.92 h**, so the default span shows every face once. At the
default 15 min cadence that is **48 epochs**, and with all four lit variants **192 renders** — about 25 seconds.

The default start, `2027-03-21T13:00:00Z`, sits inside HERA's **COP** phase
(2027-02-05 → 2027-04-30, continuous ephemeris in `hera_plan.tm`), in a stretch where the
range runs 6.7–10.1 km and the phase angle 8–42° — close enough for detail, lit enough
that the disk is full. Across COP as a whole the range varies 3.9–26.8 km and the phase
angle 1–93°, so **the start epoch is a real choice**, not a formality.

It is a choice in a second, sharper way. The camera follows HERA's **CK**, so an epoch
renders only while AFC-1 is actually pointed at Dimorphos — true for just **37 % of COP**,
in 24 continuous windows of 6 h or more. The default start sits in the window
2027-03-20T19:15 → 2027-03-22T01:25; starting the same rotation an hour later runs off its
end and the last two epochs render nothing. Check a candidate window before committing 96
renders to it — see
[Pointing and observation windows](./Pro3DTool-SimulateImage.md#pointing-and-observation-windows).

The span is a rotation not because a rotation is the only useful thing to render, but
because it is the shortest span over which the *stack* stops being redundant: frames
minutes apart see nearly the same face.

## Choosing the window: how big the body gets

The default start is a compromise — it is inside a known-good observation window, but not
a close one. Scanning every full-rotation window in COP that stays on target for the whole
11.92 h, ranked by the *median* apparent size of the body across the rotation:

| start (UTC) | median | min | max | phase range |
|---|---|---|---|---|
| **2027-02-25T06:30** | **350 px** | **307 px** | 395 px | 36–65° |
| 2027-04-22T10:00 | 343 px | 265 px | **487 px** | 3–53° |
| 2027-03-18T10:45 | 341 px | 233 px | 474 px | 41–74° |
| 2027-03-04T07:00 | 340 px | 306 px | 376 px | 35–67° |
| `2027-03-21T13:00` (default) | 230 px | 196 px | 282 px | 8–42° |

Sizes are the body's 176.9 m across a 1020 px frame at AFC-1's 93.7 µrad/px.

**Rank on the minimum, not the maximum.** 2027-04-22 reaches the largest single frame in
COP (487 px) but drops to 265 px and opens at 3° phase, where the disk is flat and
featureless. 2027-02-25 never falls below 307 px and holds 36–65° phase throughout, so
every frame in the rotation has shadows and shows detail:

```
python scripts/make-image-time-series.py --out <folder> --start 2027-02-25T06:30:00Z
```

That is ~50 % larger than the default on the median, which matters because the body covers
only a fifth of the frame at the default range — see
[why it is so small](./Pro3DTool-SimulateImage.md#pointing-and-observation-windows).

### Cadence

15 min over a rotation is 48 epochs, which already exceeds the 32-layer stack cap, so a
finer cadence is not about the stack — it is about **motion**. At 15 min the body turns
7.5° between frames, which reads as a jump; at 5 min it turns 2.5°, which reads as
rotation. For an animation, a coverage sweep, or anything where consecutive frames should
look continuous, render a second dense pass over the same window:

```
python scripts/make-image-time-series.py --out <folder>-5min     --start 2027-02-25T06:30:00Z --interval 5 --variants delit
```

143 epochs instead of 48 — and all of them, in every lit variant, render in under a minute, so
there is no longer any reason to thin the dense pass down to `--variants delit`. Use a
**separate output folder**: mixing cadences in one folder interleaves the stamps, and
`series.json` then describes the union of both at a cadence that is neither.

## Four lit variants per epoch

Same camera, same `--gain`, same `--micro-scale`. They are ordered so that each adds
exactly one thing to the one before it, which is what makes a comparison mean anything:

| stem | what it adds |
|---|---|
| `AFC1_SMOOTH_<stamp>` | constant albedo, micro-structure off — the bare shape |
| `AFC1_MICRO_<stamp>` | + procedural micro-structure, still no texture |
| `AFC1_BAKED_<stamp>` | + the real DRACO texture, **illumination and all** |
| `AFC1_DELIT_<stamp>` | + that baked illumination divided back out — the realistic frame |

`DELIT` is what you want for anything that should look like a real observation. It runs
`--deshade --deshade-layer DRACO_2`, which de-lights the part of the mosaic that carries
baked illumination (the DRACO_1 footprint, fit `r = 0.365`) and leaves the rest as-is,
because that part has none to remove (`r = 0.099` under its own best fit). `MICRO` and
`SMOOTH` keep the with/without-structure pair on a constant albedo, for isolating what
the procedural roughness contributes.

### `BAKED` vs `DELIT`: is de-lighting necessary?

`BAKED` is `DELIT` with the division switched off and **nothing else changed** — same
texture, same camera, same exposure, same normalisation. The mosaic's own illumination
stays in it and this epoch's sun lights it a second time, which is what you get if you
simply drape the texture and shade it.

That is the naive rendering, and it ships on purpose. Whether de-lighting matters is a
question about *your* reconstruction rather than about the renderer — shape-from-shading,
photogrammetry and neural reconstruction are each affected differently, and some may not
care. Run yours over both folders and compare: that is the measurement, and it needs the
frame that skips the step.

All four are lit — Lommel-Seeliger with cast shadows — and none is `--texture-only`.
Keep in mind that micro-structure is *shading only*: it is not ground truth, and a
shape-from-shading inversion will happily reconstruct it as relief (see the
[caveats](./Pro3DTool-SimulateImage.md#caveats)).

`--variants delit` renders only the realistic one.

### The reference variant: not a rendering at all

`--variants delit,micro,smooth,spice` adds `<out>/spice/`, the same epochs rendered by
**SPICE ray-casting its own DSK shape model** — same kernels, same instrument, no PRo3D
code on the path. It carries no sidecars and is not meant for projecting; it exists to be
compared against, because a renderer checked only against its own output is checked
against nothing. See [Cross-checking against SPICE](./ShapeModelCrosscheck.md) for what
that comparison established.

**Only a few epochs get one** — `--spice-count`, 8 by default, spread evenly over the
series. The reference costs ~50 s a frame against the tool's ~0.12 s, and it tests the
*renderer*, not the epoch: whether the detector axes and the FOV agree with the kernels is
the same question at every frame, and a handful spread across the series asks it under
every illumination and visible face the series contains. Rendering it 143 times costs two
hours to re-answer what frame ten already answered — which would be 99 % of a run whose
lit frames take under a minute. `--spice-count 0` renders one per epoch if you want that.

It does not parallelise, so do not try. CSPICE reads the DSK in 1 KB records through its
DAS layer, and the per-read overhead is what dominates: fifteen worker processes on this
machine each read **20 GB** and spent thirteen minutes before delivering their first
frames, while one process renders a frame in 50 s.

## The exposure is fixed on purpose

`--gain 4.492` by default. Auto-exposure normalises each frame to its own 99.5th
percentile, which across a series silently removes the very thing a series shows: the body
getting brighter and darker as the illumination changes. `--gain 0` restores per-frame
auto-exposure and gives that up.

## The stack subset, and why it is a separate folder

**A `.pro3d` scene cannot carry a projection stack.** It persists the projection
*settings* — lighting mode, winding correction — and nothing else from the image list;
`GisAppJson.read0` in [`src/PRo3D.Core/GisApp-Model.fs`](../src/PRo3D.Core/GisApp-Model.fs)
rebuilds the library and the stack from `ProjectedImageListModel.initial` on every load.
The images are session-local by design.

So `<out>/stack/` exists to make the import step small: *Import Directory* on it loads
exactly the frames meant for the stack, instead of all 48 in `delit/`. The default is **15** frames,
about 48 min and ~24° of rotation apart. The cap is **32** —
`ProjectedImages.maxCount` sizes the shader's uniform arrays, so the viewer will not take
a 33rd layer.

### In the viewer

1. Open `<out>/ImageSeries.pro3d`. The camera is on the middle subset frame's projector
   axis and the scene clock is that frame's epoch.
2. GIS tab → *Projected Images* → **Import Directory** → `<out>/stack/`.
3. **+** on the rows you want, top of the stack wins.
4. *Projection Settings*: *Orientation Source* **MBI** (the sidecars are measured, not
   assumed), and *Transfer Function* **off** if you want the frames' own pixels.
5. *Visibility* → **RelativeCount** shows how much of the body the series actually covers.

## Validation is a stage, not a step you remember

Nothing here has to be run afterwards. A series is validated **as it is produced**, at
three independent gates, and the run exits non-zero if any of them fails:

| gate | what it catches | where |
|---|---|---|
| the sidecar round trip | a frame whose `.mbi.json` does not reconstruct the camera that rendered it — the frame cannot be projected | `simulate-series`, every frame |
| the boresight invariant | `Aᵀ·TRG_POS` not pointing down the boresight, i.e. a sidecar that describes a different observation | the generator, every sidecar on disk |
| the SPICE cross-check | the renderer and the kernels disagreeing about the detector axes | the generator, `--spice-count` epochs |

The first two are self-consistency: they prove the frames agree with themselves. Only the
third is independent, which is why it exists — see
[3.1 in the cross-check](./ShapeModelCrosscheck.md).

**The verdict goes into `series.json`**, so a folder carries its own proof rather than it
living in a terminal that is gone by the time anyone asks:

```json
"validation": {
  "verdict": "pass",
  "what": "each tool frame against the SPICE DSK ray-cast of the same epoch, over the
           eight dihedral transforms; the best must be identity",
  "checked": 24,
  "tally": { "identity": 24 },
  "settings": { "cropPx": 340, "thresholdDn": 25, "minCorrelation": 0.5, "margin": 0.02 },
  "failed": [], "weak": []
}
```

The README that travels with the folder says the same in prose.

### Against other renderers

`check-series.py` compares a series with **our own** SPICE ray-cast, so it cannot catch
anything the two share — and they share our reading of the instrument kernel. The
independent check is against somebody else's implementation:

```
python scripts/check-renderers.py --refs <screenshots> --ours <series>                                   --pilucas <their frames> --silhouettes <our --no-lighting renders>
```

It compares silhouette overlap, and puts each pairing on its own footing: a renderer that
shades has a terminator, so the comparison is the lit region; one that does not light its
output has no dark side, so it is the whole outline. Comparing our lit crescent against
somebody's full disk measures nothing, which is the mistake the script exists to prevent.

Epochs where the reference is not showing the same body are rejected on evidence, not by a
hardcoded list: if its lit area differs by more than 2×, AFC-1 is pointed at Didymos and
the frame is skipped. On 2027-02-25 that removes 11:45–13:30, where the ratio runs from
4× to 21×.

The thresholds are measurements. `--update-baseline` prints what a run would support, for
when a deliberate change moves them.

### Re-checking later

```
python scripts/check-series.py --series <folder>
```

For a folder someone sent you, one whose frames have been touched since, or to try
different thresholds. It calls the same `pro3d_sim.validate_series` the generator does —
one implementation, so the recorded verdict and a later re-check cannot disagree for any
reason but the data. It says so if they do.

For every epoch holding both a tool frame and a `spice/` reference it finds which of the
eight dihedral transforms aligns the two best. The answer must be `identity`. Anything
else means the renderer and the kernels disagree about the detector axes — the fault that
once left a folder holding `smooth/` frames 90° from its `delit/` frames with nothing in
the output saying so.

Only the epochs that have a `spice/` frame are checked, so the count follows
`--spice-count`. A clean run on a one-rotation series rendered with a reference at
every epoch:

```
checked 144 frame pairs across 48 epochs
   best transform identity         138
   best transform flipUD             6
   best transform identity (tie)     6

PASS: every pair aligns best under identity (9 weak of 144)
```

Reading it:

- **`flipUD` counted as a tie.** Dimorphos is near-symmetric about some viewing axes, so
  on a *correct* frame `flipUD` can edge out identity by ~0.005. `--margin` (default 0.02)
  is how far another transform must win before it counts as a failure; a real axis error
  wins by tenths, not thousandths.
- **Weak pairs** correlate under identity but below `--min-correlation` (default 0.5).
  Almost always a low-phase epoch: a 3° disk is flat and featureless, so there is nothing
  to correlate. That is *inconclusive*, not a failure.

Two traps the script exists to avoid, both of which produced confident wrong answers by
hand: whole-frame correlation collapses to noise when a small FOV difference shifts the
body a pixel or two (so both frames are cropped around their own body centroid), and the
tool's ambient-lit night side has no counterpart in the reference, which writes 0 where
unlit (so both are thresholded well above the ambient floor, making the masks and hence
the centroids comparable).

## An index over several series

```
python scripts/make-series-index.py --root <image-series-root>
```

Reads each subfolder's `series.json` and writes a top-level `README.md` listing the
series, their epochs, cadence, variants and kernel. For a data root that holds more than
one series.

## Options

| Option | Effect |
|---|---|
| `--out <dir>` | where to write the series (required) |
| `--opc <dir>` | body OPC (default `$PRO3D_TEST_DATA/HERA/Dimorphos_opc/Dimorphos`) |
| `--obj <file>` | a Wavefront shape model instead of the OPC; `.obj.gz` works. See [Rendering from a mesh](#rendering-from-a-mesh-instead-of-the-opc) |
| `--obj-scale <v>` | metres per `--obj` file unit (default `1000`) |
| `--obj-texture <file>` | image to drape on `--obj`; without it `delit` and `baked` are refused |
| `--interval-seconds <s>` | cadence in **seconds**, overriding `--interval`. Below a minute this is the honest unit: 8 s as 0.1333.. minutes accumulates float error that truncates a stamp to the wrong second, and the stamp is the filename |
| `--occluder-body <name>` | cast the other body of the binary as a shadow (`DIDYMOS`). Without it an eclipsed epoch renders in full daylight |
| `--occluder-obj <file>`, `--occluder-obj-scale <v>` | the occluder's shape model; without one, a tessellation of its reference radii |
| `--start <iso>` | first epoch, UTC (default `2027-03-21T13:00:00Z`) |
| `--interval <min>` | cadence in minutes (default `15`) |
| `--duration <h>` | span in hours (default `11.92`, one rotation) |
| `--count <n>` | number of epochs; overrides `--duration` |
| `--kernel <file>` | explicit metakernel (default `<kernel-root>/mk/hera_plan.tm`) |
| `--kernel-root <dir>` | kernel tree; defaults to `$PRO3D_SPICE_KERNELS` |
| `--gain <v>` | fixed I/F→DN gain (default `4.492`); `0` auto-exposes each frame |
| `--albedo <v>` | normal reflectance (tool default `0.16`) |
| `--micro-scale <m>` | micro-structure feature size (default `3.0`) |
| `--micro-amplitude <v>` | strength for the `MICRO` and `DELIT` variants (default `0.3`) |
| `--distance <m>` | range override; default is the spacecraft's real range |
| `--variants <list>` | any of `delit`, `baked`, `micro`, `smooth`, `spice` (default all but `spice`) |
| `--stack-count <n>` | frames copied into `stack/` (default `15`, cap `32`) |
| `--stack-variant <v>` | which variant the subset takes: `delit`, `baked`, `micro` or `smooth` (default `delit`) |
| `--texture-layer <name>` | layer the scene shows under the projection (default `DRACO_2`) |
| `--scene-template <file>` | `.pro3d` to derive the scene from; skipped if absent |
| `--spice-count <n>` | how many `spice` reference frames to render, spread over the series (default `8`; `0` renders one per epoch) |
| `--margin <v>` | validation: how far another dihedral transform must beat identity before a frame counts as failed (default `0.02`) |
| `--list-layers` | print the OPC's texture layers and exit |

<a name="rendering-from-a-mesh-instead-of-the-opc"></a>
## Rendering from a mesh instead of the OPC

```
python scripts/make-image-time-series.py --out <folder>     --obj $PRO3D_TEST_DATA/HERA/Dimorphos_dsk/g_00243mm_spc_obj_dimo_0000n00000_v004.obj.gz     --variants micro,smooth --stack-variant smooth
```

The OPC's posts are 1.96 m apart; at 5 km an AFC pixel covers 0.48 m. The delivered frames
are therefore limited by the shape model rather than by the sensor. The shape model the
SPICE kernels ship is 0.24 m per facet, and rendering from it closes that gap — the
silhouette then matches a SPICE ray-cast of the kernels' own DSK exactly. See
[ShapeModelCrosscheck.md](./ShapeModelCrosscheck.md#closing-it-rendering-the-dsks-own-shape).

Three things change in the run:

- **Only `micro` and `smooth`.** The mesh carries no texture — the `.png` beside each
  `.bds` in the kernel set is a preview render, not a map — so `delit` and `baked` are
  **refused** rather than silently rendered as flat albedo. `--obj-texture` brings them
  back if you have a map of your own.
- **No scene.** `ImageSeries.pro3d` binds an OPC surface with a `selectedTexture`, and a
  mesh has neither. The run says `no scene written` and writes the frames and sidecars as
  usual; these frames are for comparing renderers, not for projecting in the viewer.
- **The README says which shape model it was**, and `series.json` gains
  `provenance.shapeModel` / `shapeModelKind` / `shapeModelProduct`. The old `opc` and
  `opcProduct` keys are still there and are `null` for an OBJ series, so an existing
  consumer does not break but also cannot mistake one series for the other.

## A series at video cadence

A mutual eclipse happens over about ninety minutes, which is short enough to render at a
cadence a video can use. Worked example — Dimorphos through Didymos' shadow on 2027-02-25:

```
python scripts/make-image-time-series.py --out <folder>     --start 2027-02-25T10:32:00Z --interval-seconds 8 --count 750     --variants delit,smooth --texture-layer DRACO_2 --gain 4.492     --micro-scale 3.0 --micro-amplitude 0.3     --occluder-body DIDYMOS --occluder-obj <kernels>/dsk/g_01165mm_spc_obj_didy_0000n00000_v003.obj
```

**750 frames is exactly 30 s at 25 fps**, so the frame count comes first and the cadence
follows from the span: 750 × 8 s = 100 min, which brackets the event (first contact
10:36:08, last 12:06) by about five minutes at each end. 1500 frames — both variants —
render in 212 s.

Why 8 s and not finer: the shadow's edge crosses the body in 16 min, and the body is ~250 px
wide, so the edge moves **1.9 px a frame** at 8 s. Below about 4 s the motion is smaller
than the penumbra is wide and nothing is gained; above about 15 s it starts to step.

**Totality is 60 % of it.** The body sits at the ambient floor from 10:52:08 to 11:52:00 —
450 of the 750 frames, 18 s of a 30 s video. That is the event, not a defect, but it makes
a poor thing to watch end to end. The cadence is uniform because a series with a hole in it
is not a series; the *edit* is where totality gets dropped:

```
python scripts/make-series-video.py --series <folder> --variant delit --dark-speedup 12
```

writes the MP4 directly (through OpenCV, no ffmpeg on PATH needed) and keeps every 12th
frame while the body is below 15 % of its unshadowed brightness, so ingress and egress stay
at full cadence and totality becomes a beat rather than a wait. With ffmpeg instead:

```
# straight through: 30 s, 18 s of it dark
ffmpeg -framerate 25 -pattern_type glob -i 'delit/*.png' -c:v libx264 -pix_fmt yuv420p eclipse.mp4

# ingress and egress only, totality cut: ~12 s
ffmpeg -framerate 25 -pattern_type glob -i 'delit/*.png'        -vf "select='lt(n,160)+gte(n,590)',setpts=N/25/TB" -c:v libx264 -pix_fmt yuv420p eclipse-cut.mp4
```

Frame 160 is 10:53:20 and frame 590 is 11:50:40 — the first and last frames either side of
totality. `series.json` carries every epoch's stamp, so any other cut is one lookup away.

## Re-running it

Every run renders the whole series and **recreates** the variant folders. There is no
`--force` and no skip-if-present, because there is nothing left for resuming to save: the
143-epoch series above renders in 50 seconds.

Resuming is what once left a folder holding `smooth/` frames 90° from its `delit/`
frames, because the skip logic had no notion of which binary produced a file. Dropping it
removes that whole class of bug rather than guarding against it.

Changing `--interval`, `--start` or `--kernel` produces *different* frames at *new*
stamps, which would interleave with the old ones. Use a different `--out`.

`series.json` and the README describe the **folder**: both are built by scanning every
variant directory, so what they claim and what is on disk cannot disagree.

`--micro-scale` is 3.0 here against the tool's own 0.5: at 7–8 km AFC sees roughly
0.7 m/px, and structure below the pixel averages out to nothing.

## Reproducibility

`series.json` records the metakernel and its `MK_IDENTIFIER`, the gain, the micro-structure
settings, every frame's epoch and range, and a `toolBuild` fingerprint of the binaries that
rendered it (size and mtime of `PRo3D.Tool.exe` and `PRo3D.Base.dll`). **Frames from
different builds must never be mixed**: a change to the instrument axis map or the FOV
rotates or rescales every frame rendered after it while the resume logic happily keeps the
earlier ones. A re-run whose fingerprint differs from the recorded one — or a folder with
none recorded, whose provenance is therefore unknown — re-renders everything. This matters more than it looks:
**ESA regenerates the HERA plan kernels and they move the spacecraft.** The same command
against a different tree renders different images, and an epoch that frames the body in
one tree can put it outside the AFC frame in another — the render then fails, with SPICE
saying why. Quote the `MK_IDENTIFIER` whenever a frame is questioned.

## Caveats

Everything in [`simulate-image`'s caveats](./Pro3DTool-SimulateImage.md#caveats) applies —
in particular:

- **Pointing follows the CK.** Frames are not aimed at the body centre; they use the
  spacecraft's planned attitude, which is why an epoch outside an observation window
  renders nothing. A frame is centred only to the extent the plan pointed it that way.
- **No detector model**: no PSF, no noise, no 12-bit quantisation.
- **No phase function.** With a fixed gain the *relative* brightness across the series is
  consistent, but it is not absolute radiometry: `f(α)` is not modelled, and the phase
  angle changes across a rotation.
- **Micro-structure is shading only** — it casts no shadows and does not alter the
  silhouette.
