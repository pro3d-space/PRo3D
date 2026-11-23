# Tooltips

PRo3D draws its own tooltips. The browser's native tooltip (an element's `title`
attribute) appears on macOS only after about 1–1.5 s, a delay the page cannot change, so
every `title` in the UI is shown by a small script instead, after **250 ms**.

## Behaviour

- Shown after hovering 250 ms, next to the pointer; flipped to the other side of the
  pointer near a window edge.
- Hidden on leaving the element, on a click, a key or the mouse wheel, and when the
  window loses focus.
- A `\n` in the text is a line break, as in a native tooltip.
- A title that changes while it is shown (e.g. the SPICE kernel status) updates in place.
- Works while PRo3D is busy: it runs entirely in the page, like the busy indicator.

Semantic UI's CSS tooltips (`data-tooltip`, e.g. on the navigation gizmo) are not
affected.

## Writing a tooltip

Nothing special: set a `title` attribute (`attribute "title" "…"`). The script picks up
every element with one, in every panel.

## How it works

`resources/utilities.js`, section *tooltips*. On hover the script moves the element's
`title` to `data-pro3d-title` and leaves an empty `title=""` in its place, so the native
tooltip never shows, and shows its own after the delay. On leave it puts the `title`
back, so the DOM is again exactly what aardvark.media's diff expects. While an element is
hovered a `MutationObserver` watches its `title`: a new one from the server is taken
over instead of reaching the browser, and one the server removes stays removed (the
empty placeholder is what keeps that removal observable).
