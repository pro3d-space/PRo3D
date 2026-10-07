## 0.5.0
First release on NuGet since 0.2.0: 0.3.0 and 0.4.0 below were never published, so this is the first package containing `unproject`, `simulate-image` and the `kdtree` fixes.

- `sample-layers` — new verb: assembles observations from several instruments onto a body, keyed by shape-model vertex. For every OPC vertex and every image it decides whether the image sees it (in frame, facing, not occluded by the terrain), where it lands in the image, the incidence/emission/phase angles, and every band's value at the nearest pixel. Writes `vertices.csv`, one CSV per image, one CSV per requested OPC layer (`--attributes`, default `Slope`) and a `manifest.json` with epochs, instruments and band wavelengths. Reads AFC PNGs, ASPECT 2B per-band TIFFs and HyperScout 1B stacked TIFFs as delivered. Needs SPICE kernels and kd-trees; no GPU
- `simulate-image` — `--product` writes HyperScout (one 25-plane float `_Stacked.tif`) and ASPECT (37 single-band float TIFFs) in their delivered layout, with a synthetic spectrum so that bands differ; without it these instruments render a PNG at their product size
- `simulate-image` — `--aim <body>` turns the planned boresight onto a body, keeping the planned roll. An aimed frame is not the planned observation: every aimed render warns, and its sidecar carries `PRO3DAIM`
- `simulate-image` — the camera takes position, pointing and roll from the kernels' attitude for the instrument frame (the planned attitude with `hera_plan.tm`) instead of a look-at convention; the image-axis convention is per instrument. `--observer` defaults to the spacecraft carrying `--instrument` (MILANI for ASPECT)
- `simulate-image` — `--write-mbi` writes an `.mbi.json` describing the exact render camera, verified against the viewer's own reader; `--mbi` renders the camera an existing image's sidecar describes; `--project` renders an image through PRo3D's projection shader (`--project-shader single|stack`); `--texture-layer` and `--texture-only` render a chosen OPC texture layer
- fix — `sun-angles` and `simulate-image` could hang when their GL runtime had been created on another thread (seen when called from a host such as the test suite; the command line was not affected)

## 0.4.0
- `simulate-image` — new verb: renders a simulated instrument image of a body at a SPICE time (spacecraft position from SPK, look-at pointing, instrument frustum). Lommel-Seeliger + 5% Lambert photometry per the measured Dimorphos behaviour (Li et al. 2024), procedural value-noise micro-structure via normal perturbation, cast shadows from an orthographic sun depth map, deterministic auto-exposure to an 8-bit greyscale PNG. `--deshade` (off by default, the correction is approximate) fits and divides out the illumination baked into the OPC texture and uses it as albedo, with constant-albedo fallback where the mosaic is shadowed or unobserved. `--distance` overrides the camera range along the SPICE direction for close-ups and layer-by-layer validation (`--micro-amplitude 0 --no-shadows`, then re-enable one layer at a time)

## 0.3.0
- `unproject` — converts a table of `image, x, y` rows into body-fixed surface coordinates by unprojecting each pixel through its instrument camera and intersecting the shape model. Output keeps one row per input row, in input order, with the input's own columns carried through, plus x/y/z, lat/lon/alt, range, and one column per per-vertex attribute layer the OPC carries. Needs SPICE kernels and pre-built kd-trees; no GPU
- `unproject` — the observing spacecraft is derived per image from its instrument, so one input file may mix AFC (Hera) and ASPECT (Milani) images; `--observer` overrides it
- `unproject` — `--pixel-convention image|fits` declares how the input addresses pixels (0-based top-left, or 1-based bottom-left). It cannot be inferred from the imagery, so it is declared and echoed in the log
- `kdtree` — `--degreesofparallelism` now treats `0` (the default, i.e. the option unspecified) and `-1` as "use all available cores"; `1` is single threaded. Previously `0` meant single threaded, which `1` already covered, and made sequential the accidental default. Reported by a tester
- docs — corrected the `kdtree` option spellings: the options are lower-case (`--skippatchvalidation`, `--ignoremasterkdtree`, `--degreesofparallelism`), and the camel-case forms the docs previously showed are rejected

## 0.2.0
Published from the `unproject` branch ahead of the merge, so it is a partial release: it
contains the `unproject` verb but none of the `kdtree` changes listed under 0.3.0. Prefer
0.3.0, which is the first release containing both.

## 0.1.0
First release of `pro3d-tool`, which supersedes `opc-tool`.

- `kdtree` — validates OPC directories and generates KdTrees. Carried over from `opc-tool` with unchanged option names, so `opc-tool <options> <dir>` becomes `pro3d-tool kdtree <options> <dir>`. Needs no GPU and no display
- `kdtree` — fixes an inverted guard inherited from `opc-tool`: patch validation ran only when `--skippatchvalidation` was passed, and `--generatedds` could never produce a DDS file because the conversion was skipped precisely when it was requested
- `sun-angles` — writes per-pixel incidence, emission and phase angles for instrument images as single-band float32 TIFFs in radians, pixel-aligned to the source image, with NaN as nodata and a JSON provenance sidecar. Batch by default over a folder of images
- `sun-angles` — `--false-color` additionally writes one PNG per angle, using the same colour ramp as PRo3D.ProjectionTestbed so results are directly comparable
- `sun-angles` — SPICE kernels come from `--kernel-root` or `$PRO3D_SPICE_KERNELS`; there is no implicit default, so it fails when no kernel tree is given rather than computing geometry from kernels the caller did not choose
