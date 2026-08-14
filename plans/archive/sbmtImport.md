# SBMT Annotation Import

## Goal
Import annotations produced by SBMT (Small Body Mapping Tool) into PRo3D. SBMT
exports per-feature-type "structure files" (`.points.txt`, `.lines.txt`,
`.polygons.txt`, `.circles.txt`, `.ellipses.txt`, and free-named ellipse
catalogs without the `.txt` suffix). PRo3D should be able to load these as
native annotations so features identified in SBMT can be overlaid on the same
shape model in PRo3D.

## Milestones
- **v1 — Points.** End-to-end pipeline (parsing → record building → group
  wrapping → wiring → display).
- **v2 — Ellipses.** Adds ellipse parsing and conversion to PRo3D's
  `AxisEllipse` geometry. Real-world fixtures are large boulder catalogs.
- **v3 — Lines, polylines, polygons, circles.** Largely format-only changes
  once points and ellipses work.

## Reference fixtures
- `C:\pro3ddata\shapemodels\testdata\pointOnPike.points.txt` — single point on
  Dimorphos (v1 fixture; also used by `SbmtImportAlignmentTest`).
- `C:\pro3ddata\shapemodels\testdata\anno.json` — PRo3D-native cartesian
  GeoJSON containing a manually picked Point near the same Pike feature
  (alignment reference for the test).
- `C:\pro3ddata\shapemodels\testdata\Didy_Boulders_Paj_Tusb_Lucch` — ~170
  ellipses on **Didymos** (v2 fixture, smaller).
- `C:\pro3ddata\shapemodels\testdata\Dimo_Bould_Glob_7_Maurizio` — ~4,800
  ellipses on **Dimorphos** (v2 stress fixture).
- Companion shape models: `C:\pro3ddata\shapemodels\dimorphos_g_0250mm_spc_obj_0000n00000_v003.obj`
  (and three lower-LOD global variants + one local high-res model). Didymos
  shape model is in the same `shapemodels` directory.
- SBMT manual: https://sbmt.jhuapl.edu/docs/Manual.pdf

## Coordinate frame knowledge

This section consolidates what we know about the frames the SBMT files live in,
to spare future readers from re-running the FK / OBJ investigation.

### The body-fixed frame chain (Hera SPICE kernels)

For Dimorphos (`src/../spice/kernels/fk/hera_v16.tf`):

| SPICE name | ID | Class | Relationship |
|---|---|---|---|
| `DIMORPHOS_FIXED` | -658031 | 5 (parameterised two-vector) | +X = Dimorphos→Didymos, +Y along orbital velocity, +Z = right-hand spin axis (north pole). Assumes synchronous rotation. |
| **`DIMORPHOS_SHM`** | **-6580310** | **4 (TK fixed-offset)** | **`RELATIVE = DIMORPHOS_FIXED`, `ANGLES = (0°, 180°, 0°)` on axes (1, 2, 3)** — the shape-model frame, rotated 180° around Y from FIXED. The FK comment at `hera_v16.tf:479-480` documents this as the frame "used for the crater position by GMV", i.e. the SBMT use case verbatim. |
| `DIMORPHOS_CK` | -6580311 | 3 (CK) | Body-fixed via CK file, used for post-impact orientation modelling. |

For Didymos the corresponding frames are `DIDYMOS_FIXED` (-658030, class 2
PCK) and `DIDYMOS_CK` (-6580301, class 3). There is no `DIDYMOS_SHM` in the
current kernel set; the Didymos SBMT files use whatever frame the loaded
Didymos OBJ defines via its `#ORIGIN` tag. The exact mapping is **not** part
of v1's responsibility — the modal lets the user enter it.

### `DARTSOC` ≡ `DIMORPHOS_SHM`

The SPC OBJ header carries `#ORIGIN = DARTSOC` (DART Science Operations
Center). This is a **DART-team label, not a SPICE frame**, and resolves
operationally to `DIMORPHOS_SHM`. Evidence:

1. The FK comment quoted above places shape-model crater positions in
   `DIMORPHOS_SHM`.
2. PRo3D's existing dataset (`anno.json` selected Point) matches the OBJ
   vertices × 1000 to within a few meters, confirming the frame is the same
   and the only transformation PRo3D applies on OBJ load is km → m scaling.
