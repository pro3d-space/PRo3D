## 0.2.0
- `kdtree` — `--degreesofparallelism` now treats `0` (the default, i.e. the option unspecified) and `-1` as "use all available cores"; `1` is single threaded. Previously `0` meant single threaded, which `1` already covered, and made sequential the accidental default. Reported by a tester
- docs — corrected the `kdtree` option spellings: the options are lower-case (`--skippatchvalidation`, `--ignoremasterkdtree`, `--degreesofparallelism`), and the camel-case forms the docs previously showed are rejected

## 0.1.0
First release of `pro3d-tool`, which supersedes `opc-tool`.

- `kdtree` — validates OPC directories and generates KdTrees. Carried over from `opc-tool` with unchanged option names, so `opc-tool <options> <dir>` becomes `pro3d-tool kdtree <options> <dir>`. Needs no GPU and no display
- `kdtree` — fixes an inverted guard inherited from `opc-tool`: patch validation ran only when `--skippatchvalidation` was passed, and `--generatedds` could never produce a DDS file because the conversion was skipped precisely when it was requested
- `sun-angles` — writes per-pixel incidence, emission and phase angles for instrument images as single-band float32 TIFFs in radians, pixel-aligned to the source image, with NaN as nodata and a JSON provenance sidecar. Batch by default over a folder of images
- `sun-angles` — `--false-color` additionally writes one PNG per angle, using the same colour ramp as PRo3D.ProjectionTestbed so results are directly comparable
- `sun-angles` — SPICE kernels come from `--kernel-root` or `$PRO3D_SPICE_KERNELS`; there is no implicit default, so it fails when no kernel tree is given rather than computing geometry from kernels the caller did not choose
