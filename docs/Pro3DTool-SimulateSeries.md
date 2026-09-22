# `pro3d-tool simulate-series`

Render a whole series of simulated instrument images in **one process**: many epochs ×
many shading variants, sharing one OPC load, one de-shading fit and one scene graph.

```
pro3d-tool simulate-series --opc <dir> --times-file <epochs.txt> --out <folder> --gain 4.492
```

Where [`simulate-image`](./Pro3DTool-SimulateImage.md) renders one frame, this renders a
set. Everything it does per frame is the same code — the two verbs share the shading
uniforms, the shader stack and the pointing pre-flight, so they cannot drift apart.

## Why it exists

`simulate-image` is a complete program per frame. It initialises a GL context, loads the
OPC, fits the de-shading, builds a scene graph and warms the LOD tree, all to emit one
PNG — and **none of that depends on the epoch.** Driving it once per frame put a
143-epoch × 3-variant series at about 50 minutes, of which the actual rendering was a
rounding error.

Measured on this repo, the same 429 frames:

| | per frame | 429 frames |
|---|---|---|
| `simulate-image`, once per frame | ~7 s | ~50 min |
| `simulate-series`, one process | **0.12 s** | **50 s** |

The output is **bit-identical**. Nine frames spanning a rotation — all three variants at
2027-03-21 13:00, 19:00 and 2027-03-22 00:45 — render to 0 DN maximum difference against
the per-frame path. This is a restructure, not an approximation.

## What is hoisted, and what is not

Once per process, because it is a property of the OPC rather than of the epoch:

| | |
|---|---|
| GL context, SPICE kernels | process-level setup |
| patch hierarchies, texture layer | the OPC does not change between epochs |
| **the de-shading fit** | reads a per-vertex layer and solves for the baked light direction — no camera, sun or time enters it, so its three uniforms are constant for the whole series |
| bounding box | |
| both OPC scene graphs | the sun, the camera and the shadow frustum are `cval`s, so an epoch is a `transact`, not a rebuild |
| render target, one compiled task per variant | |

Per epoch there remains the camera and sun from SPICE, a shadow-map pass (the sun moves),
and one render per variant.

The lit variants differ **only** in shading uniforms — micro-structure amplitude, whether
the texture is used as albedo, and whether its baked illumination is divided out first. So
one scene graph serves every variant as well as every epoch.

## Variants

Four presets, ordered so that each adds exactly one thing to the one before it:

| `--variants` | what it adds |
|---|---|
| `smooth` | constant albedo, micro-structure off — the bare shape |
| `micro` | + procedural micro-structure, still no texture |
| `baked` | + the real texture, **illumination and all** |
| `delit` | + that baked illumination divided back out — the realistic frame |

They share one camera and one exposure, so a neighbouring pair differs in exactly one
thing, which is what makes a comparison mean anything.

### Why `baked` exists

The OPC texture is a projected instrument image with illumination baked into it. `delit`
fits that illumination and divides it out, so this epoch's sun lights a surface carrying
real albedo detail. `baked` **skips the division**: the mosaic's own lighting stays in and
the render lights it a second time.

That is the naive rendering, and it is here on purpose. Whether de-lighting is necessary
is a question about *your* reconstruction, not about the renderer — a shape-from-shading
inversion, a photogrammetric pipeline and a neural reconstruction will each be affected
differently, and some may not care. The only way to find out is to run both and compare,
which needs the frame that skips the step.

The two are identical in every other respect, including brightness: `baked` normalises the
texture through the same `DeshadeScale` at the same fixed mid incidence that `delit`'s own
low-confidence fallback uses. The de-shading fit therefore runs whenever *either* is
requested — otherwise `baked`'s brightness would depend on whether `delit` happened to be
in the same run.

### Give it a gain

`--gain` matters more across a series than within a frame. Auto-exposure (`--gain 0`)
normalises each frame to its own percentile, which removes the very thing a series shows:
the body getting brighter and darker as the illumination changes. It also makes the
variants incomparable, since each would be stretched differently. The verb warns if you
leave it at 0.

## Epochs

`--times-file` takes one ISO-8601 UTC epoch per line; blank lines and `#` comments are
skipped, so the list can carry its own provenance.

```
# epochs of this series, on target for HERA_AFC-1
2027-02-25T06:30:00Z
2027-02-25T06:35:00Z
2027-02-25T06:40:00Z
```

