# Surface Statistics Inside an Ellipse

For each ellipse (typically a boulder outline), PRo3D integrates the loaded OPC surface
inside it. The result is the surface area, plus the mean, standard deviation, minimum
and maximum of every per-vertex attribute layer (slope, gravitational potential,
elevation, …), weighted by **surface area**.

Code: `src/PRo3D.Core/EllipseStatistics.fs` (`EllipseStatistics.compute`).
Tests: `src/Tests/EllipseStatisticsTest.fs`.

It feeds the *Boulders* CSV export: every ellipse with a stored shape gets these values as
columns, integrated over the surface it was drawn on. The columns, which surface is used and
an example row are in [AnnotationExport-CSV.md](AnnotationExport-CSV.md#boulders-surface-statistics-inside-the-ellipse).

## What is measured

| Value | Unit | Meaning |
| --- | --- | --- |
| `surfaceArea` | m² | mesh area inside the ellipse: the true surface, flanks included |
| `footprintArea` | m² | π·a·b, the ellipse's own area. `surfaceArea / footprintArea` ≥ 1 is a roughness measure |
| `vertexCount` | – | distinct mesh vertices inside the ellipse: the number of measurements behind the statistics |
| per layer `area` | m² | the part of `surfaceArea` where the layer has values. It is smaller where the layer has holes |
| per layer and channel `mean`, `std`, `min`, `max` | layer unit | area-weighted statistics |

A multi-channel layer (Normal, Gravity, LonLatRad) gets statistics per channel.

## How it is computed

**Inside** means inside the elliptic cylinder through the ellipse along its plane normal,
within `depth` metres of the plane. By default `depth` is the semi-major axis, which takes
in a boulder as high as it is wide without reaching the far side of a small body.

The per-vertex values plus the mesh define a function that is linear inside each
triangle, the same one the renderer shows. Every statistic is taken of that function:

- A triangle fully inside counts with its full area.
- A triangle on the rim is clipped against the ellipse in the ellipse plane (a 128-gon of
  equal area, within 0.02 % of the rim). Projection scales every part of a triangle by
  the same factor, so the clipped part's surface area is exact.
- **mean** = Σ area × value at the centroid ÷ Σ area. This is exact for a linear function.
- **std** from the integral of the value squared, using the edge-midpoint rule (exact
  for quadratics).
- **min / max** are taken over the corners of the clipped pieces, where the extremes of a
  linear function lie.

Weighting by surface area means small or large, flat or steep triangles each count by
the area they cover. A boulder flank counts by its real size, not by the ground it hides.
Area weighting also makes the result independent of how finely the OPC is meshed.

Only **per-vertex `*.aara` layers** are read (see [VertexAttributes.md](VertexAttributes.md)).
Textures are never decoded. An OPC without per-vertex layers gets geometry only
(`surfaceArea`, `vertexCount`) and no layer statistics.

A triangle with no value at one of its corners (NaN, or the position grid's skirt) counts
for `surfaceArea` but not for that layer. That is why a layer's `area` can be smaller than
`surfaceArea`, so a partly covered ellipse is visible in the numbers.

## Limits

- Where several surfaces overlap inside an ellipse, all of them are integrated. The
  caller picks the surface the ellipse was drawn on with the surface filter.
- Only OPC surfaces are integrated, not meshes (they have no position grid). `compute` returns
  `Error` when the filtered surfaces have no OPC patch at all, so the caller writes "unknown",
  not zero.
- A patch that cannot be read is skipped; every ellipse it touches keeps the rest and carries a
  note (`EllipseStatistics.notes`, exported as `statisticsNote`).
- A four-point ellipse is integrated as the symmetric ellipse stored for it, not its drawn
  outline (see AnnotationExport-CSV.md).
- Overhangs inside the slab are counted, all layers of them. Usually that is the point,
  since a boulder's flanks belong to it.

## Verification

The Dimorphos test compares three ellipses (~250 m², ~4,000 vertices each) against brute
force. That check shoots a 0.1 m grid of rays through the ellipse, weights each hit by
1/cos of its triangle, and samples the layers through the KdTree picking path. Areas
agree within 0.2 % and means within 0.1 %. Synthetic meshes cover the rest against
closed-form answers: flat, tilted, kinked, holey, and triangles larger than the ellipse.

Integrating the three ellipses over the whole Dimorphos OPC takes about 0.4 s; a catalog of
4,800 boulder-sized ellipses (0.5–5 m) takes about 3 s on an 18-core machine (100 s before
the three measures below). Each patch is cut once into blocks of 32 × 32 quads with
world-space bounds, so an ellipse only visits the blocks it can reach; a rim triangle is
clipped only against the polygon edges that actually cut it; and the ellipses of a patch are
integrated in parallel, since each one writes only its own sums.
