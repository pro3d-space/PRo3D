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

- Image Search (STAC): **downloaded images are no longer added to the Image Library** — + puts the image on the projection stack and selects it, a click on a result's name selects it; the library keeps only the imported folder. Images on the stack or selected stay when the results are cleared; the bin removes them ([StacImageSearch.md](docs/StacImageSearch.md))
- Projected Images: **the Projection Stack's rows can be selected** — a click on a row's name selects the image (again: deselects); the selection is highlighted in the stack, the Image Library and the Image Search results alike, whichever list it was made in ([MultiImageProjection.md](docs/MultiImageProjection.md#4-build-the-stack))
