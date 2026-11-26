# `pro3d-tool pack`

Write a copy of a surface with each OPC packed into one stored zip, `<Opc>.opc`, ready for PRo3D to load read-only.

Part of [`pro3d-tool`](./Pro3DTool.md). See there for installation.

```
pro3d-tool pack --out <output-directory> <surface-directory>
```

Packing is meant for the processing pipeline that serves OPCs. PRo3D never packs anything itself, and loads plain OPC folders exactly as before. For the packed layout and how PRo3D loads it, see [PackedSurfaces.md](./PackedSurfaces.md).

## What it does

The input is a **surface folder**: the folder holding the OPC folders (each with `Images/` and `Patches/`) and their sidecars (`.opcx`, `.opcx.json`, `metadata/`, …). The verb writes `<output-directory>\<surface name>\` with:

- every sidecar copied unchanged: all files and folders of the surface that are not OPCs;
- each OPC folder packed as `<Opc>.opc`, with its content (`Images/…`, `Patches/…`) at the top level of the zip and every entry **stored** (uncompressed);
- OPCs already packed in the source copied as they are;
- the surface's `<surface name>.trafo`, if there is one next to the surface folder, copied next to the output.

Archives are read-only in PRo3D, so each OPC must carry its `Patches/hierarchy.cache`, a KdTree per patch (`*.aakd`) and the lazy KdTree cache (`*-0.cache`). An OPC that lacks any of them is copied to a staging folder next to the output (`.<surface name>.pack-staging`). The `kdtree` step completes it there, it is packed from there, and the staging copy is removed. Existing KdTrees are kept; patch validation is skipped.

**The source is never written.** The verb also refuses:

- an output folder `<output-directory>\<surface name>` that exists already;
- an output directory inside the surface folder;
- an OPC folder given in place of its surface folder;
- an OPC present both as a folder and as an archive.

## Verification

Every archive written or copied is opened the way the viewer opens it:

- the layout and loading rules of PackedSurfaces.md;
- every entry's checksum;
- the hierarchy loads from the packed cache;
- a KdTree loads for every patch.

Opening must not write anything into the output folder. A failure ends the run with exit code 1 and names the OPC. The output is then incomplete and should be removed. Archives already opened stay locked until the tool exits.

## Example

```
> pro3d-tool pack --out D:\served "H:\...\result\MSLM-3329-MCAM01170-R-C-ALL-OPC-MULTI-SPHR-20230925"
[pack] sidecar MSLM-3329-MCAM01170-R-C-ALL-OPC-MULTI-SPHR-20230925.opcx
[pack] sidecar MSLM-3329-MCAM01170-R-C-ALL-OPC-MULTI-SPHR-20230925.opcx.json
[pack] sidecar metadata
[pack] MSLM-..._000_000 lacks KdTrees of 46 of 46 patches, the lazy KdTree cache: completing it in a staging copy, the source is left as it is
[pack] MSLM-..._000_000.opc: 671 entries, 218.23MiB
[pack] verified MSLM-..._000_000
[pack] done: D:\served\MSLM-3329-MCAM01170-R-C-ALL-OPC-MULTI-SPHR-20230925
```

Import `D:\served\MSLM-…-20230925` in PRo3D as any surface folder.

## Implementation

`src/PRo3D.Tool/Pack.fs`:
- the completeness check (`missing`);
- the archive writer (`writeArchive`, SharpZipLib, stored entries, Zip64 when needed);
- the check after writing (`verify`, which uses `OpcArchive.tryOpen` from PRo3D.Core).

Tests: the `pack` list in `src/Tests/OpcArchiveTests.fs`.
