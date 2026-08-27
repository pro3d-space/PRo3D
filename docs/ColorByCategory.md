# Color by Category

Colors every annotation by one of its attributes instead of by its own `color` field.

Synopsis: display-only recoloring of all annotations by a shared attribute
Status: Released
Interacts with: the Dip&Strike color legend (both are drawn by `FalseColorLegendApp`)

The override is purely visual — no annotation is ever written, so switching the panel off
restores the stored colors exactly. This mirrors how the Dip&Strike legend colors discs.

# UI

*Annotations → Color by Category* (`GuiEx.accordion "Color by Category"`). Unlike the other
annotation property panels this one is **global**, not per selection.

| Row | Meaning |
| --- | --- |
| *enable* | turns the override on; off restores each annotation's own color |
| *attribute* | which attribute drives the color |
| *no value* | color for annotations that have no value for the attribute (a polyline asked for a diameter, an annotation with no planar fit asked for dip, uncomputed results) |

The rest of the panel depends on the kind of attribute:

| Kind | Attributes | Panel | Legend |
| --- | --- | --- | --- |
| **categorical** (discrete) | Annotation type, Semantic, Surface | one color picker per category, *reset colors* | **none** |
| **cyclic** | Bearing, Dip azimuth, Strike azimuth | *show legend* toggle only | hue wheel strip, 0°–360° |
| **numeric** (continuous) | Slope, Length, Way length, Height, Height delta, Avg altitude, True/Vertical thickness, Area, Major/Minor diameter, Line thickness, Dip angle | the standard false-color ramp properties (*show legend*, min, max, interval, colors, invert) plus *fit range to data* | false-color bar |

Categorical attributes deliberately have no on-screen legend: the panel already lists every
category next to its color, and there is no *show legend* toggle to switch one off with.

Colors that the user has not overridden come from a qualitative palette
(`ColorByCategory.palette`) — annotation colors default to a sequential blue ramp, which is
poor for telling categories apart. Overrides are keyed `"<attribute>/<label>"`, so each
attribute keeps its own set and *reset colors* only clears the current one.

Switching the attribute auto-fits the ramp bounds to the data (`fitRange`), so a useful
gradient shows up immediately.

# Legend placement

The legend sits at the top left of the render view, in the same spot as the other color
legends (`Gui.colorByCategoryAttributes`, `left: 0%; top: 25%`). Its attribute map is a copy
of `Gui.falseColorAttributes` widened to 170px, because the hue wheel drawn for the cyclic
attributes captions itself and the caption does not fit into 55px.

**Known issue:** every render-view legend is pinned to `left: 0%` — Dip&Strike, Color by
Category, surface scalars, the projected image and the area comparison all draw on top of each
other when more than one is switched on. They are not yet told apart or laid out side by side.

# Implementation

- Model: [`src/PRo3D.Base/Annotation/ColorByCategory-Model.fs`](../src/PRo3D.Base/Annotation/ColorByCategory-Model.fs).
  `numericLegend : FalseColorsModel` carries the ramp; its `useFalseColors` field doubles as
  the panel's single *show legend* toggle. Persisted with the drawing model
  (`Groups-Model.fs`); `attribute` is written as an `int`, colors as `C4b` strings.
- App: [`src/PRo3D.Base/Annotation/ColorByCategoryApp.fs`](../src/PRo3D.Base/Annotation/ColorByCategoryApp.fs).
  - pure core (`colorOf`, `colorOfValue`, `colorOfCategory`, `categories`) over `Annotation`
  - `Settings` is a plain snapshot of the panel, so per-annotation resolution stays pure and
    is hoisted out of the renderer's annotation loop
  - `resolve` is token-based (`GetValue t`) so the packed renderer's `AVal.custom` picks up
    every dependency and the color buffers rebuild on any panel edit — no explicit
    invalidation
  - `resolveAdaptive` is the `aval` form, for the annotation list icons and the
    per-annotation scene graph path
  - `ColorByCategory.disabled` is a permanently-off instance for callers that do not offer
    the feature (OpcViewer)
- Rendering: [`PackedRendering.fs`](../src/PRo3D.Core/Drawing/PackedRendering.fs) takes the
  model and resolves colors while building the packed vertex buffers.
- GUI wiring: `Gui.colorByCategoryLegend` and `Gui.viewColorByCategory` in
  [`ViewerGUI.fs`](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs).

# Adding an attribute

1. add the case to `ColorCategoryAttribute` in `ColorByCategory-Model.fs` and run `adapt.cmd`
2. add it to `label` and `unitOf`
3. extract the value in **both** `valueOf` (plain `Annotation`) and `valueOfAdaptive`
   (token-based) — they must agree
4. if it is discrete, add it to `isCategorical` and to `categories`; if it wraps at 360°, add
   it to `isCyclic`

Enum cases are appended, never renumbered: the attribute is persisted as its **integer**
value, and the palette fallback for enum-backed categories is keyed on the same ordinal.
Case *names* matter too — category color overrides are stored under `"<attribute>/<label>"`
with both parts printed via `%A`, so renaming a case orphans the colors saved for it.
