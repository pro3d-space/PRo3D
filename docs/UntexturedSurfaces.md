# Untextured Surfaces

Switches a surface's texture off so that only its shape is visible: the surface is drawn in a
plain light grey and shaded by the lighting alone. Use it when a surface has no real texture
(the local SPC patches of Dimorphos only carry a checkerboard), or to compare reconstructions by
their geometry without the texture getting in the way, for example in
[Surface Comparison](SurfaceComparison.md).

## Usage

Select the surface. In **Surfaces → Properties**, untick **Texture** (the row above
*Primary Texture*). Tick it again to bring the texture back. Each surface has its own setting, and
it is saved with the scene (`textureEnabled`; scenes without the key load textured).

How the grey surface is lit:

- **Sun lighting on** (GIS → Lighting *Sun* or *Sun + shadows*, with the surface on a SPICE body):
  the sun shades it like a textured surface (Lommel-Seeliger, cast shadows in *Sun + shadows*).
- **Sun lighting off**: a light fixed to the camera, from the upper left, shades every facet
  by its orientation, so the relief stays readable from any direction.

Overlays still draw on top: secondary texture, contour lines, projected images, lat/lon grid,
outcrop traces and the selection highlight.

## Implementation

- Model: `Surface.textureEnabled` (`Surface-Model.fs`), read with `Json.tryRead` (default
  `true`); action `ToggleTextureEnabled` (`Surface-Properties.fs`).
- Uniform `SurfaceTextureEnabled`, per surface (`viewSingleSurfaceSg`, `Viewer-Utils.fs`).
- OPC effect (`surfaceEffectVariant`): `Shader.untexturedSurface` replaces the texture colour
  right after `improvedDiffuseTexture`, in the surface-colour band. `Shader.untexturedHeadlight`
  runs in the lighting band after the sun stages and only acts while the sun light is off.
  It takes the face normal from screen-space derivatives of the view-space position, so it
  needs no geometry stage and works in every effect variant, all in view space.
- OBJ surfaces (`objEffect`): `textureOrLightingIfPossible` falls back to its existing
  camera-light shading when the texture is off.
