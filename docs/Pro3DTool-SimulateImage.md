# `pro3d-tool simulate-image`

Simulated asteroid images: give it a time, SPICE kernels, an OPC and an instrument name,
and it renders the body as that instrument would plausibly see it — for preparing and
sanity-checking illumination-based reconstruction workflows (stereophotoclinometry /
shape-from-shading) of the kind used for Dimorphos ([Daly, Ernst, Barnouin et al. 2024,
PSJ](https://doi.org/10.3847/PSJ/ad0b07)) before real images exist.

Part of [`pro3d-tool`](./Pro3DTool.md) — see there for installation, test data and
**[SPICE kernel setup](./Pro3DTool.md#spice-kernels)**, which this verb requires.

```
pro3d-tool simulate-image --opc <body-opc> --time <iso8601-utc> [options]
pro3d-tool simulate-image --opc <body-opc> --mbi <image-or-sidecar> [options]
```

The second form renders through the camera an existing image's `.mbi.json`
sidecar describes, instead of the planned pointing at `--time`. Together with
`--write-mbi` (below) that turns the verb into a way to *check* the projection
chain rather than only to picture it.

## What goes into the image

- **Geometry from SPICE.** The spacecraft position comes from the SPK at `--time`, and the
  instrument's orientation — where it points **and its roll** — from the attitude the kernels
  give its frame (the CK; with `hera_plan.tm`, the *planned* attitude). The frustum is the
  instrument's (AFC: 5.5°, 1020×1020). The sun direction comes from the same kernels. The
  planned attitude need not put the rendered body in the middle of the frame, or in it at
  all — see [Pointing, aiming and roll](#pointing-aiming-and-roll).
- **Lommel-Seeliger photometry.** `I/F = albedo · 2μ₀/(μ₀+μ)` with a 5 % Lambert
  admixture — the photometric behaviour measured for Dimorphos ([Li et al. 2024,
  PSJ](https://doi.org/10.3847/PSJ/ad2b60): near-lunar scattering, minimal multiple
  scattering, p ≈ 0.16). Plain Lambert shading over-darkens the limb for regolith;
  SPC's own forward model is the closely related lunar-Lambert function.
- **De-shaded texture albedo (opt-in, `--deshade`).** OPC textures projected from real
  images (the DRACO mosaic) have illumination baked in — for `Dimorphos_DRACO1` this is
  measurable: per-vertex brightness follows the surface normal at r ≈ 0.6 with a hard
  terminator. With `--deshade`, the verb fits the baked light direction from the OPC's
  own per-vertex normals and brightness, divides it back out in the shader, and rescales
  so the mean matches `--albedo`. Where the source mosaic is shadowed, unobserved (DRACO
  saw only one hemisphere) or near its own terminator, the constant albedo is used
  instead. Off by default because the correction is approximate (see Caveats); the
  default surface is the constant `--albedo` plus micro-structure.
- **Procedural micro-structure.** The OPC is smooth at ~0.2 m/vertex; sub-mesh roughness
  is added by perturbing the shading normal with multi-octave value noise evaluated in
  body-fixed coordinates (`--micro-scale`, `--micro-amplitude`).
- **Cast shadows.** A 4096² depth map rendered from an orthographic sun camera over the
  body, sampled with PCF. `--ambient` keeps the night side barely distinguishable from
  space.
- **Auto-exposure.** I/F is rendered to float and tone-mapped so the 99.5th percentile of
  the body lands at DN 245; the applied gain is logged. Pass `--gain` to fix the exposure
  across a series.

Output is one 8-bit greyscale PNG at the instrument's native size.

## Options

| Option | Effect |
|---|---|
| `--opc <dir>` | OPC directory of the body (required) |
| `--time <iso8601>` | observation time, UTC, e.g. `2027-03-15T19:00:00Z` (required unless `--mbi` is given) |
| `--mbi <file>` | render the camera an existing image's `.mbi.json` declares; takes the image or the sidecar. Epoch, instrument and pointing all come from it |
| `--write-mbi` | also write `<out>.mbi.json` and `<out>.json`, so the render can be imported into the viewer and projected back |
| `--out <file>` | output PNG (default `./simulated.png`); with `--product` the extension is dropped and the rest is the file stem |
| `--instrument <frame>` | SPICE instrument frame (default `HERA_AFC-1`) |
| `--aim <body>` | turn the instrument onto the centre of a SPICE body instead of its planned pointing, keeping the planned roll. **The frame is then not the planned observation** — read [Aiming](#aiming-a-deliberate-departure-from-the-plan) first |
| `--product` | write the instrument's delivered product layout — a [spectral cube](#spectral-cubes-hyperscout-and-aspect) for `HERA_HSH` and `MILANI_ASPECT_NIR1` — instead of a PNG; no effect for AFC |
| `--observer <name>` | spacecraft carrying the instrument (default: the one `--instrument` flies on — `MILANI` for ASPECT, `HERA` otherwise) |
| `--body <name>` | SPICE body of the OPC (default `DIMORPHOS`) |
| `--frame <name>` | body-fixed reference frame (default `DIMORPHOS_FIXED`) |
| `--kernel <file>` | explicit metakernel (default `<kernel-root>/mk/hera_plan.tm`) |
| `--kernel-root <dir>` | SPICE kernel tree; overrides `$PRO3D_SPICE_KERNELS` |
| `--distance <m>` | camera range override, along the SPICE direction; `0` (default) uses the spacecraft's real distance |
| `--width`, `--height` | output size; `0` (default) uses the instrument's native size |
| `--albedo <v>` | normal reflectance (default `0.16`, measured for Dimorphos) |
| `--deshade` | fit + divide the baked illumination out of the OPC texture and use it as albedo; default off (constant albedo) |
| `--deshade-layer <name>` | per-vertex layer with the texture brightness (default `DRACO`) |
| `--micro-scale <m>` | micro-structure feature size in metres (default `0.5`) |
| `--micro-amplitude <v>` | normal perturbation strength; `0` disables (default `0.3`) |
| `--ambient <v>` | night-side floor (default `0.02`) |
| `--gain <v>` | fixed I/F→DN gain; `0` (default) auto-exposes |
| `--no-shadows` | skip the sun shadow map |
| `--shadow-bias <v>` | shadow depth bias (default `0.002`) |
| `--no-lighting` | flat white disk instead of a shaded body — the silhouette, for comparing pointing and shape without shading in the way |
| `--texture-only` | the OPC's own texture as this camera sees it: no lighting, no de-shading fit |
| `--texture-layer <name|index>` | which texture layer `--texture-only` draws, by name (`DRACO_2`) or index. **Default is the patch's own default layer, which is not necessarily the one a PRo3D scene displays** — a scene stores its own `selectedTexture`. An unmatched name lists what the OPC declares. |
| `--project <image>` | project this image onto the body through PRo3D's projection shader instead of shading it. With no `--mbi` the camera is that image's own, so the output must reproduce the input |
| `--project-shader <single|stack>` | which shader `--project` goes through: `single` (default, what sun-angles and the testbeds compose) or `stack` (a one-layer stack — what the viewer renders) |

## Generating a projection test set

[`scripts/make-projection-test-data.py`](../scripts/make-projection-test-data.py)
drives this verb to produce a set of AFC-1 frames with sidecars plus a PRo3D scene
set up to project them, and checks every sidecar it writes against the boresight
invariant. See [ProjectionValidation.md](./ProjectionValidation.md) for what that
data is used to prove.

## Example

Against the Hera workshop Dimorphos OPC (note the doubled folder — the OPC surface folder
is the inner one):

```
pro3d-tool simulate-image ^
  --opc  C:\data\Dimorphos_DRACO1\Dimorphos_DRACO1 ^
  --time 2027-03-15T16:00:00Z ^
  --distance 2500 ^
  --micro-scale 3 ^
  --deshade ^
  --out  dimorphos.png
```

or, against the public test data, via the script:

```
scripts\run-simulate-image.cmd <testdata>
scripts/run-simulate-image.sh  <testdata>
```

## What it produces — layer by layer

All renders: Dimorphos through AFC-1 from 2.5 km (2027-03-15T16:00Z, phase 63°,
`--distance 2500 --micro-scale 3`, HERA SKD as of 2026-08-20). This is also the
recommended validation procedure for a new dataset or a suspicious-looking image: switch
the layers off, then re-enable them one at a time.

**1. Bare shape, Lommel-Seeliger shading only**
(`--micro-amplitude 0 --no-shadows`). The ~2 m waffle pattern is the SPC model's native
resolution showing through — the mesh is oversampled from a ~2 m GSD DTM — not a
rendering artefact:

![](./images/simulateImage/1-geometry.png)

**2. + procedural micro-structure** (`--no-shadows`). Regolith-scale grain masks the DTM
waffle:

![](./images/simulateImage/2-micro.png)

**3. + cast shadows** (no flags — **the default**): constant albedo, micro-structure and
shadows. Concavities near the terminator darken:

![](./images/simulateImage/3-shadows.png)

**4. + de-shaded DRACO texture** (`--deshade`). Adds the real surface's albedo-like
mid-tone variation. The difference to (3) is deliberately subtle: measured albedo
variation on Dimorphos is small, and the de-shaded values are compressed and clamped, so
the texture modulates rather than dominates. The dark seam crossing the disk is the
**edge of the DRACO mosaic's footprint** — DART imaged only one hemisphere, and at this
geometry that boundary is in view (see Caveats):

![](./images/simulateImage/4-full.png)

Pick the epoch deliberately: the DRACO mosaic covers only the hemisphere DART saw, so at
epochs where the other side is sunlit the texture contributes little and the surface is
carried by the constant albedo plus micro-structure. Since micro-structure below the pixel
scale (≈ 0.85 m/px at 9 km for AFC) averages out, raise `--micro-scale` when rendering
from far away — or move closer with `--distance`.

## Writing an mbi sidecar

`--write-mbi` writes two files next to the PNG:

- `<out>.mbi.json` — the observation, in the convention PRo3D reads
  (see [COP-sidecar-issues.md](./COP-sidecar-issues.md#pointing--issues-5-6-and-7)):
  `SC_QUAT0..3` is the **spacecraft → J2000** quaternion, `TRG_POSX/Y/Z` is
  **target minus spacecraft** in km, J2000 axes, centred on `TARGET`.
- `<out>.json` — the statistics sidecar, whose only load-bearing content is the
  pixel size that `unproject` needs to turn a pixel into a ray.

Drop the pair into a folder, import it in the viewer's GIS tab and project it
onto the same OPC: the image lands exactly on the terrain it was rendered from.
Anything else is a real disagreement — a wrong texture layer, a shifted OPC, or
a projection bug — and no longer a question of whether the metadata was right.

The geometry is not asserted, it is measured. The sidecar is derived by
inverting the viewer's own projector chain, then read straight back through
`Visualization.projectDirect` — the same call the viewer makes — and the
disagreement is reported:

```
[mbi] round trip: boresight 0.000001 deg, worst corner 0.000 px, max matrix element 1.648e-011
```

Anything past a tenth of a pixel is a warning: the viewer would reconstruct a
different camera than the one the image was rendered with, and the projection
would not overlay. (`src/Tests/MbiSidecarTest.fs` asserts the same round trip,
and pins the convention against the three real HERA sidecars in the fixtures.)

Because the sidecar states the convention in a file that demonstrably works, it
also serves as the reference to hand to a data generator whose own sidecars do
not project.

## Spectral cubes: HyperScout and ASPECT

With `--product`, `HERA_HSH` and `MILANI_ASPECT_NIR1` are written in their delivered layout, so
the viewer and [`sample-layers`](./Pro3DTool-SampleLayers.md) read them like real data:

| Instrument | Size | Files |
|---|---|---|
| `HERA_HSH` (HyperScout 1B) | 409×217 | `<stem>_Stacked.tif`: one float TIFF, 25 planes (661–952 nm); its `.tif.json` lists the `wavelengths` |
| `MILANI_ASPECT_NIR1` (ASPECT 2B) | 640×512 | `<stem>_Vis_0.tif` … `<stem>_NIR2_12.tif`: 37 float TIFFs (675–1575 nm), each `.tif.json` with label and wavelength |

plus a `<stem>.mbi.json` listing every band file (`--write-mbi` is implied). Without `--product`
both render a PNG like AFC, at their product size instead of the old 1024×1024 fallback.

**The band values are made up**: rendered I/F times a synthetic reflectance curve (red slope,
shallow 1 µm band, 1 at 550 nm; `syntheticReflectance`). It only makes the bands differ, so a
band read from the wrong file or plane shows up. Values are float I/F, not tone-mapped
(`--gain` does not apply); sky is `0`, the delivered cubes' NoData.

All 37 ASPECT bands use the NIR1 frustum, as the delivered 2B cube is co-registered at 640×512.
Its 6.7°×5.4° field is 0.7% narrower than 640:512, hence a stretch warning; sidecar and
consumers use the same frustum, so the cube is self-consistent.

A series is one run per epoch. The multi-instrument test set
(`scripts/make-sample-layers-test-data.py`), aimed at Dimorphos:

```
for %t in (14 17 20 23) do (
  pro3d-tool simulate-image --opc Dimorphos --time 2027-03-21T%t:00:00Z --aim DIMORPHOS --instrument HERA_AFC-1 --write-mbi --gain 4.5 --out AFC\AFC1_SIM_20270321_%t0000.png
  pro3d-tool simulate-image --opc Dimorphos --time 2027-03-21T%t:00:00Z --aim DIMORPHOS --instrument HERA_HSH           --product --out HSH\HSH_SIM_20270321_%t0000.tif
  pro3d-tool simulate-image --opc Dimorphos --time 2027-03-21T%t:00:00Z --aim DIMORPHOS --instrument MILANI_ASPECT_NIR1 --product --out ASPECT\ASP_SIM_20270321_%t0000.tif
)
```

## Pointing, aiming and roll

### Planned pointing (the default)

Without `--aim`, the camera is what the kernels say at `--time`: spacecraft position and
instrument attitude, with `hera_plan.tm` the *planned* one. A plan serves its own purpose, not
the body you render. On 2027-03-21:

| Instrument | Spacecraft | Boresight off Dimorphos | Half field of view | Result |
|---|---|---|---|---|
| AFC-1 | Hera | 0.15° (mounting offset) | 2.8° | centred |
| HyperScout | Hera | 0.72° | 7.6° × 4.0° | near centre |
| ASPECT | Milani | 2.1–3.1° | 3.35° × 2.7° | **at or over the edge**; cut off at 14:00 |

Planned pointing is the honest default. Check the full frame: a crop hides a clipped body.

### Aiming: a deliberate departure from the plan

`--aim <body>` turns the planned boresight onto a body's centre by the smallest rotation, applied
to the whole instrument, so the planned roll is kept (ASPECT at 14:00: turned 3.1°, roll
unchanged to 0.1°).

> **⚠ An aimed frame is not the planned observation.** It matches no attitude in the kernels:
> do not compare it with a real image of that epoch or base pointing studies on it. Position,
> epoch, sun, roll and the sidecar (the camera actually used) stay exact, so projection and
> `sample-layers` are unaffected. Aimed frames are marked: a `WARNING: [camera] AIMED …` log
> line, and `PRO3DAIM = <body>` in the `.mbi.json`, which consumers must check. `--aim` is
> ignored, with a warning, for `--mbi`/`--project`.

> **⚠ Aiming at a body that is not rendered shows empty space.** Only the `--opc` body is drawn:
> `--aim DIDYMOS` on the Dimorphos OPC centres the frame on nothing and pushes Dimorphos aside
> (AFC-1 at 14:00: clipped at x 942–1019 of 1020). The verb warns; to image Didymos, render its OPC.

### Roll differs per instrument

Image "up" follows spacecraft attitude and instrument mounting. Dimorphos' north (body +Z) from
image up, clockwise, measured on the test set:

| Epoch (UTC) | AFC-1 (Hera) | HyperScout (Hera) | ASPECT (Milani) |
|---|---|---|---|
| 14:00 | 38.9° | 38.8° | 4.1° |
| 17:00 | 42.6° | 42.5° | 2.7° |
| 20:00 | 16.8° | 16.7° | 2.6° |
| 23:00 | −3.8° | −3.9° | 4.8° |

- **Same spacecraft, same roll**: AFC-1 and HyperScout agree to 0.1°; AFC-2 (17.0° at 20:00)
  too, as its different axis convention only undoes its physical mounting.
- **Other spacecraft, other roll and side**: Milani's roll is 9–40° from Hera's, and it sees
  Dimorphos from 55° away, i.e. a different side.
- **Roll drifts**: 46° over these 9 hours on Hera.

This is what the instruments see, not an error, and why [`sample-layers`](./Pro3DTool-SampleLayers.md)
combines images by surface point through each image's own camera. Only the fallback look-at
camera (no attitude in the kernels) invents a roll (up = body +Z), and warns.

## Caveats

- **Pointing is the plan, not the observation.** The attitude comes from the kernels loaded,
  with `hera_plan.tm` the *planned* one: no jitter, no re-planning, and not necessarily
  centred on the rendered body (see [Pointing, aiming and roll](#pointing-aiming-and-roll)).
  `--aim` departs from it on purpose. `--mbi` sidesteps both where a real observation exists:
  it takes the measured attitude from the sidecar.
- **De-shading is approximate.** The baked illumination is divided out with a Lambert
  term of a *fitted* light direction, while the true baked radiance is Lommel-Seeliger
  under an unknown acquisition geometry (and the mosaic blends several frames). Residual
  shading survives; de-shaded albedo is clamped to 0.5–2× of `--albedo` (Li et al. 2024
  found the real albedo variation to be small).
- **The DRACO texture is hemispheric.** The unobserved side falls back to constant
  albedo, so the two hemispheres differ in texture character, and when the mosaic's
  footprint edge is in view it shows as a seam — its texels ramp through dark values
  that neither the de-shade division nor the fallback blend can fully hide.
- **Results follow the kernel set.** The same command with a newer HERA SKD produces a
  different image — planning kernels are regenerated regularly and move the spacecraft
  and the body orientation. For reproducibility, record the kernel-set version (its git
  commit or the `MK_IDENTIFIER` in the metakernel) together with the logged gain.
- **Micro-structure is shading only.** Noise perturbs the normal; it casts no shadows,
  does not alter the silhouette, and is not real topography — a shape-from-shading
  inversion will happily reconstruct it as relief. That is acceptable for look-and-feel
  images and deliberate forward-model-mismatch tests, but the noise is not ground truth.
- **No detector model.** No PSF, no shot/read noise, no 12-bit quantisation — the image
  is cleaner than a real AFC frame.
- **No phase function.** `f(α)` is constant across one image and is absorbed by the
  exposure; absolute radiometry across a series needs `--gain` *and* an external `f(α)`.
- **Designed for small bodies.** The micro-structure noise and the shadow lookup evaluate
  body-frame coordinates in `float32` in the shader — comfortable for a body a few
  hundred metres across, but a deliberate deviation from PRo3D's planetary-scale
  precision rules. Do not point this verb at a Mars-sized OPC and expect clean output.

## Future work

- **Hapke photometry** behind a flag (w = 0.126, g = −0.36, θ̄ = 18° for Dimorphos), for
  low-phase realism (opposition surge) beyond Lommel-Seeliger.
- **Tessellation-based displacement** so micro-structure gains silhouettes and cast
  shadows, instead of normal perturbation.
- **Real CK pointing** (`--pointing ck`) for epochs with attitude coverage. `--mbi` already
  covers the case where a sidecar carries the measured attitude.
- **Detector chain**: PSF convolution, Poisson/read noise, 12-bit quantisation.
- **Float I/F output + provenance sidecar** for quantitative consumers, mirroring
  `sun-angles`.

