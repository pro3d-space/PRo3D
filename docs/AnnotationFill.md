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

## Limits

- Where two translucent fills overlap, the overlap shows one of the two colours, not a blend of
  both.
- In the 2D map view fills are drawn flat; there is no terrain to drape onto.

Implementation notes for developers: [ai/RENDERING.md](../ai/RENDERING.md#annotation-fills--stencil-volumes-841).
