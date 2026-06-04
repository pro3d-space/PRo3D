# Branch fixes — `features/crossSectoins` (2026-06-03 → 2026-06-04)

Summary of the fixes made on this branch since (and including) 2026-06-03. Each
entry lists the symptom, the root cause, the fix, the files touched, and the
commit. Upstream follow-ups are collected at the end.

---

## 1. Traverse sol-label "Fast Text" toggle
**Commit:** `4ea52f74` (2026-06-03)

**Problem.** Traverse sol-number labels are numerically unstable (jitter) at
planet scale. They are rendered with a fast batched billboard pass
(`Sg.textsWithConfig`) that applies a world-space (~3.4 M m) trafo in float in
the shader. A numerically stable per-label path (`PRo3D.Base.Sg.text`, which
renders through the camera-relative stable-trafo shader) existed as `drawSolText`
but was dead code.

**Fix.** Added a per-traverse `fastText : bool` flag (default `true` = fast) with
a **"Fast Text"** checkbox in the traverse properties. `viewTextForTraverse` now
switches reactively: `true` → `drawSolTextsFast`, `false` → `drawSolText` (stable).
JSON is backward compatible (`Json.tryRead`, default `true`); adaptify regenerated.

**Files.** `Traverse-Model.fs`, `Traverse-Model.g.fs`, `TraverseApp.fs`.

---

## 2. Surface distance filter — make it work, then move to stable view space
**Commits:** `c41c3c84` ("quickfix for Thomas", root-cause fix) → `08f685f3`
(2026-06-03, view-space form)

**Problem.** With the per-surface DistanceFilter enabled, the whole surface
clipped away, and raising `FilterDistance` to its maximum changed nothing.

