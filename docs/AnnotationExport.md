# Annotation Export

*Annotations → Export…* opens a settings window in which one export is composed from a
handful of orthogonal choices, instead of picking one of a dozen fixed menu commands.

## Why it changed

The old *Annotations → Export* submenu offered eleven hard-wired commands. Tracing them
end-to-end showed they differed along only **two** axes — how many records one annotation
produces, and which container they are written to:

| Old menu command | Container | Record | Payload |
|---|---|---|---|
| all as 'PRo3D' annotations | Chiron JSON of the group tree | — | native round-trip |
| visible as table (*.csv) | CSV | annotation | ~39 fixed columns, centre xyz + lat/lon/alt |
| selected as profile (*.csv) | CSV | point | `distance, elevation` |
| selected as multi-attribute profile | CSV | point | `distance,x,y,z` + a column per texture layer |
| visible as GeoJSON / GeoJSON xyz | `GeometryCollection` | annotation | almost no properties, latitude-first |
| latlon / xyz / both GeoJSON for QGIS | `FeatureCollection` | annotation | `isEllipse`, `isSelected` |
| continuously export as GeoJSON xyz | NDJSON | annotation | id, colour, text, surface + hashes |
| dns as 'Attitude' planes | JSON array | annotation | `uid, axes, strike, dip, rake, …` |

Two of those (the native `.pro3d.ann` save and the continuous GeoJSON stream) have no
settings at all and stayed on the menu. The rest are now one code path: a shared record
builder plus one writer per container.

## The window

```
File type      CSV table / GeoJSON / Attitude planes
Preset         Custom / GIS-QGIS / Annotation table / Profile / Attitude planes
Scope          All / Visible only / Selected only
─────────────────────────────────────────────────────────────
Granularity    one record per annotation  |  one record per point
Coordinates    Cartesian / Geographic / Both       Longitude convention
               ☑ include sampled segment points
               ⓘ what the chosen granularity does to the geometry
Annotation attributes    checkbox per attribute, grouped
Point attributes         (only for "one record per point")
─────────────────────────────────────────────────────────────
                                         [ Cancel ]  [ Export… ]
```

The window is an overlay inside the main window; it is absent from the DOM while closed.
Clicking the dimmed background or *Cancel* closes it.

### File type

| Type | Output |
|---|---|
| **CSV table** | One header row, then one row per record. Columns come from the settings, always in the same order, so the file is rectangular even when the annotations differ. |
| **GeoJSON** | A spec-shaped `FeatureCollection`; one `Feature` per record, the selected attributes as `properties`. Geographic exports carry the body name as a collection-level `properties.planet`. |
| **Attitude planes** | Dip & strike planes for external structural-geology tools. **Fixed schema** — every setting below the file type is ignored, only the scope applies. |

### Preset

A preset only pre-fills the controls below it. Changing anything afterwards is allowed and
switches the preset back to *Custom*; nothing is locked.

| Preset | Sets |
|---|---|
| GIS / QGIS | GeoJSON, per annotation, geographic, identity + the common measurements |
| Annotation table | CSV, per annotation, both coordinate kinds, all measurements |
| Profile | CSV, per point, scope *Selected*, sampled points on, all point attributes |
| Attitude planes | file type *Attitude planes* |

### Scope

Which annotations are exported, independent of everything else.

- **All** — every annotation in the group tree, including hidden ones.
- **Visible only** — what the old CSV/GeoJSON/Attitude exports did.
- **Selected only** — the multi-selection **and** the single-selected annotation. The old
  profile exports only ever looked at the single selection, so multi-selected annotations
  were silently dropped.

Annotations are written in **group-tree order** (the order shown in the annotation list).
The old exports iterated the flat hash map, so their row order varied between runs.

### Granularity — read this one

This is the setting that decides how much geometry survives.

- **One record per annotation** — one row / feature. It can only carry a *single*
  coordinate, which is the **bounding-box centre** of the annotation. This is what the old
  *visible as table* CSV did, without saying so. The GeoJSON writer still emits the full
  geometry (LineString / Polygon) for the feature; the CSV cannot.
- **One record per point** — one row / feature per point of every exported annotation. The
  annotation-level attributes are repeated on each of its rows.

