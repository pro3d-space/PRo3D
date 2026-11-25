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

## For developers

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
