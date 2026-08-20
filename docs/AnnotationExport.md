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

Only the native `.pro3d.ann` save stayed on the menu, since it has no settings. Everything
else is now one code path: a shared record builder plus one writer per container. The
continuous GeoJSON stream is a *file type* in the window — picking it arms the background
export rather than writing a file.

## The window

```
File type      CSV table / GeoJSON / Attitude planes / Continuous GeoJSON
Preset         Custom / GIS-QGIS / Annotation table / Profile / Attitude planes /
               Continuous GeoJSON
Scope          All / Visible only / Selected only
─────────────────────────────────────────────────────────────
Granularity    one record per annotation | one record per point
Coordinates    Cartesian / Geographic / Both       Longitude convention
               (Both is CSV only)
               ☑ write longitude as -180...180
               ☑ include sampled segment points
Lat/Lon/Alt    SPICE / File (.aara)
  Source       (shown only for geographic + per-point exports)
               ⓘ what the chosen granularity does to the geometry
Annotation attributes    checkbox per attribute, grouped
Point attributes         (only for "one record per point")
                         ☑ Surface properties — sample the OPC layers per point
─────────────────────────────────────────────────────────────
                                         [ Cancel ]  [ Export… ]
```

The primary button follows the file type: *Export…* normally, *Start…* for a continuous
export, and *Stop continuous export* while one is running.

The window is an overlay inside the main window; it is absent from the DOM while closed.
Clicking the dimmed background or *Cancel* closes it.

### File type

| Type | Output |
|---|---|
| **CSV table** | One header row, then one row per record. Columns come from the settings, always in the same order, so the file is rectangular even when the annotations differ. |
| **GeoJSON** | A spec-shaped `FeatureCollection`; one `Feature` per record, the selected attributes as `properties`. Geographic exports carry the body name as a collection-level `properties.planet`. |
| **Attitude planes** | Dip & strike planes for external structural-geology tools. **Fixed schema** — every setting below the file type is ignored, only the scope applies. |
| **Continuous GeoJSON** | Does not write once: it **arms a background export**. PRo3D then rewrites the chosen file as line-delimited GeoJSON (one `Feature` per line, with `id`, `color`, `geometry`, `text`, `surfaceName` and content hashes) whenever the annotations change. Fixed schema, so no setting applies. See *Starting and stopping* below. |

#### Starting and stopping the continuous export

Select file type **Continuous GeoJSON** and press *Start…*; the file you pick is written
immediately and rewritten on every change to the annotations from then on. The export keeps
running after the window is closed.

To stop it, open the window again and select **Continuous GeoJSON** — while an export is
running, the message box names the file it is writing to and the primary button reads **Stop
continuous export** instead of *Start…*. Note the export is only visible there: with any
other file type selected the window shows no sign of it, so re-select *Continuous GeoJSON*
to check or stop it.

Picking a new file while one is running simply retargets it; the previous file is left as it
was.

It also stops on its own in two situations:

- **Loading a scene** (including *New scene*). The load replaces all annotations, so an
  export armed for the previous scene would otherwise overwrite its file with the new
  scene's contents. Re-arm it after the load if you want the new scene exported.
- **Closing PRo3D.** The armed state is runtime-only and is never written to the scene or
  the `.pro3d.ann` file, so it is never resumed on startup.

Importing annotations into the *current* scene (*Annotations → Import*, or the remote API)
does **not** stop it — the imported annotations are simply picked up by the next rewrite.

### Preset

A preset only pre-fills the controls below it. Changing anything afterwards is allowed and
switches the preset back to *Custom*; nothing is locked.

| Preset | Sets |
|---|---|
| GIS / QGIS | GeoJSON, geographic, longitude *Shifted by 180°*, `colorHex` + `groupPath` + the common measurements |
| Annotation table | CSV, per annotation, both coordinate kinds, all measurements |
| Profile | CSV, per point, scope *Selected*, sampled points on, all point attributes incl. *ground distance* |
| Attitude planes | file type *Attitude planes* |
| Continuous GeoJSON | file type *Continuous GeoJSON* — arms the background export |

