# Surface Distance Filter

Per-surface filter that hides all geometry farther away than a given radius from the
surface's **home position**. Useful to cut away distant parts of a large OPC surface
(e.g. to look at a single outcrop without the rest of the tile set in the way).

## Using it

The controls live in the surface properties (select a surface in the *Surfaces* panel):

| Row | Meaning |
| --- | --- |
| `DistanceFilter` | enables the filter |
| `FilterDistance` | radius in meters around the home position that stays visible |
| `Home Position`  | stores the **current camera position** as the reference point; shows `set` / `not set` |

**The filter has no effect until a home position is set.** There is no other reference
point, so with `homePosition = None` the filter is silently disabled
(`Viewer-Utils.fs`, `filterByDistance`). Workflow:

1. navigate to the area of interest,
2. click *Home Position* (the button stores the camera view and switches to `set`),
3. tick *DistanceFilter* and adjust *FilterDistance*.

The home position is saved with the scene.

## Implementation

The test runs in the geometry shader `Shader.triangleSizeFilter` (`Viewer-Utils.fs`),
together with the triangle size filter. Everything happens in **view space**: the home
position is transformed on the CPU (`HomePositionViewSpace`, a clean `V3f`) and each
triangle is discarded unless all three vertices are within `FilterDistance` of it. This
keeps the comparison numerically stable at planetary scale — see
[ai/CONVENTIONS.md](../ai/CONVENTIONS.md) on precision.

`FilterDistance` is uploaded as `float32` (`AVal.map float32`) and read as `float32` in
the shader; a mismatch here silently clips the whole surface away.
