# pro3d-tool

`pro3d-tool` is PRo3D's command line companion. It is published as a
[dotnet tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools) and is
independent of the PRo3D viewer application — nothing here requires the viewer to be
installed.

It supersedes the older `opc-tool` (see [Migrating from opc-tool](#migrating-from-opc-tool)).

## Install

```
dotnet tool install PRo3D.Tool --global
```

```
> pro3d-tool

.--. .--.     .--. .--.
|   )|   )        )|   :
|--' |--' .-.  --: |   |
|    |  \(   )    )|   ;
'    '   ``-' `--' '--'   pro3d-tool by pro3d-space.

Command line tools for PRo3D data.

  kdtree       validate OPC directories and generate KdTrees

Run `pro3d-tool <verb> --help` for the options of a verb.
```

Each verb documents itself:

```
pro3d-tool kdtree --help
```

## Test data

The examples below run against public test data, which lives in its own repository so
that a plain PRo3D clone stays small. Clone it anywhere you like and pass the path:

```
git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git
```

A runnable script demonstrating every verb against that clone ships with the PRo3D
source tree:

```powershell
.\scripts\run-pro3d-tool-examples.ps1 -TestData <path-to-clone>
```

The script runs the tool from source via `dotnet run`, so it works in a checkout
before anything has been published to NuGet.

## `kdtree` — validate OPC directories and generate KdTrees

KdTrees are the spatial acceleration structures PRo3D uses for picking and
measurement on OPC surfaces. Generating them ahead of time turns a slow first
interaction with a surface into an instant one, which matters for large datasets and
for anything running unattended.

```
pro3d-tool kdtree [options] <surface-directory>
```

| Option | Effect |
|---|---|
| `--forcekdtreerebuild` | Rebuild and overwrite existing kd-trees |
| `--ignoreMasterKdTree` | Ignore master kd-trees; load or create per-patch kd-trees and the lazy kd-tree cache |
| `--generatedds` | Convert patch textures to DDS |
| `--overwritedds` | Overwrite existing DDS files (only meaningful with `--generatedds`) |
| `--skipPatchValidation` | Skip patch validation (textures, aara files) |
| `--degreesOfParallelism <n>` | Process this many hierarchies concurrently; `0` means single threaded |
| `--verbose` | Print all messages to standard output |

The positional argument is either a single OPC hierarchy or a directory containing
several. When it is a container, every immediate subdirectory is checked and
non-OPC folders are reported and skipped rather than failing the run.

### Example

```
pro3d-tool kdtree <path-to-clone>/1087_004779_MSLMST_0011
```

Rebuilding from scratch, over four hierarchies at a time:

```
pro3d-tool kdtree --forcekdtreerebuild --degreesOfParallelism 4 "K:\PRo3D Data\SAIIL_02_01-v3-opc\SAIIL_02_01"
```

### Notes

- This verb needs no GPU and no display. It is intended to run in data-preparation
  pipelines and on headless machines.
- DDS files are written with DXT1 compression; uncompressed DDS output is not
  currently supported.

## Migrating from `opc-tool`

`opc-tool` is deprecated. Its functionality is the `kdtree` verb, with the same option
names, so migrating means prepending the verb:

```
opc-tool  --forcekdtreerebuild "F:\pro3d\data\dimorphos"      # old
pro3d-tool kdtree --forcekdtreerebuild "F:\pro3d\data\dimorphos"   # new
```

One behavioural fix came with the move: in `opc-tool`, patch validation ran only when
`--skipPatchValidation` was passed, and `--generatedds` never actually produced DDS
files because the conversion was skipped whenever it was requested. Both now behave as
documented. If you previously worked around this, the workaround is no longer needed.
