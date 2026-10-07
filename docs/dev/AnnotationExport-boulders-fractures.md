# Annotation export for boulder and fracture mapping

2026-09-24 · CSV table export

## What the users asked for

PRo3D's CSV export covers the fracture request today. For boulders it currently delivers none of the three: the axis-length columns exist but come out empty (a bug), and orientation and raster values inside an ellipse are not implemented.

| Request | Available now | How |
| --- | --- | --- |
| Fracture: total line length | yes | `wayLength` column, one row per annotation |
| Fracture: length of each segment | yes, awkward | `segmentLength` per point row; de-duplicate on `segmentIndex` |
| Boulder: semi-axis lengths | no (bug) | `majorDiameter` / `minorDiameter` columns exist but are empty for ellipses drawn in current builds |
| Boulder: orientation | no | computed while drawing, then discarded; no column |
| Boulder: raster values inside the ellipse | no | only sampled at exported points, never the interior |

Scale of the request: tens of ellipses and tens of lines per dataset, drawn on OPC surfaces such as the Hera Dimorphos model.

## How to run the export

Open *Annotations → Export…*, choose **CSV table**, and set the scope to the annotations you want (*Selected only* after selecting them, or *All*). Everything else depends on whether you want one row per annotation or one row per point.

| Goal | Granularity | Settings | You get |
| --- | --- | --- | --- |
| Total length of each fracture | one record per annotation | Preset *Annotation table*; tick *Length, total along surface* | one row per line, `wayLength` in metres |
| Length of each fracture segment | one record per point | Preset *Profile*; keep *include sampled segment points* on | one row per sampled point, with `segmentIndex` and `segmentLength` |
| Boulder axis lengths | one record per annotation | tick *Major diameter* and *Minor diameter* | one row per ellipse (columns currently empty, see below) |
| Raster values along a line or ellipse outline | one record per point | tick *Surface properties* | one `surface_<layer>` column per OPC attribute layer |

A segment is the stretch between two points the user clicked. *Include sampled segment points* adds the points PRo3D draped onto the surface between them, which is what makes the lengths follow the terrain. Per-annotation rows carry one coordinate only, the bounding-box centre.

## Reading the columns: fractures and lineaments

Total length is `wayLength`; each segment's length is `segmentLength`, read once per `segmentIndex`. All lengths are in metres and follow the draped surface unless noted.

| Column | Level | Meaning | Answers |
| --- | --- | --- | --- |
| `wayLength` | annotation (repeated on every point row) | length of the whole line along the surface: the sum of all segments | total line length |
| `length` | annotation | straight 3D distance from the first to the last clicked point, ignoring everything in between | — (a chord, not the line) |
| `segmentIndex` | point | which segment the row belongs to, counting from 0 | groups rows per segment |
| `segmentLength` | point (repeated on every row of its segment) | draped length of that whole segment | length of each segment |
| `stepLength` | point | 3D distance from the previous row to this one | — (sampling step, ~0.18 m on Dimorphos) |
| `distance` | point | running sum of `stepLength` from the first point; the last row equals `wayLength` | position along the line |
| `groundDistance` | point | like `distance`, but with height removed first; the x-axis of a topographic profile | — (0 on every row on Dimorphos and Didymos: a bug) |
| `alt` | point | for Dimorphos and Didymos: distance from the body centre (SPICE `reclat` radius), not height above a surface | — |

Worked example: a Dimorphos profile with two clicked points produced 395 rows, all `segmentIndex` 0. `segmentLength` and `wayLength` are 82.58 m on every row, and `distance` climbs from 0 to 82.58 m in steps of about 0.18 m. The straight line between the endpoints (`length`) is 78.65 m; the difference is terrain.

Per-segment table from a per-point export: keep the first row of each (`key`, `segmentIndex`) pair, then keep `key`, `segmentIndex`, `segmentLength`. Two cautions:

- Each segment's start and end rows repeat the joint point, so rows at a joint have `stepLength` 0. This does not affect `segmentLength`.
- Without *include sampled segment points*, row *i* carries the length of the segment ending at it, and the first row's `segmentLength` is empty.

## Reading the columns: boulders (ellipses)

None of the three boulder numbers can be taken from today's export. The columns below are what exists and why each falls short.

| Column | Level | Meaning | Problem today |
| --- | --- | --- | --- |
| `majorDiameter`, `minorDiameter` | annotation | twice the stored semi-axes, so semi-axis = value / 2 | Empty for every ellipse drawn in current builds: drawing computes the ellipse but stores no result. Where a value exists, from older code, it is measured in longitude/latitude degrees, although the label says metres. |
| `bearing` | annotation | direction from the first to the last point of a line | Not an orientation: an ellipse's points are its sampled outline, so first and last are neighbours on the rim. |
| `surface_<layer>` | point | the attribute layer's value under that point, in physical units; one column per layer | Sampled on the outline only; nothing samples the interior. Per-annotation exports carry no surface columns at all. |

