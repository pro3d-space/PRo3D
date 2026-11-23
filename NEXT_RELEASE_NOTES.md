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
- Projected images: **removing the last image from the projection stack no longer leaves it projected**. Removing a stack row under the mouse kept it as the hover preview through new searches and OPC reloads, until PRo3D was restarted (docs/MultiImageProjection.md)
- Projected images: **Projection Settings → Projection** chooses what is projected: the *Full Stack* (as before), only the *Selected* image, or only the *First in Stack*. A hovered image shows alone in the one-image modes. Saved with the scene (docs/MultiImageProjection.md)
- Image Search (STAC): **hovering a result projects its image**: resting on a row for 0.3 s downloads the image and previews it like a hovered library image, without adding it to the library or the stack. **Clicking a result's name selects it** (highlighted; shown under *Selected Image*). A result on the stack shows the stack icon, which removes it. The bin also removes the downloaded images from the library and the stack (docs/StacImageSearch.md)
- Image projection: **Orientation Source *SPICE* now uses the instrument's own frame (spacecraft CK × frames kernel) and is no longer aimed at the target**, so the frames kernel's mounting, misalignment and twist are applied and pointing errors stay visible; the old behaviour (spacecraft CK only, turned onto the target centre) is kept as *SPICE, aimed at target* (`SpiceAimed`). *SPICE (instrument frame)* is now the **default orientation source**, and the dropdown shows readable labels. pro3d-tool: `--method spice` follows the viewer, `spice-aimed` gives the old one; `simulate-image` gains `--method` (`mbi`, `mbi-spice-position`, `mbi-spice-range`, `spice`/`spice-instrument`, `spice-aimed`) ([MultiImageProjection.md](docs/MultiImageProjection.md#the-frames-kernel-instrument-mounting), [Pro3DTool-SimulateImage.md](docs/Pro3DTool-SimulateImage.md))
- Image projection: **image axes and row flip come from the instrument kernel's pixel layout** — the AFC cameras now project with flipped rows by default (*Instrument Settings › flip*; pixel (0,0) is the lower left corner), the same in the viewer and every pro3d-tool verb; `simulate-image` writes frames in that row order. *SPICE (instrument frame)* takes its image axes from the layout and refuses frames and instrument kernels that disagree. Scenes save the flip where it differs from the instrument's default ([InstrumentFieldOfView.md](docs/InstrumentFieldOfView.md#where-the-default-comes-from-the-instrument-kernels-pixel-layout))
