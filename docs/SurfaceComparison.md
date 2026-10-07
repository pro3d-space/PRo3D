# Surface Comparison

Compares two surfaces (for example two reconstructions of the same scene) by measuring the
distance between them inside small areas you place on the terrain.

Open it with the **Surface Comparison** dashboard mode; it adds the **Comparison** panel.

## Workflow

1. In the Comparison panel pick **Surface1** and **Surface2** (they need distinct names).
   **T** in the 3D view then flips between them, one visible at a time.
2. Select the **Comparison area** tool (crop icon in the tool strip) and click the terrain
   (Ctrl+click unless [Direct Tool Mode](DirectToolMode.md) is on) to place an area. The new
   area is shown as a sphere with the **Default Area Radius**.
3. While it is selected, press **+** / **-** to resize it and **Enter** to finish.
4. Press **Update Measurements**. For every area, PRo3D samples the vertices of both surfaces
   inside it and computes their distances (**Distance Calculation Mode**: `SurfaceNormal`
   or `Spherical`). The sphere is replaced by the sampled points, coloured green
   (close) to red (far), with grey lines connecting each pair. Select an area to see its
   colour legend and statistics. Clicking the **Area Comparison** header recomputes only
   the areas.

**Update Measurements** also compares the two surfaces' coordinate systems (size along the
axes and their rotation; the **Origin** drop-down picks the centre). **Export** writes everything
to JSON in the PRo3D home directory.

*Annotation Length Comparison* is not functional: it needs the "Bookmark" annotation projection,
which was removed (see `Projection.ofInt` in `Annotation-Model.fs`).

The full illustrated walkthrough is in the user manual,
[`PRo3D_ShortUserManual/SurfaceComparison.tex`](PRo3D_ShortUserManual/SurfaceComparison.tex);
the test protocol covers it as tc:45–48.

There is no full-surface difference map: distances are computed only inside the areas you place.

## Notes

- The calculation runs on a background thread and makes both surfaces visible and active.
- The difference points are rendered relative to the area centre so they stay stable at
  planetary distances.
- Comparison state, areas included, is saved with the scene under `comparisonApp`.
- Each area logs one line: the vertex count on each surface, the fitted plane normal and the
  ray origin of the Spherical mode.
- Spherical mode casts from the centre of both surfaces together, so a local patch can be
  compared against a global model. SurfaceNormal fits its plane on the denser of the two
  vertex sets.
- Fine meshes in kilometre units (facets below ~30 cm) are hittable through the ray-direction
  scaling described in [KdTrees.md](KdTrees.md#small-triangles-ray-direction-scaling).
- Tests: `src/Tests/SurfaceComparisonTest.fs`. The DART Dimorphos v003 OBJs are downloaded from
  the PDS Small Bodies Node on first use (`$PRO3D_TEST_DOWNLOADS`, else
  `%LOCALAPPDATA%\PRo3D\test-downloads`). Those cases skip offline.
