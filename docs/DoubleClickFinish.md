# Double-click to finish

A double-click ends an annotation the same way **Enter** does (#824):

| Tool | Double-click |
|---|---|
| Polyline, Polygon, Dip & Strike | places its point and finishes the annotation (the polygon closes) |
| Cut (`CutAnnotation`) | places its point and applies the cut stroke |
| Point, Line, True Thickness, the ellipses | nothing extra — these finish themselves after a fixed number of points |

Enter, Backspace and Escape are unchanged. The double-click is gated like a click:
the tool has to be armed (Ctrl held, or not held in [Direct Tool Mode](DirectToolMode.md)),
and it only acts in the main view.

**Too few points:** a Polyline needs 2, a Polygon or Dip & Strike 3. A double-click
before that just places its point and drawing continues.

**Switch it off:** main menu → *Preferences* → *Drawing* → *Double-click finishes
annotation / cut*. It is a per-computer preference, like the MapView WASD inverts:
stored in `%APPDATA%/Pro3D/userPreferences.json` as `disableDoubleClickFinish`, never in
a scene, so loading someone else's scene does not change it. The field is negated on
purpose - Newtonsoft fills a field missing from an older file with `false`, which then
reads as "on", the default; older releases ignore the extra field. With it off, the hint
line only offers ENTER.

## How it works

The browser delivers a double-click as `click, click, dblclick`. Each click is a pick as
usual. If the mouse did not move at all, the second click repeats the first click's ray,
and `PickSurface` ignores a repeated ray (`lastHash`) - no duplicate. If it moved a pixel
or two, which the OS still accepts as a double-click, the second click puts a point right
next to the first. Finishing therefore drops that near-duplicate first:

1. The render control's `ondblclick` sends `ViewerAction.DoubleClickFinish` with the
   control's size in CSS pixels. The browser sends the sizes as bare integers; they are
   parsed with `Int32.TryParse`, because `Pickler.json` rejects them and an event
   callback that throws drops its message without a trace.
2. `ViewerApp.updateViewer` checks the preference, `toolArmed` and the interaction,
   then builds the "same spot" test `coincideOnScreen`: two world points coincide when
   they project within `doubleClickTolerancePx` (6 px) of each other in the main view.
   The projection runs on the CPU in double precision. An unknown size (< 2 px) never
   matches, so a real point is never dropped on a guess.
3. `DrawingApp` handles `FinishOnDoubleClick` / `ApplyCutStrokeOnDoubleClick`:
   `dropCoincidentLastPoint` removes the last point (and its segment, for projected
   annotations) only if it coincides with the one before it, then the normal
   `finish` / `ApplyCutStroke` runs. If the second click missed the surface there is no
   duplicate, and nothing is dropped.

Segments are sampled onto the surface synchronously during each click (#822), so
nothing is still being computed when the double-click finishes. Undo takes back a
double-click-finished annotation in one step, like Enter. Provenance records it like
any other finish.

## Tests

- `src/Tests/Features/DoubleClickFinishTests.fs` — dropping the duplicate, a second click
  that missed, too few points, projected segments, undo, fixed-count geometries, cut.
- `tests-ui/tests/double-click-finish.spec.ts` — real double-clicks in the running
  viewer on the Dimorphos OPC.
