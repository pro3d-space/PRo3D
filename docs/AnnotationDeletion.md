# Deleting annotations with the Delete key

Press **Delete** (labelled DEL, Entf or CANC depending on the keyboard) in the 3D view to delete
every selected annotation at once. **Ctrl+Z** brings them all back in one step, and **Ctrl+Y**
deletes them again.

Before this, the only way to delete an annotation was the **remove** button next to it in the
annotation tree, one annotation at a time.

## What gets deleted

Delete removes the union of both kinds of annotation selection:

| How it was selected | Shown as | Deleted? |
|---|---|---|
| Cube icon in the annotation tree, or Shift+Ctrl+click in the 3D view | coloured cube (multi-selection, `selectedLeaves`) | yes |
| Name in the annotation tree, or Ctrl+click in the 3D view | green name (`singleSelectLeaf`) | yes |

This is the same set that `DrawingApp.isSelected` treats as selected, for example when the
GeoJSON export marks selected annotations.

Things to be aware of:

- **Finishing a drawing selects the new annotation**: if you press Delete right after drawing,
  the annotation you just drew is deleted.
- Clicking another name moves the green selection but **keeps the cube checks**. With A and B
  cube-checked and C's name clicked, Delete removes A, B and C.
- **Deselect All** on a group clears the cube checks only. The green name, if any, is still
  deleted.
- It works for every geometry: point, line, polyline, polygon, DnS, TT and all ellipse variants.
- It works in every interaction mode. The annotation you are still drawing is never touched;
  Backspace (remove the last point) and Escape (discard it) handle that one.
- With nothing selected, Delete does nothing and adds no undo step.

## Keyboard focus

The key is handled by the main 3D view, which takes keyboard focus as soon as the mouse cursor
enters it. Pressing Delete while typing in a GUI text field, for example an annotation's name,
edits the text and deletes no annotation. The instrument view does not handle the key.

## Undo and redo

The whole deletion is a single `SnapshotDelta` on the drawing undo stack, the same mechanism that
annotation union, cut and **Clear** use. One Ctrl+Z restores every deleted annotation with its
original id, its group and its position in the group.

## Implementation

| What | Where |
|---|---|
| `DrawingAction.DeleteSelectedAnnotations` | [Drawing-Model.fs](../src/PRo3D.Core/Drawing/Drawing-Model.fs) |
| Update case: collect the selection, remove each leaf by id, push one undo snapshot | [Drawing-App.fs](../src/PRo3D.Core/Drawing/Drawing-App.fs) |
| `Keys.Delete` → `DeleteSelectedAnnotations` in `getDrawingActionForKey` | [Viewer.fs](../src/PRo3D.Viewer/Viewer/Viewer.fs) |

Leaves are removed with `GroupsApp.removeLeafById`, not the path-based `removeLeaf`. A viewport
pick stores the root path whatever group the leaf really sits in, and `removeLeaf` would silently
leave such a leaf in its sub-group. If the vertex being moved in
[edit mode](AnnotationVertexEditing.md) belongs to a deleted annotation, the grab is dropped too.

## Tests

`src/Tests/Features/Section19_UndoRedoGroupColor.fs`, TC-19.3 ("Delete key"), covers:

- single selection;
- multi-selection;
- cube-checked and name-selected annotations deleted together;
- every geometry;
- a leaf in a sub-group selected with the root path;
- one-step undo and redo;
- an empty selection;
- the annotation being drawn.

The manual steps are in the test protocol: TC-56 (Keyboard Shortcuts) and TC-58 (Undo/Redo for
Drawing).
