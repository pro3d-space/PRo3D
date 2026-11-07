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

- Annotations: **box selection.** In *Select Annotation* mode, Ctrl+drag a rectangle to select every annotation it touches; Ctrl+Shift+drag adds, Ctrl+Alt+drag toggles, Esc cancels (in Direct Tool Mode without Ctrl). Annotations hidden behind a surface are not selected, decided with the surfaces' KdTrees the way the depth test decides on screen; a Ctrl+click on the hidden part of an annotation no longer selects it either. Delete removes the selection, one Ctrl+Z restores it (#849, #854, docs/AnnotationBoxSelection.md)
- Input: **Alt (Option on macOS) no longer acts as Ctrl.** The tool modifier is **Ctrl, and on macOS Cmd** (left or right; the right Cmd key is new). Until now Alt counted as Ctrl without being documented: Alt+click picked and drew, Alt+drag box-selected, Alt+S saved and Alt+Z undid. **Mac users who used Option instead of Cmd: use Cmd.** Alt now only does what a tool says it does, such as the toggle in box selection (Ctrl+Alt+drag, Cmd+Option+drag on macOS) (docs/DirectToolMode.md)
- Input: in Direct Tool Mode, Alt no longer hands the left button back to the camera, so Alt+drag toggles a box selection there as documented
- Annotations: a plain Ctrl+click in the 3D view selects **only** the clicked annotation and clears a multi-selection, so Delete removes only what looks selected; Shift+Ctrl+click adds to the whole selection and keeps the annotation selected by a plain click (docs/AnnotationBoxSelection.md)
- Geological maps: the *Assign unit* eyedropper is **Ctrl+Alt+click** (Cmd+Option+click on macOS), or Alt+click in Direct Tool Mode, now that Alt alone no longer arms the tool (docs/GeologicalMap.md)
- Annotations: the annotation tree opens without stalling the viewer with thousands of annotations (it did quadratic work per annotation; filling the list in the browser is still slow, #900)
- Busy indicator: box selection reads *selecting annotations*, and a Ctrl+click on an annotation that loads part of the surface reads *picking*
