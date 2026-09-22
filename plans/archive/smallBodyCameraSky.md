# Small-body camera sky fix — avoid FreeFly gimbal lock on Dimorphos & co.

## Problem

When a Dimorphos (or other small-body) scene is loaded, the FreeFly controller can land in a gimbal-locked initial state: forward direction nearly antiparallel to sky. Symptom: camera roll is undefined or chaotic, view "feels stuck".

Root cause: PRo3D's camera-setup code uses `referenceSystem.up.value` as the `sky` argument to `CameraView.lookAt`. For Mars/Earth this is the local geographic up (radial outward at the system's origin point) and is perpendicular to typical near-surface viewing directions, so all is fine. For small bodies whose entire body fits inside the OPC bounding box, `bb.Max - bb.Center` ends up running near-parallel to that radial up — `CameraView.lookAt camLoc bbCenter radialUp` then degenerates the camera basis.

Four camera-setup sites have the same shape:

1. **Viewer.fs:96-98** — `lookAtBoundingBox`. Triggered when an OPC is imported on a fresh scene (`updateSceneWithNewSurface` → first import path).
2. **Viewer.fs:467-468** — `addFlyToSurfaceAnimation`, fallback branch (no stored `homePosition`). Triggered by the home button in the surface list (`SurfaceApp.fs:1248`) and the GIS app (`GisApp.fs:362`).
3. **Scene.fs:333-342** — `updateCameraUp`. Called from three places: after first import (`Viewer.fs:197`, before lookAtBoundingBox so it gets overridden); on `Interactions.PlaceCoordinateSystem` (`Viewer.fs:294`); and on planet change (`Viewer.fs:1462`).
4. **Navigation.fs:154-163** — `SetNavigationMode NavigationMode.FreeFly`. Triggered whenever the user switches into FreeFly from any other mode. The MapView branch zeroes `exploreCenter` to `V3d.OOO` and sets `orbitCenter = Some V3d.OOO`, so a subsequent MapView→FreeFly switch reframes with `forward = (V3d.OOO - cam.Location).normalized` — radial on a small body, collinear with `referenceSystem.up.value`.

All four pass `referenceSystem.up.value` as the sky. All four should pick a body-aware sky instead, otherwise the small-body fix is incomplete.

## Decision

Use **world Z (`V3d.OOI`)** as the sky reference for the four small bodies (Phobos, Deimos, Didymos, Dimorphos). Use `referenceSystem.up.value` for everything else.

Rationale:
- Matches the Aardvark FreeFly idiom: sky is meant to be a stable absolute reference, not a body-relative one.
- The four small bodies are the Hera-mission targets where typical OPC data spans the entire body — exactly the case where a radial reference-up degenerates with the viewing direction.
- Mars/Earth/Moon keep their existing local-up sky so non-equatorial Mars patches still render with horizon parallel to local terrain.

Out-of-scope variants considered and rejected:
- *Always V3d.OOI* — would tilt Mars views at non-equatorial latitudes; surprising regression.
- *Detect-and-substitute when forward ≈ sky* — masks the issue rather than committing to a clear per-body policy.

## Design

Centralise the policy in helpers so the rule lives in one spot and the four call sites collapse to one-liners.

```fsharp
// In src/PRo3D.Base/CooTransformation.fs (next to getConvention):
let isSmallBody (planet : Planet) =
    match planet with
    | Planet.Phobos | Planet.Deimos | Planet.Didymos | Planet.Dimorphos -> true
    | _ -> false

// In src/PRo3D.Core/ReferenceSystem-Model.fs, module ReferenceSystem:
let bodyAwareSky (planet : Planet) (referenceUp : V3d) : V3d =
    if CooTransformation.isSmallBody planet then V3d.OOI else referenceUp

let bodyAwareLookAt (referenceSystem : ReferenceSystem) (location : V3d) (target : V3d) : CameraView =
    let sky = bodyAwareSky referenceSystem.planet referenceSystem.up.value
    CameraView.lookAt location target sky
```

Helper home: `isSmallBody` is body knowledge — lives next to `getConvention` in `PRo3D.Base.CooTransformation`. The `bodyAwareSky` / `bodyAwareLookAt` pair lives in `PRo3D.Core.ReferenceSystem` so that both `Navigation.fs` (which is compiled before `Scene.fs`) and downstream call sites can reach it. The lens-based `Navigation.fs` site uses `bodyAwareSky` directly with `smallConfig.planet` / `smallConfig.up`; the rest use `bodyAwareLookAt`.

## Sites to modify

