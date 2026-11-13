# Next release notes

Release-note entries for changes merged since the last release, collected here until the next
release folds them into `PRODUCT_RELEASE_NOTES.md`.

- **Add** an entry here with every user-visible change, in the same change (PR) as the change
  itself, in the style of `PRODUCT_RELEASE_NOTES.md`: one bullet, area first, the effect for
  the user in bold, the issue/PR and the doc page in parentheses at the end.
- **Do not** add a section to `PRODUCT_RELEASE_NOTES.md` for unreleased work. The build parses
  every `##` heading there as a version (`Build.fs`, `ReleaseNotes.load`); an `## Unreleased`
  heading fails every build.
- **When releasing**, move the bullets below into the new version's section of
  `PRODUCT_RELEASE_NOTES.md`, write its one-line summary, and leave this file with the header
  and an empty list (docs/Build-Deploy-System.md).

## Pending

- Image projection: **each instrument's field of view can be set per scene** in *Projected Images → Instruments*, prefilled from the SPICE instrument kernels. HyperScout-H now defaults to the kernel's paraxial 15.5 × 8.3° (was 15.24 × 8.09°), and TIRI images can be projected (13.3 × 10°, image axes not yet checked against a real image) (#882, docs/InstrumentFieldOfView.md)

- Projected images: **the *Spice* orientation source projects again when the image's sidecar has no target vector.** Its depth range came from the sidecar's `TRG_POS`; with `TRG_POS = 0` the far plane sat at 100 m and nothing was painted. Spice now takes the distance from SPICE, aims at the sidecar's `TARGET` and keeps the spacecraft's roll from the SPICE attitude instead of an arbitrary one (docs/MultiImageProjection.md)
- Projected images: **new orientation source *MbiAttitudeSpiceRange*, now the default: the image's own attitude (`SC_QUAT`, including roll) with the range from SPICE**, for sidecars without a usable `TRG_POS` and for kernels whose spacecraft-to-target direction is less accurate than the measured attitude; also `--method mbi-spice-range` in `pro3d-tool` (docs/MultiImageProjection.md)
- Projected images: **Registration roll/pitch/yaw now turn the projector about the camera, in every orientation source.** Pitch and yaw move the image across the body, as a pointing offset does; before, they rotated the projector about the body centre (and did nothing at all with *MbiBased*). *MbiBased* now also measures `TRG_POS` from the sidecar's `TARGET` body, and an image whose `TRG_POS` is zero shows "no TRG_POS" in the library instead of silently projecting nothing; a zero `TRG_POS` or an all-zero `SC_QUAT` is reported in the log with the orientation source that still works (docs/MultiImageProjection.md)
- Projected images: **per-instrument *flip* in *Projected Images → Instruments*** mirrors that instrument's images vertically, for files stored bottom-up (FITS row order) — a mirrored image cannot be corrected with Registration roll/pitch/yaw. Saved with the scene. The scene's field of view now also reaches the fly-to (docs/InstrumentFieldOfView.md)
