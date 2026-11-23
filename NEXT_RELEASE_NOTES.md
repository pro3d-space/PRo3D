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
- Projected images: **no longer shaded twice under the sun**: `SunDirect` and `SunShadow` leave a projected image unshaded, like the headlight, since the photograph already shows the sun's light and shadows; the surface around it is lit as before (docs/GisView.md, docs/MultiImageProjection.md)
- Image projection: **Orientation Source *SPICE* now uses the instrument's own frame (spacecraft CK × frames kernel) and is no longer aimed at the target**, so the frames kernel's mounting, misalignment and twist are applied and pointing errors stay visible; the old behaviour (spacecraft CK only, turned onto the target centre) is kept as *SPICE, aimed at target* (`SpiceAimed`). *SPICE (instrument frame)* is now the **default orientation source**, and the dropdown shows readable labels. pro3d-tool: `--method spice` follows the viewer, `spice-aimed` gives the old one; `simulate-image` gains `--method` (`mbi`, `mbi-spice-position`, `mbi-spice-range`, `spice`/`spice-instrument`, `spice-aimed`) ([MultiImageProjection.md](docs/MultiImageProjection.md#the-frames-kernel-instrument-mounting), [Pro3DTool-SimulateImage.md](docs/Pro3DTool-SimulateImage.md))
- Image projection: **image axes and row flip come from the instrument kernel's pixel layout** — the AFC cameras now project with flipped rows by default (*Instrument Settings › flip*; pixel (0,0) is the lower left corner), the same in the viewer and every pro3d-tool verb; `simulate-image` writes frames in that row order. *SPICE (instrument frame)* takes its image axes from the layout (falling back to the relabelling nearest the spacecraft-attitude camera when the loaded frames kernel names the axes differently — shown as a notice and as a warning in the *Projected Images* panel when the kernel is loaded). Scenes save the flip where it differs from the instrument's default ([InstrumentFieldOfView.md](docs/InstrumentFieldOfView.md#where-the-default-comes-from-the-instrument-kernels-pixel-layout))
