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
| *attribute type* | *annotation measurement* (an attribute of the annotation itself, below) or *surface attribute* (a scalar layer of the surface under it — see [below](#color-by-a-surface-scalar-layer)) |
| *attribute* | which attribute drives the color — the measurement dropdown, or the surface scalar-layer dropdown when *attribute type* is *surface attribute* |
| *no value* | color for annotations that have no value for the attribute (a polyline asked for a diameter, an annotation with no planar fit asked for dip, uncomputed results, a point that missed the surface) |

Every entry of the *attribute* dropdown carries a tooltip (`ColorByCategory.tooltip`) saying
what the value is measured from and which annotations actually have one — without it the panel
gives no clue why a whole set came out in the no-value color. It is a plain `title` attribute
on the `<option>`, not a Semantic UI popup: an `<option>` may only contain text and the browser
draws the open dropdown itself, so a JS popup cannot attach to it. `PRo3D.Core.UI.wrapToolTip`
sets the same attribute under the hood, but it lives in PRo3D.Core, which compiles *after*
`ColorByCategoryApp.fs`, so it is not reachable from here.

The rest of the panel depends on the kind of attribute:

| Kind | Attributes | Panel | Legend |
| --- | --- | --- | --- |
| **categorical** (discrete) | Annotation type, Semantic, Surface | one color picker per category, *reset colors* | **none** |
| **cyclic** | Bearing, Dip azimuth, Strike azimuth | *show legend* and *interval* | hue wheel strip over one period, banded into sectors |
| **numeric** (continuous) | Slope, Length, Way length, Height, Height delta, Avg altitude, Area, Line thickness, Dip angle | the standard false-color ramp properties (*show legend*, min, max, interval, colors, invert) plus *fit range to data* | false-color bar |

Categorical attributes deliberately have no on-screen legend: the panel already lists every
category next to its color, and there is no *show legend* toggle to switch one off with.

## Color by a surface scalar layer

With *attribute type = surface attribute* the annotations are colored by a **scalar attribute
(AARA layer)** of the surface — elevation, roughness, a band ratio — sampled at the points the
user clicked when drawing each annotation, rather than by anything measured on the annotation.

- The *attribute* dropdown lists the distinct scalar-layer **labels** across every loaded
  surface's `.opcx` (`Surface.scalarLayers`). A layer only appears if that file lists it. The
  label is matched against the per-vertex `.aara` name **case-insensitively**; multi-channel
  layers use channel 0.
- Each point is sampled by casting a ray straight down and reading whichever visible surface it
  hits (`ProfileAttributeExtraction.sampleAt`) — not pinned to the annotation's origin surface.
  A point that misses, or whose surface has no such layer, is *no value*.
- *coloring* row:

  | Mode | Control-point dots | Line & polygon fill |
  | --- | --- | --- |
  | **annotation** | mean color | mean color |
  | **pointwise** | each dot by its own sampled value | neutral gray |
  | **both** | each dot by its own sampled value | mean color |

  "Mean color" is the ramp color of the **average of the finite sampled values** for that
  annotation (all-missed → the whole annotation is *no value*). In *pointwise* / *both* a
  colored dot is drawn at **every** control point of every annotation, any geometry —
  polylines and polygons otherwise show no dots.
- **Sampling is manual.** Press **resample surface**. It runs on the UI thread — one ray cast
  per control point, and the first hit on a patch pulls its position grid — so it is not
  automatic. The button turns **orange** whenever the cached colors are out of date: a point
  was moved or added, an annotation was drawn or deleted, the reference frame changed, or the
  layer was switched. Staleness is a live comparison of an input *stamp*
  (`ColorByCategory.stampOf`: layer + reference-up + every annotation's point positions)
  against the stamp stored with the samples, so it catches every edit path, undo included.
- The sampled values are **not saved** in the `.pro3d` scene (they are derived, large, and go
  stale on any edit). A freshly loaded scene shows *no value* in surface mode until you
  resample.
- *fit range to data* fits the ramp to the sampled values; the legend is the standard
  false-color bar.

## Dip angle, dip azimuth and strike azimuth are gated on the geometry

These three read `annotation.dnsResults`, but the presence of that record does **not** mean the
annotation is a dip and strike measurement. `getFinishedAnnotation` fits a plane to *every*
geometry — the ellipse tools need one — so an ordinary polyline or polygon with enough points
carries dip and strike results too.

`ColorByCategory.hasDipAndStrike` therefore gates the three attributes on
`Geometry.DnS | Geometry.TT`, the same pair `DipAndStrike.reCalculateDipAndStrikeResults`
keeps. Without that gate the panel colored polylines and polygons as though they had a dip,
and the coloring then changed *by itself* the first time the reference system was edited:
recalculation drops the results of every other geometry, so those annotations silently fell
back to the no-value color.

## Directional vs. axial cyclic attributes

Azimuths wrap, so a linear two-color ramp would put 359° and 1° — one degree apart on the
ground — at opposite ends of the scale. They get a hue wheel instead, where the two ends of
the period meet. `ColorByCategory.cyclicPeriod` says how long that period is:

| Attribute | Period | Why |
| --- | --- | --- |
| Dip azimuth | **360°** | *Directional* — it says which way the plane dips, so 45° and 225° really are opposite |
| Strike azimuth | **180°** | *Axial* — a strike line has no preferred end. `strikeAzimuth` is always `dipAzimuth ± 90`, so two planes sharing a strike line but dipping opposite ways read 180° apart and must still come out the same color |
| Bearing | **180°** | *Axial* — it is the azimuth of the chord from the annotation's first to its last point, so redrawing a polyline backwards flips it by 180° without changing its orientation |

Only the **coloring** folds. The annotation Properties panel and the CSV / Attitude exporters
keep reporting the raw 0–360° value, so "Bearing: 200°" on one annotation and "20°" on another
correctly share a hue.

Slope and Dip angle *look* angular but are not cyclic: both are inclinations from horizontal
(`asin(…)` in degrees, −90…90 and 0…90), so their endpoints are extremes rather than
neighbours and a plain ramp is right.

## Both legends are classified scales

Neither legend is a continuous blend — `interval` quantizes the *coloring*, and the legend
draws what the coloring does. Two annotations in the same class get exactly the same color.

| | How `interval` is used |
| --- | --- |
| numeric bar | directly: `FalseColorLegendApp.Draw.getColorForValue` buckets with `round((value - lower) / interval)`, one hue per bucket. `createStopps` draws the hard edges by emitting two gradient stops at the same offset — above 100 buckets it stops doing that and the bar goes smooth |
| hue wheel | via `ColorByCategory.cyclicSectors`, which snaps `period / interval` to a **whole number** of sectors. An interval that did not divide the period would leave a partial sector straddling 0°, breaking the wrap the wheel exists for — so the requested width is a request, and the panel prints the count and width actually used |

Each sector takes the hue of its own start, so 0° keeps the hue it had when the wheel was
continuous. Switching to a cyclic attribute sets the interval to `period / 12` (30° for a dip
azimuth, 15° for a bearing or strike) — `fitRange` has to do this, or the interval left over
from the previous numeric attribute carries across, and a fitted `range / 20` can mean
thousands of sectors, i.e. no visible banding at all. A degenerate interval (zero, negative,
NaN) falls back to a single sector rather than dividing by zero.

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
  (`Groups-Model.fs`); `attribute`, `attributeKind` and `surfaceColoring` are written as
  `int`, colors as `C4b` strings. `surfaceSamples : SurfaceSampleStore` (`[<TreatAsValue>]`,
  one `float[]` per annotation + its mean, plus the input `stamp`) is **not** written — it is
  refilled by the *resample* pass. `attributeKind` / `surfaceLayer` / `surfaceColoring` are
  additive, read with `Json.tryRead` + a default, so no version bump.
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
  - the surface path is parallel and does not touch `resolve`'s measurement logic:
    `surfaceMeanColor` / `surfacePointColors` read the precomputed `surfaceSamples` store on
    `Settings`; `resolvePoints` returns the per-control-point array the packed points builder
    needs; `stampOf` is the staleness hash
- Rendering: [`PackedRendering.fs`](../src/PRo3D.Core/Drawing/PackedRendering.fs) takes the
  model and resolves colors while building the packed vertex buffers. `fills` now also takes
  the model (surface / measurement coloring reaches the polygon fill); `points` emits a dot
  per control point in *pointwise* / *both*. Those dots are occluded by the terrain like the
  lines and fills are: `PointsShader.pointSpriteFragment` biases its depth exactly the way
  `PRo3D.Base.Shader.DepthOffset.depthOffsetFS` does, so an annotation on the far side of the
  planet is hidden by the planet.
- Sampling: `sampleSurfaceForCbc` in [`Viewer.fs`](../src/PRo3D.Viewer/Viewer/Viewer.fs) runs
  the `ResampleSurface` pass (`DrawingApp.update` has no `SurfaceModel` in scope). The
  `surfaceSamples` store is transient and Viewer-owned.
- GUI wiring: `Gui.colorByCategoryLegend`, `Gui.viewColorByCategory`,
  `Gui.surfaceScalarLayerLabels` and `Gui.colorByCategorySurfaceStale` in
  [`ViewerGUI.fs`](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs).

# Adding an attribute

1. add the case to `ColorCategoryAttribute` in `ColorByCategory-Model.fs` and run `adapt.cmd`
2. add it to `label`, `tooltip` and `unitOf`
3. extract the value in **both** `valueOf` (plain `Annotation`) and `valueOfAdaptive`
   (token-based) — they must agree
4. if it is discrete, add it to `isCategorical` and to `categories`; if it is an orientation,
   add it to `cyclicPeriod` with `Some 360.0` (directional) or `Some 180.0` (axial)

This is for *annotation measurement* attributes. **Surface** attributes are not enum cases —
the list is the surfaces' scalar-layer labels, gathered live by
`Gui.surfaceScalarLayerLabels`, so a new AARA layer needs nothing here.

Enum cases are appended, never renumbered: the attribute is persisted as its **integer**
value, and the palette fallback for enum-backed categories is keyed on the same ordinal.

Case *names* matter too — category color overrides are stored under `"<attribute>/<label>"`
with both parts printed via `%A`, so renaming a case orphans the colors saved for it.
