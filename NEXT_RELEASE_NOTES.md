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
- Image Search (STAC): **images listed for another body are found too** — new *Other targets* checkbox (on by default) also checks, by their pointing alone, the catalog's images of other targets in the Date range (e.g. a Didymos frame that shows Dimorphos); such a result shows the catalog's target after its name. Images whose target the loaded SPICE kernel cannot place relative to the surface's body are counted, not listed ([StacImageSearch.md](docs/StacImageSearch.md#images-of-other-targets))
