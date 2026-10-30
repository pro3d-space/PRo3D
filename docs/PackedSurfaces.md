# Packed Surfaces (*.opc, *.zip)

A surface — the folder with the `.opcx`, its metadata and one or more OPCs — can be packed into a single **uncompressed (stored) zip** and loaded directly, without unpacking. The surface name is the file name without extension.

```
MSLM-3329-...-20230925.opc            <- surface name "MSLM-3329-...-20230925"
└── MSLM-3329-...-20230925/           <- optional folder (as produced by zipping the surface folder)
    ├── MSLM-3329-...-20230925.opcx
    ├── MSLM-3329-...-20230925.opcx.json
    ├── metadata/...
    └── MSLM-3329-...-20230925_000_000/
        ├── Images/...
        └── Patches/
            ├── patchhierarchy.xml
            ├── hierarchy.cache         <- required
            └── <root patch>/*.aakd     <- KdTrees, required for picking
```

The surface content may also sit directly at the top level of the zip (no surrounding folder).

## Loading

- **Surfaces ▸ Import packed surfaces (\*.opc, \*.zip)** — pick one or more archives.
- **Surfaces ▸ Import OPCs** — choosing a folder also discovers the archives in it and below it. Zips that contain no OPC are skipped.
- **Drag & drop** surface folders or archives onto the 3D view.
- The remote API `/api/importOpc` and `PRo3D.Snapshots --opc` / `--opcs` accept archive paths as well.

Problems (a compressed archive, a missing `hierarchy.cache`, missing KdTrees, a name clash, see below) are shown in the viewer's feedback line and logged.

Saved scenes store the surface's path *inside* the archive (`<dir>\<Name>\<Name>`); loading the scene opens the archive again. Keep the archive where it was, as for unpacked surfaces.

## Packing

The archive must be **stored** — entries may not be compressed. Random access into compressed entries is not possible, and textures need it.

```powershell
# 7-Zip
7z a -tzip -mx0 Gale.opc .\Gale\
# PowerShell 7 (writes stored entries)
Compress-Archive -Path .\Gale -DestinationPath Gale.zip -CompressionLevel NoCompression
```

`Compress-Archive` only writes `.zip`; rename the result to `.opc` to mark it as a packed surface (both extensions load).

Build everything PRo3D would otherwise write **before** packing, because archives are read-only:

- `Patches/hierarchy.cache` — written the first time PRo3D loads the unpacked OPC.
- KdTrees (`*.aakd`) — `pro3d-tool kdtree "<surface folder>"`, see [Pro3DTool-KdTree.md](Pro3DTool-KdTree.md) and [KdTrees.md](KdTrees.md). An archive without KdTrees loads, but picking is disabled and a warning is shown.

## Read-only

PRo3D never writes into an archive or next to it. In particular:

- the lazy KdTree cache is not written; a packed `*-0.cache` is used if present,
- **Rebuild KdTrees** and **make surface relative** (copying the surface next to the scene) do nothing for packed surfaces and log a warning.

## Limitations

These come from how Aardvark.Data.Opc (`Prinziple`) maps archives and are checked when an archive is opened:

- **An archive and an unpacked folder of the same name cannot sit side by side** (`Gale.opc` next to `Gale\`). Once the archive is open, every read below `Gale\` is answered from the archive. Keep one of them. The same applies to folders whose name *starts* with the archive name (`Gale2\` next to `Gale.opc`) and to two archives whose names are prefixes of each other.
- Only one of `Name.opc`, `Name.opcz`, `Name.zip` may exist in a folder.
- The archive file stays open, and therefore locked on Windows, until PRo3D exits.
- Lookups in an archive are linear in its number of entries; very large archives (tens of thousands of entries) load slower than the unpacked folder.

## Implementation

`src/PRo3D.Core/Surface/SurfaceArchive.fs` opens an archive, checks its layout (OPC folders = folders holding `Patches/patchhierarchy.xml` and `Images/`, all in one folder), refuses compressed entries and name clashes, and registers it with `Prinziple` at `<dir>\<Name>`. All OPC reads then go through Prinziple. Tests: `src/Tests/SurfaceArchiveTests.fs` (layout rules, discovery, loading a real OPC, and a render that must match the unpacked folder pixel for pixel).
