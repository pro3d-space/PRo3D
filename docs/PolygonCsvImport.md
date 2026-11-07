# Polygon CSV Import

## Synopsis

PRo3D imports polygons from a plain CSV table of **body-fixed vertices**, one row per
vertex. The table is meant to be written by scripts and other tools, so the format is as
small as it can be:

```
polygon_id,x,y,z
patch_centre,355.703,-110.889,115.677
patch_centre,380.145,-42.641,117.147
patch_centre,401.450,-29.162,83.877
patch_centre,407.549,-49.084,49.247
patch_centre,387.349,-118.115,48.639
patch_centre,370.751,-132.670,82.912
triangle_south,375.544,-171.053,4.525
triangle_south,395.688,-120.233,6.128
triangle_south,388.373,-146.622,-40.357
```

Each `polygon_id` becomes one closed polygon annotation. Optionally, every vertex is
**snapped onto the surface** that is loaded in the scene, and the edges get
**sky-projected in-between points** so they follow the surface like a drawn polygon.

Polygons drawn in **image coordinates** (pixel x/y in an instrument image) are imported by
converting them to body-fixed coordinates with
[`pro3d-tool unproject`](./Pro3DTool-Unproject.md) first. The unproject output is a
valid input here as it is — see [From image coordinates](#from-image-coordinates).

## Try it

Two example files ship in the PRo3D source tree, for the Didymos model in the public
[test data](./Pro3DTool.md#test-data) (`HERA/Didymos_ASPECT`):

| File | Contents |
|---|---|
| [`scripts/example-polygons.csv`](../scripts/example-polygons.csv) | the table above plus a `label` column — a hexagon and a triangle on Didymos, in `DIDYMOS_FIXED` metres. **Import this one.** |
| [`scripts/example-polygon-pixels.csv`](../scripts/example-polygon-pixels.csv) | the same two polygons as pixel coordinates in the ASPECT test image `ASP_000000_270323T060000_2B_NIR1_0.tif` — the input for the [image-coordinate route](#from-image-coordinates) |

The first is the output of the second: `example-polygon-pixels.csv` was run through
`pro3d-tool unproject` against `HERA/Didymos_ASPECT` and the surface points rounded to
millimetres. Load `Didymos_ASPECT` as a surface, import `example-polygons.csv`, and the two
polygons appear on the part of Didymos the ASPECT test image looks at; with *Snap vertices to
the surface* they barely move, since unproject already put them on that same model.

## Using it

1. Load the surface (OPC or mesh) the polygons belong to.
2. Menu → **Extras** → **Import Polygons (\*.csv)...** opens the import window:

   | Option | Off (default) | On |
   |---|---|---|
   | **Snap vertices to the surface** | vertices placed exactly as given | each vertex moved onto the active, visible surfaces — see [Snapping](#snapping-to-the-surface) |
   | **Sky-project in-between points along the edges** | edges are straight lines | points every *spacing* metres along each edge, dropped onto the surface — see [In-between points](#in-between-points) |
   | **spacing** (m) | — | distance between in-between points; at least 0.01 m |
   | **Compute dip & strike** | no plane fit; the dip & strike panel and *colour by dip* stay empty until a vertex is moved | a plane fitted per polygon, as when a drawn polygon is finished; slow for many thousands of polygons |

   The options stay set for the rest of the session.
3. **Choose files...** and pick one or several files. Each file becomes one annotation group
   in the Annotations tree, named `<file> (<count>)`, collapsed.
4. **Undo** (Ctrl+Z) takes the whole import back — all files of one *Choose files...* at once.

A short notice reports per file how many polygons arrived, how many rows were skipped, how
many vertices missed the surface, how many in-between points were left out and how many long
edges were sampled coarser. The individual reasons are in the log, prefixed
`[PolygonImport]`.

## File format

### Coordinates

- **Metres**, in the **body-fixed frame of the scene** — the frame the loaded surface is
  in (for the HERA data, `DIDYMOS_FIXED` / `DIMORPHOS_FIXED`, as written by PRo3D's
  exports and by `pro3d-tool unproject`). The coordinates themselves are not converted.
- **With a GIS observer set** (surfaces placed by SPICE into an observer's frame), the
  polygons get the body frame of the active, visible surfaces as their reference system,
  exactly like drawn annotations: they are placed with the same transform, follow the body
  when the observer time changes, and snapping casts through that transform. If those
  surfaces are in different body frames, the first one is used, only its surfaces are hit,
  and the notice says so.
- Plain decimal numbers with `.` as the decimal mark; exponents (`1.5e3`) are fine.

### Separator and header

- **Separator** — tab, semicolon, comma or whitespace, detected from the first data row
  (in that order of preference). Values containing the separator can be put in double
  quotes: `"Crater, rim"`.
- **Header** — optional. A first row whose 2nd–4th cells are not all numbers is a header.
- **Without a header** the columns are positional: `id, x, y, z` (more columns are ignored).
- Blank lines and lines starting with `#` are ignored.

### Columns (with a header)

Names are matched case-insensitively; the first name listed that is present is used.

| Meaning | Column names | Required |
|---|---|---|
| polygon id | `polygon_id`, `polygon`, `id`, `key`, `name` | **yes** |
| x / y / z | `x_m`/`y_m`/`z_m`, else `x`/`y`/`z` | **yes** |
| vertex order | `pointIndex`, `point_index`, `vertex`, `vertex_index` | no — row order otherwise |
| label | `label`, `text` | no — the id otherwise |
| colour | `colorHex`, `color`, `colour` | no — orange otherwise |
| group | `group`, `groupPath`, `groupName` | no |
| status | `status` | no — rows whose status is not `ok` are skipped |

Every other column is ignored.

- **Rows of one polygon need not be adjacent.** Vertices are taken in row order, or by the
  vertex-order column if there is one. Polygons appear in the order their first row appears.
- **Closing vertex** — write it or don't: if the last vertex equals the first it is dropped,
  and the polygon is always stored closed, the way PRo3D stores drawn polygons.
- **Label, colour, group** are read from the **first row** of each polygon; leaving them empty
  on the following rows is fine.
- **Colour** is `#RRGGBB`, `#RRGGBBAA`, or Aardvark's `[r, g, b, a]` (what PRo3D's export writes
  in its `color` column). It is used for both outline and fill.
- **Group** puts the polygon into a sub-group of that name inside the file's group. Slashes
  are not interpreted: `A/B` is one sub-group named `A/B`.

### What is skipped

Skipped rows and polygons never abort the import; they are logged and counted in the notice.

| Case | Result |
|---|---|
| empty id | row skipped |
| x, y or z not a number (or NaN/∞) | row skipped |
| `status` present and not `ok` | row skipped |
| polygon with fewer than 3 vertices | polygon skipped |
| no id or no x/y/z column in the header | **whole file** not imported |

### Files that are already in this format

- **PRo3D's own CSV export**, one record per point with *Cartesian* coordinates
  ([AnnotationExport.md](./AnnotationExport.md)): `key` is the id, `pointIndex` the order,
  `color` the colour, `groupPath` the group. Exported polygons therefore reimport as
  polygons. (Exported polylines also come back as polygons — the importer has no geometry
  column.)
- **`pro3d-tool unproject` output** — see below.

## From image coordinates

The intended route for polygons digitised on instrument images is two steps:

```
image,x,y,polygon_id,label
ASP_000000_270323T060000_2B_NIR1_0.tif,300,236,patch_centre,Hexagon at the frame centre
ASP_000000_270323T060000_2B_NIR1_0.tif,340,236,patch_centre,Hexagon at the frame centre
...
ASP_000000_270323T060000_2B_NIR1_0.tif,285,325,triangle_south,
```

(the whole file is [`scripts/example-polygon-pixels.csv`](../scripts/example-polygon-pixels.csv))

1. Run unproject on it; with the [test data](./Pro3DTool.md#test-data) and
   [SPICE kernels](./Pro3DTool.md#spice-kernels) set up:
   ```
   pro3d-tool unproject --opc <TestData>/HERA/Didymos_ASPECT --images "<TestData>/HERA/Instrument Data" --input scripts/example-polygon-pixels.csv --out polygons.csv
   ```
   It copies `polygon_id` and `label` through and appends `status`, `x_m`, `y_m`, `z_m`, …
   (`9 of 9 row(s) hit the surface` for the example).
2. Import `polygons.csv` here — as it is, no editing.

The importer takes `x_m`/`y_m`/`z_m` over the pixel `x`/`y` (both are in that file), and
skips the rows whose `status` is not `ok` — for instance `no-hit`, a pixel off the limb.
With an `image` column a polygon is keyed by **image and id**: `p` digitised on two images
becomes two polygons, `p @ a.tif` and `p @ b.tif`, not one mixing both outlines. (One
outline continued across two images therefore needs the same image name in all its rows.)
Unproject already intersects the shape model, so snapping is only needed when the polygons
are shown on a **different** surface than the one unproject used.

Unproject takes pointing and epoch from the image's `.mbi.json` sidecar, not from
instrument/UTC columns; see [its page](./Pro3DTool-Unproject.md#what-the-image-folder-needs).

## Snapping to the surface

With *Snap vertices to the surface*, every vertex `p` is moved along the **sky** — the local
up at `p`, the same direction the drawing tool's *Sky* projection uses
(`CooTransformation.getUpVector` for the scene's planet):

| Scene planet | Sky at `p` |
|---|---|
| Earth, Mars (planetographic, SPICE) | ellipsoid normal |
| Moon, Phobos, Deimos, Didymos, Dimorphos | normal of the body's reference sphere / ellipsoid (radial for a sphere) |
| None, ENU (local frame) | +Z |
| JPL | −Z |

A small-body scene left on *Planet: None* snaps straight along +Z — set the scene's planet
to the body first.

- two rays leave `p`, one down and one up;
- the **nearer** of the two hits is taken;
- on a body, a downward hit **deeper than the body's centre** is ignored: it is on the far
  side (the surface under the vertex has a hole or is hidden), and a vertex must not jump
  through the body. Local frames have no such cut-off.

So vertices slightly above *and* slightly below the surface both land, and under an overhang
(a boulder's side) the vertex lands on the surface next to it, not on the topmost one.

Only **active and visible** surfaces are hit, using the same KdTree picking as interactive
drawing; when several surfaces are hit, surface priority decides as in picking.

A vertex where neither ray hits keeps its position. Their count is in the notice.

Only the vertices move. Without in-between points the edges stay **straight lines** (linear
projection), so a polygon with few, far-apart vertices on a curved surface cuts through or
floats above it between vertices.

## In-between points

With *Sky-project in-between points along the edges*, every edge is walked in steps of
*spacing* metres and each step is dropped onto the surface the same way as a vertex (the
nearer of the down and the up hit along the sky). The polygon is then
stored exactly like one **drawn with sky projection**: the vertices as control points, one
segment per edge carrying its in-between points, the closing edge stored first → last. So it
renders, measures, exports and can be vertex-edited like a drawn polygon — moving a vertex
re-samples the two edges next to it with the drawing tool's own sampling.

- An in-between point that misses the surface is **left out**, as when drawing; their count
  is in the notice.
- The vertices themselves are only moved if *Snap vertices* is on too. Usually you want both.
- Cost: two ray casts per in-between point. A 1 km outline at 1 m spacing is 1000 points,
  i.e. 2000 ray casts.
- **At most 1000 in-between points per edge.** A longer edge is sampled coarser (length /
  1000); the notice counts these edges. Every point is also a line vertex to render.
- Moving a vertex later re-samples its two edges with the **drawing tool's** sampling
  distance and its sky ray (from above, topmost hit), not the import's spacing and
  nearer-hit rule.

## Implementation notes

| File | Role |
|---|---|
| `src/PRo3D.Core/Importers/PolygonCsvImporter.fs` | parser (`parseLines` / `parseFile`), surface snap (`snapToSurface` / `snapAll`, ray cast passed in), `ImportOptions` and `build` (linear or sky-projected annotations, via `DrawingApp.resampleSegment`), group construction |
| `src/PRo3D.Viewer/PolygonImportApp.fs` | the sub-app: `PolygonImportModel` (window state, session-only), `PolygonImportAction`, `update`, `importFiles` (GIS frame, ray cast through `SurfaceIntersection.doKdTreeIntersection`, merge into the annotation tree, undo step, notice text) and `view` (the import window) |
| `src/PRo3D.Viewer/Viewer-Model.fs`, `Viewer/Viewer.fs`, `Viewer/ViewerGUI.fs` | wiring only: the `polygonImport` field, `PolygonImportMessage` routed to the sub-app, the *Extras* menu entry, `PolygonImportApp.view` in the layout |
| `src/Tests/PolygonCsvImportTests.fs` | parser, snapping (analytic sphere), grouping, and that the shipped examples keep importing |
| `scripts/example-polygons.csv`, `scripts/example-polygon-pixels.csv` | the examples under [Try it](#try-it) |

- Annotation measurements (`results`: length, area, …) are computed at import; dip and strike
  only with *Compute dip & strike*.
- `modelTrafo` is the translation to the first vertex, as for drawn annotations: the line
  renderer works relative to it in `float32`, so planetary-scale coordinates stay precise.
- The import is one `SnapshotDelta` on the drawing undo stack.
- Annotations are stored in scene coordinates like drawn ones; nothing new is persisted, so
  scenes with imported polygons load in older releases.

```
dotnet run --project src\Tests -- --filter "all.all tests.polygonCsvImport"
```

## Known limitations

- **Polygons only** — no polylines or points.
- **No frame selection** — coordinates must already be in the scene's body-fixed frame.
- **No in-viewer image-coordinate import** — use `pro3d-tool unproject` first.
- **One spacing for all edges** — no adaptive sampling by curvature.

## Future work

Snapping is two rays along the sky (down and up, nearer hit wins) with the far-side guard.
Open points, none of them a correctness bug:

1. **No distance limit on the up ray.** When the down hit is rejected (hole in the surface
   under the vertex), any up hit is taken, however far — under an overhang or with a second
   surface above, a vertex can jump far. Planned: a *max snap distance (m)* field, empty =
   unlimited; farther hits count as misses in the notice.
2. **One sky and one body frame per import**: the sky from the scene's planet, the frame from
   the first active surface. The drawing tool takes both from the hit surface; matters only
   for scenes mixing bodies (Didymos and Dimorphos together) — import per body, with only
   that body's surfaces active.
3. **Surface priority before distance.** Each ray takes the highest-priority surface it hits,
   then the nearest hit on it — the picking rule, so consistent, but not "geometrically
   nearest" across surfaces.
4. **Cost on SPICE bodies.** The sky on Mars/Earth is two SPICE calls per point on top of
   the two ray casts; bounded by the 1000-points-per-edge cap, same as drawing.
5. **Re-sampling after a vertex edit** uses the drawing tool's rule (from above, topmost
   hit), not the import's nearer-hit rule.
6. **All hits in one pass** instead of two rays: possible with a hit filter that records and
   rejects every hit (`tmin = −∞`), but `intersectKdTrees` hard-codes `0 .. MaxValue` and the
   walk does not stop at the first hit — likely slower than two early-stopping rays.
