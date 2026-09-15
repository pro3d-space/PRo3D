# Ellipse Annotations

PRo3D has two ellipse annotation tools, selectable from the geometry dropdown in the
annotation toolbar:

| Tool | Clicks | Construction |
|---|---|---|
| `AxisEllipse` | 3 | two points define the main axis, the third the radius orthogonal to it |
| `Axis4PEllipse` | 4 | two points define the main axis, two more give a radius on each side of it |

Both are fitted in the plane through their picked points and then sampled into an
outline, which is what gets stored as the annotation's points.

## The projection is fixed for ellipses

Unlike the other annotation tools, ellipses are only meaningful under the **sky**
projection. This is one instance of a general per-geometry rule,
`Geometry.allowedProjections` in [Annotation-Model.fs](../src/PRo3D.Base/Annotation/Annotation-Model.fs):
the ellipse tools allow `[Sky]` only, every other tool allows all four projections.

That rule is enforced in three places (see [AnnotationToolbar.md](AnnotationToolbar.md)):

- `SetGeometry` keeps the current projection if the new geometry allows it, else falls
  back to the head of `allowedProjections` — `Sky` for ellipses.
- `SetProjection` ignores a projection the current geometry does not allow.
- The projection dropdown in `viewAnnotationToolsHorizontal`
  ([Drawing.UI.fs](../src/PRo3D.Core/Drawing/Drawing.UI.fs)) **greys out** the
  disallowed entries via the shared `dropDownDisabled` helper — the same one that greys
  out `MapView` in the navigation-mode dropdown when no planet is set. The entries keep
  their plain names; a hover tooltip carries the reason (`disabledNote`).

Greying the options out is deliberate rather than hiding them or disabling the whole
`<select>`: a disabled `<option>` is unselectable but still readable, so the user can see
which projections exist. The label is left as the bare name so the disabled note cannot
stretch the `<select>` wider than the same dropdown under a non-ellipse geometry.

## Known limitation

`Axis4PEllipse` is currently not fitted. `getFinishedAnnotation` takes the plane-based
branch (`let geo = false`), and `EllipseAnnotation.constructAndSampleFromPlane` matches
only the three-point case, returning `None` for four points — so a four-point ellipse
keeps its four raw picks and no outline is sampled. The disabled geographical path
(`constructAndSampleGeographical`) does handle four points. The corresponding test,
`TC-3.7` in `src/Tests/Features/Section03_DrawingAnnotations.fs`, is skipped for this
reason.
