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
```

## What goes into the image

- **Geometry from SPICE.** The spacecraft position comes from the SPK at `--time`; the
  camera looks at the body centre with the instrument's frustum (AFC: 5.5°, 1020×1020).
  The sun direction comes from the same kernels. No CK is needed.
- **Lommel-Seiliger photometry.** `I/F = albedo · 2μ₀/(μ₀+μ)` with a 5 % Lambert
  admixture — the photometric behaviour measured for Dimorphos ([Li et al. 2024,
  PSJ](https://doi.org/10.3847/PSJ/ad2b60): near-lunar scattering, minimal multiple
  scattering, p ≈ 0.16). Plain Lambert shading over-darkens the limb for regolith;
  SPC's own forward model is the closely related lunar-Lambert function.
- **De-shaded texture albedo.** OPC textures projected from real images (the DRACO
  mosaic) have illumination baked in — for `Dimorphos_DRACO1` this is measurable:
  per-vertex brightness follows the surface normal at r ≈ 0.6 with a hard terminator.
  The verb fits the baked light direction from the OPC's own per-vertex normals and
  brightness, divides it back out in the shader, and rescales so the mean matches
  `--albedo`. Where the source mosaic is shadowed, unobserved (DRACO saw only one
  hemisphere) or near its own terminator, the constant albedo is used instead.
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
| `--time <iso8601>` | observation time, UTC, e.g. `2027-03-15T19:00:00Z` (required) |
| `--out <file>` | output PNG (default `./simulated.png`) |
| `--instrument <frame>` | SPICE instrument frame (default `HERA_AFC-1`) |
| `--observer <name>` | spacecraft carrying the instrument (default `HERA`) |
| `--body <name>` | SPICE body of the OPC (default `DIMORPHOS`) |
| `--frame <name>` | body-fixed reference frame (default `DIMORPHOS_FIXED`) |
| `--kernel <file>` | explicit metakernel (default `<kernel-root>/mk/hera_plan.tm`) |
| `--kernel-root <dir>` | SPICE kernel tree; overrides `$PRO3D_SPICE_KERNELS` |
| `--distance <m>` | camera range override, along the SPICE direction; `0` (default) uses the spacecraft's real distance |
| `--width`, `--height` | output size; `0` (default) uses the instrument's native size |
| `--albedo <v>` | normal reflectance (default `0.16`, measured for Dimorphos) |
| `--no-deshade` | skip the texture de-shading; constant albedo everywhere |
| `--deshade-layer <name>` | per-vertex layer with the texture brightness (default `DRACO`) |
| `--micro-scale <m>` | micro-structure feature size in metres (default `0.5`) |
| `--micro-amplitude <v>` | normal perturbation strength; `0` disables (default `0.3`) |
| `--ambient <v>` | night-side floor (default `0.02`) |
| `--gain <v>` | fixed I/F→DN gain; `0` (default) auto-exposes |
| `--no-shadows` | skip the sun shadow map |
| `--shadow-bias <v>` | shadow depth bias (default `0.002`) |

## Example

Against the Hera workshop Dimorphos OPC (note the doubled folder — the OPC surface folder
is the inner one):

```
pro3d-tool simulate-image ^
  --opc  C:\data\Dimorphos_DRACO1\Dimorphos_DRACO1 ^
  --time 2027-03-15T19:00:00Z ^
  --distance 2500 ^
  --micro-scale 3 ^
  --out  dimorphos.png
```

or, against the public test data, via the script:

```
scripts\run-simulate-image.cmd <testdata>
scripts/run-simulate-image.sh  <testdata>
```

## What it produces — layer by layer

All renders: Dimorphos through AFC-1 from 2.5 km (2027-03-15T19:00Z, phase 70°,
`--distance 2500 --micro-scale 3`). This is also the recommended validation procedure for
a new dataset or a suspicious-looking image: switch the layers off, then re-enable them
one at a time.

**1. Bare shape, Lommel-Seiliger shading only**
(`--no-deshade --micro-amplitude 0 --no-shadows`). The ~2 m waffle pattern is the SPC
model's native resolution showing through — the mesh is oversampled from a ~2 m GSD DTM —
not a rendering artefact:

![](./images/simulateImage-1-geometry.png)

**2. + procedural micro-structure** (`--no-deshade --no-shadows`). Regolith-scale grain
masks the DTM waffle:

![](./images/simulateImage-2-micro.png)

**3. + cast shadows** (`--no-deshade`) — the full renderer minus the texture, i.e. the
**constant-albedo variant**. Concavities near the terminator darken; use this when the
de-shading residuals are unwanted, or on OPCs whose texture has no usable brightness
layer:

![](./images/simulateImage-3-shadows.png)

**4. + de-shaded DRACO texture** (no flags — the default). Adds the real surface's
albedo-like mid-tone variation. At this epoch the difference to (3) is deliberately
subtle: measured albedo variation on Dimorphos is small, and the de-shaded values are
compressed and clamped, so the texture modulates rather than dominates:

![](./images/simulateImage-dimorphos.png)

Pick the epoch deliberately: the DRACO mosaic covers only the hemisphere DART saw, so at
epochs where the other side is sunlit the texture contributes little and the surface is
carried by the constant albedo plus micro-structure. Since micro-structure below the pixel
scale (≈ 0.85 m/px at 9 km for AFC) averages out, raise `--micro-scale` when rendering
from far away — or move closer with `--distance`.

## Caveats

- **Pointing is look-at, not CK.** The boresight is aimed at the body centre and the roll
  around it follows an up-vector convention. Real AFC pointing (and its jitter) would come
  from a CK; the frame edge and rotation of a real image will differ.
- **De-shading is approximate.** The baked illumination is divided out with a Lambert
  term of a *fitted* light direction, while the true baked radiance is Lommel-Seeliger
  under an unknown acquisition geometry (and the mosaic blends several frames). Residual
  shading survives; de-shaded albedo is clamped to 0.5–2× of `--albedo` (Li et al. 2024
  found the real albedo variation to be small).
- **The DRACO texture is hemispheric.** The unobserved side falls back to constant
  albedo, so the two hemispheres differ in texture character.
- **Micro-structure is shading only.** Noise perturbs the normal; it casts no shadows,
  does not alter the silhouette, and is not real topography — a shape-from-shading
  inversion will happily reconstruct it as relief. That is acceptable for look-and-feel
  images and deliberate forward-model-mismatch tests, but the noise is not ground truth.
- **No detector model.** No PSF, no shot/read noise, no 12-bit quantisation — the image
  is cleaner than a real AFC frame.
- **No phase function.** `f(α)` is constant across one image and is absorbed by the
  exposure; absolute radiometry across a series needs `--gain` *and* an external `f(α)`.

## Future work

- **Hapke photometry** behind a flag (w = 0.126, g = −0.36, θ̄ = 18° for Dimorphos), for
  low-phase realism (opposition surge) beyond Lommel-Seiliger.
- **Tessellation-based displacement** so micro-structure gains silhouettes and cast
  shadows, instead of normal perturbation.
- **Real CK pointing** (`--pointing ck`) for epochs with attitude coverage.
- **Detector chain**: PSF convolution, Poisson/read noise, 12-bit quantisation.
- **Float I/F output + provenance sidecar** for quantitative consumers, mirroring
  `sun-angles`.
