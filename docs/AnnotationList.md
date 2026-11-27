# Annotation list

The **Annotations** panel lists the annotation groups as a tree; each group shows its
subgroups first and then its annotations, one row per annotation (name, visibility toggle,
fly-to, point count and surface).

## Thousands of annotations (#900)

A group's annotations sit in their own scroll box (at most 60 % of the window height). Only
the rows in and around the visible part of that box are in the page; two empty blocks above
and below stand in for the rest, so the scroll bar still spans the whole group. Scrolling
swaps rows in and out at the edges.

5000 annotations in one group list in about a second (measured with
`tests-ui/tests/annotation-list.spec.ts`); before, the browser needed 2.5-4.5 minutes to
lay out one row per annotation.

What follows from it:

- Every row has the same fixed height (42 px). A long name is cut off rather than wrapped.
- A group with many annotations scrolls inside the panel; the panel scrolls as before.
- Selecting an annotation elsewhere (in the 3D view, or in a second annotations window)
  scrolls its group's box to it, unless the row is already in view. Only a new selection
  scrolls: adding or deleting annotations above the selected one leaves the view where it is.
- A single pick in the 3D view also opens the folders down to the picked annotation (they
  stay open, and are saved open with the scene). A multi-selection (Shift), a box
  selection, or a pick that deselects opens nothing.
- Text search in the browser (Ctrl+F) finds only the rows currently in the page.
- With the annotations list open in two windows at once, both show the rows around
  whichever was scrolled last; the other one shows the right rows again as soon as it is
  scrolled. (The page is built once for all windows, so the scroll position is shared.)

## Selecting a range (Shift+click)

Click an annotation in the list (its name or its cube icon), then Shift+click another one in
the same group: every annotation between the two is selected, both included. The one
Shift+clicked is the highlighted (green) annotation; the others are in the multi-selection,
so Delete, *Move to active group* and the boolean operations act on all of them.

- The range starts at the annotation last clicked **without** Shift (or picked in the 3D
  view, with or without Shift) - also when that click deselected it. A further Shift+click spans
  again from that same annotation and replaces the selection, so the range can be shortened
  or flipped.
- A plain click on a name (no Shift) starts fresh: the range and any cube checks are cleared
  and only the clicked annotation is selected, as a click in the 3D view does. With nothing
  but the highlighted annotation selected, clicking that annotation again deselects it.
- Rows scrolled out of view are included.
- The range stays inside one group. If the last clicked annotation is in another group (or
  nothing was clicked yet), Shift+click selects or deselects just the clicked annotation and
  keeps the rest of the selection, including the highlighted annotation, as a Shift+click in
  the 3D view does. The range then starts at that annotation: the next Shift+click in the same
  group spans from it.
- Deleting the annotation the range starts at forgets it: the next Shift+click selects just
  the clicked annotation (and the range starts there).

## For developers

Range selection is `GroupsAppAction.SelectLeafRange` (`GroupsApp.selectLeafRange`): the anchor
is `GroupsModel.rangeAnchor` (not persisted). Plain clicks, cube clicks and 3D picks (Shift
too, adding or removing) set it; a Shift+click in a list that spans a range leaves it alone;
removing its leaf clears it. Without a usable anchor, `GroupsApp.shiftToggleLeaf` toggles in
the effective selection (multi-selection plus the green leaf) and makes the clicked leaf the
anchor; `DrawingApp.shiftPickAnnotation` (Shift+pick in the 3D view) is the same function. A
range makes the Shift+clicked leaf the `activeChild`, so the leaf panel's *Remove* acts on the
green row. The list sends it from `DrawingUI.onClickShift`.
A plain name click is `GroupsAppAction.PlainSelectLeaf` (`GroupsApp.plainSelectLeaf`, shared
with the 3D pick); surface and bookmark lists still send `SingleSelectLeaf`, which keeps cube
checks. They can send `SelectLeafRange` too.

`PRo3D.Base.VirtualList.view` is the windowed list; any list with fixed-height rows can use
it (surfaces and bookmarks still render every row). The scroll position is view-local state
— two `cval`s written from the scroll handler — so no model field or message carries it.
The recipe is aardvark.media's `src/Scratch/19 - VirtualTree`, which windows a whole tree;
moving the annotation tree to that (one scroll box, groups included)
is the follow-up.

Rows sit at `index * rowHeight` in the box; `scrollTo` sends that index to the browser over
a channel. The box sets `overflow-anchor: none`: with Chrome's scroll anchoring on, a
growing top spacer shifted `scrollTop` by the overscan and the target landed out of view.

UI tests that read annotations from the list's DOM see only the rows in view.
