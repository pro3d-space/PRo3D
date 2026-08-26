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
projection. Picking either ellipse tool therefore forces `Projection.Sky`
(`SetGeometry` in `PRo3D.Core/Drawing/Drawing-App.fs`).

Forcing it once is not enough on its own: `SetProjection` will happily move the
projection off `Sky` afterwards, leaving an ellipse tool active under a projection its
construction does not handle. So while an ellipse tool is selected the projection
dropdown is **greyed out**, with each entry labelled `(fixed for ellipses)` and the same
note in its tooltip. Choosing any non-ellipse geometry releases the lock and resets the
projection to `Projection.Linear`.

The lock lives in `viewAnnotationToolsHorizontal` (`PRo3D.Core/Drawing/Drawing.UI.fs`).
It is a plain use of the shared `dropDownDisabled` helper — the same one that greys out
`MapView` in the navigation-mode dropdown when no planet is set — with the disabled set
rebuilt from `model.geometry`.

Greying the options out is deliberate rather than hiding them or disabling the whole
`<select>`: a disabled `<option>` is unselectable but still readable, so the user can see
which projections exist and why they are unavailable right now.

## Known limitation

`Axis4PEllipse` is currently not fitted. `getFinishedAnnotation` takes the plane-based
branch (`let geo = false`), and `EllipseAnnotation.constructAndSampleFromPlane` matches
only the three-point case, returning `None` for four points — so a four-point ellipse
keeps its four raw picks and no outline is sampled. The disabled geographical path
(`constructAndSampleGeographical`) does handle four points. The corresponding test,
`TC-3.7` in `src/Tests/Features/Section03_DrawingAnnotations.fs`, is skipped for this
reason.
