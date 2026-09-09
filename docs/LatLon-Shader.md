# Feature LatLon Shader

Synopsis: Additive latitude/longitude graticule drawn on OPC planetary surfaces.
Status: Work-In-Progress (Phase 1 — grid lines; Phase 2 — degree-indicator labels)
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
`referenceSystem.planet` and switches on `LatLonShaderApp.hasBody`.

| Field | Meaning |
|---|---|
| enabled | Draw the graticule on this surface. |
| lat interval | Degrees between parallels. Chosen from the integer divisors of 360 (`1, 2, 3, 4, 5, 6, 8, 9, 10, 12, 15, 18, 20, 24, 30, 36, 40, 45, 60, 72, 90, 120, 180, 360`) so the spacing is equidistant and the equator and poles always lie on a line. |
| lon interval | Degrees between meridians, same divisor list, independent of the latitude interval. |
| line color | Colour blended into the surface along the grid lines (default amber, readable on Mars terrain). |
| line width | Grid line width in screen pixels; constant while zooming. |
| show labels | Degree-indicator text on the lines. **Phase 2** — the flag persists now, rendering follows. |
| text size | Label size in screen pixels (**Phase 2**). |

# Implementation

- Model / update / view: `LatLonShaderModel` in
  [src/PRo3D.Core/VisualizationAndTFModel.fs](../src/PRo3D.Core/VisualizationAndTFModel.fs),
  `LatLonShaderApp` in
  [src/PRo3D.Core/Surface/LatLonShaderApp.fs](../src/PRo3D.Core/Surface/LatLonShaderApp.fs).
  Held per surface as `Surface.latLonModel`
  ([src/PRo3D.Core/Surface-Model.fs](../src/PRo3D.Core/Surface-Model.fs)); routed through
  `SurfaceProperties.LatLonShaderMessage`. Adding the field needs **no scene version
  bump** — `Surface.read1` reads it with `Json.readOrDefault ... LatLonShaderModel.initial`.
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
  screen-space distance to the nearest grid line (`ddx`/`ddy` for a constant pixel
  width), and blends `LatLonLineColor` over the incoming colour. Per-surface uniforms
  `LatLonGridParams` (`X` = lat interval°, `≤ 0` disables; `Y` = lon interval°;
  `Z` = line width px) and `LatLonLineColor` are fed from `viewSingleSurfaceSg`.

# Caveats

- Longitude lines are suppressed one pixel wide at the ±180° seam and where meridians
  converge at the poles (the screen-space derivative blows up there); the parallels
  are unaffected.
- Non-planetary reference frames (`Planet.None` / `JPL` / `ENU`) render no graticule.
- The grid follows the planet frame, not a per-surface Transformation offset — it
  stays consistent with the coordinate readout.
- OBJ surfaces are not supported (they use `objEffect`), same as Contours.
- Phase 1: the *show labels* toggle and *text size* control persist but have no
  visible effect until Phase 2.