3. The OBJ header reports the body's max-MoI principal axis at
   `[-0.0019, -0.0104, -0.9999]` (i.e. essentially −Z). For a tidally-locked
   asteroid this axis is the spin axis. In `DIMORPHOS_FIXED` the spin axis is
   +Z by definition, so SHM's spin axis being at −Z is consistent with the
   180°-around-Y rotation in the FK.

### Axis cheat-sheet

In `DIMORPHOS_SHM` ≡ `DARTSOC`:

| Direction | DIMORPHOS_FIXED | **DIMORPHOS_SHM** |
|---|---|---|
| +X | toward Didymos | **away from Didymos** |
| +Y | orbital velocity | orbital velocity |
| +Z (spin axis, north pole) | +Z | **−Z** |
| anti-spin axis (south pole) | −Z | **+Z** |

Practical consequence for PRo3D: every Dimorphos dataset loaded from a SPC OBJ
sits in `DIMORPHOS_SHM` with units = meters. The north pole points to
**negative Z**, which the reference-system UI's default world-up doesn't know
about.

### Didymos: SBMT ↔ OBJ frames do NOT match (test case)

Observed: loading the v2 Didymos fixture
(`Didy_Boulders_Paj_Tusb_Lucch`, ~170 ellipses) against the Didymos SPC OBJ
with the identity trafo + km → m places the boulders **off the body**.
The Dimorphos pipeline (same code, identity trafo, `DIMORPHOS_SHM`) lands
on-surface — so the discrepancy is frame-specific to Didymos, not a general
importer bug.

Hypothesis: the SBMT export uses `DIDYMOS_FIXED` (the class-2 PCK frame),
while the SPC OBJ for Didymos is authored in a different shape-model frame
(no `DIDYMOS_SHM` exists in `hera_v16.tf`; the OBJ's `#ORIGIN` tag carries a
DART-team label whose SPICE equivalent we have not yet pinned down). The two
likely differ by a rotation analogous to the 180°-around-Y between
`DIMORPHOS_SHM` and `DIMORPHOS_FIXED`, but the exact mapping needs
verification against the Didymos OBJ header and FK.

This is exactly the case the importer's `Trafo3d` seam is meant to handle:
v1 still imports with identity (intentionally wrong placement for Didymos,
correct for Dimorphos), and the eventual SPICE-derived rotation gets passed
in by the caller without changing the importer.

Use this as a **regression test case** when wiring SPICE-based reprojection:
import `Didy_Boulders_Paj_Tusb_Lucch` with the correct SBMT-frame → OBJ-frame
trafo and verify the ellipses sit on the loaded Didymos OBJ surface.

### Units
SBMT structure files: kilometers. PRo3D internal: meters. Importer
multiplies by 1000.

## SBMT structure file format (general)
- Plain text, **tab-separated** columns.
- Lines starting with `#` are comments; blank lines ignored.
- Header line `# type,<kind>` declares the structure type (`point`,
  `line`, `polyline`, `polygon`, `circle`, `ellipse`).
- Column count and meaning depends on the structure type — see the per-type
  sections below.
- Angle units: degrees. Length units: kilometers.

### Point file format — 17 columns (v1)

  | # | Field | Notes |
  |---|---|---|
  | 1 | `id` | int |
  | 2 | `name` | string |
  | 3–5 | `centerXYZ[3]` | body-fixed cartesian, **km** |
  | 6–8 | `centerLLR[3]` | lat°, lon°, radius km |
  | 9–12 | `coloringValue[4]` | slope / elevation / acceleration / potential (often `NA`) |
  | 13 | `diameter` | km — irrelevant for points |
  | 14 | `flattening` | 0..1 — irrelevant for points |
  | 15 | `regularAngle` | deg — irrelevant for points |
  | 16 | `colorRGB` | `R,G,B` (0..255, comma-separated, no spaces) |
  | 17 | `label` | quoted string |

### Ellipse file format — 18 columns (v2)

Same as the point file with an extra `gravityAngle` column inserted between
`colorRGB` and `label`:

  | # | Field | Notes |
  |---|---|---|
  | 1–8 | id, name, centerXYZ, centerLLR | as above |
  | 9–12 | `coloringValue[4]` | slope, **elevation (m)**, **acceleration (m/s²)**, **potential (J/kg)** |
  | 13 | `diameter` | **km**, major-axis diameter of the ellipse |
  | 14 | `flattening` | 0..1; **1 = circle, 0 = degenerate line**. semi-minor = semi-major × flattening |
  | 15 | `regularAngle` | deg, angle between major axis and the local longitude line, projected onto the surface |
  | 16 | `colorRGB` | `R,G,B` |
  | 17 | `gravityAngle` | deg, angle between major axis and the local gravity vector (often `NA`) |
  | 18 | `label` | quoted string |

