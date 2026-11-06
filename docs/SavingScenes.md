# Saving scenes

A save must never lose work and must never fail because of a single object (#834).

## What gets written

Saving `<dir>/<name>.pro3d` writes:

| File | Content |
|---|---|
| `<name>.pro3d` | the scene: surfaces, views, reference system, configuration, GIS, view plans, … |
| `<name>.pro3d.ann` | annotations, the dip & strike legend, colour by category, geological maps |
| `<name>/` | one file per sequenced bookmark |
| `<name>.pro3d.layout` | the window layout (see [WindowLayouts.md](WindowLayouts.md)) |

If the name has no `.pro3d` extension (the Linux save dialog returns the name exactly as
typed), `.pro3d` is appended. Without it the bookmark folder `<dir>/<name>` would be the
scene path itself.

## How a save stays safe

1. **Serialize first, write later.** Everything is turned into JSON in memory. Only then
   are files touched. If that fails, nothing on disk changes, the model stays as it was, and
   the message `SAVE FAILED, nothing was written: …` stays on screen for 20 s.
2. **One bad object does not stop the rest.** If a surface, view, annotation or sequenced
   bookmark cannot be serialized, it is left out of the file and everything else is saved.
   The object stays in the running session, so nothing is lost before the next save. The
   message `Scene saved, but not completely: …` names what was left out, and the log
   has the details.
3. **No half-written files.** Each file is written to `<file>.saving` and then moved over
   the old one. A failing write (disk full, permissions, crash) leaves the previous file
   intact.

## Numbers that are not numbers

The JSON library stores numbers as decimals. It cannot write `NaN`, `±Infinity`, or anything
above about 7.9·10²⁸, and used to abort the whole save when it met one. Now:

- **Values that may be undefined** (measurements, statistics, bearings, thicknesses) are
  written as `null`, which every release reads back as "undefined".
- **Values that older releases read strictly** (camera frustum, near/far planes, focal
  lengths, bookmark timing, instrument intrinsics, transfer-function ranges, GIS radii) get
  a finite fallback and a log warning. A `null` there would make the file unloadable in
  older releases.
- **A dip & strike with undefined angles** is saved as "no dip & strike".
- Vectors, planes and transformations are written as text and handle `NaN` in every release.

This is only a safety net. The places that produced such values are fixed so they no longer
do:

- **Manual dip angle/azimuth on annotations other than TT.** These start out undefined.
  Setting only one of them, or scrolling over the field, produced an undefined dip & strike.
  Union and cut results carried these values over.
- **A manual dip of exactly 90°**, and the vertical thickness of a vertical bed.
- **Dip & strike of an exactly horizontal plane.** It now has dip 0 and strikes north.
- **Reference-system up/north passing through (0,0,0)** while being edited component by
  component.
- **A hidden or collapsed view (size 0)**, which corrupted the saved camera frustum.
- **GIS entity radius / trajectory length.** The input evaluates what is typed, so `1e400`
  or `1/0` got through.
- **OPCX channel ranges** given as `NaN` / `Infinity`.
- **Remote API surface transforms** that are not finite or not invertible. These are now
  rejected with `400 Bad Request`.
- **Switching to FreeFly from a top-down view**, which produced a camera without a direction.

The automatic GeoJSON export no longer fails the drawing action that triggers it. An
annotation it cannot export is left out and logged.

Tests: `src/Tests/SaveRobustnessTests.fs`.
