# Annotation Toolbar (Draw Annotation)

While **Draw Annotation** is the active interaction, the second toolbar row shows the
parameters for the next annotation. They are built by `viewAnnotationToolsHorizontal`
in [Drawing.UI.fs](../src/PRo3D.Core/Drawing/Drawing.UI.fs).

| Group | Controls | Always shown? |
|---|---|---|
| **Annotation:** | geometry dropdown, projection dropdown, thickness | yes |
| **Sampling:** | sampling amount, sampling unit | only for viewpoint / sky projection |
| **Fill/Alpha:** | fill-new-annotations toggle, default fill alpha | only for closed (fillable) geometries |

## Conditional visibility

The row is an `alist` fed into `Incremental.tr`; the two optional groups are appended
only when they are relevant, so an irrelevant control never takes up space or invites a
change that would be ignored.

- **Sampling** (`projectionUsesSampling`) — the sampling amount and unit only drive the
  extra ray casting done for `Projection.Viewpoint` and `Projection.Sky`. For
  `Projection.Linear` and `Projection.Bookmark` there is no sampling, so the group is
  hidden.
- **Fill/Alpha** (`geometryIsFillable`) — fill only applies to closed geometries whose
  interior can be triangulated: `Polygon`, `Ellipse`, `AxisEllipse`, `Axis4PEllipse`.
  This mirrors `PackedRendering.isFillable`. For points, lines, polylines and `DnS` the
  group is hidden (`DnS` shows its fitted plane through `showDns`, not a fill).

Both predicates live next to the view code. If `isFillable` or the set of
sampling-driven projections changes, update the predicate here to match.

## Related

- [EllipseAnnotations.md](EllipseAnnotations.md) — ellipse tools force `Projection.Sky`
  and grey out the projection dropdown; with Sky selected the Sampling group is visible.
- [ToolStrip.md](ToolStrip.md) — the tool rail and how the second toolbar row is hosted.
