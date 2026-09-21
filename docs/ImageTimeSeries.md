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

## What it writes

| path | content |
|---|---|
| `<out>/micro/` | every epoch with micro-structure: PNG + `.mbi.json` + `.png.json` |
| `<out>/smooth/` | every epoch without it, same cameras |
| `<out>/stack/` | the `--stack-count` subset, evenly spaced over the series |
| `<out>/ImageSeries.pro3d` | scene with body, reference frame, kernel, epoch and focal length set |
| `<out>/series.json` | what was rendered, against which kernels, at what gain |
| `<out>/README.md` | the same in short form, for whoever receives the folder |

## The default series is one rotation

Dimorphos turns in about **11.92 h**, so the default span shows every face once. At the
default 15 min cadence that is **48 epochs**, and with both variants **96 renders**.

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

## Two variants per epoch

Same camera, same `--gain`, same `--micro-scale`; one difference.

| stem | what it is |
|---|---|
| `AFC1_MICRO_<stamp>` | micro-structure on (`--micro-amplitude 0.3`) — the realistic frame |
| `AFC1_SMOOTH_<stamp>` | micro-structure off — the bare shape under the same light |

Both are lit: Lommel-Seeliger, cast shadows, constant albedo. Neither is `--texture-only`.
The pair isolates what the procedural regolith contributes at each illumination, which is
worth having because micro-structure is *shading only* — it is not ground truth, and a
shape-from-shading inversion will happily reconstruct it as relief (see the
[caveats](./Pro3DTool-SimulateImage.md#caveats)).

`--variants micro` renders only the realistic one and halves the run.

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
exactly the frames meant for the stack, instead of all 48 in `micro/`. The default is **15** frames,
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

## Options

| Option | Effect |
|---|---|
| `--out <dir>` | where to write the series (required) |
| `--opc <dir>` | body OPC (default `$PRO3D_TEST_DATA/HERA/Dimorphos_opc/Dimorphos`) |
| `--start <iso>` | first epoch, UTC (default `2027-03-21T13:00:00Z`) |
| `--interval <min>` | cadence in minutes (default `15`) |
| `--duration <h>` | span in hours (default `11.92`, one rotation) |
| `--count <n>` | number of epochs; overrides `--duration` |
| `--kernel <file>` | explicit metakernel (default `<kernel-root>/mk/hera_plan.tm`) |
| `--kernel-root <dir>` | kernel tree; defaults to `$PRO3D_SPICE_KERNELS` |
| `--gain <v>` | fixed I/F→DN gain (default `4.492`); `0` auto-exposes each frame |
| `--albedo <v>` | normal reflectance (tool default `0.16`) |
| `--micro-scale <m>` | micro-structure feature size (default `3.0`) |
| `--micro-amplitude <v>` | strength for the MICRO variant (default `0.3`) |
| `--distance <m>` | range override; default is the spacecraft's real range |
| `--variants <list>` | `micro`, `smooth` or both (default both) |
| `--stack-count <n>` | frames copied into `stack/` (default `15`, cap `32`) |
| `--stack-variant <v>` | which variant the subset takes (default `micro`) |
| `--texture-layer <name>` | layer the scene shows under the projection (default `DRACO_2`) |
| `--scene-template <file>` | `.pro3d` to derive the scene from; skipped if absent |
| `--force` | re-render frames that already exist |
| `--list-layers` | print the OPC's texture layers and exit |

## Re-running it

A frame whose PNG is already there is **skipped**, so an interrupted run continues where
it stopped, and changing `--stack-count` or `--stack-variant` costs nothing but the copy.
`--force` re-renders.

Changing `--interval`, `--start` or `--kernel` produces *different* frames at *new*
stamps, which interleave with the old ones in the same folder. Use a different `--out`, or
`--force` a clean one — `series.json` describes only the last run.

`--micro-scale` is 3.0 here against the tool's own 0.5: at 7–8 km AFC sees roughly
0.7 m/px, and structure below the pixel averages out to nothing.

## Reproducibility

`series.json` records the metakernel and its `MK_IDENTIFIER`, the gain, the micro-structure
settings, and every frame's epoch and range. This matters more than it looks:
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