Every preset also sets the **signed −180…180 longitude range** and the **File (.aara)**
lat/lon/alt source, both of which are the defaults anyway; only an explicit change (which
switches the preset to *Custom*) moves off them. Selecting *Custom* is not a preset and
changes nothing.

### Scope

Which annotations are exported, independent of everything else.

- **All** — every annotation in the group tree, including hidden ones.
- **Visible only** — what the old CSV/GeoJSON/Attitude exports did.
- **Selected only** — the multi-selection **and** the single-selected annotation. The old
  profile exports only ever looked at the single selection, so multi-selected annotations
  were silently dropped.

Annotations are written in **group-tree order** (the order shown in the annotation list).
The old exports iterated the flat hash map, so their row order varied between runs.

If the chosen scope matches **no** annotations, no file is written and the window stays open
with a warning in its header saying which scope came up empty and what to change. The same
happens when writing the file fails, and when the export asked for geographic coordinates
but the scene has **no reference frame** (`Planet.None`, `JPL` or `ENU`) — there the file
*is* written, but `lat`/`lon`/`alt` come out empty and GeoJSON features get a `null`
geometry, so the window stays open to say so. Set the body under *Coordinate System* in the
config panel, or export cartesian coordinates. The warning clears as soon as any control is
touched.

### Granularity — read this one

This decides how much geometry survives, and it does **not** mean the same thing in both
file types. It applies to CSV and GeoJSON alike; only *Attitude planes* ignores it.

| | CSV | GeoJSON |
|---|---|---|
| **one record per annotation** | one row; the coordinate columns hold the **bounding-box centre** and the individual vertices are not in the file — what the old *visible as table* CSV did, without saying so | one `Feature` per annotation carrying its **full** `LineString` / `Polygon` geometry; only the `lat/lon/alt` *attribute* columns hold the centre |
| **one record per point** | one row per point of every exported annotation, with the annotation attributes repeated on each row | one **`Point`** feature per vertex |

In GeoJSON this also decides the layer's geometry type, which matters because a GIS layer
has one geometry type and one renderer.

Why per-point matters in GeoJSON: a GIS evaluates labels and symbology **once per feature**.
Per-vertex values *can* be carried on a single line feature as array-valued properties —
that is legal GeoJSON and GDAL maps homogeneous arrays to list fields — but no GIS can bind
array element *i* to vertex *i*. So a point-per-vertex layer is the only way to label or
colour individual sample points.

The window shows the applicable case as an inline message under the settings.

### Coordinates

*Cartesian* writes `x, y, z` (body-fixed metres). *Geographic* writes `lat, lon, alt`.
*Both* writes both sets of columns and is offered for **CSV only**: a GeoJSON `Feature` has
a geometry, and that geometry is written in one coordinate system or the other, never both —
so for GeoJSON the choice is binary. Switching the file type to GeoJSON while *Both* is
selected falls back to *Geographic*, which leaves the geometry exactly as *Both* would have
written it and only drops the extra `x, y, z` properties.

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
| Shifted by 180° | `lon + 180` | same direction, prime meridian moved to the antimeridian |
| Flipped and shifted | `180 − lon` | mirrored, with the prime meridian on the antimeridian |

plus **Longitude range**, which only changes the notation, never the location: `(−180, 180]`
by default and after every preset, `[0, 360)` when unticked.

The result is always wrapped into `[0, 360)` before the range setting applies, so the two
choices stay independent of each other and of the raw value's range.

Both settings apply to **every** file type, GeoJSON included. GeoJSON briefly forced the
signed range on the grounds that its positions are WGS84 by spec — but PRo3D's bodies are not
Earth, and which notation a downstream product wants is not something the format can decide.
Be aware that a GIS reading `[0, 360)` values against an Earth CRS may draw them beyond the
edge of the world, since that CRS defines longitude on `(−180, 180]`.

