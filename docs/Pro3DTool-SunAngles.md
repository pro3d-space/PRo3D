# `pro3d-tool sun-angles`

Per-pixel illumination geometry for instrument images.

Part of [`pro3d-tool`](./Pro3DTool.md) — see there for installation, test data and
**[SPICE kernel setup](./Pro3DTool.md#spice-kernels)**, which this verb requires.

```
pro3d-tool sun-angles --opc <body-opc> --images <image-folder> [options]
```

For each instrument image, the body is rendered as that instrument saw it, and the local
illumination geometry is written out as float32 rasters that are **pixel-aligned to the
source image**.

## Output per image

| File | Contents |
|---|---|
| `<image>_incidence.tif` | angle between the surface normal and the direction to the Sun |
| `<image>_emission.tif` | angle between the surface normal and the direction to the observer |
| `<image>_phase.tif` | Sun–surface–observer angle |
| `<image>_angles.json` | provenance: epoch, body, frame, observer, kernel, units, caveats |
| `<image>_<angle>_color.png` | false-colour rendering, with `--false-color` |

The rasters are single-band float32 **in radians**, with **NaN as nodata** where no surface
was rasterised. They are plain TIFFs with **no georeferencing tags** — an instrument image
frame has no map projection to encode — and are read as unreferenced float rasters by GDAL,
QGIS, `tifffile`, PIL and anything else that speaks baseline TIFF.

## Options

| Option | Effect |
|---|---|
| `--opc <dir>` | OPC directory of the body (required) |
| `--images <dir>` | folder of instrument images with `.mbi.json` sidecars (required) |
| `--image <file>` | process only this image; default is every image in the folder |
| `--out <dir>` | output directory (default `./sun-angles`) |
| `--body <name>` | SPICE body of the OPC (default `DIDYMOS`) |
| `--frame <name>` | body-fixed reference frame (default `DIDYMOS_FIXED`) |
| `--observer <name>` | observing spacecraft (default `MILANI`) |
| `--kernel <file>` | explicit metakernel, overriding the sidecar's declaration |
| `--kernel-root <dir>` | SPICE kernel tree; overrides `$PRO3D_SPICE_KERNELS` |
| `--method spice\|mbi` | projection method (default `mbi`) |
| `--width`, `--height` | output size; `0` (default) uses the source image's native size |
| `--false-color` | also write one false-colour PNG per angle |

Batch is the normal mode: point `--images` at a folder and every image with a sidecar is
processed, reusing one SPICE kernel and one render context for the whole run. A single image
failing is reported and the run continues; the exit code is non-zero if any failed.

Leaving `--width`/`--height` at `0` is what guarantees pixel alignment. The instrument
frustum's aspect ratio is fixed, so rendering into a differently-shaped viewport stretches
the result; the tool warns when the requested size differs from the source.

## Example

```
pro3d-tool sun-angles ^
  --opc     <testdata>\HERA\Didymos_ASPECT ^
  --images  "<testdata>\HERA\Instrument Data" ^
  --out     .\sun-angles ^
  --false-color
```

or via the script, which passes exactly that:

```
scripts\run-sun-angles.cmd <testdata>
scripts/run-sun-angles.sh  <testdata>
```

## What it produces

The ASPECT frame that goes in — Didymos, with Dimorphos at the upper left:

![](./images/sunAngles-source.png)

Incidence, false-coloured. Blue is near-perpendicular illumination, red is grazing; the
low-angle region sits offset towards the Sun:

![](./images/sunAngles-incidence.png)

Emission, false-coloured. Blue where the surface faces the instrument, red at the limb.
Note it is roughly radially symmetric about the centre of the disc, whereas incidence is
not — that difference is the quickest check that the two are being computed independently:

![](./images/sunAngles-emission.png)

**Only the body named by `--opc` is rendered.** Dimorphos is visible in the source frame but
absent from the rasters, since a single OPC was given. This is also why coverage is a third
of the frame rather than all of it: everything off the body is nodata.

The float32 TIFFs carry the actual radian values; these PNGs exist only to make them
reviewable.

## Making the result interpretable — `--false-color`

Radians in a float TIFF are the data product, but they are not reviewable at a glance.
`--false-color` additionally writes one PNG per angle using a **blue → cyan → green →
yellow → red** ramp, where blue is 0 and red is full scale (90° for incidence and emission,
180° for phase). Hue boundaries produce readable iso-angle contours in a way a grey ramp
does not.

This is the **same colour ramp** PRo3D.ProjectionTestbed uses for its angle visualisations —
the function lives in `PRo3D.GIS` and is shared by both, so a tool output and a testbed
render of the same scene are directly comparable rather than merely similar.

Two conventions when reading these images:

- **Nodata is black**, which sits outside the ramp and so cannot be mistaken for a low
  angle.
- On the **emission** image, pixels above 90° are painted **magenta** rather than
  colour-mapped. Expect a thin magenta rim at the limb where facets are near edge-on;
  magenta anywhere else points at an inconsistent shape model.

## Important caveats

**Terrain self-shadowing is not evaluated.** These are *local* illumination angles, computed
from the surface normal and the sun/observer directions at each pixel. A point lying in the
shadow of nearby relief still reports its geometric incidence angle. Combine with a shadow
test if you need actual illumination rather than illumination geometry.

**Emission angles above 90° are preserved, not clamped.** They mean a facet facing away from
the observer was nevertheless rasterised — expected along the limb, and otherwise a sign of
an inconsistent shape model.

Both notes are repeated in every output's JSON sidecar, because a float raster separated
from its provenance is easy to misread.

**A substituted metakernel is logged, loudly.** Sidecars routinely name a kernel version that
is not on disk. When that happens the tool falls back to `hera_plan.tm` and says so — the
geometry may differ from the image, so the warning is worth reading rather than scrolling
past.
