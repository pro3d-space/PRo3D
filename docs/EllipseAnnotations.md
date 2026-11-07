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

## Editing an ellipse

A finished `AxisEllipse` can be reshaped with **Edit Annotation**, the vertex-editing mode
([AnnotationVertexEditing.md](AnnotationVertexEditing.md)). Select the ellipse and five handles
appear: at the ends of its two axes and at its centre. Ctrl+click one (Cmd+click on macOS) to pick
it up and Ctrl+click on the surface to drop it:

| Handle | What the drop does |
|---|---|
| a major-axis end | the opposite end stays put; the axis turns and stretches to the drop. The minor length is kept. |
| a minor-axis end | the major axis stays put; the minor length becomes the drop's distance from it. |
| the centre | the whole ellipse moves to the drop, axis lengths and axis ratio kept, and tilts to fit the terrain there. |

**Telling the axes apart.** The major-axis ends are drawn **magenta** and the minor-axis ends
**cyan**, and a thin guide line in the same colour runs along each axis through the ellipse, so
it is plain which handle does what before anything is picked up. The hint line next to the tool
settings says it in words, with the current full axis lengths ("Ctrl+click an axis end to move
it: magenta = major axis (120.32 m), cyan = minor axis (70.00 m)"), and while a handle is in
hand it says what the drop will do to which axis. Hovered handles turn yellow and the grabbed
one green, as for polylines. The colours are `PackedRendering.EllipseEditColors`, chosen away
from the handle palette and from red and orange, the usual annotation colours. The centre
handle is white, where the two guides cross.

While an axis end is in hand, a green line runs from the opposite end of the same axis to the
cursor. While the centre is in hand, a green copy of the outline follows the cursor, so it can be
lined up with a boulder before it is dropped.
The guides, that line and the handles are drawn over the terrain (no depth test), in passes after
the main one (`ellipseAxisGuidePass`, then `vertexHandlePass`): the fitted plane and a straight
line between surface points both run partly under the surface.
On drop the outline is re-sampled and draped, and the stored shape (centre, semi-axes, long-axis
azimuth), dip and strike and the measurements are recomputed. One drop is one undo entry. A minor
end dragged out past the major length makes that axis the major one; the handles follow.

**The plane is not re-fitted.** An ellipse is constructed in the plane fitted through its three
clicks. Editing keeps that plane: it is recovered from the stored shape (the plane through
`center` spanned by `semiMajorAxis` and `semiMinorAxis`), the drop is projected
orthogonally onto it, and the ellipse is rebuilt there with the same construction
(`EllipseConstruction.constructEllipseOrtho2d`) the tool uses while drawing. Re-fitting a plane
through surface points on every edit would drift the ellipse a little each time; this way an
ellipse only changes by what was moved. The cost is that an edit cannot tilt the ellipse: an
ellipse whose plane is wrong has to be redrawn.

**Moving fits the plane to the new place.** The axis ends keep the plane - they are for fine
tuning, which must not tilt anything. A move is different: the ellipse is on another boulder
now, so it gets that boulder's orientation, as if drawn there. Dropping the centre first shifts
the ellipse within its old plane by as much as the handle was dragged (both ends projected onto
the plane). Its three construction points - the two major ends and the minor end, standing in
for the clicks of the tool - are then stamped onto the terrain with the Sky projection, and the
plane is fitted through them as the tool fits one through its clicks
(`EllipseEdit.tryRefit`). Unlike a redraw the ellipse keeps its size: the axis lengths and the
axis ratio are kept exactly, the major axis is turned only as far as the new tilt needs (its
old direction projected onto the new plane), and the outline is stamped onto the terrain again.
The new centre is where the sky ray through the moved centre meets the new plane, so on a slope
it stays under the cursor.
Centre, dip and strike, the long-axis azimuth, area and the other measurements are those of the
new place.

The green preview is the shift within the old plane, before the re-fit and the stamping, so
over bumps it can float or dip a little where the dropped outline will not. A centre dropped
less than 1 % of the minor semi-axis from where it was is no move at all - no re-fit, no undo
entry - so picking it up and putting it down again does not tilt the ellipse. A move that cannot
be made (its axis ends would miss the surface, the terrain there gives no plane) is refused with
a notice saying why, like an axis collapse.

The axis-end handles are drawn at the outline point nearest each axis end, so they sit on the
terrain where the outline is, not on the plane, which may run under or above it. The centre
handle is the middle of the draped outline (the mean of its points), not the plane's centre: the
plane can be metres off the terrain the outline is stamped onto - after an end was dragged onto
higher ground, say - and a handle there showed up far from its ellipse in an oblique view. The drop is draped with
the Sky projection whatever the drawing toolbar is set to.

A drop that would collapse an axis (a minor end dropped onto the major axis, a major end onto
the fixed end), or an outline that can no longer be draped, is refused: the ellipse stays as it
was and no undo entry is pushed.

Not editable: `Axis4PEllipse` (see below), ellipses imported from SBMT (`Projection.Linear`,
not draped), and ellipses saved before v6.3.4, which do not carry the stored shape
(`"ellipseShape"`). Nothing new is persisted, so an edited ellipse loads in older releases
like any other.

**Supporting profiles follow.** An ellipse with supporting profiles
([SupportingProfiles.md](SupportingProfiles.md)) takes them along on every edit, moved or
reshaped: they are laid across the edited ellipse again, in the directions they had (a compass
set stays compass, an axis set follows the axes, whatever the Supporting Profiles panel is set
to now). Each keeps its key, group, colour, thickness and label, so the Craters export still
links them; a profile reshaped by hand is replaced. They are sampled with the toolbar's current
sampling distance, as when generated. Ellipse and profiles are one undo step. If one profile
cannot be rebuilt, the edit is refused - the ellipse and its profiles stay as they were - and a
short notice says why (`DrawingApp.regenerateEllipseProfiles`).

The code is `EllipseEdit` in [Annotation-Model.fs](../src/PRo3D.Base/Annotation/Annotation-Model.fs)
(plane, handles, handle moves, `CentreHandle`, `tryMovedOutline` for the preview) and
`DrawingApp.moveEllipseHandle` / `DrawingApp.editHandles` /
`DrawingApp.regenerateEllipseProfiles` in [Drawing-App.fs](../src/PRo3D.Core/Drawing/Drawing-App.fs).

The geometry of an edit is tested in `src/Tests/VertexEditingTests.fs` (`editing a
three-point ellipse`): which end stays, which length is kept, ten drops in place without drift,
undo, refused drops; `moving a three-point ellipse` covers the centre: the move in the plane,
ten drops in place, undo, and the profiles that follow it or refuse it.
`tests-ui/tests/ellipse-editing.spec.ts` does it in the viewer on the
Dimorphos OPC: it finds the four handles in the render, picks a major end up (it must turn
green), pulls it out by 35 % and reads the outline back with the same ellipse fit as the drawing
spec (`tests-ui/src/ellipse-fit.ts`): major axis longer by about that much, minor width kept,
opposite end in place. Then Ctrl+Z, and the same for a minor end. It also checks the colours:
the handles on the picked major ends are magenta, the other two cyan, and a guide of each colour
runs between its pair. Last it generates compass profiles, drags the white centre handle and
checks that the outline and the profiles moved by about as much, at the same size.

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

## Test

`TC-3.6` in `src/Tests/Features/Section03_DrawingAnnotations.fs` covers the model side
against a synthetic surface sampler: geometry, `Projection.Sky`, and an outline of more
than three points.

What a unit test cannot see is the shape the *real* drape produces, so
`tests-ui/tests/ellipse-annotation.spec.ts` draws an AxisEllipse on the Dimorphos OPC and
reads the drawn curve back out of the render: it thins the coloured band to one point per
angular sector and least-squares fits `p·u² + q·v² = 1` in the curve's principal frame. A
correct drape sits on its own fitted ellipse to within a mean residual of 0.018; the
body-less figure described above scores 0.097, and its 95th percentile 0.251 against
0.041 — a 5x gap, so the check can be strict without being flaky. The spec also requires
the outline to be closed (no angular sector empty, which a drape that failed to reproject
part of the curve would leave behind) and 201 points long (`planeSampleNumber` + 1).

Its second case is the gate: on a scene left in J2000, where the planet stays `None`, the
ellipse entries must be disabled and carry "needs a reference body", while `Line` and
`Polyline` stay available.

Run it with `PRO3D_DOC_SHOTS=1` to also refresh the ellipse figure in the short user
manual.

## While drawing, there is almost no feedback

Ellipses generate no segments while they are being picked (`allowSegmentGeneration` in
`DrawingApp.addPoint`), so the working annotation has no polyline to draw: until the third
click lands, the render view shows only the picked points, as dots of `thickness * 1.5`
pixels (`Sg.drawWorkingAnnotation`). At the default thickness that is a 4-5 px speck per
pick and no line along the major axis being defined. Older PRo3D versions drew that axis,
which is why older screenshots show one.
