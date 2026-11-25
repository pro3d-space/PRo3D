# Packed OPCs (*.opc)

An OPC (a folder holding `Images/` and `Patches/`) can be packed into a single **uncompressed (stored) zip** and loaded directly, without unpacking. OPCs are immutable, so the processing pipeline can serve them packed and ready: hierarchy cache and KdTrees included, nothing left for PRo3D to compute or write.

The **surface stays a plain folder.** Its sidecars, the `.opcx`, its `.json` and `metadata/`, describe all OPCs in the folder and are never packed. Only each OPC is:

```
MSLM-3329-...-20230925\                     surface folder, imported as before
├── MSLM-3329-...-20230925.opcx             sidecar for all OPCs of the surface
├── MSLM-3329-...-20230925.opcx.json        sidecar
├── metadata\...                            sidecar
├── MSLM-3329-...-20230925_000_000.opc      one stored zip per OPC, in place of the folder
│   ├── Images\...                          the OPC's content at the top level of the zip
│   └── Patches\
│       ├── patchhierarchy.xml
│       ├── hierarchy.cache                 required
│       └── <patch>\*.aakd, *-0.cache       KdTrees, required for picking
└── MSLM-3329-...-20230925_000_001\         plain and packed OPCs may be mixed
```

The archive's name is the OPC folder's name plus `.opc`. `.opcz` and `.zip` load as well.

## Loading

Import the **surface folder** as for unpacked surfaces:

- **Surfaces ▸ Import OPCs**: choose the surface folder, or a folder above it; discovery finds the surfaces below.
- **Drag & drop** surface folders onto the 3D view.
- The remote API `/api/importOpc` and `PRo3D.Snapshots --opc` / `--opcs` take surface folders as well.

OPCs are always imported through their surface folder, packed or not. A single `.opc` chosen or dropped on its own is not loaded; the message asks for the folder it lies in.

Problems are shown in the viewer's feedback line and logged:

- **Refused:** an archive that is compressed, has no `hierarchy.cache`, or clashes with a folder (see below). The OPC is left out and the rest of the surface loads; a surface left with no OPC is not imported.
- **Warning:** an archive without KdTrees loads, but picking is off for that OPC.

Saved scenes store the surface folder and its OPC paths as usual. Loading the scene opens the archives again.

## Preparing and packing

Archives are read-only, so everything PRo3D would otherwise write must be built **before** packing:

```powershell
# 1. hierarchy.cache and KdTrees (*.aakd, lazy KdTree cache) for every OPC of the surface
pro3d-tool kdtree "<surface folder>"

# 2. pack each OPC folder's content, stored, as <Opc>.opc next to it; then remove the folder
cd "<surface folder>\<Opc>"
7z a -tzip -mx0 "..\<Opc>.opc" Images Patches
```

`pro3d-tool kdtree` writes `Patches/hierarchy.cache` if it is missing, with the same serializer the viewer reads it with, and builds the KdTrees. See [Pro3DTool-KdTree.md](Pro3DTool-KdTree.md) and [KdTrees.md](KdTrees.md).

Pack the **content** of the OPC folder (`Images`, `Patches`), not the folder itself: the entries must sit at the top level of the zip. Entries must be **stored** (`-mx0`), because textures need random access, which compressed entries do not give. Do not leave the unpacked OPC folder next to its archive (see Limitations).

## Read-only

PRo3D never writes into an archive or next to it:

- a packed `hierarchy.cache` that cannot be read is an error, it is not rebuilt;
- the lazy KdTree cache is not written; a packed `*-0.cache` is used if present;
- **Rebuild KdTrees** skips packed OPCs and logs a warning.

**Make surface relative** copies the surface folder next to the scene, archives included.

## Limitations

These come from how Aardvark.Data.Opc (`Prinziple`) maps archives, and are checked when an archive is opened:

- **An archive and a folder of the same name cannot sit side by side** (`X_000_000.opc` next to `X_000_000\`). Prinziple reads that folder before the archive; it is where Prinziple would write. Keep one of them. The same applies to folders whose name *starts* with the OPC name (`X_000_000_old\`).
- Only one of `X.opc`, `X.opcz`, `X.zip` may exist.
- An archive file stays open, and therefore locked on Windows, until PRo3D exits.
- Lookups in an archive are linear in its number of entries; very large archives (tens of thousands of entries) load slower than the unpacked folder.

## Implementation

`src/PRo3D.Core/Surface/OpcArchive.fs` checks an archive's entries (`Patches/patchhierarchy.xml` and `Images/` at the top level, all stored, `hierarchy.cache` present), refuses clashes, and registers it with `Prinziple` at the OPC path; all reads of the OPC then go through Prinziple. Because Prinziple matches that path by plain string prefix, OPCs are read through the exact registered form (`OpcArchive.prepare` / `normalize`). Tests: `src/Tests/OpcArchiveTests.fs` (layout and rejection rules, import, a real OPC's hierarchy and KdTrees, and a render that must match the unpacked folder pixel for pixel).