The `surface_<layer>` columns on the Dimorphos model are Elevation, Slope, Magnitude and Potential as single numbers. Gravity, Normal and LonLatRad are 3-component vectors written in one cell as `x;y;z`, and DRACO_1 and DRACO_2 are image colours. Only layers the OPC's `.opcx` declares as a data map are exported; colour-only textures such as `Earth` are skipped (PR #810).

What survives drawing: the plane fitted through the clicked points (its dip-and-strike result) and the ellipse outline draped onto the surface. The metric ellipse on that plane (`ellipseOnPlane`, axes in metres) is computed while drawing and then discarded (`Drawing-App.fs`, the `Axis4PEllipse`/`AxisEllipse` branch sets `ellipticResults = None`). Keeping it is the first step of every boulder proposal below.

## Proposed changes

Five changes close every gap; the first is a prerequisite for the boulder features, and the rest are independent. Effort assumes one developer familiar with the export code.

| # | Change | Fixes | Effort |
| --- | --- | --- | --- |
| 1 | Keep the metric ellipse when an ellipse is drawn | empty diameter columns; basis for 2 and 3 | ~1 day |
| 2 | Ellipse axes and orientation fields | semi-axes, boulder orientation | ~1 day |
| 3 | Surface statistics inside an ellipse | raster values within each boulder | ~3–4 days |
| 4 | One record per segment | per-segment length table | ~1 day |
| 5 | Ground distance on small bodies | `groundDistance` 0 on Dimorphos/Didymos | ~0.5 day |

With changes 1–4 in, the two CSV files the users need would look like this (illustrative values):

```csv
key,text,semiMajorAxis,semiMinorAxis,majorAxisAzimuth,majorAxisPlunge,surfaceSamples,surface_Slope_mean,surface_Slope_max,surface_Potential_mean
3f2a…,boulder 1,2.41,1.37,63.5,4.2,512,18.7,31.2,-0.00412
9c1e…,boulder 2,1.08,0.66,141.0,-2.8,103,22.4,29.9,-0.00409
```

```csv
key,text,segmentIndex,segmentLength,segmentChord,segmentBearing,wayLength
7d4e…,fracture A,0,12.84,12.61,37.2,31.05
7d4e…,fracture A,1,18.21,17.90,52.8,31.05
```

### 1. Keep the metric ellipse

Store the fitted ellipse (plane, centre, both semi-axis vectors, in metres) on the annotation when drawing finishes, instead of discarding it. It is a new persisted field read with a default, so existing scenes load unchanged; no scene version bump. For ellipses saved before the change, refit the ellipse from the stored outline projected onto the stored plane when the scene loads.

### 2. Axes and orientation

New per-annotation columns, all from the stored ellipse:

- `semiMajorAxis`, `semiMinorAxis` (m): the numbers the users asked for, with no halving.
- `majorAxisAzimuth` (deg, 0–180, clockwise from local north): the long axis's direction projected into the local horizontal plane at the ellipse centre. An axis has no head, so 0–180 rather than 0–360.
- `majorAxisPlunge` (deg): the long axis's tilt out of that horizontal plane, useful on slopes.

`majorDiameter`/`minorDiameter` keep their names but switch to metres from the stored ellipse. The degree-based values they could hold before were never correct.

### 3. Statistics inside an ellipse

For each ellipse, sample the interior on its plane, drape every sample onto the surface with the same ray cast the export already uses, and read the attribute layers there. Write per layer and per channel: `surface_<layer>_mean`, `_min`, `_max`, `_std`, plus one `surfaceSamples` count. These columns sit on the per-annotation row, next to the axes.

- Sample spacing = the OPC's vertex spacing (about 0.2 m on Dimorphos), capped at ~2,000 samples per ellipse. A 5 m boulder gets ~500 samples.
- Cost: the warm export measured ~2 ms per sample, so 50 boulders × 500 samples is about 50 s on first run, far less once cached.
- Samples that miss the surface are left out and show in the count, so a boulder partly off the model is visible, not silently averaged.
- Alternative: collect the OPC vertices inside the ellipse and average the per-vertex values directly. It is exact, but needs a spatial search over the patch grids; worth it only if interpolated samples prove too smooth.

### 4. One record per segment

A third granularity, *one record per segment*, beside per annotation and per point: one row per clicked-to-clicked stretch, with `key`, `segmentIndex`, start and end coordinates, `segmentLength` (draped), `segmentChord` (straight) and `segmentBearing`. It removes the de-duplication step from the fracture workflow.

### 5. Ground distance on small bodies

On Dimorphos and Didymos the coordinate conversion stores distance from the body centre as `alt`, so flattening to altitude 0 collapses every point to the centre and `groundDistance` stays 0. Flatten onto the body's mean radius instead (77.17 m for Dimorphos) for spherical-convention bodies.

### Open questions

- [ ] Orientation reference: azimuth from the body's north pole direction, or from the map grid the users draw on?
- [ ] Statistics set: are mean/min/max/std enough, or do users also want median or percentiles?
- [ ] Do the users need statistics for polygons as well as ellipses? Change 3 extends to any closed shape at little extra cost.
