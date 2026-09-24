# CSV export: field reference

What every column of the annotation CSV export means, for the three presets users work with: **Profiles** and **Boulders** (available), and **Fractures** (planned, issue #644, see [dev/PLAN-export-ellipses-and-segments.md](dev/PLAN-export-ellipses-and-segments.md)). The export window itself is described in [AnnotationExport.md](AnnotationExport.md).

Columns come in a fixed order: the annotation columns in the order of the window's attribute list, then the coordinates.

## Conventions for every file

| Topic | Rule |
| --- | --- |
| Encoding | UTF-8, comma-separated, one header row, [RFC 4180](https://www.rfc-editor.org/rfc/rfc4180) quoting (only cells containing `,` `"` or a line break are quoted). |
| Numbers | Invariant culture: `.` as decimal separator, no thousands separator, full double precision (shortest round-trip form). |
| Empty cell | "No value": not applicable to this row, or not computable. Never 0. |
| Lengths | Metres. |
| Angles | Degrees. |
| Azimuths | Clockwise from local north, **axial 0–180** (a line or axis has no head, so 0° and 180° are the same direction). |
| Coordinates `x, y, z` | Body-fixed cartesian, metres, the frame the OPC is in. |
| Coordinates `lat, lon, alt` | Degrees, degrees, metres; meaning depends on `latLonAltSource` (below). |
| Multi-channel values | One cell, channels separated by `;`, e.g. `0.12;-0.40;0.91`. |
| Row order | The order of the annotation tree, then drawing order within an annotation. |

### Geographic columns, shared by all presets

| Column | Meaning |
| --- | --- |
| `lat` | Latitude, degrees, −90 to 90. |
| `lon` | Longitude, degrees. −180 to 180 (default) or 0 to 360, per the *Longitude range* setting. **Direction** per the *Longitude* setting: *Native* = as the body defines it; *Flipped* = 360 − lon (east and west mirrored; the CSV default for Profiles). |
| `alt` | Depends on `latLonAltSource`: `spice_reclat` and `aara_file` → **distance from the body centre** (Dimorphos, Didymos, the small bodies); `spice_ellipsoidal` → height above the body's tri-axial ellipsoid; `spice_recpgr` → height above the reference spheroid (Mars and other planets). |
| `body` | The body the coordinates refer to, e.g. `Dimorphos`. |
| `latLonAltSource` | Which routine produced this row's `lat, lon, alt`: `spice_reclat`, `spice_ellipsoidal`, `spice_recpgr` (computed from `x, y, z`), or `aara_file` (read from the OPC's per-vertex LonLatRad layer). Per row, because a point the OPC data does not cover falls back to SPICE. Empty when the scene has no geographic frame. |

---

## Profiles

**Preset *Profile*** · one row per sampled point along each selected line · longitude: *Flipped* (default) · available now.

A profile is the line followed across the surface point by point. Each segment (the stretch between two clicked points) is draped onto the terrain as sample points ~0.2 m apart on Dimorphos, and every sample is one row.

| Column | Unit | Meaning |
| --- | --- | --- |
| `key` | — | Annotation id (GUID); the same on every row of one line. |
| `text` | — | Annotation label. |
| `surfaceName` | — | Surface the line was drawn on. |
| `pointIndex` | — | Row number within the line, from 0. |
| `segmentIndex` | — | Segment this point belongs to, from 0. Where two segments meet, the joint point appears twice: as the last row of one segment and the first of the next. |
| `x, y, z` | m | Position of the point. |
| `lat, lon, alt, body, latLonAltSource` | | See *Geographic columns*. |
| `stepLength` | m | 3D distance from the previous row; 0 on the first row and at a repeated joint point. |
| `segmentLength` | m | Draped length of the **whole** segment this point belongs to, repeated on each of its rows. |
| `distance` | m | Distance along the line from its first point: running sum of `stepLength`. The last row equals the line's total draped length. |
| `groundDistance` | m | Like `distance`, with height removed (each point projected onto the reference surface first): the horizontal axis of a topographic profile. **Known bug:** 0 on every row on Dimorphos and Didymos. |
| `surface_<layer>` | layer's unit | Only with *Surface properties* ticked: the value of the OPC's per-vertex (`.aara`) layer at this point, interpolated across the triangle; one column per layer (e.g. `surface_Slope`, `surface_Elevation`). Vector layers (`Gravity`, `Normal`, `LonLatRad`) use the `;` form. Layers that exist only as textures are not sampled (#817). Empty where the point is off the surface. |

To plot a profile: `distance` (or, once fixed, `groundDistance`) on the horizontal axis, `alt` or a `surface_<layer>` on the vertical.

---

## Boulders

**Preset *Boulders (ellipses)*** · one row per ellipse · annotation types: *ellipses only* · scope: all · longitude: *Native*.

| Column | Unit | Meaning |
| --- | --- | --- |
| `key` | — | Annotation id (GUID). |
| `text` | — | Annotation label, e.g. a boulder id. |
| `surfaceName` | — | Surface the ellipse was drawn on or imported to. Empty for SBMT imports. |
| `groupPath` | — | Group the ellipse sits in, nested groups separated by `/`, e.g. `Dimorphos/boulders/north`. |
| `semiMajorAxis` | m | Half the ellipse's long axis. |
| `semiMinorAxis` | m | Half the ellipse's short axis. |
| `majorAxisAzimuth` | deg | Direction of the long axis, clockwise from local north at the ellipse centre, 0–180. Empty for an ellipse whose long axis points straight up (no horizontal direction). |
| `x, y, z` | m | Ellipse centre. |
| `lat, lon, alt, body, latLonAltSource` | | Of the centre; see *Geographic columns*. |

Where the numbers come from: the ellipse is fitted on a plane through the clicked points (drawn ellipses) or taken from the catalog (SBMT imports), and stored with the annotation when it is created. Axes are measured on that plane, not along the draped outline; the azimuth uses the local up and north at the centre, and on a body without a geographic frame the reference system's own up and north. The values never change afterwards.

- **Three-point ellipse** (*AxisEllipse*): the first two clicks are the ends of one axis, the third sets the other. Whichever turns out longer is `semiMajorAxis`, so the long axis is not necessarily the one clicked first.
- **Four-point ellipse** (*Axis4PEllipse*): two half-ellipses on either side of the clicked axis, each with its own width. It is reported as the symmetric ellipse of the same extent: `semiMinorAxis` is half the full width across the clicked axis (the mean of the two half-widths), and the centre sits in the middle of that width, so it can lie off the clicked axis.
- An ellipse saved before these values existed exports empty cells, and its row falls back to the outline's bounding-box centre.

### Boulders: surface statistics inside the ellipse (planned)

Added after the columns above, from `EllipseStatistics` (issue #644, PR D). Every statistic integrates the OPC's per-vertex layers over the mesh triangles inside the ellipse (clipped at the rim), weighted by surface area.

| Column | Unit | Meaning |
| --- | --- | --- |
| `surfaceArea` | m² | True mesh area inside the ellipse: a boulder's flanks count by their real size. |
| `footprintArea` | m² | π · `semiMajorAxis` · `semiMinorAxis`, the ellipse's own area. |
| `vertexCount` | — | Distinct OPC vertices inside the ellipse: how many measurements the statistics rest on. |
| `surface_<layer>_mean` | layer's unit | Area-weighted mean of the layer over the inside, e.g. `surface_Slope_mean`. Vector layers: one value per channel, `x;y;z`. |
| `surface_<layer>_std` | layer's unit | Area-weighted standard deviation. |
| `surface_<layer>_min`, `surface_<layer>_max` | layer's unit | Extremes over the inside. |

All empty for an ellipse off every surface. A layer with holes (no value at some vertices) is averaged only over the area where it has values.

---

## Fractures

**Preset *Fractures*** · one row per segment of each line · scope: all · longitude: *Native* · planned.

A segment is the stretch between two clicked points. A line with 4 clicked points has 3 segments, so 3 rows. Ellipses produce no rows (they have no clicked-to-clicked segments); polygon edges do.

| Column | Unit | Meaning |
| --- | --- | --- |
| `key` | — | Annotation id (GUID); the same on every row of one line. |
| `text` | — | Annotation label. |
| `surfaceName` | — | Surface the line was drawn on. |
| `wayLength` | m | Total draped length of the **whole line**, repeated on each of its rows. Equals the sum of its rows' `segmentLength`. |
| `groupPath` | — | Group path, `/`-separated. |
| `segmentIndex` | — | Segment number within the line, from 0, in drawing order. |
| `startX, startY, startZ` | m | Segment start point. |
| `startLat, startLon, startAlt` | | Segment start point, geographic; see *Geographic columns*. |
| `endX, endY, endZ` | m | Segment end point. |
| `endLat, endLon, endAlt` | | Segment end point, geographic. |
| `body, latLonAltSource` | | As in *Geographic columns*; one value for both endpoints. |
| `segmentLength` | m | Length of the segment **along the surface** (draped). |
| `segmentChord` | m | Straight 3D distance from start to end. Never larger than `segmentLength`; the difference is terrain. |
| `segmentAzimuth` | deg | Direction start → end, clockwise from local north at the segment's midpoint, 0–180. |

For a total-length-only table, keep one row per `key`: `key, text, wayLength`.
