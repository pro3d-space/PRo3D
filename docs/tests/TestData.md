# Test data

Some tests need a real OPC surface — importing it, building its scene graph, flying to its
bounding box, saving and reopening a scene that references it. That data is binary and far
too large for the main repository, so it lives in its own repository and is consumed as a
git submodule.

| | |
|---|---|
| Repository | https://github.com/pro3d-space/PRo3D.Resources.TestData |
| Mounted at | `src/Tests/data/opc` |
| Size | ~167 MB (one OPC surface) |

## Getting it

```
git submodule update --init src/Tests/data/opc
```

Or clone PRo3D with `--recurse-submodules` in the first place. The submodule is **not**
required to build or to run the test suite — see *Running without it* below.

Note that this is a different submodule from `src/ModelViewer/resources`
(`PRo3D.Resources.Models`), which holds the spacecraft and terrain **models** used by the
ModelViewer. The two are fetched independently so that neither audience has to download the
other's data.

## What is in it

`1087_004779_MSLMST_0011` — an MSL Mastcam OPC surface: the `.opcx` surface descriptor plus
one OPC directory (`1087_004779_MSLMST_0011_000_000`) with `Images/` textures,
`Patches/` geometry and `patchhierarchy.xml`, and pre-built `.aakd` kd-trees so that picking
works without running [`opc-tool`](../OpcTool.md) first.

The fixture is imported exactly as a user would import a folder, via
`ViewerAction.ImportSurface`, so the surface directory must stay importable as-is: keep each
fixture in its own top-level directory named after the surface.

## Running without it

Tests that need the fixture skip themselves rather than fail. `Render.skipReason` in
[`src/Tests/Features/TestHelpers.fs`](../../src/Tests/Features/TestHelpers.fs) reports why a
section was skipped — a missing fixture, or a machine with no OpenGL context (the scene
graph, and therefore every surface bounding box, cannot be built without one). The skip
message names the command above.

This is also why CI does not initialise the submodule: its runners have no GL context, so
the OPC-backed sections would skip regardless and the download would be wasted.

## Adding a fixture

1. Commit it to `PRo3D.Resources.TestData` in a top-level directory named after the surface.
2. In PRo3D, bump the submodule pointer (`git -C src/Tests/data/opc pull`, then commit the
   changed `src/Tests/data/opc` entry) so the fixture version is pinned per PRo3D commit.
3. Reference it from `Render` in `TestHelpers.fs`, and gate the new tests on
   `Render.skipReason` like the existing sections do.

Weigh every megabyte: anyone who initialises the submodule downloads all of it.

## Other data

- `src/Tests/data/` holds the small, text-based fixtures that are checked into PRo3D
  directly (instrument metadata sidecars, annotation files). These need no submodule.
- The SPICE tests need the non-public HERA kernels and self-skip without them;
  `runTests.cmd` / `runTests.sh` pass `--skip-hera` so they skip deterministically.
- `ProfileAttributeExtractionTest` takes its data from `--testdatasource <path>` instead,
  and is only added to the run when that argument is given.