*Which one?* Symptoms map directly onto the settings: annotations coming out
**mirror-inverted** means the mirror is wrong (switch *Flipped* ↔ *Native*); annotations at
the right orientation but **exactly 180° away** means the prime meridian is wrong (switch
*Native* ↔ *Shifted by 180°*, or *Flipped* ↔ *Flipped and shifted*). A longitude that is
merely *negative* where you expected it positive is neither — that is the range, not the
convention, and it names the same point.

There is no universally correct choice, because the convention has to match **whatever you
are lining the export up against**, not just the body. A worked example: an Earth texture
draped on the Dimorphos shape model is placed by a UV mapping whose origin sits 180° from the
body-fixed +X axis that longitude 0 follows, so exports matching that texture need *Shifted by
180°* — even though PRo3D's own longitude is perfectly self-consistent. The **GIS / QGIS
preset** selects *Shifted by 180°* for exactly that case; the general default stays *Flipped*,
which is what the CSV export this replaces did. Override per export when the target says
otherwise.

> **GeoJSON coordinate order changed.** Positions are now written in the spec order
> `[longitude, latitude, altitude]`. All three predecessors wrote latitude first, which no
> spec-compliant reader — QGIS included — interprets correctly. Downstream scripts that
> compensated for the old order need updating.

### Lat/Lon/Alt source

Where the geographic coordinates come from. Two options, and the default is **File
(.aara)**:

| Option | What it does |
|---|---|
| **SPICE** | Derives lat/lon/alt from the point's cartesian position, picking the right convention for the body automatically (see below). Works for any body with a geographic frame, whatever the terrain data looks like. |
| **File (.aara)** (default) | Reads the values out of the patch's own per-vertex `LonLatRad` grid, barycentrically interpolated at the point. These are the numbers the OPC was built from, rather than numbers re-derived from a position. Requires an OPC that ships the layer — see [Per-Vertex Attribute Layers](VertexAttributes.md). |

The row is only shown when it can apply: *Coordinates* must be **Geographic** or **Both**
(a cartesian export writes no lat/lon at all) and the granularity must be **one record per
point** (a per-annotation record has no single position to sample a layer at, so those
exports always use SPICE). Switching to per-annotation resets the setting to *SPICE*; a
cartesian export merely hides it and keeps your choice for when it becomes relevant again.

#### Which SPICE routine

`SPICE` is one entry, not two, because the right convention is a property of the **body**
and not something worth choosing per export:

| Body | Routine | and `alt` is |
|---|---|---|
| Mars, Earth, Moon, Phobos, Deimos, Didymos | **recpgr** (planetographic) | height above the spheroid |
| Dimorphos | **reclat** (planetocentric) — it is tri-axial and has no PCK rotation pole, so `recpgr` is not valid for it | radial distance from the body centre |
| `None` / `JPL` / `ENU` | none — no geographic frame | empty |

#### `alt` changes meaning with the source

