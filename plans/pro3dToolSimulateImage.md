# pro3d-tool `simulate-image` — design notes

Status: implemented (tool release 0.3.0). User docs: [docs/Pro3DTool-SimulateImage.md](../docs/Pro3DTool-SimulateImage.md).

## Goal

Scientists reconstruct asteroid shapes from image *illumination* (stereophotoclinometry,
SPC — Gaskell/Palmer; used by Daly/Ernst/Barnouin for Dimorphos) rather than
photogrammetry. To support preparing such workflows we want a generator that takes
**time + SPICE kernels + OPC + instrument name** and produces a plausible simulated
asteroid image — like the `sun-angles` verb, but producing a shaded picture instead of
angle backplanes.

## Research that shaped the design

- **SPC's forward model is lunar-Lambert against a normalized-albedo layer** (Palmer et
  al. 2022, PSJ, doi:10.3847/PSJ/ac460f). Plain Lambert over-darkens the limb for
  regolith.
- **Dimorphos photometry is measured** (Li et al. 2024, PSJ, doi:10.3847/PSJ/ad2b60):
  nearly pure Lommel-Seliger with a ~5 % Lambert admixture, geometric albedo 0.16,
  minimal albedo variation, Hapke w = 0.126, g = −0.36, θ̄ ≈ 18°.
- **The DRACO texture in `Dimorphos_DRACO1` has illumination baked in** — verified
  numerically on the data: per-vertex brightness vs n·L correlates r ≈ 0.64 over a ~4.6×
  ramp with a hard terminator, and the mosaic covers only the hemisphere DART saw. It is
  radiance, not albedo; draping it unprocessed double-shades the body.
- **AFC** (hera_afc_v06.ti): 5.5°×5.5°, 1020×1020, panchromatic — matches the frustum
  table in `PRo3D.Base.InstrumentProjection`.
- Prior art for exactly this pipeline: Caballo Perucha et al. 2020 (EPSC2020-123)
  rendered 731 simulated AFC frames of Didymos with PRo3D for SfM reconstruction.

## Decisions (agreed 2026-08-19)

| Topic | Decision |
|---|---|
| Photometry | Lommel-Seeliger + fixed 5 % Lambert, normalized so i=e=0 → albedo. Phase function omitted (constant per image, absorbed by exposure). Hapke = future flag. |
| Albedo | De-shade the OPC texture in-shader: fit the baked light direction from per-vertex `Normal.aara` × brightness layer (linear least squares, two passes), divide by the clamped Lambert term, rescale to `--albedo`, clamp to 0.5–2× (Li et al.: real variation is small), fall back to constant albedo where the mosaic is dark/unobserved. |
| Micro-structure | Tangent-free normal perturbation from the gradient of 4-octave value noise on body-fixed coordinates. Tessellation displacement deferred. |
| Shadows | 4096² depth map from an orthographic sun camera over the body bbox, 2×2 PCF. Note: `Frustum.ortho` takes the view-space box Z verbatim — near/far must be negated when the camera sits outside the box. |
| Pointing | Look-at body centre; position from SPK (`getRelState`), no CK. Roll is an up-vector convention. |
| Kernels | `--kernel-root` / `$PRO3D_SPICE_KERNELS` via the shared `resolveKernelRoot`; metakernel = `--kernel` or `mk/hera_plan.tm`. |
| Output | One 8-bit greyscale PNG, instrument-native size, deterministic auto-exposure (99.5th percentile → DN 245, gain logged). No sidecar/float output in v1. |

## Structure

- `src/PRo3D.Tool/Cli.fs` — `SimulateImageOptions`
- `src/PRo3D.Tool/SimulateShaders.fs` — value noise, de-shade + LS lighting + PCF shadow fragment, shadow-position vertex stage
- `src/PRo3D.Tool/SimulateImage.fs` — de-shade fit, shadow pass, SPICE-only camera, tonemap, verb `run`
- Shares with sun-angles: `FloatTarget`, `withOpcScaffolding`, `resolveKernelRoot`, `patchHierarchiesOf`, `OpcSg.build` (asyncLoading = false), `InstrumentObservation.sunDirection`, the `Sg.ProjectedImages` sun-direction path, and the test pattern (public `processImage`, `run` owns the SPICE lifetime).

## Out of scope (documented as future work)

Hapke photometry, tessellation displacement, CK pointing, detector effects (PSF, noise,
12-bit), float I/F + provenance sidecar, photometrically rigorous albedo maps.
