# pro3d-tool

Command line tools for [PRo3D](https://github.com/pro3d-space/PRo3D) data. Does not require
the PRo3D viewer to be installed.

```
dotnet tool install PRo3D.Tool --global
```

## Verbs

| Verb | Purpose |
|---|---|
| `kdtree` | Validates OPC directories and generates KdTrees. No GPU or SPICE kernels needed. |
| `sun-angles` | Writes per-pixel incidence, emission and phase angles for instrument images as float32 TIFFs in radians. Requires SPICE kernels and a GPU. |
| `unproject` | Converts image pixel coordinates to body-fixed surface coordinates on a shape model. Requires SPICE kernels; no GPU. |
| `simulate-image` | Renders a simulated instrument image of a body at a SPICE time: Lommel-Seeliger sun lighting, procedural micro-structure, cast shadows, optional de-shaded texture albedo. Requires SPICE kernels and a GPU. |

```
pro3d-tool kdtree --help
pro3d-tool sun-angles --help
pro3d-tool unproject --help
pro3d-tool simulate-image --help
```

## SPICE kernels

`sun-angles`, `unproject` and `simulate-image` need SPICE kernels, which ESA publishes
separately:

```
git clone https://spiftp.esac.esa.int/git/hera.git
```

Set `PRO3D_SPICE_KERNELS` to that clone or to its `kernels` subdirectory, or pass
`--kernel-root <dir>`. There is no default: the verbs fail when no kernel tree is given.

## Documentation

- [pro3d-tool](https://github.com/pro3d-space/PRo3D/blob/main/docs/Pro3DTool.md) — install, test data, SPICE setup, migration from `opc-tool`
- [`kdtree`](https://github.com/pro3d-space/PRo3D/blob/main/docs/Pro3DTool-KdTree.md)
- [`sun-angles`](https://github.com/pro3d-space/PRo3D/blob/main/docs/Pro3DTool-SunAngles.md)
- [`unproject`](https://github.com/pro3d-space/PRo3D/blob/main/docs/Pro3DTool-Unproject.md)
- [`simulate-image`](https://github.com/pro3d-space/PRo3D/blob/main/docs/Pro3DTool-SimulateImage.md)

## Migrating from `opc-tool`

`opc-tool` is deprecated. Its functionality is the `kdtree` verb with unchanged option
names, so prepend the verb:

```
opc-tool          --forcekdtreerebuild <dir>
pro3d-tool kdtree --forcekdtreerebuild <dir>
```

MIT licensed.