- `src/PRo3D.Base/CooTransformation.fs` — add `isSmallBody`.
- `src/PRo3D.Core/ReferenceSystem-Model.fs` — add `bodyAwareSky` and `bodyAwareLookAt` in module `ReferenceSystem`.
- `src/PRo3D.Viewer/Scene.fs` — `updateCameraUp` calls `ReferenceSystem.bodyAwareLookAt`.
- `src/PRo3D.Viewer/Viewer/Viewer.fs:96-98` — `lookAtBoundingBox` uses `ReferenceSystem.bodyAwareLookAt`.
- `src/PRo3D.Viewer/Viewer/Viewer.fs:467-468` — `addFlyToSurfaceAnimation` (no-homePosition branch) uses `ReferenceSystem.bodyAwareLookAt`.
- `src/PRo3D.Viewer/Navigation.fs:154-163` — `SetNavigationMode NavigationMode.FreeFly` derives sky via `ReferenceSystem.bodyAwareSky` instead of `smallConfig.up.Get(bigConfigB)` directly.

## Coverage after applying

| Trigger | Path | Fixed? |
|---|---|---|
| Import OPC (first surface) | `updateSceneWithNewSurface` → `lookAtBoundingBox` | yes |
| Click home in surface list (no stored homePosition) | `addFlyToSurfaceAnimation` | yes |
| `PlaceCoordinateSystem` interaction | `updateCameraUp` (call 2) | yes |
| Planet change | `updateCameraUp` (call 3) | yes |
| Switch nav mode → FreeFly (esp. from MapView with a loaded small-body scene) | `Navigation.SetNavigationMode FreeFly` | yes |
| Stored `homePosition` (saved with the scene) | uses `hp.Up` directly — out of scope |
| App startup default camera state | separate path — out of scope |

## Follow-up: MapView guard on Dimorphos load

Observed after the four-site fix landed: loading `testdimo.pro3d` produced gimbal *inside* MapView itself, before any nav-mode switch. Different code path, related root cause.

`MapViewCameraController.updateCameraForMapView` (MapViewCameraController.fs:83-93) builds its basis from a hardcoded world-Z reference:

```fsharp
let up   = getUpVector point planet |> Vec.Normalized   // radial-ish from body
let east = V3d.OOI.Cross(up).Normalized                 // collapses if up ‖ V3d.OOI
let north = up.Cross(east).Normalized                   // → zero → lookAt explodes
```

On Mars this is robust because camera locations are at non-polar latitudes — radial-up is never parallel to world Z. On Dimorphos the combination of (a) `5d5b5f6e` flipping `getUpVector` for Dimorphos from inward (`-p̂`, the prior failure-as-zero accident) to outward (`+p̂`), which propagates through `ReferenceSystem.fs:75` to `referenceSystem.up.value`, and (b) `e77c7f02` introducing `V3d.OOI` as the small-body sky for fresh-import lookAt, biases the saved/restored `cam.Location` toward the world-Z axis — exactly where the cross product collapses.

MapView is the *intended* controller for small bodies (Hera workshop scenes), so disabling it is the wrong direction. Pick a fallback reference axis instead:

```fsharp
let referenceAxis =
    if V3d.OOI.Cross(up).LengthSquared > 1e-6 then V3d.OOI else V3d.IOO
let east = referenceAxis.Cross(up).Normalized
```

Threshold `1e-6` on `LengthSquared` ≈ within ~0.06° of the pole. Outside that window the basis is identical to before; inside it, `V3d.IOO` substitutes so `east` stays well-defined and `north` is non-zero. The "world-Z is north" idiom is preserved everywhere it actually applies.

Site:
- `src/PRo3D.Viewer/MapViewCameraController.fs:83-100` — `updateCameraForMapView` uses the guarded reference axis.

Principled follow-up (out of scope here): replace world-Z with the body's actual rotation pole (via SPICE PCK or the planet's body-fixed-to-world rotation), so "north" is real geographic north on every body and pole-crossings stay smooth.

## Verification

After the edits:
1. `dotnet build src/PRo3D.Viewer/PRo3D.Viewer.fsproj` — clean build.
2. Empty scene → import Dimorphos OPC → camera target view is well-formed (forward not collinear with sky); FreeFly can drag/rotate without instantly snapping back.
3. Empty scene → import Mars OPC → status bar shows the same camera framing as today (no regression).
4. Mars scene → click home on a surface → camera animates to the same view as today.
5. Dimorphos scene → click home on the body → camera animates to an off-axis view with stable up.
6. Load `testdimo.pro3d` (Dimorphos OPC scene) → switch to MapView (overview) → switch back to FreeFly → camera basis is well-formed (no gimbal); mouse-drag rotation is stable.
7. Load `testdimo.pro3d` directly into MapView (overview) → no gimbal at load; pan/zoom/rotate stable.

## Out of scope

- Stored `homePosition` on disk: a Dimorphos scene with a saved hp.Up that is itself radial will keep using that value. Would need scene-file rewrite or load-time validation; flagged as a follow-up if it surfaces.
- Initial camera state at app start (before any scene/OPC is loaded): independent code path, not touched here.
- The `Spherical` vs `Ellipsoidal` choice for Dimorphos in `getConvention`: orthogonal concern; see `plans/CooTrafoUpdatePlan.md`.
