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

- Projected images (macOS): **the colour legend shows the colour map's real colours.** It read the map with red and blue swapped, so e.g. Magma's orange and cream appeared as blue and cyan in the legend (docs/MultiImageProjection.md)

- Projected images: **an image's *False Colour* switch now applies on the surface too**, not only in its preview: with it off the image is projected in grey values (docs/MultiImageProjection.md)

- Projected images: **the *Transfer Function* checkbox is now *Apply Image Adjustments*.** While it is off, the selected image's Properties say that Range, Brightness/Contrast and False Colour only change the preview, with a button to turn it on (docs/MultiImageProjection.md)

- Image projection: **each instrument's field of view can be set per scene** in *Projected Images → Instrument Settings*, prefilled from the SPICE instrument kernels. HyperScout-H now defaults to the kernel's paraxial 15.5 × 8.3° (was 15.24 × 8.09°), and TIRI images can be projected (13.3 × 10°, image axes not yet checked against a real image) (#882, docs/InstrumentFieldOfView.md)

- Projected images: **the *Spice* orientation source projects again when the image's sidecar has no target vector.** Its depth range came from the sidecar's `TRG_POS`; with `TRG_POS = 0` the far plane sat at 100 m and nothing was painted. Spice now takes the distance from SPICE, aims at the sidecar's `TARGET` and keeps the spacecraft's roll from the SPICE attitude instead of an arbitrary one (docs/MultiImageProjection.md)
- Projected images: **new orientation source *MbiAttitudeSpiceRange*, now the default: the image's own attitude (`SC_QUAT`, including roll) with the range from SPICE**, for sidecars without a usable `TRG_POS` and for kernels whose spacecraft-to-target direction is less accurate than the measured attitude; also `--method mbi-spice-range` in `pro3d-tool` (docs/MultiImageProjection.md)
- Projected images: **Registration roll/pitch/yaw now turn the projector about the camera, in every orientation source.** Pitch and yaw move the image across the body, as a pointing offset does; before, they rotated the projector about the body centre (and did nothing at all with *MbiBased*). *MbiBased* now also measures `TRG_POS` from the sidecar's `TARGET` body, and an image whose `TRG_POS` is zero shows "no TRG_POS" in the library instead of silently projecting nothing; a zero `TRG_POS` or an all-zero `SC_QUAT` is reported in the log with the orientation source that still works (docs/MultiImageProjection.md)
- Projected images: **per-instrument *flip* in *Projected Images → Instrument Settings*** mirrors that instrument's images vertically, for files stored bottom-up (FITS row order) — a mirrored image cannot be corrected with Registration roll/pitch/yaw. Saved with the scene. The scene's field of view now also reaches the fly-to (docs/InstrumentFieldOfView.md)
- GIS: **Projected Images has its own panel.** Import, stack, library and projection settings moved out of the GIS View into a *Projected Images* tab next to it in the built-in layouts; a saved layout gets it through *Layout → Reopen Panel*. The panel shows the imported folder next to *Import Directory* (or *not set*) and folds into three sections, *Projection Settings*, *Images* (the library, with the selected image's preview **and its display settings**, formerly the rows' *Edit* panels, pinned at its top while scrolling) and *Projected Images* (the stack). **The library can be filtered** by phase angle (sun–target–camera, from the sidecar), instrument, distance, ground resolution (m/px, from distance, instrument field of view and image width) and observation date/time (UTC; range by number or date/time input, or a two-handle slider) (docs/MultiImageProjection.md)
- GIS: **the *Selected Image* preview shows PNG and JPG images**; it stayed white for them, only TIFF and EXR were drawn (docs/MultiImageProjection.md)
- GIS: **selecting a projected image a second time no longer freezes PRo3D.** The colour legend re-read its colour map from a stream that was already at its end, and the image load never returned (docs/MultiImageProjection.md)
- Top bar: **Load SPICE kernel sits next to Reference System**, with the loaded kernel's file name (*default (pck00010.tpc)* until one is) and a check mark (or a red mark; a `--defaultSpiceKernel` that is missing or fails to load is now shown as failed instead of loaded, and PRo3D falls back to its default kernel); the kernel path box left the GIS View, and *Load Spice and Time* was removed from the Projected Images panel (load the kernel with *Load SPICE kernel*; *Fly to* on a library row still sets the scene time to the image's). **Direct Tool Mode moved to the second row**, right of the active tool's chip (docs/GisView.md, docs/DirectToolMode.md)
- GIS: **Brightness / Contrast for projected images**, and *Selected Image › Properties* regrouped into *Data* (channel, range), *Tone* (brightness, contrast — in the preview and on the surface with *Transfer Function* on; a hint warns that the legend no longer matches while they are changed) and *False Colour* (switched by its header checkbox; colour map and legend, which the main view only shows while the colour map is on) (docs/MultiImageProjection.md)
- UI: **tooltips appear after a quarter second**, on macOS too, where the native ones took over a second; PRo3D now draws them itself (docs/Tooltips.md)
- GIS: **projected images now project when the SPICE kernel is loaded after the image folder.** Their projections, computed (and failed) without the kernel, were kept; loading a kernel now recomputes them (docs/MultiImageProjection.md)
- Image search (STAC): **the *Image Search* tool searches a STAC catalog for the instrument images that show the clicked point (or, with Ctrl held, each point the cursor rests on), checked against each image's own pointing and listed by pixel size; selecting the tool connects to the catalog and, if it asks for one, opens a sign-in dialog (user name and password, kept for the session only); hovering a result shows its frustum and footprint in 3D; a click downloads the image and puts it on top of the projection stack.** The catalog comes from `PRO3D_STAC_URL` or the GIS tab; a time range and a sun incidence range narrow every search, and the list shows the best 100. Where the catalog has no footprints (e.g. for a newly visited body), the images without footprint are checked by their pointing alone (docs/StacImageSearch.md)
