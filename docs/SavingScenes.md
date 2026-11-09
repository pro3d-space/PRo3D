# Saving scenes

Saving `<name>.pro3d` writes the scene, the annotations (`<name>.pro3d.ann`), the sequenced
bookmarks (folder `<name>/`) and the window layout (see [WindowLayouts.md](WindowLayouts.md)).

## Broken annotations do not stop the save

Some annotations can end up with values that cannot be written, e.g. a dip & strike whose
angles are NaN (the file format cannot store NaN or infinity, #834). Such an annotation used to
abort the whole save.

Now it is left out of the annotation file and everything else is saved. The message
*"N annotation(s) are broken and could NOT be saved: …"* names them (by their text, or their
geometry if they have none) and stays on screen for 30 s; the log has the details. The broken
annotations stay in the running session, so they can be inspected, fixed or redrawn before the
next save. The annotation file is only written once everything in it could be serialized, so it
is never left half-written.

Test: `src/Tests/BrokenAnnotationSaveTest.fs`.
