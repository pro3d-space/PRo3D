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

- Export: **the Boulders, Craters, Lineaments and Fractures presets now include the annotation metadata columns** (confidence, comment, shape model, shape model version, projected images, credit); before, only the Annotation table preset had them (#887, docs/AnnotationExport-CSV.md)
- Annotations: **add points to a finished line, polyline or polygon.** In *Edit Annotation*, Ctrl+click on the selected annotation's line picks up a new point there, and a Ctrl+click on the surface puts it down (Esc cancels). The terrain samples of a Viewpoint/Sky line show as grey dots near the cursor; Ctrl+click one (blue under the cursor) to turn it into a control point where it is, without moving the line. A line that gets a third point becomes a polyline. One Ctrl+Z per added point (#639, docs/AnnotationVertexEditing.md)
