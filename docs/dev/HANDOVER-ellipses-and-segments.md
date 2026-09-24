# Handover: ellipses and segments in the CSV export (#644)

2026-09-24. Start here with a fresh context. The goal is to get **ellipse (boulder) and segment (fracture) CSV output** landed. Read this page, then [PLAN-export-ellipses-and-segments.md](PLAN-export-ellipses-and-segments.md) (design and decisions) and [AnnotationExport-CSV.md](../AnnotationExport-CSV.md) (the exact target columns). [AnnotationExport-boulders-fractures.md](AnnotationExport-boulders-fractures.md) is background only.

**Status, later on 2026-09-24:** PR B is implemented on this branch (code, tests, docs). Where it departs from the steps below, the plan's section *Corrections found while building PR B* says why; read it before PR C or D. Next: open PR B, then PR C.

**Status, evening of 2026-09-24:** session `boulders` took over the statistics from `sampling` (which stood down; its worktree `pro3d-ellipse-stats` is left untouched and can be removed). Branch `features/644_ellipse-stats-export`, on top of PR B: first commit = the `EllipseStatistics` library as `sampling` wrote it (encoding repaired; it can be cut out as PR A), then the speed-up (4,800 ellipses: 100 s → ~3 s) and the export wiring (PR D). Decisions: statistics always on for per-annotation ellipse rows, no switch; each ellipse integrates only the surface it was drawn on, hidden or not; imported ellipses (no surface) get `footprintArea` only, a later phase; per-layer coverage `surface_<layer>_area` added; `LonLatRad` left out; circles get no azimuth.

## Where you work

