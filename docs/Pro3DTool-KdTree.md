# `pro3d-tool kdtree`

Validate OPC directories and generate KdTrees.

Part of [`pro3d-tool`](./Pro3DTool.md) — see there for installation and test data.

```
pro3d-tool kdtree [options] <surface-directory>
```

KdTrees are the spatial acceleration structures PRo3D uses for picking and measurement on
OPC surfaces (see [KdTrees.md](./KdTrees.md)). Generating them ahead of time turns a slow
first interaction with a surface into an instant one, which matters for large datasets and
for anything running unattended.

## Options

| Option | Effect |
|---|---|
| `--forcekdtreerebuild` | Rebuild and overwrite existing kd-trees |
| `--ignoreMasterKdTree` | Ignore master kd-trees; load or create per-patch kd-trees and the lazy kd-tree cache |
| `--generatedds` | Convert patch textures to DDS |
| `--overwritedds` | Overwrite existing DDS files (only meaningful with `--generatedds`) |
| `--skipPatchValidation` | Skip patch validation (textures, aara files) |
| `--degreesOfParallelism <n>` | Process this many hierarchies concurrently; `0` means single threaded |
| `--verbose` | Print all messages to standard output |

The positional argument is either a single OPC hierarchy or a directory containing several.
When it is a container, every immediate subdirectory is checked, and non-OPC folders are
reported and skipped rather than failing the run.

## Examples

Against the test data:

```
pro3d-tool kdtree <testdata>\1087_004779_MSLMST_0011
```

or via the script:

```
scripts\run-kdtree.cmd <testdata>
scripts/run-kdtree.sh  <testdata>
```

Rebuilding from scratch, four hierarchies at a time:

```
pro3d-tool kdtree --forcekdtreerebuild --degreesOfParallelism 4 "K:\PRo3D Data\SAIIL_02_01-v3-opc\SAIIL_02_01"
```

## Notes

- **No GPU, no display, no SPICE kernels needed.** This verb is intended for
  data-preparation pipelines and headless machines, and creates no render context.
- **A plain run is read-only.** `--forcekdtreerebuild` is not: it rewrites the `.aakd`
  files in place, which will dirty a git checkout of the test data.
- DDS files are written with DXT1 compression; uncompressed DDS output is not currently
  supported.
