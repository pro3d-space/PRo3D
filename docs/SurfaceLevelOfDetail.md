# Surface level of detail

An OPC surface is a quadtree of patches. A patch is replaced by its children when

1. its bounding box intersects the view frustum, and
2. its average triangle size (`AvgGeometrySizes`), at the box's closest point to the camera,
   exceeds the pixel budget set by the surface's **Quality** slider.

**Lod colors** (Config) colours each level.

The frustum test is `ViewProjection.intersects` (Aardvark.Rendering.Common), called by the
deciders in `src/PRo3D.Core/Surface/Surface.Sg.fs`: a conservative plane test, OpenGL clip
space to match `Frustum.projTrafo`. Do not project box corners to NDC instead; corners
behind the camera flip in the divide by w, and patches around the camera stay coarse.
Regression test: `src/Tests/LodCullTest.fs`.
