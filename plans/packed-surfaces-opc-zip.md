# Plan: load surfaces from stored (uncompressed) zip archives (`.opc` / `.zip`)

Status: **implemented** on `features/322-load-opc-as-an-uncompressed-zip`. Upstream limitations are documented, not addressed (see the end of this file).

## Context

A PRo3D *surface* is a folder `<Name>/` with `<Name>.opcx`, its `.json` metadata, a `metadata/` folder and one or more OPC folders (`<Name>_000_000/Patches|Images`).

We want to ship a surface as one read-only file, `<Name>.opc` (or `<Name>.zip`): a **stored** zip whose surface name is the file name without its extension. Plain folders must keep loading exactly as before.

v1 scope:
- import from the UI (a menu dialog plus drag & drop);
- a minimal scene round trip.

Not in scope: Windows file association and a packing tool.

## Approach

- **Prinziple** is a module in the Aardvark.Data.Opc NuGet package, source in `..\aardvark.data\src\Aardvark.Data.Opc\Prinziple.fs`. It already serves reads from a zip registered at the root `<dir>\<Name>`.
- **Virtual surface folder.** The surface lives at `<dir>\<Name>\<Name>` when the archive wraps it in a `<Name>/` folder, and at `<dir>\<Name>` when the content sits at the top level. No library change is needed.
- **Stored entries are seekable.** SharpZipLib returns a `PartialInputStream`, which can seek. Textures decode from it pixel-identically to the folder; the render test confirms this.
- **Read-only.** Nothing is written into or next to an archive. The hierarchy cache must be packed; KdTrees should be.

## Changes

| File | Change |
|---|---|
| `src/PRo3D.Core/Surface/SurfaceArchive.fs` (new, compiled before `KdTrees.fs`) | See the list below. |
| `src/PRo3D.Core/Surface/Surface.Files.fs` | `isOpcFolder'` and `isSurfaceFolder` go through Prinziple, and `isSurfaceFolder` no longer throws on a missing directory. New `surfaceFolderExists`, which reopens the archive. `retrieve` lists OPCs inside archives. `superDiscoveryMultipleSurfaceFolder` also returns `*.opc` / `*.zip` files. `makeSurfaceRelative` is guarded. |
| `src/PRo3D.Core/Surface/SurfaceApp.fs` | `SurfaceUtils.mkFromArchive`; `mkFromPaths`, which returns surfaces plus problems for the user; `SurfaceAttributes.read` uses `Prinziple.readXmlDoc`; Rebuild KdTrees is guarded; the missing-data icon is archive-aware. |
| `src/PRo3D.Core/Surface/Surface.Sg.fs` | `createSgSurfaces` uses `surfaceFolderExists`, which reopens archives on scene load. `PatchHierarchy.load` gets a pickle that fails instead of rewriting `hierarchy.cache` inside an archive. |
| `src/PRo3D.Core/KdTrees.fs` | For hierarchies inside an archive: never construct or write KdTrees, and skip writing the lazy KdTree cache. |
| `src/PRo3D.Viewer/Scene.fs` | `.opcx` probe and fallback through Prinziple. |
| `src/PRo3D.Viewer/Viewer/Viewer.fs` | `ImportSurface` / `DiscoverAndImportOpcs` accept archive files and use `mkFromPaths`. `UserFeedback.queueSurfaceProblems`. The legacy `isZippedOpcFolder` filter is replaced. |
| `src/PRo3D.Viewer/Viewer/ViewerGUI.fs` + `resources/utilities.js` | Menu "Import packed surfaces (*.opc, *.zip)". Drag & drop on the render view and the main page through `attachSurfaceDrop` (Electron 42: `webUtils.getPathForFile`), sending `onchoose` to `ImportDiscoveredSurfacesThreads`. |
| `src/PRo3D.Viewer/Viewer/MapProjectionHost.fs` | The `Patches` check goes through Prinziple. |
| `src/Tests/SurfaceArchiveTests.fs` | 15 tests, see below. |
| `docs/PackedSurfaces.md`, `ai/DOMAIN.md` | Docs. |

`SurfaceArchive.fs` provides:
- `tryOpen`, which reads the layout, rejects compressed or encrypted entries and a missing `hierarchy.cache`, warns about missing `*.aakd`, checks for name clashes, and calls `Prinziple.tryRegister`;
- `tryOpenForPath`, used on scene load;
- `isInArchive`, `directoryExists` (also true for the archive root), and `tryFindCapturingRoot`.

Not changed:
- The legacy per-OPC zip support: `Files.isZippedOpcFolder` and `Prinziple.register` per OPC path.
- `HeadlessPicking` and the Tool/MapProjection CLIs. They take folders.

## Limitations (documented, not addressed)

These belong to Prinziple and would need an aardvark.data PR, a release and a Paket bump:

1. **Plain prefix match, no disk fallback.** For that reason `tryOpen` refuses:
   - an unpacked folder of the same name next to the archive;
   - folders whose name starts with the archive name;
   - archives whose names are prefixes of each other;
   - several of `Name.opc` / `.opcz` / `.zip`.

   Plain-folder imports that a loaded archive would capture are refused as well.
2. **Lookups are linear in the number of entries.** Large archives load slower.
3. **The archive stays open and locked until exit.** `Prinziple.close` is not called, to avoid racing in-flight patch loads.
4. **Path casing.** Prinziple's root match is case-sensitive. Virtual paths are always derived from the registered root string, which mitigates this.

## Verification

- `dotnet Tests.dll --skip-hera --filter-test-list "surface archives"`: 15/15 pass. The tests cover:
  - the wrapper and flat layouts, reopening, and scene-path reopening;
  - rejections: compressed, no `hierarchy.cache`, side-by-side folder, prefix folder, and a zip without OPC;
  - discovery, which skips non-surface zips and reports them when chosen explicitly;
  - the real Didymos_ASPECT OPC, with and without the lazy KdTree cache: hierarchy and KdTrees load from the archive, and nothing is written next to it;
  - a headless render (MapSg / Aardvark.GeoSpatial.Opc) from the archive that is **pixel-identical** to the folder.
- Full suite run for regressions.
- Manual check left to do: import via the menu and drag & drop in the Electron viewer, and save/reload a scene with a packed surface. Use a repacked sample with KdTrees, not sitting next to its unpacked folder.