(Circle files use the ellipse format with `flattening = 1`. We treat them as
ellipses internally.)

## v1 strategy — points

- Convert km → m on import (factor 1000).
- **Trust the cartesian XYZ as-is.** Do not recompute from LLR. Do not apply
  any SPICE transform.
- Ask the user for the reference frame name via a modal at import time (see
  "UI" below). Default value: `"DIMORPHOS_SHM"`.
- Pass the entered string through to each annotation — see the open TODO
  "Reference-system field storage" below for the actual field choice.

LLR-based validation against the active spheroid and SPICE-based re-projection
are explicitly **out of scope for v1**.

### Future-friendly seam: extra `Trafo3d` parameter
The importer entry points accept an additional `Trafo3d` (default
`Trafo3d.Identity`) that is applied to every parsed XYZ **after** the
km → m unit scale. This mirrors the existing pattern in
`MeasurementsImporter.getAnnotation` and `AnnotationGroupsImporter.import`.

- **v1 callers pass `Trafo3d.Identity`** — annotations land in the source
  frame (SHM for Dimorphos) with km → m only.
- **Later (v2+) callers can supply a SPICE-derived rotation**, e.g.
  ```fsharp
  let frameTrafo =
      PRo3D.SPICE.CooTransformation.getRotationTrafo
          sourceFrame "DIMORPHOS_FIXED" time
      |> Option.defaultValue Trafo3d.Identity
  ```
  to reproject SHM → DIMORPHOS_FIXED (or any other frame) in a single pass,
  without changing the importer.
- The km → m scaling stays a hardcoded step inside the importer (not folded
  into the trafo), so output is always in meters regardless of what the
  caller passes.
- Caveat for later: `getRotationTrafo`
  (`src/PRo3D.GIS/SpiceInterfacing.fs:33`) silently returns
  `Some Trafo3d.Identity` if SPICE fails (e.g. kernels not loaded). Before
  wiring it into a real conversion path, change that failure mode to surface
  an error to the user.

### Point field mapping
| SBMT field | PRo3D `Annotation` field | Notes |
|---|---|---|
| `centerXYZ` × 1000, then `trafo` | `points = IndexList.singleton (V3d)` | only point geometry for v1 |
| `colorRGB` | `color.c : C4b` | parse comma-separated triple, alpha = 255 |
| `label` | `text : string` | strip surrounding quotes if present |
| (modal input) | reference-frame metadata | see open TODO below |
| `centerLLR` | — | dropped in v1 |
| `coloringValue[4]` | — | dropped (mostly `NA`) |
| `name` | — | dropped (revisit `surfaceName` later) |
| `diameter`, `flattening`, `regularAngle` | — | not meaningful for point type |

Geometry: `Geometry.Point`. Projection: `Projection.Linear`. Other annotation
record fields filled with the same defaults `MeasurementsImporter.getAnnotation`
uses (`Annotation.current`, `Semantic.Horizon0`, default thickness/textsize,
`visible = true`, etc.). For a single-point geometry, `dnsResults` degenerates;
pass `refSys.up.value` / `refSys.north.value` for parity with the v1 XML
importer and accept that the computed dip/strike will be null.

## v2 strategy — ellipses

Maps SBMT ellipses to PRo3D's `Geometry.AxisEllipse`, which the existing
codebase already describes as "inspired by SBMT elliptic annotations"
(`docs/PRo3D_ShortUserManual/ViewerFeatures.tex:909`).

### Constructing the ellipse in 3D

SBMT specifies an ellipse via center + scalar parameters; PRo3D's `AxisEllipse`
needs three points (center + endpoints of major and minor semi-axes). The
construction at import time uses the radial direction at the center as the
local "up" (no surface picking required, no SPICE call required):

```
C        = centerXYZ × 1000                            // center in meters
upR      = C / |C|                                     // radial out from body COM
east     = normalize( cross(zAxis, upR) )              // longitude tangent
                                                       // (degenerates at poles)
north    = cross(upR, east)                            // co-tangent
rot      = regularAngle in degrees, in tangent plane
majorDir =  cos(rot) * east + sin(rot) * north
minorDir = -sin(rot) * east + cos(rot) * north
a        = diameter * 1000 / 2                         // semi-major (m)
b        = a * flattening                              // semi-minor (m)
majorEnd = C + a * majorDir
minorEnd = C + b * minorDir
```

