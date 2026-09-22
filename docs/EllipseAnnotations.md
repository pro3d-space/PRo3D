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

## Needs a reference body

The outline is sampled on the fitted plane and then **draped onto the surface with the Sky
projection**, which shoots its rays along the scene's up vector. With `Planet.None` there
is no such vector: the drape returns too few points, `getFinishedAnnotation` discards the
annotation with a warning, and before it ever gets that far the geometry dropdown greys the
ellipse entries out (`needs a reference body`, `Geometry.needsReferenceBody`).

This is worth knowing when reading older material: an ellipse drawn on a body-less scene
came out as a lumpy blob rather than an ellipse, which is what the user manual's figure used
to show. Set the scene body ([SceneBody.md](SceneBody.md)) and the outline is a real ellipse draped over
the terrain.

## While drawing, there is almost no feedback

Ellipses generate no segments while they are being picked (`allowSegmentGeneration` in
`DrawingApp.addPoint`), so the working annotation has no polyline to draw: until the third
click lands, the render view shows only the picked points, as dots of `thickness * 1.5`
pixels (`Sg.drawWorkingAnnotation`). At the default thickness that is a 4-5 px speck per
pick and no line along the major axis being defined. Older PRo3D versions drew that axis,
which is why older screenshots show one.
