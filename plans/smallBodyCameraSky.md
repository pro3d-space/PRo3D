# Small-body camera sky fix — avoid FreeFly gimbal lock on Dimorphos & co.

## Problem

When a Dimorphos (or other small-body) scene is loaded, the FreeFly controller can land in a gimbal-locked initial state: forward direction nearly antiparallel to sky. Symptom: camera roll is undefined or chaotic, view "feels stuck".

Root cause: PRo3D's camera-setup code uses `referenceSystem.up.value` as the `sky` argument to `CameraView.lookAt`. For Mars/Earth this is the local geographic up (radial outward at the system's origin point) and is perpendicular to typical near-surface viewing directions, so all is fine. For small bodies whose entire body fits inside the OPC bounding box, `bb.Max - bb.Center` ends up running near-parallel to that radial up — `CameraView.lookAt camLoc bbCenter radialUp` then degenerates the camera basis.

Three camera-setup sites have the same shape:

1. **Viewer.fs:96-98** — `lookAtBoundingBox`. Triggered when an OPC is imported on a fresh scene (`updateSceneWithNewSurface` → first import path).
2. **Viewer.fs:467-468** — `addFlyToSurfaceAnimation`, fallback branch (no stored `homePosition`). Triggered by the home button in the surface list (`SurfaceApp.fs:1248`) and the GIS app (`GisApp.fs:362`).
3. **Scene.fs:333-342** — `updateCameraUp`. Called from three places: after first import (`Viewer.fs:197`, before lookAtBoundingBox so it gets overridden); on `Interactions.PlaceCoordinateSystem` (`Viewer.fs:294`); and on planet change (`Viewer.fs:1462`).

All three pass `m.scene.referenceSystem.up.value` as the sky. All three should pick a body-aware sky instead, otherwise the small-body fix is incomplete.

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

Centralise the policy in two helpers so the rule lives in one spot and the three call sites collapse to one-liners.

```fsharp
// In src/PRo3D.Base/CooTransformation.fs (next to getConvention):
let isSmallBody (planet : Planet) =
    match planet with
    | Planet.Phobos | Planet.Deimos | Planet.Didymos | Planet.Dimorphos -> true
    | _ -> false

// In src/PRo3D.Viewer/Scene.fs (next to updateCameraUp):
let bodyAwareLookAt (referenceSystem : ReferenceSystem) (location : V3d) (target : V3d) : CameraView =
    let sky =
        if CooTransformation.isSmallBody referenceSystem.planet
        then V3d.OOI
        else referenceSystem.up.value
    CameraView.lookAt location target sky
```

Split rationale: `isSmallBody` is body knowledge — lives next to `getConvention` in `PRo3D.Base.CooTransformation`. `bodyAwareLookAt` composes that with `CameraView.lookAt` — lives in `Scene.fs` where the camera-side conventions already live (`updateCameraUp`).

## Sites to modify

- `src/PRo3D.Base/CooTransformation.fs` — add `isSmallBody`.
- `src/PRo3D.Viewer/Scene.fs` — add `bodyAwareLookAt`; rewrite `updateCameraUp` to use it.
- `src/PRo3D.Viewer/Viewer/Viewer.fs:96-98` — `lookAtBoundingBox` uses `bodyAwareLookAt`.
- `src/PRo3D.Viewer/Viewer/Viewer.fs:467-468` — `addFlyToSurfaceAnimation` (no-homePosition branch) uses `bodyAwareLookAt`.

## Coverage after applying

| Trigger | Path | Fixed? |
|---|---|---|
| Import OPC (first surface) | `updateSceneWithNewSurface` → `lookAtBoundingBox` | yes |
| Click home in surface list (no stored homePosition) | `addFlyToSurfaceAnimation` | yes |
| `PlaceCoordinateSystem` interaction | `updateCameraUp` (call 2) | yes |
| Planet change | `updateCameraUp` (call 3) | yes |
| Stored `homePosition` (saved with the scene) | uses `hp.Up` directly — out of scope |
| App startup default camera state | separate path — out of scope |

## Verification

After the four edits:
1. `dotnet build src/PRo3D.Viewer/PRo3D.Viewer.fsproj` — clean build.
2. Empty scene → import Dimorphos OPC → camera target view is well-formed (forward not collinear with sky); FreeFly can drag/rotate without instantly snapping back.
3. Empty scene → import Mars OPC → status bar shows the same camera framing as today (no regression).
4. Mars scene → click home on a surface → camera animates to the same view as today.
5. Dimorphos scene → click home on the body → camera animates to an off-axis view with stable up.

## Out of scope

- Stored `homePosition` on disk: a Dimorphos scene with a saved hp.Up that is itself radial will keep using that value. Would need scene-file rewrite or load-time validation; flagged as a follow-up if it surfaces.
- Initial camera state at app start (before any scene/OPC is loaded): independent code path, not touched here.
- The `Spherical` vs `Ellipsoidal` choice for Dimorphos in `getConvention`: orthogonal concern; see `plans/CooTrafoUpdatePlan.md`.
