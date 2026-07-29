# TODOs (deferred follow-ups)

## CooTransformation cleanup follow-ups

- **Rename `SphericalCoo.altitude`.** The field's meaning depends on the body's
  `ConventionKind`:
  - `Planetographic` → height above the spheroid surface (the natural
    "altitude" interpretation).
  - `Ellipsoidal` → height above the tri-axial surface (same natural
    interpretation).
  - `Spherical` → **radial distance from body centre** (SPICE `reclat`
    convention), not a height above any surface.

  The current single-field name `altitude` is therefore a misnomer for the
  Spherical case. The semantics are documented at the type definition in
  `src/PRo3D.Base/CooTransformation.fs` and surfaced in the GUI via the
  per-body "Convention" row in `ViewerGUI.fs` / `ReferenceSystem.fs`, but the
  field name itself should be renamed at the next breaking-change pass.

  Candidates:
  - rename to `verticalCoord` / `r` / `radial` (generic, no semantic).
  - split into a discriminated union (`Altitude of double | Radius of
    double`) — most precise, but ripples through every caller that reads
    `sc.altitude`.

  Rippling scope: ~16 caller files currently read `.altitude` directly.
  Defer until we are ready to absorb that breaking change.

## Small-body bearing / pitch overlay

The text overlay (`ViewerGUI.fs:132+`) currently shows `n/a` for bearing
and pitch when the active planet is a small body (per
`CooTransformation.isSmallBody`). The underlying math is wrong on
irregular small bodies for two reasons; suppressing the readouts is the
interim "better nothing than crap" workaround.

1. **`ReferenceSystem.northVector`** (`ReferenceSystem.fs:77-79`) builds
   `east = V3d.OOI.Cross(up)` — i.e. it assumes world +Z is the body's
   spin pole / north pole. For Earth/Mars this matches the IAU
   convention. For Dimorphos in `DIMORPHOS_SHM` (= DARTSOC, the frame
   the loaded SPC OBJs live in) the spin pole is **−Z**, so the derived
   "north" points 180° wrong. See `plans/sbmtImport.md` "Coordinate frame
   knowledge" for the proof.
2. **`AnnotationHelpers.pitch`** (`AnnotationHelpers.fs:44`) measures
   forward-direction angle against `Plane3d(up.Normalized, 0.0)` — a
   plane through the *world* origin, not the camera position. For a
   planet-scale body the camera is at the surface so this approximates
   horizon; for a 170 m asteroid where the camera sits a body-radius
   away, the global-plane reading is meaningless.

Fix sketch:
- Make `northVector` take the body's spin-pole direction explicitly
  (resolved from PCK / SPICE, or via a small lookup keyed on `Planet`).
  Add a singularity fallback for camera positions near the pole.
- Recompute pitch against a tangent plane at the camera location
  (`Plane3d(up.Normalized, camera.Location)`), not through origin.
- Re-enable the overlay rows for small bodies once both are in.
