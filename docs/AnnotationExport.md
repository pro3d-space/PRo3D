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

**Longitude convention** — the bodies PRo3D handles do not agree on one, and the old
exporters each picked a different one without asking:

| Setting | Meaning | Matches the old… |
|---|---|---|
| Native | exactly what the transform returns for the body | QGIS exports |
| Flipped | `360 − lon`, wrapped to `[0, 360)` | CSV and plain GeoJSON exports (the default) |
| Flipped and signed | as above, wrapped to `(−180, 180]` | — |

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

37 attributes read straight off the annotation model, in five groups: *Identity*,
*Measurements*, *Ellipse*, *Dip and strike*, *Planar fit errors*. Column names match the
old CSV export's names (`wayLength`, `dipAzimuth`, `manualDip`, …) so existing downstream
scripts keep working.

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
