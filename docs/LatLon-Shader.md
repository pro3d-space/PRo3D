# Feature LatLon Shader

Synopsis: Additive multi-scale latitude/longitude graticule drawn on OPC planetary surfaces.
Interacts with: [Reference System / SPICE](../ai/DOMAIN.md), [Contour Lines](./Contour-Lines.md)

The LatLon shader is an **overlay**: it is composed into the surface effect stack
right after the contour-line shader and blends grid lines over whatever the surface
already renders. It never replaces another shader, and it is configured per surface.

# UI

Surfaces panel &rarr; **LatLon Shader** accordion (directly below **Contours**).

The graticule only has a meaning once the reference system is bound to a celestial
body. When the reference system is `None`, `JPL` or `ENU` the accordion hides every
parameter and shows the italic hint *"Set reference system to a body to display the
latlon shader"* instead. The controls reappear as soon as a body is selected
(Reference System panel). The gate is `LatLonShaderApp.view`, which takes
`referenceSystem.planet` and switches on `LatLonShaderApp.hasBody`
(`CooTransformation.getConvention <> NonPlanetary`).

| Field | Meaning |
|---|---|
| enabled | Draw the graticule on this surface. |
| lat lines | Three checkboxes — **1°**, **5°**, **15°** parallels. Independent; any combination can be on. Each level has a fixed screen line width: 1° → 0.5 px, 5° → 1.0 px, 15° → 2.0 px. |
| lon lines | Same three checkboxes for the meridians, independent of the parallels. |
| line color | Colour blended into the surface along the 1°/5°/15° grid lines (default amber, readable on Mars terrain). |

The **equator** (yellow) and **prime meridian** (red) are always drawn at 2.5 px
whenever the shader is enabled on a body, regardless of the checkboxes and the
line-colour picker.

Line widths are constant in screen pixels while zooming. Where several levels
coincide (e.g. a point on the 15° line is also on the 5° and 1° line) the shader
paints fine-to-coarse, so the widest matching line wins; the equator / prime
meridian paint last and sit on top.

# Implementation

- Model / update / view: `LatLonShaderModel` in
  [src/PRo3D.Core/VisualizationAndTFModel.fs](../src/PRo3D.Core/VisualizationAndTFModel.fs),
  `LatLonShaderApp` in
  [src/PRo3D.Core/Surface/LatLonShaderApp.fs](../src/PRo3D.Core/Surface/LatLonShaderApp.fs).
  The model holds six independent `bool`s (`lat1`/`lat5`/`lat15`, `lon1`/`lon5`/`lon15`)
  plus `enabled` and `lineColor`. `LatLonShaderApp.levels = [1; 5; 15]` is the single
  source of the offered granularities. Held per surface as `Surface.latLonModel`
  ([src/PRo3D.Core/Surface-Model.fs](../src/PRo3D.Core/Surface-Model.fs)); routed through
  `SurfaceProperties.LatLonShaderMessage`.
- Persistence: `LatLonShaderModel` carries its own `version` (`current = 1`) with
  `FromJson`/`ToJson`. `read0` migrates pre-multi-scale scenes (single lat/lon
  interval + width) by keeping `enabled` / `lineColor` and taking the v1 defaults for
  the granularity toggles; `read1` reads the toggles with `Json.readOrDefault`.
  `Surface.read1` reads the whole model with `Json.readOrDefault ... LatLonShaderModel.initial`,
  so adding it needs **no scene version bump**.
- Latitude/longitude are computed **on the CPU in `double`** per patch vertex in
  [src/PRo3D.Core/Surface/Surface.Sg.fs](../src/PRo3D.Core/Surface/Surface.Sg.fs)
  (`getVertexAttributes`), packed as `(sinφ, cosφ, sinλ, cosλ)` into the `LatLonSinCos`
  vertex attribute (sin/cos so linear interpolation does not tear at the ±180° seam).
  Latitude is corrected from planetocentric to **planetographic** with a closed-form
  oblate-spheroid term `latG = atan(tan latC / (1 - f)²)`, where the body flattening
  `f` is derived once per body from `CooTransformation.tryGetBodyRadius` — this matches
  the latitude PRo3D's coordinate readout shows without a per-vertex native SPICE call.
  The attribute is baked for every planetary OPC surface (`Sg.applyLatLonGrid`,
  [src/PRo3D.Core/Surface/OpcRenderingProperties.fs](../src/PRo3D.Core/Surface/OpcRenderingProperties.fs)),
  gated on the body rather than the enable flag, so toggling the overlay or changing
  its parameters is a pure uniform change with no patch reload.
- Shader: `Shader.latLonLines` in
  [src/PRo3D.Base/Utilities.fs](../src/PRo3D.Base/Utilities.fs), added to `surfaceEffect`
  in [src/PRo3D.Viewer/Viewer/Viewer-Utils.fs](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs)
  after `Shader.contourLines`. It reconstructs lat/lon with `atan2`, measures the
  screen-space derivative (`ddx`/`ddy`) so each line keeps a constant pixel width, and
  composites each active level over the incoming colour with `overlayLine`
  (fine-to-coarse, then equator/prime meridian). The equator and prime meridian use
  `latLonCoverage` with a 360° interval so only `lat = 0` / `lon = 0` produce a line.
  Per-surface uniforms, fed from `viewSingleSurfaceSg`:
  - `LatLonLatLevels : V4f` — `X ≤ 0` disables the whole overlay (off, or a
    non-planetary body); `Y`/`Z`/`W` are 1/0 flags for the 1°/5°/15° parallels.
  - `LatLonLonLevels : V4f` — `X`/`Y`/`Z` are 1/0 flags for the 1°/5°/15° meridians.
  - `LatLonLineColor : V4f` — colour of the 1°/5°/15° lines (equator/prime meridian
    colours are fixed in the shader).

# Caveats

- Longitude lines are suppressed one pixel wide at the ±180° seam and where meridians
  converge at the poles (the screen-space derivative blows up there); the parallels
  are unaffected.
- The 1° grid is suppressed when zoomed out far enough that the per-pixel angular
  derivative exceeds half the interval (`latLonCoverage`'s `w < intervalDeg * 0.5`
  guard), which also keeps the coarser levels from aliasing.
- Non-planetary reference frames (`Planet.None` / `JPL` / `ENU`) render no graticule.
- The grid follows the planet frame, not a per-surface Transformation offset — it
  stays consistent with the coordinate readout.
- OBJ surfaces are not supported (they use `objEffect`), same as Contours.