**Root cause.** The `FilterDistance` uniform was declared/uploaded as a `double`
(F# `float`) but read back as a 32-bit float in the geometry shader, so it landed
as ~0. Every triangle then failed `distance < filterRange` and was discarded.
(It is *not* a general double→float problem — every other scalar in that shader is
inferred as `float32`; `FilterDistance` was the lone one explicitly typed `float`.)

**Fix (root cause).** Convert on the CPU (`AVal.map float32`) and read as `float32`
in the shader.

**Fix (form).** The filter had earlier been refactored to a `wp` world-space
comparison as a workaround; once the uniform bug was fixed, it was moved back to
the original **stable view-space** check: the home position is transformed into
view space on the CPU and uploaded as a clean `V3f`, and the per-vertex range test
runs against `vp`. The `[<WorldPosition>] wp` vertex attribute was removed again.

**Files.** `Viewer-Utils.fs` (`createSg` uniform upload, `Shader.Vertex`,
`stableTrafo`, `triangleSizeFilter`).

---

## 3. Sequenced bookmarks — NRE from null camera view on coincident segments
**Commit:** `44f55c73` (2026-06-04)

**Problem.** Playing a bookmark sequence could crash with a
`NullReferenceException` because `navigation.camera.view` became null.

**Root cause.** `Aardvark.UI` `Animation.Camera.interpolate` derives its duration
from the camera path, so two **coincident** views collapse to a zero-duration
animation. `Animation.seconds` then cannot rescale it
(*"Cannot scale composite animation with zero duration"*) and it samples to
`Unchecked.defaultof<CameraView>` = **null**, which is written straight into the
navigation model via the `setModel_`/`_bookmark` lens. Two triggers:
- the **pause/delay** branch interpolates `src → src` (identical by construction)
  whenever a bookmark has `delay > 0`;
- two **consecutive bookmarks with the same camera location**.

This regressed when **smooth path was disabled** (`#261`, `48c9f31a`,
rnowak 2023-06-20): the smooth path dropped coincident control points
(`ifSameRemove`); the pairwise `pathWithPausing` path that replaced it does not.

**Fix.** `cameraInterpolateSafe` — a wrapper that returns a unit-duration constant
hold for coincident endpoints instead of the broken zero-duration animation; both
`interpolateBm` call sites use it. Plus a defensive guard in `ViewerLenses` so a
null `cameraView` can never reach `navigation.camera.view` (logs the offending
bookmark).

**Files.** `BookmarkAnimations.fs`, `ViewerLenses.fs`.

---

## 4. Remove leftover "computed hvov" debug logging
**Commit:** `ed2aaebb` (2026-06-04)

**Problem.** Console flooded with `computed hvov: …` during bookmark/focal
animation.

**Root cause.** `FrustumUtils.calculateFrustum`/`calculateFrustum'` logged the
computed hfov on every call; `interpVcm` recomputes the frustum from the
interpolated focal length **every animation frame**. Debug prints introduced in
the hera3d merge (`b8b87a8d`, brunn 2026-01-07).

**Fix.** Removed both `Log.line` lines.

**Files.** `Utilities.fs`.

---

## 5. Coordinate system re-placed while navigating with arcball
**Status:** fix applied, pending commit (2026-06-04)

**Problem.** Place a coordinate system (F4), then move the camera with arcball —
the coordinate system is placed again. (Affects all pick/place interactions, not
just coordinate systems.)

**Root cause.** `c07773ab` ("Fix camera change mode", sudokuMonaco 2026-01-19)
removed the third match element `(m.ctrlFlag <> m.inverseFlag)` from the whole
`update` match. The committing `PickSurface (p,name,true)` arm previously matched
**only** when that element was `true` (picking mode); without it, a surface pick
is processed in **every** mode, so an arcball/freefly mouse-down runs
`matchPickingInteraction` and re-triggers the active interaction. Navigation
gating was moved into `renderControlAtts` (`inverseFlag = ctrlFlag`), but the
equivalent picking guard was never re-added.

**Fix (landed via PR #608).** This was independently rediscovered here, but the same
bug was already fixed upstream by Sophie Pichler — `14583377` "Only spawn picking
when necessary" (PR #608, merged 2026-04-20; Sophie is also the author of the
`c07773ab` regression). Her fix gates **only the Click handler** inline, in
`Viewer-Utils.fs`:

```fsharp
let surfacePickingActivated = (m.ctrlFlag |> AVal.force) <> (m.inverseFlag |> AVal.force)
if surfacePicking && surfacePickingActivated then true, [PickSurface (sceneHit, name, true)]
else true, []
```

It mirrors how `c07773ab` gated navigation at emission — picking is now gated the
same way, at the spawn — and it covers every interaction routed through
`matchPickingInteraction` at once. Because it gates only the click (not the shared
`surfacePicking`), the hover preview cursor **stays visible during navigation**.

We dropped our own redundant fix (which had gated the shared `surfacePicking`, also
hiding the preview during navigation) in favour of #608 during the branch
integration. See `docs/story-picking-during-navigation.md` for the full story.

**Files.** `Viewer-Utils.fs` (upstream #608).

**Other arms collapsed by `c07773ab`** (assessed, no action taken): `SetCamera`,
`SetCameraAndFrustum`, `SetCameraAndFrustum2`, `HeightValidation` (were nav-only,
now run in any mode — benign / arguably more correct) and `NavigationMessage`
(compensated, since nav attributes are only emitted in nav mode). The picking-gate
pattern at `Viewer.fs:561` (`DrawLog` annotation pick) still has its guard and was
not affected.

---

## Upstream follow-ups (file against the relevant repos)

- **aardvark.media / aardvark.ui:** zero-length camera interpolation. Both
  `Animation.Camera.interpolate` (identical endpoints) and a static
  `Animation.create (fun _ -> view)` produce a zero-duration animation that
  `Animation.seconds` cannot scale and that samples to a null `CameraView`. A
  zero-length / zero-time interpolation should degrade to a constant hold, not
  null. (`cameraInterpolateSafe` is a local workaround to be dropped once fixed.)
- **PRo3D `develop`:** the picking-mode guard is already on `develop` via PR #608
  (`14583377`), which this branch picked up through the `develop` merge. No action
  needed there.
