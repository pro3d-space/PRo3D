# Boolean operations on annotations

Union (merge) and cutting of annotations in the viewer. Design in
`plans/viewerIntegration.md`; the underlying geometry is documented in
`plans/booleanOperations.md` and exercised interactively in the [Geometry Lab](GeometryLab.md).

## Union

Select two or more closed annotations (shift+click while ctrl-picking in *PickAnnotation* mode,
or via the annotations tree), then press the **union** icon (object-group symbol) in the
annotations panel header, next to the recalculate icon.

The selected annotations are replaced by their union:

- **One annotation per component.** Overlapping shapes merge into one; disjoint shapes produce
  one annotation each.
- **Holes are refused.** If the union would enclose a gap (e.g. a U-shape capped by a bar), the
  operation is refused with a log message and nothing changes — a single-ring annotation cannot
  represent a hole.
- **Metadata comes from the first operand in tree order**: colour, thickness, fill, semantic,
  text and projection are copied from it; the result is always a `Polygon`; measurements are
  recomputed, never copied. The result lands in the active group.
- **One undo step** restores all originals.
- **Self-intersection artifacts are dropped.** An outline that folds over itself in projection
  (a hand-drawn spike) resolves into tiny extra contours; every legitimate union component
  contains at least one whole operand, so components smaller than the smallest operand — and
  micro-holes from the same folds — are recognised as artifacts and removed rather than turned
  into absurd sliver annotations.

## Cut

Select exactly one closed annotation, switch the interaction mode to **CutAnnotation**, and
Ctrl+click a polyline stroke on the terrain across the annotation. The stroke colours itself by
a live dry-run: **green** when it would cut, **red** when it would not (a cutting stroke needs
both *ends* outside the annotation — interior points may dip inside, so the cut can follow a
terrain feature instead of a straight chord). Keys, matching annotation drawing:

- **Enter** applies the cut: the annotation is replaced by one polygon per piece, metadata
  copied to each, measurements recomputed. One undo step restores the original.
- **Backspace** removes the last stroke point; **Escape** discards the stroke.
- A refused cut (red stroke applied anyway, or no selection) logs the reason and keeps the
  stroke so it can be corrected.

## How the geometry works

The union is computed in a 2D chart: a plane fitted over all operands' points. Ring vertices
that survive the union keep their exact world position (terrain projection included) through
the region attribute channel; vertices invented where outlines cross are landed on the surface
with the same sky-direction raycast the drawing pipeline uses (falling back to the edge chord
where no surface is hit). An annotation whose points the shared chart cannot cover refuses the
operation with a message.

Implementation: `PRo3D.Base.Geometry.AnnotationRegionOps` (pure, fully unit-tested), driven by
`DrawingAction.UnionSelectedAnnotations`; message-level tests in
`src/Tests/Features/Section20_BooleanOperations.fs`.