Notes:
- This is a flat-plane approximation tangent to the body's circumscribing
  sphere at C, not a geodesic on the actual shape. Good enough for boulder
  catalogs where ellipse size ≪ body curvature scale; revisit if needed.
- The east/north basis singularity at the poles is handled by detecting
  `|upR × zAxis| < ε` and falling back to a body-fixed convention (e.g.
  `east = xAxis`).
- `regularAngle` is *between the major axis and the line of longitude*. Sign
  convention follows SBMT's manual (positive = counter-clockwise from east
  when viewed from outside the body).

### Ellipse field mapping
| SBMT field | PRo3D `Annotation` field | Notes |
|---|---|---|
| `centerXYZ` × 1000, then `trafo` | construction `C` (see above) | |
| `diameter`, `flattening`, `regularAngle` | construction inputs | major/minor endpoints derived |
| `colorRGB` | `color.c : C4b` | |
| `label` | `text : string` | |
| `gravityAngle` | — | dropped (or stash in metadata; not used for rendering) |
| other coloring values | — | dropped |

Geometry: `Geometry.AxisEllipse`. `points` populated with the three
construction points in the order PRo3D's manual-pick flow expects (verify
against `src/PRo3D.Core/Drawing/EllipseAnnotation.fs` when wiring).

### Performance note
The larger fixture has ~4,800 ellipses. The existing import pipeline
(`AnnotationGroupsImporter.getGroups`) computes per-annotation `dnsResults`
and `results`, which involves geometry kernels. Profile bulk import on
`Dimo_Bould_Glob_7_Maurizio` before declaring v2 done; consider deferring or
batching the results computation if it dominates.

## UI flow
1. Menu entry in `ViewerGUI.fs` under the existing import menu (next to
   "Import v1 Annotations (\*.xml)"), labeled e.g. "Import SBMT annotations".
2. Electron `showOpenDialog` with filter for the standard SBMT extensions
   plus `*.*` (since real fixtures like `Didy_Boulders_Paj_Tusb_Lucch` use no
   extension).
3. **Modal prompts the user for the reference frame name.**
   - Default text: `DIMORPHOS_SHM`.
   - No validation against loaded SPICE kernels in v1 — just a free-text
     string stored on each imported annotation.
   - Cancel = abort import.
4. Confirmed import dispatches `ImportSbmtAnnotations` action with the file
   path(s) and the frame string.
5. The importer dispatches per file: read the `# type,<kind>` header, pick
   the matching parser (point in v1; point + ellipse in v2; etc.).

## File layout
- `src/PRo3D.Core/Importers/SbmtImporter.fs` (new)
  - Mirrors `MeasurementsImporter.fs`.
  - `detectStructureType : path:string -> StructureType` — read the `# type,X`
    header.
  - `parsePointLine : trafo:Trafo3d -> referenceFrame:string -> string -> Annotation option`
  - `parseEllipseLine : trafo:Trafo3d -> referenceFrame:string -> string -> Annotation option` (v2)
  - `startImporter : trafo:Trafo3d -> referenceFrame:string -> path:string -> IndexList<Annotation>`
- Extend `src/PRo3D.Core/Importers/AnnotationGroupsImporter.fs` with
  `importSbmt : trafo:Trafo3d -> path:string -> refSys:ReferenceSystem -> referenceFrame:string -> (groups, flat, lookup)`.
  - v1 callers pass `Trafo3d.Identity` for `trafo`.
  - Wraps parsed annotations in a `Node` named after the file.
  - Computes `dnsResults` and `results` per annotation (same as existing
    `import`).
  - Honors a sibling `.trafo` file if present, as the existing importer does
    (deferred — only if needed for testdata).

## Wiring
- `src/PRo3D.Viewer/Viewer-Model.fs` — add action next to
  `ImportPRo3Dv1Annotations`:
  ```fsharp
  | ImportSbmtAnnotations of files : list<string> * referenceFrame : string
  ```
- `src/PRo3D.Viewer/Viewer/Viewer.fs` — handle the new case adjacent to the v1
  XML handler (~line 987). v1 call site:
  ```fsharp
  AnnotationGroupsImporter.importSbmt Trafo3d.Identity path refSys referenceFrame
  ```
  This is the seam where a SPICE-derived `Trafo3d` will eventually be
  computed (e.g. SHM → DIMORPHOS_FIXED) and passed in instead of identity.