| | |
| --- | --- |
| Worktree | `C:\Users\haral\Desktop\pro3d\pro3d-644` |
| Branch | `features/644_boulders-fractures-export`, from `origin/develop` 1ed7c3a3 |
| Commits on it | one, local, unpushed: these `docs/dev` files |
| Issue | [#644 "Annotation Export"](https://github.com/pro3d-space/PRo3D/issues/644): the users' request verbatim (ellipse semi-axes, orientation, raster values inside; line total and per-segment lengths) |

**Do not work in `C:\Users\haral\Desktop\pro3d\pro3d-6`.** It belongs to the `simulator` session (branch `features/801_simulate-series`), and the shared `stash@{0}` holds someone's unrelated work: never pop or drop it.

## What the users asked for, and what exists

| Request | State on develop |
| --- | --- |
| Line total length | works: `wayLength` |
| Length of each segment | only per point (`segmentLength` repeated on every sampled point); no per-segment rows |
| Ellipse semi-axes | **broken**: `majorDiameter`/`minorDiameter` exist but are always empty (see *Root cause*) |
| Ellipse orientation | missing |
| Raster values inside an ellipse | library done by session `sampling`, not wired (PR A / D below) |

### Root cause of the empty ellipse columns

`src/PRo3D.Core/Drawing/Drawing-App.fs:207` has `let geo = false`, so drawing takes the plane branch (`:219`, `EllipticAnnotations.constructAndSampleFromPlane`). That branch computes a `ConstructedEllipse` (`src/PRo3D.Core/Drawing/EllipseAnnotation.fs:35`: `constructionPlane : Plane3d`, `ellipseOnPlane : Ellipse2d`, metres) and then stores `ellipticResults = None`. The export reads `ellipticResults` (`src/PRo3D.Base/Annotation/Exporters/AnnotationFields.fs:239`, `ellipseValue`) and so writes empty cells. The SBMT importer does the same: `src/PRo3D.Core/Importers/SbmtImporter.fs:180` `parseEllipseLine` computes `center`, `semiMajor` (`:226`) and `semiMinor` in metres, then stores `ellipticResults = None`. Separately, the stored `geographicalEllipse` (when the dormant `geo = true` branch ran) is in lon/lat **degrees** (`EllipseAnnotation.fs:46`, `createGeographicalEllipse` via `Conv.geographicalToCartesian` = `V2d(lon, lat)`), so even the old values were not metres.

## Decisions already made by the user (do not re-ask)

- Two presets, **Boulders** and **Fractures**. A CSV has one row shape; the user exports twice.
- *Boulders*: one row per ellipse; a visible **"Annotation types: all | ellipses only"** filter that the preset sets (other presets reset it to *all*).
- *Fractures*: new granularity **one record per segment**, **CSV only**, **fixed columns** (no checkbox section): endpoints, `segmentLength`, `segmentChord`, `segmentAzimuth`.
- Scope of both presets: **All**. Longitude: **Native** (Profile keeps *Flipped*).
- Orientation = **azimuth only** (no plunge), **clockwise from local north, axial 0–180°**.
- **Store ellipse measures at construction** (drawing and SBMT import), so export and colour-by-category only read stored values; they also appear in **colour by category**.
- **Retire `majorDiameter`/`minorDiameter`** now (enum numbers 24/25 stay unused); no transition.
- **No refitting / no migration**: there is no legacy data; a file without the new fields exports empty ellipse columns.
- Boulder row coordinates = the stored ellipse centre, not the outline's bounding-box centre.
- Principle: **minimal change, safety first**. Nothing beyond the two presets.
- Columns and naming: exactly as in [AnnotationExport-CSV.md](../AnnotationExport-CSV.md) (camelCase, metres, empty cell = no value, vectors as `x;y;z`).

## The work, as PRs (all under #644)

| PR | Branch | Owner | Contents | Depends on |
| --- | --- | --- | --- | --- |
| A | `features/644_ellipse-surface-stats` | `sampling` | statistics library, no export wiring | — |
| **B** | `features/644_boulders-fractures-export` (**this worktree**) | you | store the metric ellipse; `semiMajorAxis`, `semiMinorAxis`, `majorAxisAzimuth`; ellipses-only filter; *Boulders* preset; colour-by-category attributes; retire diameters; field reference to `docs/` | — |
| **C** | `features/644_fracture-segments` (new, from develop after B) | you | `ExportGranularity.PerSegment`, *Fractures* preset | B |
| D | new, after A and B | `sampling` | statistics columns on *Boulders* | A, B |
| E | `bugs/644_ground-distance-small-bodies` | anyone | `groundDistance` is 0 on Dimorphos/Didymos | — |

### PR B, concretely

1. **Model** `src/PRo3D.Base/Annotation/Annotation-Model.fs:443` `EllipticAnnotationResult` (field on `Annotation` at `:542`): add `center : V3d`, `semiMajorAxis : V3d`, `semiMinorAxis : V3d` (body-fixed world space, the same space as the annotation's points, metres) and `majorAxisAzimuth : float` (NaN when the axis is near-vertical). Write them in `ToJson`, read them in `readV0` with `Json.tryRead` + default (no version bump; CLAUDE.md rule). Keep `geographicalEllipse` for the GeoJSON writer (`src/PRo3D.Base/Annotation/Exporters/GeoJSON.Export.fs:89`). (Correction: the record is not a model type itself, only a field of `Annotation`; the metric fields use new JSON keys because `center`/`major`/`minor` are taken by the lon/lat ellipse.)
2. **Fill it on drawing**: `Drawing-App.fs:219` branch: map `ellipseOnPlane.Center/Axis0/Axis1` to world with `constructionPlane.GetPlaneToWorld()`, compute the azimuth (step 4), store. Up/north are parameters of `getFinishedAnnotation` (`:179`) and `finishAndAppend` (`:244`); on a body re-derive them at the ellipse centre with `ReferenceSystem.upVector` (`src/PRo3D.Core/ReferenceSystem.fs:74`) and `northVector` (`:88`).
3. **Fill it on SBMT import**: `SbmtImporter.fs:180` `parseEllipseLine` already has `center`, `semiMajor`, `semiMinor`, `east`, `north`; store them in its record (`ellipticResults = None` at `:251`). The record at `:131` is for points and stays `None`.
4. **Azimuth helper** (one function in `PRo3D.Core`, used by both): `h = m − (m·u)u`, `e = n × u`, `azimuth = atan2(h·e, h·n)` in degrees, folded into [0, 180); NaN when `|h| < 1e-9·|m|`.
5. **Fields** `AnnotationFields.fs:15`: new `SemiMajorAxis = 39`, `SemiMinorAxis = 40`, `MajorAxisAzimuth = 41` (last used is `ColorHex = 38`), group *Ellipse* (`groupOf` `:106`), `columnName` (`:121`), `label` (`:158`), values read from the stored result (`ellipseValue` `:239`). Remove 24/25 from `all` (`:96`) and from the presets.
6. **Row coordinates**: `AnnotationExport.fs:329` `perAnnotationRecord` uses the bounding-box centre; use the stored ellipse centre when present.
7. **Type filter**: `ExportTypeFilter` enum (`All = 0`, `EllipsesOnly = 1`) in `ExportSettings.fs`, a `typeFilter` field on `AnnotationExportSettings` (`:79`) and `AnnotationExportModel` (`src/PRo3D.Core/AnnotationExport-Model.fs:50`, Adaptify), a dropdown row under *Scope* in `src/PRo3D.Core/AnnotationExportApp.fs`, applied in `src/PRo3D.Viewer/Viewer/AnnotationExportViewer.fs:50` `annotationsInScope`; empty result → a message like `emptyScopeMessage` (`:61`): "No ellipses in scope, so there is nothing to export." Keeps `AxisEllipse`, `Axis4PEllipse`, `Ellipse`.
8. **Preset** `ExportPreset` (`ExportSettings.fs:68`) + `applyPreset` (`:149`, see the `Profile` case at `:198`): *Boulders* = CSV, per annotation, scope All, longitude Native, `typeFilter = EllipsesOnly`, fields `key, text, groupPath, surfaceName, semiMajorAxis, semiMinorAxis, majorAxisAzimuth`, geographic + cartesian coordinates. Every other preset sets `typeFilter = All`.
9. **Colour by category** `src/PRo3D.Base/Annotation/ColorByCategory-Model.fs:20`: append `SemiMajorAxis = 15`, `SemiMinorAxis = 16`, `MajorAxisAzimuth = 17` (persisted enum: append, never renumber); labels (`ColorByCategoryApp.fs:~37`), `valueOf` (`:283`), `valueOfAdaptive` (`:361`); NaN for non-ellipses.
10. **Docs**: move `docs/dev/CSV-export-fields.md` to `docs/AnnotationExport-CSV.md` (drop the "planned" markers for what B delivers), update `docs/AnnotationExport.md` (presets, type filter, retired diameters), `docs/SbmtImport.md` (imported ellipses carry their axes), `docs/ColorByCategory.md`.

### PR C, concretely

`ExportGranularity.PerSegment = 2` (`ExportSettings.fs:21`), offered only for CSV (`AnnotationExportApp.fs:326`; switching away from CSV falls back to per annotation), `granularityLabel` (`:238`). `buildRecords` (`AnnotationExport.fs:466`) gets a third branch that groups `resolvePoints` (`:170`, it already tags each point with segment index and draped length) into one row per segment; `schemaOf` (`:221`) lists the fixed columns. Linear-projection lines without stored segments: segment *i* = clicked point *i* → *i+1*. Ellipses give no rows. `segmentAzimuth` is computed at export time, so `write` (`:485`) needs a `north` (flat frames: the reference system's north; bodies: derived at the segment midpoint, which needs a `northVector` in `PRo3D.Base`). The *Point attributes*, *Surface properties* and *Sampled points* controls are hidden for per-segment (`AnnotationExportApp.fs:377` pattern). *Fractures* preset: CSV, per segment, scope All, Native, fields `key, text, groupPath, surfaceName, wayLength`.

## Work of the `sampling` session (PR A, then D)

| | |
| --- | --- |
| Worktree | `C:\Users\haral\Desktop\pro3d\pro3d-ellipse-stats` |
| Branch | `features/644_ellipse-surface-stats`, fast-forwarded to develop 1ed7c3a3 |
| State | code done, **uncommitted**; waiting for the user's go-ahead in that session to commit and open PR A |
| Tests | `profile tests` list 16/16 green (8 EllipseStatistics: 7 synthetic, 1 Dimorphos brute-force comparison) |
| Files | new `src/PRo3D.Core/EllipseStatistics.fs` (518 lines), new `src/Tests/EllipseStatisticsTest.fs`, new `docs/EllipseStatistics.md`; helpers only in `VertexAttributes.fs` (`VertexAttributeRows`, `tryReadRows`), `ProfileAttributeExtraction.fs` (`tryFindPatchInfo`), `Surface.fs` (`SurfaceIntersection.surfaceTrafo`); `sampleAt`/`doKdTreeIntersection` untouched. No overlap with B or C. |

What it does: area-weighted integration of the OPC's per-vertex `.aara` layers over the mesh triangles inside an ellipse (triangles clipped at the rim against a 128-gon of equal area, inside = within `depth` of the ellipse plane, default depth = semi-major axis). Per ellipse: `surfaceArea`, `footprintArea`, `vertexCount`, and per layer the area plus mean/std/min/max per channel. Textures are never decoded.

**Interface (agreed):**

```fsharp
type SurfaceEllipse = { center : V3d; semiMajor : V3d; semiMinor : V3d }   // world space, metres
EllipseStatistics.compute surfacesModel refSys observedSystem observerSystem filterSurface wanted depth ellipses
    -> Option<EllipseStatistics>[]
```

PR B's stored `center` / `semiMajorAxis` / `semiMinorAxis` map 1:1 onto `SurfaceEllipse`; neither type changes. **`sampling` owns PR D** (the statistics columns, second table in [AnnotationExport-CSV.md](../AnnotationExport-CSV.md)) after A and B are on develop. Message it via `SendMessage` to `sampling` when B lands.

## Build and test

```powershell
cd C:\Users\haral\Desktop\pro3d\pro3d-644
$env:__DOTNET_PREFERRED_BITNESS = $null         # VS dev-prompt env breaks the build otherwise
.\adapt.cmd                                     # fresh worktree: *.g.fs are not in git on develop; also after model changes
dotnet build src\Tests\Tests.fsproj -c Release
$env:PRO3D_TEST_DATA = "C:\Users\haral\Desktop\pro3d\PRo3D.Resources.TestData"
$env:PRO3D_TEST_DATA_PRIVATE = "C:\pro3ddata\private"   # SBMT fixtures (private, never in the public repo)
$env:PRO3D_SPICE_KERNELS = "C:\Users\haral\Desktop\pro3d\spice"   # pinned set; snapshot-sun-lighting needs ...\spice2
dotnet bin\Release\net9.0\Tests.dll --filter "all.all tests.annotation export" --summary
```

- **Run test lists one per process** (`--filter "all.all tests.<list>"` or `--filter-test-list`). The full suite can **deadlock** in GL tests (#816: GLFW `Invoke` from a thread that does not own the instance). A stuck run sits at 0% CPU; kill it.
- Test data: Dimorphos OPC `HERA\Dimorphos_opc\Dimorphos_DRACO1_DRACO2_Earth\Dimorphos` (the only Dimorphos to reference; `TestUtils.Roots.dimorphosOpc`), MSL OPC `MSL\1087_004779_MSLMST_0011`. SBMT fixtures: `C:\pro3ddata\private\imports\basicSBMT-dimorphos-v4\sbmtimport.ellipses.txt` etc. (`TestUtils.Roots.privateRoots`).
- Unit tests go next to `src/Tests/AnnotationExportTest.fs` (helper `csvSettings` at `:48`) and `src/Tests/SbmtImportAlignmentTest.fs`; the drawing path can be tested headless with the `Draw` harness (`src/Tests/Features/TestHelpers.fs:196`, used by `Section03_DrawingAnnotations.fs`). The test table in PLAN-export-ellipses-and-segments.md (*Testing*) lists what to cover; Playwright (`tests-ui/`) only for the export window and one end-to-end run after B and C.

## Rules that bite

- CLAUDE.md: Paket only; never edit `*.g.fs`; total functions only; prefix generics; `docs/` updated in the same PR; branch `features/<issue>_name`, PR to `develop`.
- **No `Co-Authored-By` trailer** in commits (user preference).
- Don't cancel the PR's CI; let it go green before asking to merge.
- One session per worktree. Before touching anything outside `pro3d-644`, ask.
- Longitudes: the CSV default and *Profile* are *Flipped* (360 − lon); the new presets are *Native*.
- LonLatRad on the current Dimorphos OPC is degrees (older HERA exports: gradians; the `.opcx` range tells).

## Related, not part of this work

#816 (GL test deadlock), open. Worktree cleanup was started and then parked; 13 worktrees remain, several with other sessions' unmerged work. Leave them.