The cadence lives in whatever produced the file — normally
[`make-image-time-series.py`](./ImageTimeSeries.md), which also filters epochs against
the CK before writing them. The tool deliberately knows nothing about rotation periods or
observation windows.

With `--pointing ck` (the default) the camera is aimed by the kernels, so an epoch where
the instrument was observing something else renders nothing. The pointing pre-flight says
so, with the boresight offset and the frustum half-angles, before any GPU work — see
[Pointing and observation windows](./Pro3DTool-SimulateImage.md#pointing-and-observation-windows).

## Validation is part of rendering

Every frame's `.mbi.json` is written from the trafo the render actually used and then read
back **through the viewer's own path**, so what is measured is the real round trip rather
than a restatement of what was just written. A frame whose sidecar does not reconstruct
its own camera to within `--max-reprojection-error` (default 0.1 px) is a **failure**, not
a warning: such a frame cannot be projected, and finding that out after the series is
finished costs the series.

```
[series] every frame rendered and every sidecar reconstructs its own camera to within 0.100 px
```

By default the run stops at the first failure; `--keep-going` renders the rest and still
exits non-zero, naming every frame that failed.

That check is self-consistency — it proves the sidecar matches the render, not that either
matches the kernels. The independent check against a SPICE ray-cast is the generator's
job; see [Cross-checking against SPICE](./ShapeModelCrosscheck.md) and
[the validation stage](./ImageTimeSeries.md#validation-is-a-stage-not-a-step-you-remember).

## No resume, by design

A run renders every frame it was asked for and **recreates** the variant folders. There is
no `--force` and no skip-if-present.

Resuming is what once left a folder holding `smooth/` frames 90° from its `delit/` frames,
because the skip logic had no notion of which binary produced a file. With a whole series
rendering in one process at 0.12 s a frame, resuming saves nothing worth that class of
bug.

## Options

| Option | Effect |
|---|---|
| `--opc <dir>` | OPC directory of the body (required) |
| `--times-file <file>` | epochs, one ISO-8601 UTC time per line (required) |
| `--out <dir>` | output directory; one subdirectory per variant (required) |
| `--variants <list>` | any of `delit`, `baked`, `micro`, `smooth` (default all four) |
| `--stem-prefix <s>` | filename prefix (default derived from the instrument: `HERA_AFC-1` → `AFC1`) |
| `--instrument`, `--observer`, `--body`, `--frame` | as `simulate-image` |
| `--kernel`, `--kernel-root` | as `simulate-image` |
| `--pointing ck\|lookat` | where the camera orientation comes from (default `ck`) |
| `--distance <m>` | range override; default is the spacecraft's real range at each epoch |
| `--width`, `--height` | output size; 0 uses the instrument's native detector size |
| `--albedo <v>` | normal reflectance (default 0.16) |
| `--deshade-layer <name>` | layer the `delit` and `baked` variants draw (`delit` also de-shades it); selects `--texture-layer` too |
| `--texture-layer <name>` | layer to draw; defaults to `--deshade-layer` |
| `--micro-scale <m>` | micro-structure feature size (default 0.5) |
| `--micro-amplitude <v>` | strength for `delit` and `micro`; `smooth` is always 0 |
| `--ambient <v>` | ambient floor so the night side is distinguishable from space |
| `--gain <v>` | fixed I/F→DN gain; 0 auto-exposes each frame and is warned about |
| `--no-shadows`, `--shadow-bias <v>` | the sun-side depth pass |
| `--max-reprojection-error <px>` | how far a frame's own sidecar may sit from the camera that rendered it (default 0.1) |
| `--keep-going` | render the remaining epochs after a failure; still exits non-zero |

## Output

```
<out>/delit/    AFC1_DELIT_<yyyyMMdd_HHmmss>.png
                AFC1_DELIT_<yyyyMMdd_HHmmss>.mbi.json    the observation
                AFC1_DELIT_<yyyyMMdd_HHmmss>.png.json    statistics, incl. pixel size
<out>/baked/    AFC1_BAKED_<stamp>.*
<out>/micro/    AFC1_MICRO_<stamp>.*
<out>/smooth/   AFC1_SMOOTH_<stamp>.*
```

The same epoch is the same stamp in every variant. Sidecars sit next to their PNG because
the viewer's *Import Directory* expects them there.

## Caveats

Everything in [`simulate-image`'s caveats](./Pro3DTool-SimulateImage.md#caveats) applies
unchanged — same renderer, same shaders, same geometry. In particular there is no detector
model and no phase function, and micro-structure is shading only.
