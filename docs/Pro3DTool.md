# pro3d-tool

`pro3d-tool` is PRo3D's command line companion. It is published as a
[dotnet tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools) and is
independent of the PRo3D viewer application — nothing here requires the viewer to be
installed.

It supersedes the older `opc-tool` (see [Migrating](#migrating-from-opc-tool)).

## Verbs

| Verb | What it does | Documentation |
|---|---|---|
| `kdtree` | Validate OPC directories and generate KdTrees | **[Pro3DTool-KdTree.md](./Pro3DTool-KdTree.md)** |
| `sun-angles` | Per-pixel illumination geometry for instrument images, for photometric work such as image calibration | **[Pro3DTool-SunAngles.md](./Pro3DTool-SunAngles.md)** |
| `simulate-image` | Simulated instrument image of a body at a SPICE time: Lommel-Seeliger sun lighting, procedural micro-structure, cast shadows, optional de-shaded texture albedo | **[Pro3DTool-SimulateImage.md](./Pro3DTool-SimulateImage.md)** |

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

  kdtree          validate OPC directories and generate KdTrees
  sun-angles      render per-pixel illumination geometry for instrument images
  simulate-image  render a simulated instrument image of a body at a SPICE time

Run `pro3d-tool <verb> --help` for the options of a verb.
```

Each verb documents itself:

```
pro3d-tool kdtree --help
pro3d-tool sun-angles --help
pro3d-tool simulate-image --help
```

## Test data

The examples on the verb pages run against public test data, which lives in its own
repository so that a plain PRo3D clone stays small. Clone it anywhere and pass the path:

```
git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git
```

It contains an MSL/Stimson OPC surface, and under `HERA/` a Didymos OPC together with an
ASPECT instrument image and its metadata sidecars.

One runnable script per verb ships in the PRo3D source tree, in Windows and POSIX
variants. They invoke the tool via `dotnet run`, so they work in a checkout before
anything is published to NuGet:

```
scripts\run-kdtree.cmd          <path-to-clone>
scripts\run-sun-angles.cmd      <path-to-clone>
scripts\run-simulate-image.cmd  <path-to-clone>
```

```
scripts/run-kdtree.sh           <path-to-clone>
scripts/run-sun-angles.sh       <path-to-clone>
scripts/run-simulate-image.sh   <path-to-clone>
```

## SPICE kernels

Anything involving planetary geometry — body positions, orientations, the direction to the
Sun — needs SPICE kernels. These are **not** part of the PRo3D test data: ESA publishes
them separately, as a git repository.

```
git clone https://spiftp.esac.esa.int/git/hera.git
```

No credentials are needed. Expect roughly 6.5 GB; the ESA server does not support partial
clones, so `--filter` and sparse-checkout will not reduce this.

Then set **`PRO3D_SPICE_KERNELS`** to the clone, or to its `kernels` subdirectory — either
works:

```
setx PRO3D_SPICE_KERNELS C:\path\to\hera        REM Windows
export PRO3D_SPICE_KERNELS=/path/to/hera        #   POSIX
```

`--kernel-root <dir>` overrides the variable for a single run. There is deliberately **no
implicit default**: with neither the flag nor the variable set, a verb that needs kernels
fails rather than quietly using some other tree, because output computed from unintended
kernels looks perfectly valid.

## Migrating from `opc-tool`

`opc-tool` is deprecated. Its functionality is the `kdtree` verb, with the same option
names, so migrating means prepending the verb:

```
opc-tool           --forcekdtreerebuild "F:\pro3d\data\dimorphos"   # old
pro3d-tool kdtree  --forcekdtreerebuild "F:\pro3d\data\dimorphos"   # new
```

One behavioural fix came with the move: in `opc-tool`, patch validation ran only when
`--skippatchvalidation` was passed, and `--generatedds` never actually produced DDS files
because the conversion was skipped whenever it was requested. Both now behave as
documented. If you previously worked around this, the workaround is no longer needed.
