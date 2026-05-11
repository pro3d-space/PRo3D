# CooTransformation error handling — continuation notes

## Problem observed
While flying the camera in PRo3D, the log spams `cootrafo errorcode -3` and the
info-GUI (status bar top-left) shows bogus coordinates (lat=0, lon=360, alt=0)
as if the conversion succeeded.

## Root cause
In `src/PRo3D.Base/CooTransformation.fs`:

- The wrappers initialise out-params to `0.0` (`let private init = 0.0`).
- On native-library failure, the native DLL does not overwrite them, so they
  stay `0.0`.
- The wrappers then return a fully-populated `SphericalCoo`/`V3d` with those
  zeros — no way for the caller to know the conversion failed.
- `ViewerGUI.fs:141` checks `x.longitude.IsNaN()` — never true, so the GUI
  happily prints `0 deg` / `360 - 0 deg` / `0.00 m`.
- Log line fires every frame (no throttle).

## Related finding: Sky projection
Sky mode (`Viewer.fs:1188`) routes through `CooTransformation.getUpVector`,
which calls `getLatLonAlt` + `getXYZFromLatLonAlt`. On Dimorphos (or any body
the native lib cannot convert), the up-vector becomes NaN and sky-ray picking
is silently broken. **Profile extraction is NOT affected** — it takes `up`
from `referenceSystem.up.value.Normalized` (Viewer.fs:574), no cootrafo call.

## Plan (agreed direction)
Minimal, no logging throttle machinery. In `CooTransformation.fs`:

1. Add `tryGet*` variants returning `Option`:
   - `tryGetLatLonAltPlanet`, `tryGetLatLonAlt`, `tryGetLatLonRad`
   - `tryGetXYZFromLatLonAltPlanet`, `tryGetXYZFromLatLonAlt`, `tryGetXYZFromLatLonAlt'`
   - `tryGetAltitude` (convenience for GUI; mirrors `getAltitude` shape)
2. Mark the existing 6 direct wrappers `[<System.Obsolete>]`. They stay as
   thin forwards to the `tryGet*` variants with `Option.defaultValue nan`/`V3d.NaN`
   so existing callers keep compiling.
3. Update the internal helpers (`getHeight`, `getAltitude`, `getElevation'`,
   `getUpVector`) to use `tryGet*` directly — avoids obsolete warnings inside
   this module. External signatures unchanged, behaviour unchanged (NaN on
   failure).
4. **GUI migration (only GUI sites, not the rest):**
   - `src/PRo3D.Viewer/Viewer/ViewerGUI.fs` around line 141: switch `spericalc`
     and `altitude` to the `tryGet*` variants. On `None`, display
     `"conversion failed"` (user's wording) instead of current `"not available"`.
     Also add `IsNaN`/`None` guard for the altitude row (currently would
     render `"NaN m"`).
   - `src/PRo3D.Core/ReferenceSystem.fs` around line 154: same treatment for
     the reference-system panel's lat/lon/alt display.

**Explicitly out of scope for now:**
- Do NOT ripple the Option API through Bookmarks, RemoteApi, Drawing-App,
  EllipseAnnotation, Traverse/*, GeoJSON.Export — those keep calling the
  obsolete variants; the compiler warning is the signal to migrate later.
- Do NOT touch sky-projection fix (`Viewer.fs:1188`). It is currently broken
  on bodies the native lib cannot convert — separate issue.
- No log throttling. (Earlier attempt with `HashSet`/`lock` was rejected.)

## Files to edit when resuming
- `src/PRo3D.Base/CooTransformation.fs` — add tryGet variants, add Obsolete,
  rewrite internal helpers.
- `src/PRo3D.Viewer/Viewer/ViewerGUI.fs` lines ~139–165 — use tryGet variants,
  display "conversion failed" on `None`.
- `src/PRo3D.Core/ReferenceSystem.fs` line ~154 — same pattern.

## Current state of the branch (other in-flight work)
The `features/profileDataExtraction` branch also has unrelated in-flight
changes for profile attribute extraction. Recent in-session edits:

- `src/Tests/ProfileAttributeExtractionTest.fs`
  - End-to-end test now builds `ProfileSample` records and calls
    `ProfileAttributeExtraction.writeCsv` (same helper as the GUI's
    `Viewer.fs:581`). CSV output goes to
    `TestUtils.outputDir parameters "ProfileExtraction" / "test_multi_attr_profile.csv"`.
  - Added Stopwatch-based timing for: load annotation, load hierarchies,
    load kdtrees, build `patchInfoLookup`, per-point loop (`intersectAllKdTrees`,
    `buildTriangleToGridMapping`, `getUVAtHit`, `extractAttributesAtUV`),
    and `writeCsv`. Logs memory before/after each step.
  - `intersectAllKdTrees` now accepts a caller-owned cache
    (`ref<HashMap<string, ConcreteKdIntersectionTree>>`) so repeated calls
    across points reuse loaded object-sets. Cache size logged after the loop.

- `src/PRo3D.Base/PatchOverrides.fs` — `Patch.tryExtractTexturePath` now
  returns `(path, attributeName)` where `attributeName = Path.GetDirectoryName(fn)`
  (the attribute-folder containing per-patch textures), not the full filename.
  Fixes the dictionary-key-per-patch bug in attribute aggregation.

### Open code-duplication item (not yet acted on)
`intersectAllKdTrees` is duplicated in:
- `src/Tests/ProfileAttributeExtractionTest.fs:40–63`
- `src/OpcViewer/MultiTexturingViewer.fs:325–345` (experimental, has its
  own shadowed copies of `computeBarycentric`, `buildTriangleToGridMapping`,
  `getUVAtHit`, `extractAttributesAtUV`, `extractAttributesFromHit`).

Production has `SurfaceIntersection.doKdTreeIntersection` in `Surface.fs:202`
but at a different level (SurfaceModel + trafos + priority groups). No
existing SurfaceModel-free "iterate all bboxes + nearest hit" helper in core.

User asked whether `intersectAllKdTrees` should move to core. Answer pending:
option (1) move into `ProfileAttributeExtraction` (least churn, dedupes test
+ MultiTexturingViewer), option (2) refactor `doKdTreeIntersection` to layer
on top (larger, risks behaviour drift in picking).
