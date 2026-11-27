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
- Main menu: **menu lists no longer run off the bottom of the window** — every list of the top-left main menu (e.g. *Extras*, *Layout > Reopen Panel*) is capped to the space below it and scrolls when it is longer (docs/MainMenu.md)
- Annotations: **Shift+click in the annotation list selects every annotation between the last clicked one and the clicked one**; a plain click on a name now starts a fresh selection, as a click in the 3D view does (docs/AnnotationList.md)
