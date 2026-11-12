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

