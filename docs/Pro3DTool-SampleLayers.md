# `pro3d-tool sample-layers`

Assemble observations from several instruments — AFC, ASPECT, HyperScout — onto the surface of
a body, as one dataset keyed by surface point.

Part of [`pro3d-tool`](./Pro3DTool.md) — see there for installation, test data and
**[SPICE kernel setup](./Pro3DTool.md#spice-kernels)**, which this verb requires.

```
pro3d-tool sample-layers --opc <body-opc> --images <folder> [<folder> ...] --out <dir> [options]
```

Every instrument has its own pixel grid, field of view, resolution and viewpoint, so its
pixels cannot be compared with another instrument's pixels directly. What they have in common
is the body. This verb fixes a set of surface points — in stage 1, **every vertex of the shape
model** — and for each point and each image works out:

- whether the image **sees** the point (in the frame, facing the camera, not hidden by terrain),
- **where** in the image it lands (continuous pixel coordinates),
- the **illumination geometry** there (incidence, emission, phase),
- the value of **every band** at the nearest pixel.

Each result is written against the point's id, so every instrument and every epoch line up by
construction. Per-vertex layers the OPC itself carries (slope, gravity, …) are written against
the same ids.

![three instruments, three pixel grids](./images/sample-layers-inputs.png)

![the same vertices, sampled from each](./images/sample-layers-assembled.png)

These figures, and every excerpt on this page, come from a real run over the simulated test set
(see [Test data](#test-data)). `scripts/make-sample-layers-figures.py` regenerates them.

## Example

```
pro3d-tool sample-layers ^
  --opc    PRo3D.Resources.TestData\HERA\Dimorphos_opc\Dimorphos ^
  --images PRo3D.Resources.TestData\HERA\Dimorphos_opc\SampleLayers_2027-03-21\AFC ^
           PRo3D.Resources.TestData\HERA\Dimorphos_opc\SampleLayers_2027-03-21\ASPECT ^
           PRo3D.Resources.TestData\HERA\Dimorphos_opc\SampleLayers_2027-03-21\HSH ^
  --out    sample-layers
```

```
[images] 12 observation(s) in 3 folder(s)
[body] DIMORPHOS in DIMORPHOS_FIXED
[layers] 2328412 vertices (0.9 s)
[layers] 6 kd-tree(s) loaded
[out] vertices.csv
[out] attributes/Slope.csv
[AFC1_SIM_20270321_140000] 2328412 vertices in the frame, 1165125 facing the camera, 1103763 of those unoccluded
[AFC1_SIM_20270321_140000] HERA_AFC-1 at 2027-03-21T14:00:00.0000000Z: 1103763 vertices seen, 1 band(s) (4.6 s)
...
[ASP_SIM_20270321_200000] MILANI_ASPECT_NIR1 at 2027-03-21T20:00:00.0000000Z: 1136050 vertices seen, 37 band(s) (9.0 s)
...
[HSH_SIM_20270321_230000] HERA_HSH at 2027-03-21T23:00:00.0000000Z: 1122653 vertices seen, 25 band(s) (6.3 s)
```

The whole set — 2.3 million vertices, 12 observations, 252 band columns — takes about 75 s. No GPU is
needed. The OPC must have kd-trees; build them once with
[`pro3d-tool kdtree`](./Pro3DTool-KdTree.md).

## Options

| Option | Effect |
|---|---|
| `--opc <dir>` | OPC directory of the body (required) |
| `--images <dir> [<dir> ...]` | one or more folders of observations (required); every `.mbi.json` in them is one observation |
| `--out <dir>` | output directory (default `./sample-layers`) |
| `--attributes <a,b,...>` | per-vertex OPC layers to write, comma separated (default `Slope`; `none` for none). Case-insensitive; an unknown name fails the run and lists what the OPC has |
| `--body <name>` | SPICE body of the OPC (default: the images' `TARGET`, e.g. `DIMORPHOS`) |
| `--frame <name>` | body-fixed frame the OPC is in (default `<body>_FIXED`) |
| `--observer <name>` | spacecraft; default follows the instrument per image (ASPECT → `MILANI`, else `HERA`) |
| `--kernel <file>` | explicit metakernel; default is the one the first sidecar names |
| `--kernel-root <dir>` | SPICE kernel tree; overrides `$PRO3D_SPICE_KERNELS` |
| `--method <spice\|mbi>` | projection method, as in [`unproject`](./Pro3DTool-Unproject.md) (default `mbi`) |
| `--occlusion-tolerance <m>` | how far in front of a vertex something must be to hide it (default `0.05`) |

The body defaults to what the images say they observed rather than to a fixed name. Sampling a
stack of Dimorphos frames against `DIDYMOS_FIXED` would produce plausible-looking nonsense.

## Input: observations

An observation is one `.mbi.json` sidecar with the band files it declares (`mbi_bands` in
ASPECT exports, `bands` elsewhere). A sidecar that declares no usable file falls back to the
image named like it (`X.mbi.json` → `X.png`/`X.tif`), which is how the COP delivery works. The
three HERA layouts are all read as delivered:

| Instrument | Layout | Columns in the output |
|---|---|---|
| AFC-1/-2 | one 8-bit PNG (or TIFF) | one, named after the file (raw DN, not normalised) |
| ASPECT 2B | one single-band float TIFF per band | one per band, named by its label (`Vis_0` … `NIR2_12`) |
| HyperScout 1B | one float TIFF holding 25 planes (`_Stacked.tif`) | one per plane (`Stacked_0` … `Stacked_24`) |

The pointing comes from the sidecar through the viewer's own reader, exactly as in
[`unproject`](./Pro3DTool-Unproject.md#what-the-image-folder-needs), so the same folders work in
both. All bands of one observation must share one pixel grid; an observation whose bands do not
is reported and skipped.

## Output

```
sample-layers/
  vertices.csv                   id, x, y, z             -- every surface point, once
  attributes/Slope.csv           id, Slope               -- one file per --attributes layer
  images/<observation>.csv       id, image coords, angles, bands -- one file per observation
  manifest.json                  what is where: images, instruments, epochs, band wavelengths
```

Complete excerpts are in [`docs/examples/sample-layers/`](./examples/sample-layers/).

### `vertices.csv`

Every vertex of the finest level of detail, in the body-fixed frame, metres. The row index is
the id.

```
id,x,y,z
0,0.00000,0.00000,57.10000
1,-0.16609,0.00000,57.09905
2,-0.16609,-0.00097,57.09905
```

Each surface point appears **once**. An OPC stores more than that: neighbouring patches share
their edges, and a global lon/lat grid collapses a whole row onto each pole and doubles the
0/360° seam. Vertices are therefore merged by position, to 1 mm. The Dimorphos OPC has 2 342 530
valid grid vertices, which merge into 2 328 412 points. Only vertices under the attribute grid are
taken. The position grid's skirt duplicates the neighbour's geometry and carries no attributes.

### `attributes/<layer>.csv`

A per-vertex layer of the OPC, against the same ids. Multi-component layers get one column per
component (`Gravity_0,Gravity_1,Gravity_2`). A vertex whose patch lacks the layer is empty.

```
id,Slope
0,0.360530615
1,0.382033587
```

### `images/<observation>.csv`

One row per vertex the observation **sees**. Vertices it does not see have no row.

```
id,imageCoordX,imageCoordY,incidence_deg,emission_deg,phase_deg,AFC1_SIM_20270321_200000
483986,564.734,423.241,23.3778,49.8239,33.3411,207
659123,556.389,487.991,38.4388,6.0704,32.9894,159
864290,563.726,571.908,94.4795,62.6612,32.5344,3
```

```
id,imageCoordX,imageCoordY,incidence_deg,emission_deg,phase_deg,Vis_0,Vis_1,...,NIR2_12
483986,16.717,313.227,23.3778,30.0049,47.2654,0.170540929,0.171593338,...
```

```
id,imageCoordX,imageCoordY,incidence_deg,emission_deg,phase_deg,Stacked_0,Stacked_1,...,Stacked_24
483986,228.743,94.364,23.3778,49.8239,33.3411,0.197273195,0.198021531,...
```

| Column | Meaning |
|---|---|
| `id` | the vertex, as in `vertices.csv` |
| `imageCoordX`, `imageCoordY` | where the vertex projects, **0-based, origin top-left, integers at pixel centres** — the `image` convention of [`unproject`](./Pro3DTool-Unproject.md#pixel-convention). Continuous: feeding the row back to `unproject` returns the vertex |
| `incidence_deg` | angle between the vertex normal and the sun direction at the image's epoch |
| `emission_deg` | angle between the vertex normal and the direction to the camera |
| `phase_deg` | angle between sun and camera, seen from the vertex |
| one column per band | the value at the **nearest pixel** (the one whose centre is closest), as stored: float TIFFs in their own units, 8/16-bit images as raw DN |

Vertex 483986 appears in all three. Its incidence depends only on the epoch and the sun, so it is
the same in every row. Emission and phase are the same for AFC and HyperScout, both of which fly
on Hera, and differ for ASPECT, which flies on Milani.

Together, the per-image files give one surface point every instrument's view of it. Joining the
ASPECT and HyperScout rows of one vertex by `id` gives its spectrum across both instruments:

![one point, two instruments' bands](./images/sample-layers-spectrum.png)

### `manifest.json`

What the run used and where each file came from: OPC, body, frame, metakernel, and per image
its sidecar, instrument, SPICE frame, observer, epoch, size, range and number of vertices seen,
plus every band column with its file, plane and wavelength. A downstream tool should read the
band wavelengths from here rather than parse column names.

## What "seen" means

A vertex is seen by an observation when all of these hold:

1. **In front of the camera and inside the frame.** The vertex is projected through the
   observation's camera, the same one the viewer projects that image with.
2. **Facing the camera.** Its normal (the OPC's per-vertex `Normal` layer, oriented outwards)
   points towards the camera.
3. **Not occluded.** A ray from the camera to the vertex, cast through the OPC's kd-trees in
   double precision, meets nothing more than `--occlusion-tolerance` in front of the vertex.
   The ray ends on the surface, so without a tolerance a vertex would hide behind its own
   triangles.

The log prints the count after each step. On Dimorphos from 7 to 8 km, occlusion removes 3–6% of
the vertices that face the camera: crater walls and boulders hiding what lies behind them.

![coverage of the combined dataset](./images/sample-layers-coverage.png)

The tests check visibility against an independent ray cast. For a strided subset of the seen
vertices, the ray through the reported pixel may meet nothing in front of the vertex.

## How it was checked

On the simulated test set, whose sidecars describe the exact render camera:

| Check | Result |
|---|---|
| each ASPECT/HyperScout band divided by the spectrum it was simulated with, per vertex | identical across all bands to 5·10⁻⁸ — every band comes from the right file and plane |
| sampled value vs. the Lommel-Seeliger term `cos i / (cos i + cos e)` from the output angles | correlation 0.89–0.97, the rest being the simulator's micro-structure and cast shadows |
| AFC vs. HyperScout at the same epoch, per vertex | correlation 0.92–0.99 |
| well-lit vertices (i < 60°, e < 70°) that sample a 0 (sky) pixel | 0, for every instrument. Zeros occur only at grazing emission, where the nearest pixel is off the limb |
| 300 sampled `(image, x, y)` rows fed to [`unproject`](./Pro3DTool-Unproject.md) | back to the vertex: median 0.8 mm, p99 3 cm |
| two runs | byte-identical output (the kd-tree queries run in parallel) |

## Test data

`PRo3D.Resources.TestData/HERA/Dimorphos_opc/SampleLayers_2027-03-21/` holds simulated
observations of Dimorphos by all three instruments at 14:00, 17:00, 20:00 and 23:00 UTC,
rendered from the OPC next to it:

```
AFC/     AFC1_SIM_<epoch>.png (+ .png.json, .mbi.json)            HERA/AFC-1, 1020x1020
ASPECT/  ASP_SIM_<epoch>_<band>.tif (+ .tif.json), .mbi.json       Milani/ASPECT, 37 x 640x512
HSH/     HSH_SIM_<epoch>_Stacked.tif (+ .tif.json), .mbi.json      HERA/HyperScout, 25 x 409x217
```

The ASPECT and HyperScout values are the render's I/F times a made-up spectrum. They check
where bands land and are no use as spectroscopy — see
[simulate-image](./Pro3DTool-SimulateImage.md#spectral-cubes-hyperscout-and-aspect).
`scripts/make-sample-layers-test-data.py` regenerates the set.

The observations import into the viewer like delivered data (GIS tab → Projected Images →
Import Directory, one folder at a time) and project onto the same OPC. Rendered through the
viewer's projection shader from their own camera, each reproduces its input image:

| Layout | Reproduction |
|---|---|
| AFC | exact |
| ASPECT | correlation 0.9998, p95 difference under 1 DN |
| HyperScout | correlation 0.9998, p95 difference under 1 DN |

At these ranges Dimorphos is only 40–50 pixels across in ASPECT and HyperScout, which is the
real geometry and not a flaw of the data.

## Caveats

- **Stage 1 samples at vertices only.** The points are the OPC's own vertices, a 1.96 m grid
  on Dimorphos (denser towards the poles). Sampling at arbitrary points or on a regular grid is not implemented yet.
- **Nearest pixel, no footprint.** Every band is read at one pixel. An 8.6 m HyperScout pixel
  therefore gives the same value to every vertex under it. The blocks in the figure above are
  this, not an error. No resampling or area weighting is done.
- **Vertex normals.** Facing and the incidence/emission angles use the OPC's per-vertex normal.
  [`sun-angles`](./Pro3DTool-SunAngles.md) uses face normals, so the two differ slightly on
  rough terrain. An OPC without a `Normal` layer gets no facing test (occlusion still applies)
  and empty incidence/emission.
- **Cast shadows are not flagged.** `incidence_deg` is local. A vertex in the shadow of a
  boulder has a normal incidence but a dark value. Flagging it would take a sun-side occlusion
  test.
- **One metakernel per run.** SPICE holds one at a time, so the whole run uses the one the first
  sidecar names (or `--kernel`).
- **Size.** Values are written at full float precision. An ASPECT observation of 1.1 million
  seen vertices × 37 bands is about 550 MB of CSV. A columnar format (Parquet) would be a
  natural next step.

## Future work

- Stage 2: sample at arbitrary points (a regular lon/lat grid, or user-supplied points), not
  only at vertices.
- A cast-shadow flag per vertex and image, from a sun-side ray cast through the same kd-trees.
- Bilinear sampling, or pixel-footprint integration, as an option next to nearest-pixel.
- Parquet output.
