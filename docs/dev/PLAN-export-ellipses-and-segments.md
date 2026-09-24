# Plan: ellipse geometry and per-segment rows in the CSV export

2026-09-24 · covers proposals 1, 2 and 4 (here steps 1, 2, 3) of [AnnotationExport-boulders-fractures.md](AnnotationExport-boulders-fractures.md). Proposal 3 (surface statistics inside an ellipse) is built in parallel by session `sampling`; see *Parallel work*.

Goal: two clean CSV exports, one per question.

- **Boulders**: one row per ellipse, with semi-axes (m) and long-axis azimuth (deg).
- **Fractures**: one row per segment, with that segment's length and the line's total.

## Landing plan (issue #644)

State on 2026-09-24, against `origin/develop` 1ed7c3a3 (#810, #811, #813, #815, #817 and #818 merged):

- **Profiles**: done. Per-point CSV, only per-vertex layers sampled (#817), Earth decode gone (#810); tests resolve the new test data (#818).
- **Boulders**: nothing exported yet. Diameter columns come out empty; no orientation; no statistics.
- **Fractures**: total length (`wayLength`) and per-point `segmentLength` work; no per-segment rows.
- **Statistics inside an ellipse**: library written by session `sampling` (worktree `pro3d-ellipse-stats`, branch `features/644_ellipse-surface-stats`), uncommitted, on a pre-#818 base.

Five PRs, each small enough to review in one sitting, each merged before the next one that depends on it:

| PR | Branch | Owner | Contents | Depends on |
| --- | --- | --- | --- | --- |
| A | `features/644_ellipse-surface-stats` | sampling | `EllipseStatistics` library, its tests, `docs/EllipseStatistics.md`. No export or UI change. | — |
| B | `features/644_boulders-fractures-export` | boulders | Step 1 (store the metric ellipse on drawing and SBMT import) + step 2 (`semiMajorAxis`, `semiMinorAxis`, `majorAxisAzimuth` columns; diameters retired; colour-by-category attributes) + the ellipses-only type filter + *Boulders* preset. Field reference moves to `docs/AnnotationExport-CSV.md`. | — |
| C | `features/644_fracture-segments` | boulders | Step 3: *one record per segment* (CSV only) + *Fractures* preset. | B (shared preset/window code) |
| D | `features/644_ellipse-stats-export` | sampling | Statistics columns on *Boulders* (see [AnnotationExport-CSV.md](../AnnotationExport-CSV.md), second stage). | A, B |
| E | `bugs/644_ground-distance-small-bodies` | anyone | `groundDistance` 0 on Dimorphos/Didymos: flatten onto the mean radius for spherical-convention bodies. | — |

A, B and E can be built in parallel; they share no files. C and D wait for their dependencies to be on `develop`, then branch from it fresh.

### Definition of done, per PR

- Branch from current `origin/develop`; rebase, don't merge, before the PR.
- Unit tests for everything testable without the viewer (see *Testing*); run each touched test list in its own process (full-suite runs can deadlock, #816).
- Build green in CI, not only locally; don't cancel the PR's CI run.
- `docs/` updated in the same PR (CLAUDE.md rule).
- One session per worktree. Nothing of this work goes into `pro3d-6`.

### Playwright

After B and C are on `develop`, one spec in `tests-ui/`: draw an ellipse and a two-segment line on the Dimorphos OPC, export with *Boulders* and *Fractures*, check both CSV headers against [AnnotationExport-CSV.md](../AnnotationExport-CSV.md) and the ellipse's `semiMajorAxis` against the drawn size. Plus the window checks listed under *Testing*.

## Presets: two, not one

A CSV export has one granularity, so one row shape. Boulders need a row per ellipse, fractures a row per segment. One file mixing both would leave half the columns empty in every row. Hence two presets, and the user exports twice:

| Preset | File type | Granularity | Scope | Columns (in order) |
| --- | --- | --- | --- | --- |
| *Boulders* | CSV | one record per annotation | All | `key, text, groupPath, surfaceName, semiMajorAxis, semiMinorAxis, majorAxisAzimuth`, centre `x, y, z, lat, lon, alt, body, latLonAltSource` |
| *Fractures* | CSV | one record per segment (new) | All | `key, text, groupPath, surfaceName, wayLength, segmentIndex`, `start…`/`end…` coordinates, `body, latLonAltSource, segmentLength, segmentChord, segmentAzimuth` |

Field-by-field meaning of every column, including *Profile*'s: [AnnotationExport-CSV.md](../AnnotationExport-CSV.md).

Rejected alternative: one *Mapping* preset that writes two files, `<name>.boulders.csv` and `<name>.fractures.csv`. It saves one click but is the only export that writes two files from one dialog. Revisit if users ask.

Rows that do not fit a preset are left out, not padded:

- *Boulders* sets the new **Annotation types: ellipses only** filter (below), so lines, points and polygons produce no rows.
- *Fractures* needs no filter: an ellipse's outline has no clicked-to-clicked segments, so it yields no segment rows. Polygon edges count as segments.

### Annotation type filter

A new setting, shown in the window as a dropdown row under *Scope*, so it is never a hidden rule:

```
Annotation types   all | ellipses only
```

- `ExportTypeFilter` enum (`All = 0`, `EllipsesOnly = 1`) in `ExportSettings.fs`, field `typeFilter` on `AnnotationExportSettings` and on the export window model (`AnnotationExport-Model.fs`, regenerate with Adaptify). Session-only like every other export setting, so nothing is persisted.
- Applied in `AnnotationExportViewer.annotationsInScope`, after the scope: *ellipses only* keeps `AxisEllipse`, `Axis4PEllipse` and `Ellipse` geometries.
- *Boulders* sets `EllipsesOnly`; every other preset sets `All`, so switching presets never leaves the filter on by surprise.
- An export whose filter leaves nothing reports it like an empty scope: "No ellipses in scope, so there is nothing to export." 

## Naming rules

Existing columns are camelCase with no unit suffix; units live in the window labels and in `docs/AnnotationExport.md`. The new columns follow the same rules, plus:

- **Semi-axes, not diameters.** `semiMajorAxis` / `semiMinorAxis` are the numbers users ask for. `majorDiameter` / `minorDiameter` are retired: their enum numbers (24, 25) stay unused like 30–36, and the columns disappear. They have never held a correct value (empty now, degrees before).
- **Azimuth = clockwise from local north, in degrees, axial 0–180.** Used for both `majorAxisAzimuth` and `segmentAzimuth`. An axis or a lineament has no head, so 180° and 0° are the same direction. The existing line field `bearing` is left as it is.
- **`segment…` prefix** for everything per segment; `start…` / `end…` prefixes for segment endpoints (`startX … endAlt`), each set gated by the coordinate mode like the point columns.
- **Metres throughout.** Body-fixed, like every other length the export writes.

| Column | Unit | Meaning |
| --- | --- | --- |
| `semiMajorAxis`, `semiMinorAxis` | m | ellipse semi-axes on its fitted plane |
| `majorAxisAzimuth` | deg, 0–180 | long axis projected into the local horizontal at the centre |
| `segmentIndex` | — | 0-based, in drawing order |
| `segmentLength` | m | draped length (same value as the per-point column) |
| `segmentChord` | m | straight 3D distance start → end |
| `segmentAzimuth` | deg, 0–180 | start → end, projected into the local horizontal at the segment midpoint |

## Step 1: keep the metric ellipse

**Model.** Extend `EllipticAnnotationResult` (`src/PRo3D.Base/Annotation/Annotation-Model.fs`) with the ellipse in body-fixed metres:

```fsharp
center           : V3d    // m, body-fixed
semiMajorAxis    : V3d    // m, vector along the long axis
semiMinorAxis    : V3d    // m
majorAxisAzimuth : float  // deg, 0–180 from local north; NaN for a near-vertical axis
```

The azimuth is computed **once, at construction**, with the up and north the constructor already has, and stored. It is the same practice as dip and strike (`dnsResults`), which are fixed at construction too. The export and colour-by-category then only read stored values: neither needs a reference system.

Written in `ToJson`, read in `readV0` with `Json.tryRead` and a default. Adding fields needs no version bump. `geographicalEllipse` stays for the GeoJSON writer (`GeoJSON.Export.fs:89`). There is no legacy data to migrate: a result read without the new fields simply exports empty ellipse columns. No refitting.

**Where it gets filled.**

| Source | Today | Change |
| --- | --- | --- |
| Drawing, plane branch (`Drawing-App.fs`, `AxisEllipse`/`Axis4PEllipse`, `geo = false`) | computes `ConstructedEllipse` (`ellipseOnPlane` on `constructionPlane`), stores `ellipticResults = None` | map `ellipseOnPlane` centre and axes to world with `constructionPlane.GetPlaneToWorld()` and store them; also store `geographicalEllipse` from the existing `createGeographicalEllipse` |
| SBMT import (`SbmtImporter.parseEllipseLine`) | computes `center`, `semiMajor`, `semiMinor` in metres, stores `None` | store them directly. The boulder catalogs come in this way. |

`geo = true` (the dormant geographic branch) is left alone; delete it in a follow-up if nobody needs it.

**Tests** (`src/Tests/AnnotationExportTest.fs`, plus a drawing test through the `Draw` harness):

- A drawn axis ellipse stores a result whose axes match the three clicked points.
- JSON round-trip keeps the new fields; a file without them still loads, with empty ellipse columns.
- An SBMT ellipse row imports with `semiMajorAxis` = diameter / 2 and `semiMinorAxis` = that × flattening. Fixture: `imports/basicSBMT-dimorphos-v4/sbmtimport.ellipses.txt` under `PRO3D_TEST_DATA_PRIVATE`.

## Step 2: axes and azimuth columns

**Row coordinates.** A per-annotation row's `x, y, z` / `lat, lon, alt` are the bounding-box centre of the annotation's points. For an ellipse with a stored result they become the stored ellipse centre instead: one line in `perAnnotationRecord`.

**Fields.** New `AnnotationField` values 39–41 (`SemiMajorAxis`, `SemiMinorAxis`, `MajorAxisAzimuth`) in the *Ellipse* group, read straight from the stored result (lengths of the axis vectors, the stored azimuth). Retire 24–25 from `AnnotationFields.all` and the presets.

**Local frame, at construction.** Drawing: `finishAndAppend` already receives `up` and `north`; on a body they are re-derived at the ellipse centre (`ReferenceSystem.upVector` / `northVector`, both in `PRo3D.Core` next to the drawing code). SBMT import: `parseEllipseLine` already builds the local `east` / `north` at the centre. No change to the export's signature.

**Math** (one helper in `PRo3D.Core`, used by both constructors), with `m` = semi-major vector, `u` = up, `n` = north, `e = n × u`:

```
h                = m − (m·u) u                         // horizontal part
majorAxisAzimuth = atan2(h·e, h·n) in degrees, mod 180
```

A near-vertical axis (|h| < 1e-9·|m|) has no azimuth: write empty, not 0.

**Tests**: a synthetic ellipse with known axes at a known lat/lon gives the expected azimuth. SBMT cross-check: `majorAxisAzimuth` = (90° − SBMT regular angle) mod 180. The importer measures that angle from east toward north.

**Colour by category.** Append `SemiMajorAxis = 15`, `SemiMinorAxis = 16`, `MajorAxisAzimuth = 17` to `ColorCategoryAttribute` (persisted, so appended, never renumbered). Add them to `valueOf` and `valueOfAdaptive` in `ColorByCategoryApp.fs`, reading `ellipticResults`; NaN for non-ellipses, like dip for a line. Azimuth is axial, so a cyclic colour ramp would suit it; the existing ramps are used as they are for now.

## Step 3: one record per segment

**Settings.** `ExportGranularity.PerSegment = 2`, offered in the granularity dropdown (`AnnotationExportApp.fs:326`) **only when the file type is CSV**; `granularityLabel` and the hint cover it. Switching the file type away from CSV while per segment is chosen falls back to per annotation. GeoJSON, Attitude and continuous GeoJSON are untouched.

**Records.** `buildRecords` gets a third branch. `resolvePoints` already tags every sampled point with its segment and draped length, so a segment row is a group-by over the same list:

- `segmentIndex`, `segmentLength` from the group.
- `start…` / `end…` coordinates from the group's first and last point (geographic via the same `resolveGeographic`).
- `segmentChord` = distance start → end; `segmentAzimuth` from start → end at the midpoint's local frame. Segments are computed at export time, so here the export does need a local frame: on a body, up/north at the midpoint from `CooTransformation.getUpVector` plus a `northVector` moved to `PRo3D.Base`; in flat frames the reference system's `north`, passed into `write` next to `up`.
- Annotation fields repeat on every row, as in per-point rows.
- A line drawn with linear projection has no stored segments: segment *i* = clicked point *i* → *i+1* (the fallback per-point export already uses).

**Fixed columns.** Per-segment rows always carry the same segment columns: `segmentIndex`, the `start…`/`end…` coordinates (gated by the coordinate mode), `segmentLength`, `segmentChord`, `segmentAzimuth`. They follow the chosen annotation fields. No new enum, no checkbox section: the *Point attributes* accordion is simply hidden, as it is for per-annotation exports. *Surface properties* and *Sampled points* are hidden too; neither changes per-segment rows (a per-segment statistic comes with proposal 3).

**Tests**: a two-segment line gives two rows whose `segmentLength`s sum to `wayLength`; `segmentChord` ≤ `segmentLength`; an ellipse in scope produces no rows; the *Fractures* preset's header is exactly the column list above. The *Boulders* preset on a mixed scene exports only the ellipses; switching to another preset resets the filter to *all*.

## Testing

Almost everything is testable without the viewer, in `src/Tests` (Expecto):

| What | How | Level |
| --- | --- | --- |
| Column names and order per preset | `AnnotationExport.schemaOf (applyPreset …)` equals the lists in [AnnotationExport-CSV.md](../AnnotationExport-CSV.md) | unit |
| Boulder values | synthetic annotation with a known `EllipticAnnotationResult` → `buildRecords` → semi-axes, azimuth, centre | unit |
| Azimuth helper | known vectors at known lat/lon, incl. the 0/180 fold and a vertical axis → empty | unit |
| Ellipse stored on drawing | the drawing finish function with a stub `sampleSurface` that projects onto a plane (no GL, no OPC) | unit |
| SBMT ellipses | parse `sbmtimport.ellipses.txt` (private data) → stored axes and azimuth match diameter, flattening, angle | unit, data-backed |
| Fracture rows | two-segment line → two rows; lengths sum to `wayLength`; chord ≤ length; ellipse → no rows | unit |
| Ellipses-only filter | mixed annotation list through `annotationsInScope` | unit |
| Colour by category | `ColorByCategoryApp.valueOf` for the three new attributes; NaN for a line | unit |
| JSON round-trip | new fields survive write/read; a file without them loads | unit |

Playwright (`tests-ui/`) only where the behaviour lives in the window:

- **Export window**: choosing *Boulders* shows *Annotation types: ellipses only*; *Fractures* shows *one record per segment*; the per-segment option disappears when the file type is not CSV; switching presets resets the type filter.
- **One end-to-end run**: draw an ellipse and a two-segment line on the Dimorphos OPC, export with both presets, check the CSV headers and that the ellipse's `semiMajorAxis` is within a few percent of the drawn size.

## Documentation and delivery

- `docs/AnnotationExport.md`: the two presets, the annotation type filter, the per-segment granularity, the new columns and units, and the retirement of the diameter columns.
- `docs/SbmtImport.md`: imported ellipses now carry their axes.
- One issue and one branch off `develop` (`features/<issue>_export-ellipses-segments`), three commits in step order. One PR, or two (steps 1+2, step 3) if review prefers smaller pieces.

## Parallel work: surface statistics inside an ellipse (phase b)

Session `sampling`, worktree `pro3d-ellipse-stats`, branch `features/644_ellipse-surface-stats` (issue #644). It integrates the mesh triangles inside an ellipse: per layer and channel mean/std/min/max, plus surface area, footprint area and vertex count. It touches no file of this plan (only `EllipseStatistics.fs` (new), `VertexAttributes.fs`, `Surface.fs`, `ProfileAttributeExtraction.fs` helpers).

- **Interface:** its input `SurfaceEllipse = { center; semiMajor; semiMinor }` (world space, metres) maps 1:1 onto step 1's stored `center`, `semiMajorAxis`, `semiMinorAxis`. Same space as the annotation's points, no conversion.
- **Hook-up:** `sampling` adds the statistics columns to *Boulders* after step 2 is on `develop`, following [AnnotationExport-CSV.md](../AnnotationExport-CSV.md).

## Decisions (2026-09-24)

- Azimuth reference: **local north**.
- Default scope of both presets: **All**.
- Longitude of both presets: **Native** (the body's own longitudes, like the QGIS preset). *Profile* and the CSV default stay *Flipped*.
- `majorDiameter` / `minorDiameter`: **retired** now, no transition release.
- **No refitting** of ellipses saved before the change; there is no legacy data.
- Orientation: **azimuth only**, no plunge.
- *Boulders* uses a visible **ellipses-only type filter**.
- Ellipse measures are **stored at construction** and also offered in **colour by category**.
- Segment rows: **fixed columns** — endpoints, `segmentChord`, `segmentAzimuth` included; no selectable segment fields.
- Per segment is **CSV only**; the GeoJSON writer stays untouched.
- Principle: **minimal change, safety first**. Anything not needed for the two presets stays out.

## Corrections found while building PR B (2026-09-24)

The plan above was checked against the code before B was written. Where it was wrong or silent, B does the following, and C/D should assume it:

- **JSON keys.** `EllipticAnnotationResult` already writes `center` / `major` / `minor`, holding the *lon/lat* ellipse. The metric fields use their own keys: `worldCenter`, `semiMajorAxis`, `semiMinorAxis`, `majorAxisAzimuth`. Reusing `center` would have clashed.
- **`geographicalEllipse` is now `Option<Ellipse2d>`.** Plane-fitted and imported ellipses have no lon/lat ellipse, and the old reader required one. The JSON stays the same whenever it is present.
- **The lon/lat ellipse is not filled on drawing.** The plan said to store `createGeographicalEllipse`'s result too. That would newly emit `EllipseProperties` in lon/lat degrees in the GeoJSON export, a change nobody asked for and in the unit this plan calls wrong. GeoJSON output stays as it was; the dead `createGeographicalEllipse` call on the drawing path is gone.
- **Axis0 is not necessarily the major axis.** `constructEllipseOrtho2d` takes the axis from the first two clicks and a *signed* minor from the third, so the third click can give the longer axis. The stored result sorts by length.
- **Four-point ellipses were not specified.** Stored as the symmetric ellipse of the same extent: semi-minor = half the full width across the clicked axis, centre in the middle of that width (it can lie off the clicked axis). Documented in AnnotationExport-CSV.md.
- **Local frame.** Drawing receives the reference system's `northO` (north with the user's offset). Flat frames (`None`, `JPL`, `ENU`) keep it, which matches `bearing` and dip/strike. On a body, up and north are re-derived at the ellipse centre (`EllipticAnnotations.Measures.localFrame`, the same rule as `updateCoordSystemAt`), so the north offset does not apply there.
- **Colour by category already has an axial hue wheel** (`cyclicPeriod`, 180° for strike and bearing). `majorAxisAzimuth` uses it; the plan's "existing ramps for now" was not needed.
- **Column order follows the enum, not the preset list.** `AnnotationExportModel.toSettings` re-sorts the fields by enum value, so the window writes `key, text, surfaceName, groupPath, …` whatever order the preset lists. *Boulders* is listed in that order, so the preset and the window agree. **For C:** *Fractures* comes out as `key, text, surfaceName, wayLength, groupPath`, then the segment columns; AnnotationExport-CSV.md already says so.
- **`EllipticAnnotationResult` is not a model type**, only a field of one; changing it needs no Adaptify run (the new `typeFilter` on the export window model does).
- `Geometry.Axis4PEllipse` and `Ellipse` still throw in the *legacy* GeoJSON writer (`GeoJSON.Export.fs`); the new exporter degrades them to rings. Not touched here.
