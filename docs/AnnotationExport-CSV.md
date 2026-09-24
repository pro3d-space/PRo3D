# CSV export: field reference

What every column of the annotation CSV export means, for the three presets users work with: **Profiles**, **Boulders** and **Fractures** (issue #644). The export window itself is described in [AnnotationExport.md](AnnotationExport.md).

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

A profile is the line followed across the surface point by point, one row per point. The points are not made by the export: they are the ones the line was **drawn** with. While drawing with *Sky* or *Viewpoint* projection, each segment (the stretch between two clicked points) is walked along its straight chord in steps of the **sampling distance** set in the drawing tools (amount and unit, default 1 m), and every step is cast onto the surface (*Sky*: straight down along the local up; *Viewpoint*: from the camera). The hits are stored with the annotation; steps that miss the surface are dropped. The export writes these stored points when *Include sampled segment points* is on (the *Profile* preset turns it on). A line drawn with *Linear* projection has no draped points, only its clicked ones. So the row spacing is the sampling distance chosen when the line was drawn, a little more where the terrain is steep; to get a denser profile, redraw the line with a smaller sampling distance.

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

### Example

A line of three clicked points on the floor of Gale crater (Mars, 4.6° S 137.4° E), each segment draped as one interior sample, exported with the *Profile* preset. This is the exporter's actual output, full precision included:

```csv
key,text,surfaceName,pointIndex,segmentIndex,x,y,z,lat,lon,alt,body,latLonAltSource,stepLength,segmentLength,distance,groundDistance
60086695-3803-4414-a056-b13fec0644f9,F-01,Gale_HiRISE,0,0,-2488533.042481201,2288403.5967716263,-271980.3869067464,-4.599499999999999,137.399,-4499.999999999484,Mars,spice_recpgr,0,41.840414962850396,0,0
60086695-3803-4414-a056-b13fec0644f9,F-01,Gale_HiRISE,1,0,-2488544.487996643,2288394.081505143,-271965.6996725798,-4.599250000000001,137.39925,-4499.199999999805,Mars,spice_recpgr,20.9106424446417,41.840414962850396,20.9106424446417,20.923054508441023
60086695-3803-4414-a056-b13fec0644f9,F-01,Gale_HiRISE,2,0,-2488556.226917929,2288384.8360203668,-271951.04449888744,-4.599,137.3995,-4498.000000000339,Mars,spice_recpgr,20.929772518208697,41.840414962850396,41.840414962850396,41.846112677470586
60086695-3803-4414-a056-b13fec0644f9,F-01,Gale_HiRISE,3,1,-2488556.226917929,2288384.8360203668,-271951.04449888744,-4.599,137.3995,-4498.000000000339,Mars,spice_recpgr,0,42.98627769660064,41.840414962850396,41.846112677470586
60086695-3803-4414-a056-b13fec0644f9,F-01,Gale_HiRISE,4,1,-2488569.78305936,2288369.245503309,-271956.93703987973,-4.599099999999999,137.39985,-4498.100000000383,Mars,spice_recpgr,21.483836528073553,42.98627769660064,63.32425149092395,63.35820795108502
60086695-3803-4414-a056-b13fec0644f9,F-01,Gale_HiRISE,5,1,-2488582.7521122536,2288353.115143978,-271962.7654316903,-4.5992,137.40020000000004,-4498.999999999665,Mars,spice_recpgr,21.50244116852709,42.98627769660064,84.82669265945104,84.87030043736804
```

How to read it:

- `pointIndex` 2 and 3 are the same point, the joint between the two segments: the last row of segment 0 and the first of segment 1, with `stepLength` 0 on the repeat.
- The two `segmentLength` values (41.84 m, 42.99 m) add up to the last `distance` (84.83 m), the line's total draped length.
- `lon` is 137.4, the familiar east longitude of Gale. Mars coordinates come from SPICE's planetographic routine (`spice_recpgr`), whose longitudes are **west-positive** (Gale = 222.6° W). *Profile* writes *Flipped* longitudes, 360 − 222.6 = 137.4, which turns them east-positive.
- `alt` is the height above the Mars reference spheroid, here about 4.5 km below it, on the crater floor.
- `groundDistance` is measured on the reference surface, 4.5 km above the line, so it comes out slightly longer than `distance`.

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
| `majorAxisAzimuth` | deg | Direction of the long axis, clockwise from local north at the ellipse centre, 0–180. Empty for a circle (no long axis) and for an ellipse whose long axis points straight up (no horizontal direction). |
| `x, y, z` | m | Ellipse centre. |
| `lat, lon, alt, body, latLonAltSource` | | Of the centre; see *Geographic columns*. |

Where the numbers come from: the ellipse is fitted on a plane through the clicked points (drawn ellipses) or taken from the catalog (SBMT imports), and stored with the annotation when it is created. Axes are measured on that plane, not along the draped outline; the azimuth uses the local up and north at the centre, and on a body without a geographic frame the reference system's own up and north. The values never change afterwards.

- **Three-point ellipse** (*AxisEllipse*): the first two clicks are the ends of one axis, the third sets the other. Whichever turns out longer is `semiMajorAxis`, so the long axis is not necessarily the one clicked first.
- **Four-point ellipse** (*Axis4PEllipse*): two half-ellipses on either side of the clicked axis, each with its own width. It is reported as the symmetric ellipse of the same extent: `semiMinorAxis` is half the full width across the clicked axis (the mean of the two half-widths), and the centre sits in the middle of that width, so it can lie off the clicked axis.
- An ellipse saved before these values existed exports empty cells, and its row falls back to the outline's bounding-box centre.

### Example

Three boulders on the floor of Gale crater (Mars) and one traced fracture, exported with the *Boulders (ellipses)* preset. This is the exporter's actual output, full precision included. The fracture line has no row, because the preset exports ellipses only:

```csv
key,text,surfaceName,groupPath,semiMajorAxis,semiMinorAxis,majorAxisAzimuth,x,y,z,lat,lon,alt,body,latLonAltSource
88f31cf1-1a64-434e-96c0-4dfe369faef5,B-001,Gale_HiRISE,Gale/boulders,2.9999999999361315,1.9999999999962261,29.999999998045922,-2488571.2350755627,2288358.55669152,-272009.8896569475,-4.600000000000001,-137.40000000000003,-4500.000000000831,Mars,spice_recpgr
f742b572-34d3-4879-a419-4270b1d69ac3,B-002,Gale_HiRISE,Gale/boulders,4.999999999970896,1.5,0,-2488611.5125193717,2288314.7545268126,-272009.88965694746,-4.599999999999949,-137.4010084737893,-4499.99999996358,Mars,spice_recpgr
d26a3ca7-93d9-4ece-8d02-c94d591edb5e,B-003,,Gale/boulders/catalog,4.5,2.7,70.00000000000001,-2488567.7400990394,2288355.342895861,-272068.89509520313,-4.601000000000001,-137.40000000000003,-4500.000000000362,Mars,spice_recpgr
```

Where each row comes from:

| Row | Source | Input | Result |
| --- | --- | --- | --- |
| B-001 | drawn, three clicks | axis clicks 3 m either side of the centre, rotated 30° east of north; third click 2 m off the axis | semi-axes 3 m / 2 m, azimuth 30° |
| B-002 | drawn, four clicks | axis clicks 5 m north and south of a point at 137.401° E; width clicks 2 m east and 1 m west | semi-axes 5 m / 1.5 m (half the 3 m width); azimuth 0° (due north); the centre sits 0.5 m east of the clicked axis, which is why `lon` is −137.4010085 rather than −137.401 |
| B-003 | SBMT catalog row | diameter 0.009 km, flattening 0.6, regular angle 20° | semi-axes 4.5 m / 2.7 m, azimuth 90 − 20 = 70°; `surfaceName` empty, because imports are not bound to a surface |

`lon` is −137.4 for Gale because *Boulders* writes *Native* longitudes: for Mars those are planetographic and **west-positive** (222.6° W, written in the signed −180…180 range). Choose *Flipped* in the window for east-positive longitudes, as *Profile* does.

The trailing digits (2.9999999999361315 for 3 m) are the round trip through the fitted plane, written at full double precision like every number in the export. Round them in the tool you read the file with.

### Boulders: surface statistics inside the ellipse

After the columns above, every *Boulders* row carries statistics of the surface **inside** the ellipse (`EllipseStatistics`, see [EllipseStatistics.md](EllipseStatistics.md)). They integrate the OPC's per-vertex layers over the mesh triangles inside the ellipse, clipped at the rim and weighted by surface area.

| Column | Unit | Meaning |
| --- | --- | --- |
| `surfaceArea` | m² | True mesh area inside the ellipse: a boulder's flanks count by their real size. |
| `footprintArea` | m² | π · `semiMajorAxis` · `semiMinorAxis`, the ellipse's own area. `surfaceArea / footprintArea` ≥ 1 is a roughness measure. |
| `vertexCount` | — | Distinct OPC vertices inside the ellipse: how many measurements the statistics rest on. |
| `surface_<layer>_area` | m² | The part of `surfaceArea` where the layer has values. Smaller than `surfaceArea` where the layer has gaps, so a partly covered boulder shows in the numbers. |
| `surface_<layer>_mean` | layer's unit | Area-weighted mean over the inside, e.g. `surface_Slope_mean`. Multi-channel layers: one value per channel, `x;y;z`. |
| `surface_<layer>_std` | layer's unit | Area-weighted standard deviation. |
| `surface_<layer>_min`, `surface_<layer>_max` | layer's unit | Extremes over the inside. |

There is one set of five columns per per-vertex layer of the OPC, in alphabetical order; textures are not read. `LonLatRad` is left out: the row's own `lat`, `lon` and `alt` give the position, and a mean longitude is wrong for an ellipse across the 0/360° meridian.

Which surface is integrated:

- **Drawn ellipses:** only the surface named in `surfaceName`, the one the ellipse was drawn on, **whether it is currently visible or not**. Other surfaces overlapping the same spot do not count. Surfaces are matched by name, so two loaded surfaces with the same name would both count.
- **Inside** means within the elliptic cylinder through the ellipse, up to one semi-major axis above and below the ellipse's plane: a boulder as high as it is wide is taken in, the far side of a small body is not.
- **Imported ellipses** (SBMT, empty `surfaceName`) get `footprintArea` only; the other statistics cells stay empty for now.
- An ellipse drawn on a surface that is **not loaded** gets empty statistics too, and the export window says which surfaces were missing.

Cost: tens of ellipses take milliseconds. A catalog of 4,800 boulders on the Dimorphos OPC takes about 3 s on an 18-core machine.

#### Example

One boulder on the Dimorphos OPC (`HERA/Dimorphos_opc/Dimorphos_DRACO1_DRACO2_Earth`), a 4 m × 2.5 m ellipse, exported with the *Boulders (ellipses)* preset. This is one row of the exporter's actual output, shown vertically (`column = value`); the `DRACO_2`, `Magnitude` and `Potential` layers are left out here for length:

```text
key                  = 6fb72c82-75e4-4526-9067-efb8ccfeab64
text                 = B-drawn
surfaceName          = Dimorphos
groupPath            =
semiMajorAxis        = 4
semiMinorAxis        = 2.5000000000000004
majorAxisAzimuth     = 90
x, y, z              = -79.57764912670397, -33.157698865315496, 8.647869725308858
lat, lon, alt        = 5.728322923295338, -157.37992335996321, 86.64191182886991
body                 = Dimorphos
latLonAltSource      = spice_reclat
surfaceArea          = 32.35510856272484
footprintArea        = 31.41592653589794
vertexCount          = 502
surface_DRACO_1_area = 32.35510856272484
surface_DRACO_1_mean = 212.34986566501397;212.34986566501397;212.34986566501397
surface_DRACO_1_std  = 16.296851381644778;16.296851381644778;16.296851381644778
surface_DRACO_1_min  = 156;156;156
surface_DRACO_1_max  = 251.69935462891655;251.69935462891655;251.69935462891655
surface_Elevation_area = 32.35510856272484
surface_Elevation_mean = 103.1973746161071
surface_Elevation_std  = 1.3954836837695053
surface_Elevation_min  = 100.18873291831798
surface_Elevation_max  = 105.91154216430638
surface_Gravity_area = 32.35510856272484
surface_Gravity_mean = 3.663793904324056E-05;1.816978780768838E-05;-7.4443307910942955E-06
surface_Gravity_std  = 2.703974341430514E-07;9.54720698201544E-07;1.080866394303633E-06
surface_Gravity_min  = 3.6159848089373235E-05;1.6382044993118538E-05;-9.676138271517114E-06
surface_Gravity_max  = 3.722269492636348E-05;2.0034603363789166E-05;-5.259019320349042E-06
surface_Normal_area  = 32.35510856272484
surface_Normal_mean  = -0.8255718906880123;-0.49616386234028576;0.24323639011379763
surface_Normal_std   = 0.029396381784865602;0.04366943341889763;0.042124248675882624
surface_Normal_min   = -0.884804693545063;-0.5821338295936584;0.13692576022017128
surface_Normal_max   = -0.766028216861157;-0.4207836452484297;0.35578876455860964
surface_Slope_area   = 32.35510856272484
surface_Slope_mean   = 7.824717870971677
surface_Slope_std    = 2.6216979026213503
surface_Slope_min    = 4.126854041673854
surface_Slope_max    = 14.483165206510783
```

How to read it:

- `surfaceArea` (32.4 m²) is 3 % above `footprintArea` (31.4 m²): gently rough ground, which `surface_Slope_mean` of 7.8° (4.1–14.5°) agrees with.
- The statistics rest on 502 mesh vertices, and every layer covers the whole inside (each `_area` equals `surfaceArea`).
- `DRACO_1` is a camera image stored as three identical grey channels, so each of its cells repeats one value three times. The export writes the layer as the OPC stores it.
- `Gravity` and `Normal` are vectors (body-fixed x;y;z); their mean is taken per channel, so the mean normal is not unit length.
- `alt` is 86.6 m from the body centre (`spice_reclat`); `Elevation` is the OPC's own elevation layer, a different quantity.

---

## Fractures

**Preset *Fractures (segments)*** · one row per segment of each line · scope: all · longitude: *Native*.

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
| `body, latLonAltSource` | | As in *Geographic columns*; one value for both endpoints. Always a SPICE routine: the *File (.aara)* source is a per-point setting and is not offered for segments. |
| `segmentLength` | m | Length of the segment **along the surface** (draped). |
| `segmentChord` | m | Straight 3D distance from start to end. Never larger than `segmentLength`; the difference is terrain. |
| `segmentAzimuth` | deg | Direction start → end, clockwise from local north at the segment's midpoint, 0–180. |

Where the segments come from: a line drawn with *Sky* or *Viewpoint* projection stores each segment with its draped points, and `segmentLength` is the length along those points (the same value as the *Profile* rows' `segmentLength`). A line drawn with *Linear* projection stores no segments; its segments are then clicked point *i* to *i + 1*, and `segmentLength` equals `segmentChord`. The start and end columns are the clicked points.

`segmentAzimuth` is axial, like the ellipse's long-axis azimuth: a lineament has no head, so a segment drawn the other way round gives the same value.

For a total-length-only table, keep one row per `key`: `key, text, wayLength`.

### Example

The fracture `F-01` from the *Profile* example above (Gale crater, Mars), exported with the *Fractures (segments)* preset. This is the exporter's actual output:

```csv
key,text,surfaceName,wayLength,groupPath,segmentIndex,startX,startY,startZ,startLat,startLon,startAlt,endX,endY,endZ,endLat,endLon,endAlt,body,latLonAltSource,segmentLength,segmentChord,segmentAzimuth
60086695-3803-4414-a056-b13fec0644f9,F-01,Gale_HiRISE,84.82669265945103,Gale/fractures,0,-2488533.042481201,2288403.5967716263,-271980.3869067464,-4.599499999999999,-137.399,-4499.999999999484,-2488556.226917929,2288384.8360203668,-271951.04449888744,-4.599,-137.3995,-4498.000000000339,Mars,spice_recpgr,41.840414962850396,41.83850849624824,44.90760305435428
60086695-3803-4414-a056-b13fec0644f9,F-01,Gale_HiRISE,84.82669265945103,Gale/fractures,1,-2488556.226917929,2288384.8360203668,-271951.04449888744,-4.599,-137.3995,-4498.000000000339,-2488582.7521122536,2288353.115143978,-271962.7654316903,-4.5992,-137.40020000000004,-4498.999999999665,Mars,spice_recpgr,42.98627769660064,42.978834309381995,105.99427314320522
```

How to read it:

- Two clicked segments, two rows. `wayLength` (84.83 m) repeats on both and equals the sum of their `segmentLength` (41.84 m + 42.99 m), the same lengths the *Profile* rows carry.
- `segmentChord` is a few millimetres shorter than `segmentLength`: the terrain along this fracture is almost flat.
- The first segment runs north-east (`segmentAzimuth` 44.9°), the second east-south-east (106.0°). Each is measured at the segment's own midpoint.
- `startLon` is −137.399: *Native* Mars longitudes are planetographic and west-positive (see the *Boulders* example); choose *Flipped* for east-positive ones.
- Row 1 starts where row 0 ends (the second clicked point).
