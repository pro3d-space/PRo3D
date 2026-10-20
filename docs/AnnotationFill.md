# Filled annotations

Closed annotations (polygons and ellipses) can be drawn filled: tick **Fill** in the annotation
properties (or *Fill/Alpha* in the [annotation toolbar](AnnotationToolbar.md) for new ones) and
set the fill colour and alpha there. Colour by Category recolours fills as well.

## The fill lies on the terrain

The fill is drawn *onto* the surface, not as a separate sheet: every terrain pixel inside the
outline is tinted, at every level of detail, including terrain that rises above or dips below
the outline - a boulder or a ridge inside a mapped area is coloured on its full relief. Earlier
versions drew a flat sheet between the outline points, which the terrain cut through (#841).

Outlines, points and labels are always drawn on top of the fill.

## Fill reach

A fill colours terrain up to a certain distance above and below its outline - the **fill
reach**, in *Viewer configuration → Fill Reach (m)*:

- **auto** (default): half the outline's size, at most the body's maximum below.
- untick **auto** to set a fixed reach in metres.

| body | maximum auto reach |
|---|---|
| Mars, Earth, Moon | 1000 m |
| Phobos | 200 m |
| Deimos | 100 m |
| Didymos | 50 m |
| Dimorphos | 15 m |
| no body (local frames) | 100 m |

The outline's own height differences are always included on top, so an outline drawn across a
slope or on a vertical rock face is filled completely whatever the reach.

When to change it:

- **parts of the terrain inside the outline stay uncoloured** (a tall boulder, a deep pit):
  increase the reach.
- **terrain that is not part of the mapped area gets coloured** - the far side of a ridge
  behind the outline, the back of a boulder, the far side of a small body: decrease it.

The direction of the reach is perpendicular to the outline (its best-fit plane): "up" for an
outline on the ground, "out of the face" for an outline drawn on a rock wall.

## Outlines drawn with Linear projection

A *Linear* outline is a set of straight lines between the points you clicked: it runs above
valleys and through bumps instead of along the ground. The fill lies on the ground, so seen
from the side its edge and the straight outline drift apart - most where the terrain inside
the outline is high or rough. Seen from above they coincide. Outlines drawn with *Sky* or
*Viewpoint* projection follow the terrain and match the fill closely.

## Performance

All fills are drawn together, so many of them are cheap: ten thousand filled polygons cost a
fraction of a millisecond per frame.

## Limits

- Where two translucent fills overlap on the terrain, the overlap shows a mix of both colours
  (an average weighted by their opacities), not one laid over the other.
- In the 2D map view fills are drawn flat; there is no terrain to drape onto.
- Snapshots (PRo3D.Snapshots) use a slower fill renderer that gives the same picture.
- Screenshots requested through the server screenshot API render the surfaces at the requested
  size and sample count, so any resolution or aspect ratio works. Very large screenshots need
  correspondingly more GPU memory while they are taken.


How the fills are drawn, step by step: [docs/dev/AnnotationFill-technique.md](dev/AnnotationFill-technique.md). Implementation notes: [ai/RENDERING.md](../ai/RENDERING.md#annotation-fills--against-the-surface-depth-841).