- `src/PRo3D.Viewer/Viewer/ViewerGUI.fs` — add the menu entry and the modal
  (Electron dialog + frame-input prompt) next to the v1 import entry (~line
  629).

## Validation / test plan
1. Automated: `SbmtImportAlignmentTest` (`src/Tests/SbmtImportAlignmentTest.fs`)
   parses `pointOnPike.points.txt` and `anno.json`, applies the v1 import
   transformation (`Trafo3d.Identity`, km → m), and asserts the two points
   agree to within 10 m. A second test confirms a rotation around Y (the
   analytic SHM → FIXED transform) preserves the distance — a sanity check
   for the eventual SPICE-based path. Skipped automatically when fixtures
   under `C:\pro3ddata` are absent.
2. Manual (v1): load the Dimorphos 0.25 m global OBJ as a PRo3D surface,
   import `pointOnPike.points.txt` via the new menu, accept default frame
   `DIMORPHOS_SHM`, verify the annotation sits on the surface near "Pike".
3. Manual (v2): import `Didy_Boulders_Paj_Tusb_Lucch` against the Didymos OBJ;
   spot-check a handful of ellipses for surface contact and orientation.
   Stress-test with `Dimo_Bould_Glob_7_Maurizio` (~4,800 ellipses).
   **Known mismatch:** with identity trafo the Didymos ellipses land off the
   OBJ surface (see "Didymos: SBMT ↔ OBJ frames do NOT match" above). Treat
   this as the canonical test case for the future SPICE-based reprojection
   path; the v1 identity-trafo path will not place these ellipses correctly.

## Open TODOs

### Reference-system field storage
The plan currently says "the modal-entered frame string is stored on each
imported annotation," but `Annotation` does **not** have a free `string`
field for this. Current candidates:

- Reuse `Annotation.referenceSystem : Option<ReferenceSystem>` — but that's a
  full record, not a string label. Would need to synthesize a meaningful
  `ReferenceSystem` value, which has its own semantics (planet, north/up
  vectors, etc.).
- Add a new optional `string` field to `Annotation` for "source frame label".
  Trivial but touches the model + adaptified model + serialization.
- Stash the string in `surfaceName` or `text`. Semantically incorrect.

Decision deferred until the importer is being written. None of the choices
block v1 because the frame string is already needed only for post-import
re-projection (out of scope for v1). The modal will collect it; where it
lands on the record is a follow-up.

### Other deferred items
- **Precise ellipse "up" via surface intersection.** v2 uses the radial
  direction `C / |C|` as the local up when constructing the tangent plane.
  This is the body's circumscribing-sphere normal, not the actual mesh
  normal — fine for boulder catalogs but visibly wrong at craters,
  outcroppings, and steep slopes where the surface normal can deviate
  significantly from radial. The fix is to ray-cast the loaded OBJ
  (KdTree-based, like the existing picking pipeline in
  `src/PRo3D.Core/ProfileAttributeExtraction.fs` / `src/PRo3D.Viewer/Viewer/Picking.fs`)
  along the radial line through `C`, take the hit's facet normal, and use
  *that* as `upR` in the tangent-plane construction. Costs a per-ellipse
  intersection (cheap individually, ~thousands for the big fixture — worth
  measuring).
- Line / polyline / polygon import (each lives in its own
  `<basename>.<type>.txt` SBMT file).
- LLR-based sanity check vs. XYZ using the active spheroid (relevant given
  ongoing `CooTransformation` work on this branch).
- Frame-aware re-projection via SPICE when the imported frame string differs
  from the active reference system. Couples to the storage decision above.
- `.trafo` sibling-file support if a use case appears.
- Validate `referenceFrame` input against loaded SPICE kernels.
- Map SBMT `name` field to `surfaceName` once the right semantics are
  settled.
- Make `getRotationTrafo` fail loud (currently returns identity on SPICE
  failure — silently wrong if kernels aren't loaded).
- North-pole/world-up handling in the reference-system UI for SHM datasets
  (north pole is −Z, not +Z).
- For very large fixtures (Dimo boulders, ~4,800 ellipses): profile bulk
  `dnsResults` / `results` computation and consider lazy or batched
  evaluation.
