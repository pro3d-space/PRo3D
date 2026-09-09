# Annotation Groups (tree, context menu, Actions)

The **Annotations** accordion in the Viewer shows the annotation group tree, built by
`viewTree` / `viewAnnotationGroups` in
[Drawing.UI.fs](../src/PRo3D.Core/Drawing/Drawing.UI.fs). Each group is a folder that
holds annotations (leaves) and sub-groups. Group-level messages are `GroupsAppAction`
cases handled in [GroupsApp.fs](../src/PRo3D.Core/GroupsApp.fs), routed through
`DrawingAction.GroupsMessage` (see [Drawing-App.fs](../src/PRo3D.Core/Drawing/Drawing-App.fs)).

## Group row

Inline on every group row, left to right:

| Element | Icon | Action |
|---|---|---|
| Group name | – | – (double-click the name in the Group properties panel to rename) |
| Set active | `circle` / `circle outline` | `SetActiveGroup` — new annotations land in the active group; the circle is filled + green for the active one |
| Add Group | `plus` | `AddGroup` — adds a sub-group |
| Toggle visibility | `unhide` / `hide` | `ToggleGroup` — flips the whole group's visibility (group flag **and** every leaf), icon reflects `group.visible` |
| Group actions | `ellipsis vertical` | opens the context menu (below) |

Each **annotation row** underneath a group carries, next to the FlyTo (`home`) icon,
a red **`times`** icon that removes that annotation directly from the list
(`RemoveLeaf`, undoable).

## Context menu

Click the `ellipsis vertical` icon to open a Semantic UI dropdown menu
(`$('#__ID__').dropdown({ action: 'hide', on: 'click' })`). Every row is icon +
label and the whole row is clickable.

| Row | Icon | Action |
|---|---|---|
| Select all | `bookmark` | `SetSelection(path, true)` — add the group's annotations to the green multi-selection |
| Deselect all | `bookmark outline` | `SetSelection(path, false)` |
| Show all | `unhide` | `SetVisibility(path, true)` |
| Hide all | `hide` | `SetVisibility(path, false)` |
| Default color | *colour picker* | `SetGroupDefaultColor` — the colour new annotations in this group get (the picker widget replaces the row icon) |
| Clear group | `eraser` | `ClearGroup(path)` — removes every annotation from the group, keeps the (empty) group |
| Remove group | `trash alternate` | `RemoveGroup(path)` — deletes the group and its annotations |

**Undo.** `ClearGroup`, `RemoveGroup` and `RemoveSelectedLeaves` push a `SnapshotDelta`
onto the drawing undo stack (`Drawing-App.fs`, `GroupsMessage` handler), so **Ctrl+Z**
restores the group / annotations and **Ctrl+Y** re-applies. Other menu actions are not
individually undoable (they only change selection / visibility / default colour).

`RemoveGroup` on the root group is a no-op (`GroupsApp.updateStructureAt` leaves the
tree unchanged for the empty path).

## Actions accordion

The **Actions** accordion (`viewAnnotationActions` in `Drawing.UI.fs`) is a labelled
list (`Html.table` / `Html.row`), always annotation-scoped — it no longer branches on
whether a group or an annotation was last clicked:

| Row | Icon | Action |
|---|---|---|
| Remove | `remove` (red) | `RemoveSelectedLeaves` — removes the green multi-selection, falling back to the single selection; undoable |
| Move | `move` | `MoveLeaves` — moves the green multi-selection into the active group |
| Recalculate | `calculator` | `RecalculateMeasurements` — recomputes measurements for the selected annotations |
| Selection | `remove circle` | `ClearSelection` — clears the green multi-selection everywhere |

## Related

- [AnnotationToolbar.md](AnnotationToolbar.md) — parameters for the next annotation.
- [ColorByCategory.md](ColorByCategory.md) — category colouring overrides the group
  default colour while enabled.
- [AnnotationExport.md](AnnotationExport.md) — group paths in exported/imported files.
