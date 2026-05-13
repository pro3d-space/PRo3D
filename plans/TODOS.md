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