This is the reason the setting is called *Lat/Lon/**Alt** Source*. `recpgr` gives a height
above the spheroid; `reclat` and the `LonLatRad` layer both give a **radial distance from
the body centre**. The same column therefore holds different quantities depending on the
body and the source — which is what the provenance column is for.

#### The `latLonAltSource` column

Written into every record whose geographic columns are written, and **only** then, so a
cartesian export has no such column. It records what actually ran, per row:

| Value | Meaning |
|---|---|
| `spice_recpgr` | planetographic, native `Xyz2LatLonAlt` |
| `spice_reclat` | planetocentric polar coordinates |
| `spice_ellipsoidal` | planetocentric lat/lon with a tri-axial altitude reference (no body selects this today) |
| `aara_file` | interpolated from the patch's `LonLatRad` layer |
| *(empty)* | no geographic frame, so lat/lon/alt are empty too |

Per **row**, not per file: a single export can mix them, because a point the per-vertex data
does not cover falls back to SPICE (see below).

The *Longitude* convention and *Longitude range* settings apply whichever source produced
the value — they are notation transforms of a longitude, not part of deriving one.

#### When the data is not there

| Situation | What happens |
|---|---|
| **No loaded surface ships the layer** | The export is **refused**: nothing is written and the window says so. Set the source to *SPICE*, or load an OPC with `.aara` attribute layers. |
| **Some points fall outside it** — the annotation runs off the surface, or onto a patch without the layer | The file *is* written. Those rows are resolved by SPICE and their `latLonAltSource` says so, and the window reports how many. |

The option is never greyed out, so the refusal is what tells you the data is missing. Note
that `.aara` is also the default for every preset, so on an OPC without per-vertex layers a
per-point geographic export refuses until you switch the source — and picking a preset arms
it again. That is deliberate: an export must never quietly use a source other than the one
selected.

`groundDistance` is unaffected by this setting — it needs the inverse transform to flatten a
point onto the reference surface, which the file has no equivalent for, so it is always
computed through SPICE.

#### Cost

Taking the coordinates from the file means **re-picking every exported point** against the
surface KdTrees — the same work as an interactive pick, and the same machinery *Surface
properties* uses. Expect seconds rather than milliseconds, dominated by how many **patches**
the annotation crosses rather than how many points it has: the first hit on a patch loads
its triangle set and grid mapping, and everything after that is cheap. Those caches are
shared with interactive picking, so a patch you have already clicked on costs nothing. PRo3D
is unresponsive while the export runs.

Ticking *Surface properties* as well is free — both read the same sample, so the point is
only picked once. Leaving it off is what keeps the attribute *textures* out of the picture,
which is the genuinely expensive part.

### Sampled segment points

An annotation stores both the picked control points and, per segment, the densely sampled
polyline that follows the surface.

- **On** (default) — export the sampled polyline, i.e. `Annotation.retrievePoints`. Note
  that the vertex shared by two consecutive segments appears twice, as it always has.
- **Off** — export only the picked control points, giving one row per segment.

The old exporters were split on this without documenting it: the GeoJSON and profile
exports used the sampled points, the CSV, QGIS and Attitude exports used the control points.

### Annotation attributes

32 attributes read straight off the annotation model, in four groups: *Identity*,
*Measurements*, *Ellipse*, *Dip and strike*. Column names match the old CSV export's names
(`wayLength`, `dipAzimuth`, `manualDip`, …) so existing downstream scripts keep working.

The **planar-fit error measures** (`errorAvg`, `errorMin`, `errorMax`, `errorStd`,
`sumOfSquares`, `minAngularError`, `maxAngularError`) are deliberately **not** offered here.
They are the residuals of the very plane the dip and strike values are derived from — fit
diagnostics rather than measurements of the annotation — and they made the attribute list
noticeably longer for every export. The angular errors remain available through the
*Attitude planes* export, which writes them as `min_angular_error` / `max_angular_error`.

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
| `segmentIndex` | which segment the point belongs to (empty on the first point) |
| `x, y, z` / `lat, lon, alt` | the point's position, per the coordinate setting |
| `stepLength` | distance to the previously exported point |
| `segmentLength` | length of the whole segment this point belongs to |
| `distance` | running length from the first point, **through 3D space** |
| `groundDistance` | running length from the first point with the **height removed** |
| `surface_<layer>` | one per OPC layer sampled under the point — see [Surface properties](#surface-properties) |

#### The two distances

Every point is flattened onto the reference surface (altitude 0) before
`groundDistance` is measured, so it is the horizontal run — the x-axis of a topographic
profile. `distance` measures between the actual 3D points and therefore includes the
vertical climb. On a line running 100 m horizontally while climbing 30 m, `groundDistance`
ends at 100 and `distance` at about 104.4.

**The old *selected as profile* export's `distance` column was the ground distance**, and
its `elevation` column is now `alt`. The *Profile* preset ticks both, so it reproduces the
old numbers. `groundDistance` is empty for bodies without a geographic frame
(`Planet.None`/`JPL`/`ENU`), since there is no height to remove.

#### Segment lengths

`segmentLength` is repeated on every row of that segment, so a pivot on `segmentIndex`
gives the per-segment table, and the segment lengths sum to the annotation's `wayLength`.

A "segment" is the stretch between two *picked* points, including the surface-following
points PRo3D drapes in between. Annotations drawn with `Projection.Linear` have no such
stretches — the picked points are the whole geometry — so there `segmentLength` falls back
to the plain hop and equals `stepLength`, mirroring the fallback `wayLength` already uses.

Note that `wayLength` is **not** redundant with the final `distance`: per-*annotation*
exports have no `distance` column at all, and with *include sampled segment points* off the
two diverge (chords versus the draped path).

### Surface properties

The one point attribute that is not read off the annotation. Tick **Surface properties** and
every exported point is sampled against the **OPC layers** of the surface underneath it — the
scalar and texture attributes an OPC dataset carries besides its base texture (gravity,
altitude, slope maps, secondary image layers, whatever the dataset ships). This reproduces
the old *selected as multi-attribute profile* export, with the rest of the window's columns
available alongside.

One column per layer found, named `surface_<layer>` after the layer in the patch:

| | |
|---|---|
| Prefix | `surface_` — the layer names come from the *data*, so without a namespace of their own a layer called `alt` or `x` would shadow a coordinate column |
| Order | after every column the settings named, alphabetically among themselves — so the table does not reorder itself depending on which patch was hit first |
| Multi-channel layers | kept in **one** cell, `0.25;0.5;0.75` (a JSON array in GeoJSON), because the channel *count* would otherwise depend on which texture a point landed on |
| Points that hit nothing | empty cells; the export still succeeds |

Only offered for **one record per point** — a per-annotation record has no single position to
sample at — and ignored by the fixed-schema file types. It works for CSV and GeoJSON alike.

#### How a value is found

An annotation point does not remember where it came from, so each point is **re-picked**: a
ray is shot from 10 m above the point, along the reference system's up axis, into the visible
and active surfaces' KdTrees. The patch that is hit identifies the textures; barycentric
coordinates on the hit triangle interpolate the UV, and each layer image is sampled at that
UV (nearest pixel, no filtering). The value therefore comes from the same patch the renderer
draws there, and *not* from the annotation's `surfaceName`.

Consequences worth knowing:

- **The surface has to be switched on.** Hidden or inactive surfaces are not picked, so an
  annotation drawn on a surface that is now off yields no values at all. If not one point
  produced any, the file is still written and the window stays open to say so.
- **Only OPC surfaces.** Mesh (`.obj` and friends) data sources have no layers to sample.
- **KdTrees are required**, as for any picking — build them with `opc-tool` if picking does
  not work on the surface either.
- The first layer of a patch (the base texture) is skipped; it is the colour PRo3D renders,
  not an attribute.

#### It is slow, on purpose off by default

A ray cast plus one texture read per layer per point. With *include sampled segment points*
on, a drawn polyline easily has thousands of points, and PRo3D is **busy** — the whole export
runs synchronously — until it finishes. Decoded layer images and per-patch texture
coordinates are cached (bounded, oldest-first), so consecutive points on the same patch are
cheap and the cost scales with the number of *patches* crossed rather than points sampled.
No preset switches it on; the *Profile* preset does not either, so an ordinary profile export
stays fast. The accordion's **all** / **none** buttons do cover it.

The KdTree cache is shared with interactive picking, so surfaces loaded for the export stay
loaded for the next pick.

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

Verified against `git show releases/6.0.0:src/PRo3D.Core/Drawing/Drawing-App.fs`. Note the
**sampled points** column — the old exports disagreed with each other about it, so half of
them need it switched off to reproduce.

| Old command | New settings | Sampled points |
|---|---|---|
| visible as table (*.csv) | CSV · Visible · per annotation · Both · Flipped | **off** |
| selected as profile (*.csv) | CSV · Selected · per point · Geographic + *ground distance* (preset *Profile*) | on |
| selected as multi-attribute profile | CSV · Selected · per point · Cartesian + **Surface properties** | on |
| visible as GeoJSON | GeoJSON · Visible · Geographic · Flipped | on |
| visible as GeoJSON xyz | GeoJSON · Visible · Cartesian | on |
| latlon GeoJSON for QGIS | GeoJSON · Visible · Geographic · **Native** | **off** |
| xyz GeoJSON for QGIS | GeoJSON · Visible · Cartesian | **off** |
| latlon for QGIS + xyz metadata | **no equivalent** — closest is GeoJSON · Visible · Geographic · **Native** | **off** |
| dns as 'Attitude' planes | file type *Attitude planes* · Visible | n/a |
| continuously export as GeoJSON xyz | file type *Continuous GeoJSON* (preset of the same name) | n/a |
| all as 'PRo3D' annotations | unchanged, still on the menu | n/a |

Three of these are not exact equivalents:

- ***latlon for QGIS + xyz metadata*** has no equivalent at all. It wrote a `cartesian`
  property containing **every** point, which no setting reproduced even before; and since
  *Both* is no longer offered for GeoJSON, the cartesian position is not carried alongside
  geographic geometry either. Export the same annotations twice — once *Geographic*, once
  *Cartesian* — if both sets of numbers are needed.
- ***selected as profile*** wrote exactly two columns, `distance` and `elevation`. Those are
  now `groundDistance` and `alt`, with the same values, alongside whatever else is ticked
  (and `key`, which is always written).
- ***selected as multi-attribute profile*** wrote `distance,x,y,z` plus one bare layer column
  each. The layer columns are now prefixed `surface_`, `distance` is the ticked `distance`
  (or `groundDistance`) column, and its rows were the *re-picked* positions whereas the new
  export writes the annotation's own point and samples the surface at it — a difference of
  millimetres, but they are no longer literally the same numbers.

The three QGIS variants applied **no** longitude transform, i.e. *Native*. The *GIS / QGIS*
preset instead selects *Shifted by 180°* — pick *Native* in the dropdown to reproduce the old
files.

Note the **longitude range** also differs from all of the old exports: they wrote `[0, 360)`,
and the signed `(−180, 180]` range is now the default. Wherever a longitude exceeds 180° the
numbers therefore differ by 360. Untick *Longitude range* to get byte-comparable output — this
works for every file type, GeoJSON included.

Two properties are no longer emitted: **`isSelected`** (no equivalent attribute) and
**`isEllipse`** (derivable from the `geometry` column).

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
| `src/PRo3D.Base/Annotation/Exporters/AnnotationExport.fs` | `schemaOf` / `schemaFor` / `buildRecords` / `write` — the shared record builder, and the lat/lon/alt resolution behind `latLonAltSource` |
| `src/PRo3D.Core/AnnotationExport-Model.fs` | `AnnotationExportModel` + actions (session-only, on the root `Model`, not persisted) |
| `src/PRo3D.Core/AnnotationExportApp.fs` | `update` and the window `view` |
| `src/PRo3D.Core/ProfileAttributeExtraction.fs` | `sampleAt` — the KdTree re-pick, UV interpolation and layer sampling behind *Surface properties*; `tryLonLatRadius` / `hasLonLatRadLayer` behind the *File (.aara)* source |
| `src/PRo3D.Viewer/Viewer/AnnotationExportViewer.fs` | scope resolution, the surface sampler, and running the export |
| `src/Tests/AnnotationExportTest.fs` | schema, lengths, culture-invariance, presets, surface columns |

Adding an annotation-level attribute means adding one enum case in `AnnotationFields.fs`
plus its `columnName`, `label`, `groupOf` and `valueOf` branch — the window, the schema and
both writers pick it up automatically.

The surface properties are the one exception to that pattern, because their columns come from
the data rather than an enum. `AnnotationExport` never sees a surface: it takes a
`SurfacePropertySampler` (`V3d -> list<string * ExportValue>`) that the viewer supplies, since
sampling needs the surface model and its KdTrees. That is also why the complete column list
comes from `schemaFor settings records` rather than `schemaOf settings` — the layer columns
are only known once the records exist.

## See also

- [ai/DOMAIN.md](../ai/DOMAIN.md) — the annotation model these attributes are read from
- [CrossSections.md](CrossSections.md) — the other consumer of annotation geometry
