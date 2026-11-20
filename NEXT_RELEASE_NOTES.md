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

- Tool strip: **one camera button and one light button**, each showing the current mode, with a small triangle; a click opens the modes to the left. The light button sets the Sun / Lighting Mode of GIS View → Settings (docs/ToolStrip.md)
- GIS: **the observation Time field shows and takes UTC**, like every observation time and the image lists; the calendar before showed local time (an image at 03:16 UTC read as 04:16 in UTC+1). A click on a date/time field opens its picker, a second click closes it (docs/GisView.md)
- Projected images: **a clock next to the fly-to arrow in both image lists** sets the GIS time to the image's observation time, without moving the camera (docs/MultiImageProjection.md, docs/StacImageSearch.md)
- GIS: **Sun / Lighting Mode has a `Headlight` mode** (what `Off` did before: a camera light that shows untextured surfaces' shape); `Off`, the default for new scenes, now turns all lighting off. Without the sun a projected image shows only its own pixels, never shaded by the headlight. The setting moved to *GIS View → Settings*; older scenes keep their look (docs/GisView.md, docs/MultiImageProjection.md)
- Projected images: **the Date filter spans 2026-11-01 to today**, and the Image Search always asks the catalog for the selected dates, also while the filter is off (docs/MultiImageProjection.md, docs/StacImageSearch.md)
- Projected images: **sidecars are found for images delivered as `.tiff` while the sidecar declares `.tif`, and for image names carrying an instrument suffix the sidecar lacks** (`<obs>_AFC1.tiff` ↔ `<obs>.mbi.json`), in the viewer, folder discovery and `pro3d-tool sample-layers` (docs/MultiImageProjection.md)