The window shows this as an inline message that follows the setting.

### Coordinates

*Cartesian* writes `x, y, z` (body-fixed metres). *Geographic* writes `lat, lon, alt`.
*Both* writes both sets of columns; in GeoJSON, the geometry then uses the geographic
positions.

Conversion always goes through the convention-aware
`CooTransformation.tryGetLatLonAlt`, which picks planetographic / spherical / ellipsoidal
per body. The QGIS exporter this replaces bypassed that dispatch with a raw P/Invoke and
silently produced a *string* `"Error: No / invalid reference frame set"` inside coordinate
arrays for bodies like Dimorphos. Unconvertible points now yield empty cells (CSV) or a
`null` geometry with a log warning (GeoJSON).

**Longitude convention** — the bodies PRo3D handles do not agree on one. Planetographic
longitude is west-positive for prograde bodies while most basemaps are east-positive, and
prime meridians differ; the old exporters each picked something different without saying so.
Two independent settings:

| Convention | Formula | Effect |
|---|---|---|
| Native | `lon` | as the transform returns it |
| **Flipped** (default) | `360 − lon` | mirrors east against west — what the old CSV and plain GeoJSON exports did |
| Shifted by 180° | `lon + 180` | same direction, prime meridian on the antimeridian |
| Flipped and shifted | `180 − lon` | both |

plus **Longitude range**, which only changes the notation, never the location: `[0, 360)`
by default, `(−180, 180]` when ticked.

The result is always wrapped into `[0, 360)` before the range setting applies, so the two
choices stay independent of each other and of the raw value's range.

*Which one?* Symptoms map directly onto the settings: annotations coming out
**mirror-inverted** means the mirror is wrong (switch Flipped ↔ Native); annotations at the
right orientation but **exactly 180° away** means the prime meridian is wrong (add or remove
*Shifted*). The **GIS / QGIS preset** selects *Shifted by 180°*, which is what matched real
Mars data in QGIS; the general default stays *Flipped* so CSV output matches the export it
replaces.

> **GeoJSON coordinate order changed.** Positions are now written in the spec order
> `[longitude, latitude, altitude]`. All three predecessors wrote latitude first, which no
> spec-compliant reader — QGIS included — interprets correctly. Downstream scripts that
> compensated for the old order need updating.

### Sampled segment points

An annotation stores both the picked control points and, per segment, the densely sampled
polyline that follows the surface.

- **On** (default) — export the sampled polyline, i.e. `Annotation.retrievePoints`. Note
  that the vertex shared by two consecutive segments appears twice, as it always has.
- **Off** — export only the picked control points, giving one row per segment.

The old exporters were split on this without documenting it: the GeoJSON and profile
exports used the sampled points, the CSV, QGIS and Attitude exports used the control points.

### Annotation attributes

39 attributes read straight off the annotation model, in five groups: *Identity*,
*Measurements*, *Ellipse*, *Dip and strike*, *Planar fit errors*. Column names match the
old CSV export's names (`wayLength`, `dipAzimuth`, `manualDip`, …) so existing downstream
scripts keep working.

Four in the *Identity* group are worth knowing about:

| Attribute | Column | Notes |
|---|---|---|
| Id | `key` | the annotation Guid. **Always exported**, ticked or not — see [Preparing for a later reimport](#preparing-for-a-later-reimport). |
| Group | `groupName` | the innermost containing group only |
| Group path (nested) | `groupPath` | the full chain, `"Outcrop A/Bedding"`, root excluded |
| Colour | `color` / `colorHex` | `color` is Aardvark's exact format (reimports losslessly); `colorHex` is `#RRGGBB` for GIS styling |

Values that were never computed (a polyline asked for a diameter, an annotation with no
planar fit asked for dip) are written as **empty cells** / JSON `null`, not as the text
`NaN`.

Two length attributes are worth calling out:

- **`length`** — straight-line distance from the first to the last control point.
- **`wayLength`** — the **total** length following the surface, summed over all segments.

### Point attributes

Only shown for *one record per point*.

| Column | Meaning |
|---|---|
| `pointIndex` | running index within the annotation |
| `segmentIndex` | which segment the point belongs to (empty for the first control point when sampled points are off) |
| `x, y, z` / `lat, lon, alt` | the point's position, per the coordinate setting |
| `stepLength` | distance to the previously exported point |
| `segmentLength` | length of the whole segment this point belongs to |
| `distance` | running length from the annotation's first point |

`segmentLength` is repeated on every row of that segment, so a pivot on `segmentIndex`
gives the per-segment table. The segment lengths sum to the annotation's `wayLength`, and
the last row's `distance` agrees with it.

**Surface properties at each point** — sampling the OPC scalar / texture layers at each
point is not available yet; the window shows a placeholder where those checkboxes will go.

## Using the GeoJSON export in QGIS

Everything in a feature's `properties` becomes a column in the layer's **attribute table**.
Because the writer emits real JSON types — numbers unquoted, missing values as `null` —
`slope`, `dipAngle`, `area` and `wayLength` arrive as *numeric* fields and unavailable
values as NULL. That makes them directly usable for graduated (colour-ramp) styling,
categorized styling, labels, filters, and data-defined symbol properties such as rotating a
strike-and-dip marker by `dipAzimuth`.

### Colour needs one manual step

**QGIS ignores styling properties in GeoJSON.** It does not honour the *simplestyle*
convention (`stroke`, `fill`, `marker-color`) that GitHub previews, geojson.io, Leaflet and
Mapbox use — no matter what the fields are named, QGIS reads them as plain attributes and
applies its own default symbology. To colour features by the annotation's own colour:

1. Tick **Colour (#RRGGBB, for GIS styling)** in the export window — this writes the
   `colorHex` column. (The `color` column is Aardvark's own format, kept because it
   reimports exactly; QGIS cannot interpret it.)
2. In QGIS: *Layer Properties → Symbology*. In the tree at the top, **select the symbol
   *layer*, not the parent symbol** — the parent (`Line` / `Linie`) only offers overrides
   for opacity and width; the child (`Simple line` / `Einfache Linie`, `Simple fill` /
   `Einfache Füllung`, `Simple marker` / `Einfacher Marker`) is where the colour
   properties live.
3. Click the data-defined override button (▤) next to **Stroke colour** / *Strichfarbe*
   — and **Fill colour** / *Füllfarbe* for polygons and points — then *Field type* /
   *Feldtyp* → `colorHex`.

To make this stick across sessions, save the styled layer's style (*Layer Properties →
Style → Save Style → As QGIS QML*) next to the exported file using the same base name —
QGIS applies a matching `.qml` automatically the next time a layer of that name is loaded.

### Labels

*Layer Properties → Labels* (*Beschriftungen*) → **Single labels** (*Einzelne
Beschriftungen*) → **Value** (*Wert*) takes any exported field, or an expression built with
the **ε** button:

```
concat("text", ' — ', round("dipAngle", 1), '°')
concat("text", '\n', round("wayLength", 2), ' m')
concat("groupPath", ' / ', "text")
```

**Use `concat()`, not `||`.** The `||` operator yields NULL if any operand is NULL, which
blanks the entire label — and unavailable measurements are deliberately exported as NULL
(a polyline has no `dipAngle`). `concat()` treats NULL as an empty string.

Every label property — size, colour, rotation, opacity — has a data-defined override too,
so `dipAzimuth` can rotate label text and `colorHex` can colour it. For line annotations,
*Placement* → **Curved** / **Parallel** runs the label along the polyline. Note that a
*one record per point* export labels every vertex; use per-annotation granularity, or a
filter under *Rendering*, when labelling.

Units are not part of the column names — they are in the export window's labels and in the
[Annotation attributes](#annotation-attributes) table.

### Group structure

QGIS's Layers-panel groups live in the **project** file, not in the data, so no vector
format carries the PRo3D group tree directly. Export the **Group path (nested)** attribute
instead: it holds the full chain, `"Outcrop A/Bedding"`, `/`-separated, root excluded.
Categorize or filter by it, or run *Vector → Data Management Tools → Split Vector Layer* to
fan it out into one layer per group. `groupName` remains the innermost group only.

### Coordinate reference system

GeoJSON is WGS84 by spec, so QGIS/GDAL tags the layer EPSG:4326 — an *Earth* ellipsoid.
Features still land at the right lat/lon over a planetary basemap once the project CRS is
set, but any length or area **QGIS itself** computes would use Earth's ellipsoid. The
measurement columns in the export are unaffected: they are computed in PRo3D against the
correct body. Recent QGIS versions ship IAU_2015 planetary CRS definitions; older ones need
a custom CRS. The body is written per feature as `body` (a collection-level property would
not appear in the attribute table).

### One geometry type per layer

A QGIS layer has one geometry type and one renderer. A file mixing points, lines and
polygons loads as a generic layer that is awkward to style — export one file per geometry
kind (or narrow the scope) when that matters.

### Preparing for a later reimport

There is no import path yet, but the export is shaped so one can be added:

- **`key` is always written**, whether or not it is ticked. It is the annotation's Guid and
  the only stable handle for matching a feature back to its annotation.
- **`groupPath`** carries enough to rebuild the group tree.
- **Cartesian `x/y/z` is the authoritative geometry.** Numbers are written in their shortest
  round-trippable form. Treat lat/lon as display-only: at planetary radius, a
  cartesian → geographic → cartesian trip loses precision.
- Editing geometry in QGIS invalidates the measurement columns; an importer would re-run
  PRo3D's `RecalculateMeasurements` rather than trust them.

## Migrating from the old menu

| Old command | New settings |
|---|---|
| visible as table (*.csv) | CSV · per annotation · Visible · Both |
| selected as profile (*.csv) | CSV · per point · Selected · Geographic (preset *Profile*) |
| selected as multi-attribute profile | CSV · per point · Selected — surface columns pending |
| visible as GeoJSON | GeoJSON · per annotation · Visible · Geographic |
| visible as GeoJSON xyz | GeoJSON · per annotation · Visible · Cartesian |
| latlon GeoJSON for QGIS | GeoJSON · per annotation · Geographic (preset *GIS / QGIS*) |
| xyz GeoJSON for QGIS | GeoJSON · per annotation · Cartesian |
| latlon for QGIS + xyz metadata | GeoJSON · per annotation · Both |
| dns as 'Attitude' planes | file type *Attitude planes* |
| all as 'PRo3D' annotations | unchanged, still on the menu |
| continuously export as GeoJSON xyz | unchanged, still on the menu |

Beyond the GeoJSON coordinate order, two other behaviours changed on purpose:

- **CSV is now culture-invariant.** The old reflective writer formatted numbers with the
  current culture, so on a decimal-comma system (`de-AT`, …) it emitted `23,32` into a
  comma-separated file and corrupted it.
- **Ellipse geometries no longer abort the export.** The old GeoJSON writer threw on
  `Ellipse` / `Axis4PEllipse`; unhandled geometry kinds now degrade to a closed ring.

## Where the code lives

| File | Role |
|---|---|
| `src/PRo3D.Base/Annotation/Exporters/ExportRecord.fs` | `ExportValue` / `ExportRecord`, the CSV writer and the GeoJSON `FeatureCollection` writer |
| `src/PRo3D.Base/Annotation/Exporters/AnnotationFields.fs` | the attribute enums, their labels, column names and value accessors |
| `src/PRo3D.Base/Annotation/Exporters/ExportSettings.fs` | the settings snapshot and the presets |
| `src/PRo3D.Base/Annotation/Exporters/AnnotationExport.fs` | `schemaOf` / `buildRecords` / `write` — the shared record builder |
| `src/PRo3D.Core/AnnotationExport-Model.fs` | `AnnotationExportModel` + actions (session-only, on the root `Model`, not persisted) |
| `src/PRo3D.Core/AnnotationExportApp.fs` | `update` and the window `view` |
| `src/PRo3D.Viewer/Viewer/AnnotationExportViewer.fs` | scope resolution and running the export |
| `src/Tests/AnnotationExportTest.fs` | schema, lengths, culture-invariance, presets |

Adding an annotation-level attribute means adding one enum case in `AnnotationFields.fs`
plus its `columnName`, `label`, `groupOf` and `valueOf` branch — the window, the schema and
both writers pick it up automatically.

## See also

- [ai/DOMAIN.md](../ai/DOMAIN.md) — the annotation model these attributes are read from
- [CrossSections.md](CrossSections.md) — the other consumer of annotation geometry
