# SBMT Annotation Import

## Synopsis

PRo3D can import annotation catalogs exported from **SBMT** (the [Small Body
Mapping Tool](https://sbmt.jhuapl.edu/), JHU/APL) and display them as native
PRo3D annotations. This lets features that were mapped in SBMT — boulders,
craters, individual points of interest — be overlaid on the same small-body
shape model inside PRo3D.

Currently supported: **points**, **ellipses** and **circles**.
Lines, polylines and polygons are recognised but not yet imported.

## Using it

1. Load the body's shape model as a PRo3D surface (for the DART/Hera datasets
   this is an SPC-derived `.obj`, e.g.
   `dimorphos_g_0250mm_spc_obj_0000n00000_v003.obj`).
2. Menu → **Import** → **Import SBMT Annotations**.
3. Pick a structure file. The file dialog's filter is deliberately wide
   (`*.txt` plus `*`) because real catalogs are often shipped with **no file
   extension** at all.
4. The file arrives as one new annotation group in the Annotations tree, named
   after the file.

The imported group is **collapsed** by default, and catalogs larger than 100
entries are bucketed into numbered sub-folders (`1 - 100`, `101 - 200`, …).
This is not cosmetic: the annotation tree materialises a DOM node for *every*
leaf the moment its group is expanded, and expanding a 4,800-row group in one
go visibly stalls the UI.

### Coordinate frame

The importer applies **only a km → m unit conversion** and an identity
transform. It trusts the file's cartesian `centerXYZ` as-is — it does not
recompute positions from the lat/lon/radius columns, and it applies no SPICE
reprojection. The reference frame is currently hardcoded to the label
`DIMORPHOS_SHM` at the call site in `Viewer.fs`.

Consequences:

- **Dimorphos catalogs land correctly.** The SPC OBJ and the SBMT export are
  both authored in `DIMORPHOS_SHM` (the DART-team label `DARTSOC` resolves to
  the same frame), so identity is the right transform.
- **Didymos catalogs land off the body.** The Didymos SBMT export and the
  Didymos OBJ are *not* in the same frame, and no `DIDYMOS_SHM` exists in the
  current Hera kernel set to bridge them. This is a known limitation, not an
  importer bug — see [Known limitations](#known-limitations).

`SbmtImporter.startImporter` and `AnnotationGroupsImporter.importSbmt` both
take a `Trafo3d` that is applied to every position *after* the km → m scale.
That is the seam where a SPICE-derived rotation will eventually be supplied;
the km → m step stays inside the importer so the output is always in meters
regardless of what the caller passes.

For the full frame derivation (`DIMORPHOS_FIXED` vs `DIMORPHOS_SHM`, the
180°-around-Y relationship, the `DARTSOC` identification, and the axis
cheat-sheet) see `plans/archive/sbmtImport.md`.

## File format

A structure file is plain text:

- Tab-separated columns, one structure per line.
- Lines starting with `#` are comments; blank lines are ignored.
- A comment line `# type,<kind>` declares the structure type. `<kind>` is one
  of `point`, `line`, `polyline`, `polygon`, `circle`, `ellipse`. The importer
  fails with an explicit error if this header is missing.
- **Angles in degrees, lengths in kilometers.**

### Point files — 17 columns

| # | Index | Field | Used |
|---|---|---|---|
| 1 | 0 | `id` | – |
| 2 | 1 | `name` | – |
| 3–5 | 2–4 | `centerXYZ[3]` — body-fixed cartesian, km | **yes** |
| 6–8 | 5–7 | `centerLLR[3]` — lat°, lon°, radius km | – |
| 9–12 | 8–11 | `coloringValue[4]` — slope / elevation / acceleration / potential | – |
| 13 | 12 | `diameter` | – (meaningless for points) |
| 14 | 13 | `flattening` | – |
| 15 | 14 | `regularAngle` | – |
| 16 | 15 | `colorRGB` — `R,G,B` in 0..255, comma-separated, no spaces | **yes** |
| 17 | 16 | `label` — quoted string | **yes** |

### Ellipse and circle files — 18 columns

Identical to the point layout with one extra `gravityAngle` column inserted
between `colorRGB` and `label`:

| # | Index | Field | Used |
|---|---|---|---|
| 1–12 | 0–11 | id, name, `centerXYZ[3]`, `centerLLR[3]`, `coloringValue[4]` | `centerXYZ` only |
| 13 | 12 | `diameter` — **major-axis diameter**, km | **yes** |
| 14 | 13 | `flattening` — `b/a` in 0..1 (1 = circle) | **yes** |
| 15 | 14 | `regularAngle` — deg, major axis vs. the local line of longitude | **yes** |
| 16 | 15 | `colorRGB` | **yes** |
| 17 | 16 | `gravityAngle` — deg, major axis vs. local gravity vector (often `NA`) | – |
| 18 | 17 | `label` | **yes** |

Circle files use the ellipse layout with `flattening = 1` and are handled by
the same parser.

Rows with fewer columns than the type requires are skipped silently, so a
truncated trailing line will not abort an import.

## How annotations are constructed

### Points

`centerXYZ × 1000`, then the caller's trafo, becomes a single-point
`Geometry.Point` annotation. `colorRGB` becomes the annotation colour (alpha
forced to 255; an unparseable colour column falls back to magenta), and
`label` — with surrounding quotes stripped — becomes the annotation text.

### Ellipses

SBMT describes an ellipse by centre plus scalars, whereas PRo3D needs an
explicit boundary. The importer builds a tangent-plane basis at the centre and
samples the boundary into a `Geometry.AxisEllipse`:

```
C        = centerXYZ * 1000                    // centre, meters
upR      = C / |C|                             // radial out from the body centre
east     = normalize(cross(zAxis, upR))        // local longitude tangent
north    = normalize(cross(upR, east))
rot      = regularAngle in radians
majorDir =  cos(rot) * east + sin(rot) * north
minorDir = -sin(rot) * east + cos(rot) * north
a        = diameter * 1000 / 2                 // semi-major
b        = a * flattening                      // semi-minor
```

The boundary is then sampled at **60 points** around the full turn (no
duplicated closing point). PRo3D's interactive ellipse construction uses 200
samples; 60 keeps the silhouette smooth at realistic zoom levels while letting
a ~4,800-ellipse catalog parse and merge in about a second.

Two approximations are baked in here:

- **The tangent plane is perpendicular to the radial direction `C/|C|`** — the
  circumscribing sphere's normal, not the actual mesh normal. For boulder
  catalogs (ellipse ≪ body curvature) this is visually fine; at craters and
  steep slopes it will visibly tilt.
- **Pole singularity**: when `C` is parallel to `+Z` the east direction is
  undefined, and the code falls back to a basis derived from the world X axis.

`dnsResults` and `results` are deliberately **not** computed per annotation
during import. The per-row dip-and-strike regression and SVD dominate import
time on large catalogs, points have no meaningful dip and strike, and an
ellipse already encodes its plane in the sampled boundary. PRo3D recomputes on
demand when a user actually inspects an annotation.

## Known limitations

- **Didymos catalogs are placed wrongly.** The SBMT frame and the Didymos OBJ
  frame differ by an as-yet unidentified rotation. Importing a Didymos boulder
  catalog against the Didymos OBJ puts the ellipses off the surface. This is
  the canonical regression case for the future SPICE-based reprojection path.
- **The reference-frame label is not stored.** It is threaded all the way
  through the parsers as a parameter but then dropped: `Annotation` has no
  field for a source-frame string, and `referenceSystem` is set to `None`.
  Deciding where it lands is a prerequisite for frame-aware reprojection.
- **No frame modal.** The plan called for prompting the user for the frame at
  import time; the current build hardcodes `DIMORPHOS_SHM`.
- **Lines, polylines and polygons** are detected and logged as unimplemented;
  the import yields an empty group rather than failing.
- Only the **first** selected file is imported. The dialog allows multi-select
  and the action carries a list, but the handler takes `List.tryHead`.
- `centerLLR`, `coloringValue[4]`, `gravityAngle` and `name` are all dropped.

## Where the code lives

| File | Role |
|---|---|
| `src/PRo3D.Core/Importers/SbmtImporter.fs` | Header detection, per-line point/ellipse parsers, `startImporter` dispatch |
| `src/PRo3D.Core/Importers/AnnotationGroupsImporter.fs` | `importSbmt` — wraps parsed annotations into a `Node`, chunks large catalogs, builds `flat` / `lookup` |
| `src/PRo3D.Viewer/Viewer-Model.fs` | `ImportSbmtAnnotations of list<string>` action |
| `src/PRo3D.Viewer/Viewer/Viewer.fs` | Action handler — merges the imported group into `m.drawing.annotations` |
| `src/PRo3D.Viewer/Viewer/ViewerGUI.fs` | Import menu entry and the Electron open dialog |
| `src/Tests/SbmtImportAlignmentTest.fs` | Test suite (see below) |
| `plans/archive/sbmtImport.md` | Original design plan, frame investigation, open TODOs |

## Tests

`SbmtImportAlignmentTest` mixes two kinds of test.

**Self-contained tests** synthesize SBMT files in a temp directory and always
run. They cover header detection (including missing and unsupported headers),
point column mapping, colour and quoted-label handling, short-row rejection,
km → m scaling, trafo application, circle-as-ellipse dispatch, ellipse
planarity and semi-axis lengths, `regularAngle` orientation, and the grouping
and chunking behaviour of `importSbmt`.

**Fixture-backed tests** read real SBMT exports and **skip themselves** when the
data is absent. They cover a v4 point export importing with km → m applied,
ellipse planarity, alignment of an SBMT point against a manually picked PRo3D
annotation on the same feature, distance preservation under a frame rotation,
and bulk import performance plus drawing-model integrity on the ~4,800-ellipse
Dimorphos catalog.

Fixtures live under `imports/` in a
[PRo3D.Resources.TestData](https://github.com/pro3d-space/PRo3D.Resources.TestData)
checkout, resolved the way the rest of the data-backed suite resolves it:
`PRO3D_TEST_DATA` first, the suite-wide `--testdatasource` second.

| Fixture | Contents |
|---|---|
| `imports/basicSBMT-dimorphos-v4/sbmtimport.points.txt` | 3-point SBMT v4 export of Dimorphos |
| `imports/basicSBMT-dimorphos-v4/sbmtimport.ellipses.txt` | ellipse export of the same body |
| `imports/basicSBMT-dimorphos-v4/sbmtimport.circles.txt` | circle export — not yet exercised |
| `imports/basicSBMT-dimorphos-v4/sbmtimport.paths.xml` | path export — the importer does not read lines yet |
| `imports/basicSBMT-dimorphos-v4/sbmtimport.polygons.xml` | polygon export — likewise |

Two fixtures are too large or not redistributable and are therefore **not** in
the checkout:

| Fixture | Contents |
|---|---|
| `pointOnPike.points.txt` | one SBMT point on the Dimorphos "Pike" feature |
| `anno.json` | PRo3D-native cartesian GeoJSON with a manually picked point on the same feature |
| `Dimo_Bould_Glob_7_Maurizio` | ~4,800 ellipses on Dimorphos |

They are searched under `<root>/imports` first, so dropping them into the
checkout is enough. Otherwise they are looked for at
`<PRO3D_PRIVATE_TESTDATA>/shapemodels/testdata` — the root for fixtures that
cannot be committed, defaulting to `C:\pro3ddata` when the variable is unset.
`PRO3D_SBMT_TESTDATA` names that one directory directly if it sits elsewhere.

```
set PRO3D_TEST_DATA=C:\path\to\PRo3D.Resources.TestData
dotnet run --project src\Tests -- --filter "all.all tests.sbmtImport"
```

With the checkout alone: 22 pass, 4 skip. With the external catalogs as well:
26 pass, 0 skip. With neither: 20 pass, 6 skip.

`run-tests.cmd` (Windows) and `run-tests.sh` work too; they pass
`--testdatasource` for you.
