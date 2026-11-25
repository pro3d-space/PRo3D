# Plan: load packed OPCs, one stored zip per OPC (`<Opc>.opc`)

Status: **loader implemented** on `features/322-load-opc-as-an-uncompressed-zip`; `pro3d-tool pack` next. User docs: [docs/PackedSurfaces.md](../docs/PackedSurfaces.md).

## Context

A PRo3D *surface* is a folder holding its sidecars (`<Name>.opcx`, `.opcx.json`, `metadata/`) and one or more OPCs, each a folder with `Images/` and `Patches/`.

OPCs are immutable. The processing pipeline should serve them ready to load, with the hierarchy cache and KdTrees inside, so the viewer never computes or writes anything for them. Nothing zips an OPC automatically after loading.

## Design

- **The unit packed is the OPC, not the surface.** `<surface>\<Opc>.opc` replaces the folder `<surface>\<Opc>`, and its entries (`Images/…`, `Patches/…`) sit at the top level of the zip.
- **The surface stays a plain folder.** Its sidecars belong to all of its OPCs and are never packed. A surface may mix plain and packed OPCs.
- **This is Prinziple's native model.** `Prinziple.tryRegister <opcPath>` opens `<opcPath>.opc|.opcz|.zip` and answers every read below the OPC path from the archive. Surface paths, OPC paths and scenes stay ordinary paths.
- **Read-only.** Prinziple would write into a folder `<opcPath>` next to the archive and read that folder before the zip. PRo3D never writes there, and refuses an archive that has such a folder next to it.
- The first version, a whole surface packed with its `.opcx` inside one zip, was replaced by this design.

## Changes

| File | Change |
|---|---|
| `src/PRo3D.Core/Surface/OpcArchive.fs` (new, compiled before `KdTrees.fs`) | `tryOpen` validates an archive and registers it. It requires the OPC entries at the top level, refuses compressed or encrypted entries and a missing `hierarchy.cache`, warns about missing KdTrees, and refuses a folder of the same name next to it, a folder whose name starts with the OPC name, two archive variants, and prefix clashes with other registered archives. Also `prepare` (open if packed, return the path to read through), `normalize`, `isInArchive` and `isOpcArchiveFile` (entry list, cached). |
| `src/PRo3D.Core/Surface/Surface.Files.fs` | `isOpcArchive` and `isOpc`. `isSurfaceFolder` and `retrieve` (`getOPCNames`/`getOPCPaths`) count `<Opc>.opc` files as OPCs. Replaces the legacy `.zip`-only `isZippedOpcFolder`. |
| `src/PRo3D.Core/Surface/Surface.Sg.fs` | Hierarchies load through `OpcArchive.prepare`, which also reopens archives on scene load. A rejected OPC is left out. The `hierarchy.cache` of a packed OPC is never rewritten. |
| `src/PRo3D.Core/Surface/SurfaceApp.fs` | `mkFromPaths` and `openArchives`: import stores the surface folder in the registered path form, leaves out rejected OPCs, and reports problems. A lone `.opc` is reported, not loaded. Rebuild KdTrees skips packed OPCs with a warning. |
| `src/PRo3D.Core/KdTrees.fs` | Packed OPCs: never construct or write KdTrees, and never write the lazy KdTree cache. |
| `src/PRo3D.Viewer/Viewer/Viewer.fs`, `RemoteApi.fs`, `MapProjectionHost.fs`, `PRo3D.SceneRender/OpcRender.fs` | Import through `mkFromPaths` with problems shown in the feedback line. The remote cutout loads through `prepare`. |
| `src/PRo3D.Viewer/Viewer/ViewerGUI.fs`, `resources/utilities.js` | Drag & drop of surface folders onto the 3D view and the main page. |
| `src/Tests/OpcArchiveTests.fs` | 19 tests, see Verification. |

## Limitations (Prinziple, not addressed)

These would need an aardvark.data PR, a release and a Paket bump:

1. **Plain string-prefix match.** Hence the clash refusals, and reading only through the registered path form. A short (8.3) path broke loading until `prepare`/`normalize` were added.
2. **Lookups are linear** in the number of entries (`ZipFile.FindEntry`).
3. **The archive stays open and locked until exit.** `Prinziple.close` is not called, to avoid racing in-flight patch loads.

## Verification

- `dotnet Tests.dll --skip-hera --filter-test-list "opc archives"`: 19/19 pass. The tests cover:
  - layout: registration at the OPC path, mixed plain and packed OPCs, reopening, other spellings of the path;
  - rejections;
  - import: a rejected OPC left out of its surface, all OPCs rejected, a lone `.opc`;
  - Didymos_ASPECT repacked per OPC: hierarchy and KdTrees load, and nothing is written into the surface folder;
  - a render that is pixel-identical to the folder.
- **MSL test case:** `H:\Mission\MSL\SteveBanham-BurialHistory-JGR-25\OPCs\Job_3329_101170\result\MSLM-3329-MCAM01170-R-C-ALL-OPC-MULTI-SPHR-20230925` (one OPC, 69 patches, 215 MB). It came with a `hierarchy.cache` but no KdTrees.
  - Prepared on a copy with `pro3d-tool kdtree`: 46 `*.aakd` plus one lazy KdTree cache. The existing `hierarchy.cache` was readable and left unchanged.
  - Packed with `7z -mx0` (219 MB). Sidecars kept beside it.
  - Imported in the viewer through `/api/importOpc`: the hierarchy loaded from the packed cache and the KdTrees from the packed lazy cache. The 3D view is pixel-identical to the same surface unpacked, and nothing was written into the surface folder.
- Still to do by hand in the Electron viewer: drag & drop, picking on a packed OPC, and save/reload of a scene.

## Next

`pro3d-tool pack <surface folder> --out <dir>`:
- runs the kdtree step on the plain surface;
- writes the output surface: sidecars copied, one stored `<Opc>.opc` per OPC;
- verifies each archive with `OpcArchive.tryOpen`.

It never writes into the source.
