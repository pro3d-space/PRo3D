# Map Projection View

Synopsis: a panel showing a small body's OPC surfaces as a 2D map, **equirectangular** or
**polar stereographic**, with a lat/lon graticule. It runs standalone too.
Issue: [#772](https://github.com/pro3d-space/PRo3D/issues/772).
Interacts with: [LatLon Shader](LatLon-Shader.md), [Scene Body](SceneBody.md), [Window Layouts](WindowLayouts.md).

> **Phases 1, 1.5 and 2.** OPC surfaces with their primary texture, the graticule, pan and zoom,
> a map-space LoD decider, and annotations (lines, fills, points, ellipses). Not yet: annotation
> labels, dip-and-strike glyphs, picking or cursor readout, OBJ meshes, planets.

## Using it

- **In PRo3D:** the panel is part of the built-in *M2020* (default), *PRo3D Core* and *GIS*
  layouts, as a tab of the right-hand stack.
  - Closed it? Main menu (**☰**, top left) → *Layout* → *Reopen Panel* → *Map Projection*
    brings it back, as for any panel. It returns as a tab of the largest side stack, unselected
    and possibly behind that stack's overflow chevron.
  - Layouts are saved per user, so your own arrangement wins over the built-in one.
  - Or open `?page=mapprojection` directly.
- **Standalone:**

  ```
  PRo3D.MapProjection.exe --opc <dir> [--opc <dir> ...] [--annotations <file> ...]
                          [--frame DIMORPHOS_SHM] [--planet Dimorphos] [--port 4330] [--server]
  ```

  `--opc` takes an OPC directory (its patch hierarchies are found below it) or a single
  hierarchy. `--annotations` takes a PRo3D annotation file, or an SBMT structure file with points,
  ellipses or circles, read in `--frame` (SBMT lines and polygons aren't imported yet). Without `--server` it opens an Aardium window. With `--server` it only serves
  `http://localhost:<port>/` and runs until stdin closes; this is what the Playwright spec uses.

| Input | Effect |
|---|---|
| *Equirectangular / Polar north / Polar south* | Switches the projection and resets the view |
| Left-drag | Pan (grab and drag: the map point under the pointer follows it) |
| Mouse wheel | Zoom about the pointer (1× to 512×) |
| *Reset view* | Whole map, zoom 1 |

The panel only draws for **small bodies** (`CooTransformation.isSmallBody`: Phobos, Deimos,
Didymos, Dimorphos). For any other planet it shows a hint and creates **no render control**.
There is then no render task, no patch loading and no shader compile, so a Mars session
pays nothing, even with the panel in its layout.

In PRo3D the panel follows the scene's **planet**, which requires a body-fixed scene
(docs/SceneBody.md). A scene observing Dimorphos in `J2000` has planet `None`: its world axes
are not the body's, so longitude and latitude would be rotated. The panel then shows the hint,
just as MapView is unavailable there. The panel draws the scene body's OPC surfaces: surfaces
bound to the scene body, or unbound surfaces inheriting it.

## Map conventions

- **Planetocentric, raw body-fixed axes.** +Z is the pole and longitude 0 lies along +X,
  east positive, longitude in [−180°, 180°]. This matches PRo3D's coordinate readout and
  the LatLon overlay.
- **Open question: orientation.** Data in `DIMORPHOS_SHM` has its spin pole at −Z, so
  astronomical north is at the *bottom* of the map. Every axis assumption lives in one
  place (`Projection.fs` and the matching shader helpers), so a later rotation stays a
  local change.
- **Equirectangular:** x = longitude, y = latitude (radians). The map is 2:1.
- **Polar stereographic** (USGS convention), unit sphere: ρ = 2·tan(colatitude/2).
  - North map: longitude 0 points down and east is counterclockwise.
  - South map: longitude 0 points up.
  - Each map shows its hemisphere down to the equator (`Projection.defaultMaxColatitude`).
- **Depth** is the radius, normalised to the body's bounding radius. Where the body is not
  star-shaped from its centre (overhangs, boulders), the **outermost** surface wins.
- **Graticule:** every 15°. The equator is yellow and the prime meridian red, the colours
  of the LatLon shader.

## How it works

| File | Role |
|---|---|
| [`src/PRo3D.MapProjection/Projection.fs`](../src/PRo3D.MapProjection/Projection.fs) | Double-precision math: lon/lat/radius, forward and inverse, seam/pole classification, view matrix. The source of truth the tests check pixels against |
| [`src/PRo3D.MapProjection/Shaders.fs`](../src/PRo3D.MapProjection/Shaders.fs) | Map shader stages mirroring `Projection` |
| [`src/PRo3D.MapProjection/MapSg.fs`](../src/PRo3D.MapProjection/MapSg.fs) | Scene graphs. **The single composition the panel and the tests share** |
| [`src/PRo3D.MapProjection/MapAnnotations.fs`](../src/PRo3D.MapProjection/MapAnnotations.fs) | Annotations on the map: PRo3D's packed annotation buffers with the map shader stages; loading annotation files for the standalone app |
| [`src/PRo3D.MapProjection/MapProjectionApp.fs`](../src/PRo3D.MapProjection/MapProjectionApp.fs) | Model update (pan, zoom, projection), view, and `app` for standalone use |
| [`src/PRo3D.MapProjection/Program.fs`](../src/PRo3D.MapProjection/Program.fs) | `PRo3D.MapProjection.exe` |
| [`src/PRo3D.Viewer/Viewer/MapProjectionHost.fs`](../src/PRo3D.Viewer/Viewer/MapProjectionHost.fs) | Everything the viewer knows about the panel: which surfaces it draws and where they sit |

### Rendering

The projection is done in the shaders; there is no render-to-cube-map step. The research
behind that choice is in #772. In short, a cube map rendered from the body centre would
have to resample annotations (smearing pixel-width lines), would see the body from inside
(backfaces, inverted shading, reversed depth), and has a fixed resolution.

1. **Vertex stage** (`lonLatRadius`): body-centred position `ModelTrafo * pos` → (lon, lat, r).
2. **Geometry stage**, one per projection:
   - `equirectangular`:
     - Unwraps each triangle's longitudes relative to its first corner, so a triangle
       crossing ±180° stays contiguous.
     - The map repeats every 2π: each triangle is drawn at whichever of the copies −2π, 0
       and +2π can reach the viewport (`onScreen`). This covers the seam and a view panned
       or zoomed past ±180°. Phase 1 only copied triangles straddling the seam and left the
       part of a view beyond ±180° empty; the zoomed-seam LoD render test found it.
     - A triangle **containing a pole** (its longitudes wind once around) is drawn as a strip
       from its corners to the pole row, at the same copies.
     - The copies are written out instead of looping: FShade does not generate a geometry
       stage for a `for` loop around those yields.
   - `polarStereographic`: drops triangles that lie entirely beyond the cutoff or reach
     near the opposite pole, where ρ runs to infinity. There is no seam, because longitude
     only enters through sin/cos.

   Both write the clip position from the map view matrix (`MapViewProj`), composed on
   the CPU in double.
3. **Fragment stage:** `OPCFilter.improvedDiffuseTexture`.

The two projections form a local `Sg.effectPool`, so switching swaps the program without
reloading patches. The map shaders are **not** part of the viewer's shared OPC effect pool
and compile only when the panel first renders.

**Precision: a deliberate, gated exception to the local → view rule.** The body-centred
position is formed in float32. For the bodies the panel allows (≤ ~11 km, Phobos) that costs
at most ~1 mm. Planets would need a per-patch double anchor with float32 offsets (a map-space
`stableTrafo`) and a LoD decider.

### Surfaces and LoD

`MapSg.surfaces` builds the map's **own** PatchNodes with `OpcSg.build`, the path the
pro3d-tool render verbs and the sun shadow map use. It does not reuse the main view's nodes:
the LoD decider is a constructor argument, and the map needs a different one. Opening the
panel loads patches a second time.

**Map LoD (phase 1.5):** `MapSg.mapLod`, a small per-patch heuristic in map space. A patch
refines while its average triangle size (`RenderPatch.triangleSize`, from the hierarchy's
`AvgGeometrySizes`) covers more than `defaultTargetPixels` (2) map pixels.
- **Patches with a direction:** the patch is treated as a bounding sphere, and must also
  overlap the map window (including the ±2π copies, and the polar cutoff).
- **Stretch:** the longitude stretch toward the poles (equirectangular, capped at 20) and the
  stereographic scale sec²(colatitude/2) are taken at the patch's far edge.
- **Patches wrapping the body centre:** they have no direction to cull by, so triangle size
  alone decides, at their outer radius. On a small body most coarse patches are like this;
  in the Dimorphos test OPC both level-1 patches are, so zooming in loads all six leaves
  rather than only the visible ones.
- **Nothing disappears:** not refining only means the coarser parent is drawn.

`MapSg.finestLod` (always refine) remains for comparisons and benchmarks.

The host places each surface with `SunShadowMap.surfacePlacement`, the same placement the
main render uses. It passes only OPC surfaces of the scene body, so a Didymos surface in a
Dimorphos scene is left out.

### Annotations (phase 2)

The map reuses PRo3D's packed annotation buffers (`PackedRendering.linesNoIndirect`, `fills`,
`pointsGeometry`) and only swaps their shader stages.
- **Identity view:** each packer's `MV` uniform is then the annotation pivot, so `MV × pos` is
  the body-centred position (the same float32 exception as the surfaces). Points are
  view-transformed on the CPU, so their positions already are body-centred.
- **Fills:** body position, then the surfaces' projection stage (seam copies, pole caps, polar
  cutoff).
  - That stage carries a `SourceVertexIndex`, which is how FShade passes values it doesn't name
    through a geometry stage: fill colours here, texture coordinates for the surfaces.
- **Lines:** body position, then a line seam stage (unwrap from the first end, emit the −2π, 0 and
  +2π copies on screen; polar: drop segments beyond the cutoff), then PRo3D's own
  `LineShader.thickLine` for pixel widths.
- **Points:** a map vertex stage and a round-dot fragment. A dot exactly on ±180° shows on one
  edge only.
- **Ellipses** are sampled polylines already (SBMT import: 60 samples, drawn ellipses: 200), so
  they need nothing extra.
- **No densification:** a straight annotation segment stays straight in lon/lat. Draped
  annotations are densely sampled anyway; long "Linear" chords bend slightly wrong, most visibly
  near a pole.
- **Overlay:** annotations are drawn after the surfaces and the graticule, without a depth test,
  so an annotation under an overhang still shows.
- **Colours:** colour-by-category and the selection highlight work as in the 3D view.
- **Not yet:** labels, dip-and-strike glyphs, vertex handles and picking.

The only change to shared code: `PackedRendering.points` is split into `pointsGeometry` plus its
shader.

## Tests

A testing ladder: each rung is green before the next.

| Rung | Where | What |
|---|---|---|
| 1. CPU math | `src/Tests/MapProjectionMathTests.fs` | Round trips on every map, polar orientation, seam/pole classification, view fit and aspect, graticule |
| 2. Shader codegen | `src/Tests/MapProjectionShaderTest.fs` | Every map effect generates GLSL with its geometry stage and reads only attributes an OPC patch has. No GPU |
| 3. Headless render | `src/Tests/MapProjectionRenderTest.fs` | The Dimorphos OPC rendered through `MapSg`. For every pixel, the interpolated surface position must lie at the lon/lat the pixel means (independent of the shader's own lon/lat) |
| 4. Benchmark | `Tests.dll --bench-map` (`src/Tests/MapProjectionBenchmark.fs`) | Frame time per projection × LoD (finest vs root) × size, on the #719 harness |
| 5. Playwright | `tests-ui/tests/map-projection.spec.ts` | Standalone app: the surface texture is drawn, the graticule sits where the projection says, drag moves the map by exactly the drag, polar north puts the prime meridian below the pole, a planet gets the hint. In PRo3D, *Layout → Reopen Panel → Map Projection* opens the map as a panel, the `mapprojection` page shows the same map for a body-fixed Dimorphos scene, and the J2000 test scene gets the hint. The PRo3D cases use a fresh `PRO3D_LAYOUT_DIR` |

Notes on rung 3:
- It runs a flipped control: with mirrored rows the error must be large, proving the metric
  can fail.
- The textured case only checks that texture reaches the map. The test OPC's default layer
  (`DRACO_1`) is the raw DRACO frame stored as a 2:1 map raster, so the correct map shows that
  photo undistorted.

Phase 2 adds:
- **Rung 1:** a synthetic annotation fixture (`src/Tests/MapProjectionAnnotationFixture.fs`)
  with a round trip through a PRo3D annotation file. The fixture has:
  - a polyline across ±180°;
  - a polyline at 70° N;
  - a filled polygon;
  - two points;
  - an ellipse parsed from an SBMT row.

  Each has its own colour. `Tests.dll --write-map-annotations <file>` writes it; the committed
  `tests-ui/fixtures/map-projection-annotations.pro3d.ann` came from there. Synthetic on purpose:
  no real catalog gets into a test.
- **Rung 2:** the six annotation effects (fills, lines, points × projection) generate GLSL and read
  nothing the packed buffers don't provide.
- **Rung 3:** the fixture rendered headless, equirectangular, zoomed across the seam, and polar
  north. At every CPU-projected check point (along each segment, each point, inside the fill) the
  pixel must have that annotation's colour; a flipped control must fail.
- **Rung 4:** `--bench-map` adds `maplod+boulders` arms when the non-public boulder catalog is on the
  machine (`PRO3D_PRIVATE_TESTDATA/shapemodels/testdata`). It measures only and writes no image.
- **Rung 5:** Playwright checks each fixture annotation's colour at its lon/lat, in the standalone app
  (`--annotations`) and in PRo3D, with the fixture as the scene's `.ann` file.

Phase 1.5 adds to rung 3:
- a CPU walk of the Dimorphos hierarchy through `mapLod`: only the root at zoom 1, leaves when
  zoomed in;
- renders with `mapLod`: whole map, zoom 16 across the seam, polar zoom 4. Each must still
  cover the view and put every surface point at its pixel's lon/lat.

### Benchmark (Windows laptop, NVIDIA RTX 500 Ada)

Median GPU ms per frame (draw calls), Dimorphos test OPC (`g_01960mm`), zoom 1, with the
all-copies geometry stage:

| Size | Equirect finest | Equirect **map LoD** | Equirect root | Polar finest | Polar **map LoD** | Polar root |
|---|---|---|---|---|---|---|
| 1024×768 | 15.0 (7) | **2.4 (2)** | 2.4 (2) | 9.5 (7) | **1.6 (2)** | 1.6 (2) |
| 1920×1080 | 19.3 (7) | **2.5 (2)** | 2.6 (2) | 9.5 (7) | **1.6 (2)** | 1.6 (2) |

At zoom 1 the map LoD costs what the root costs, about 6× less than full detail.

With the non-public boulder catalog (4,757 SBMT ellipses) drawn over the map LoD surface:

| Size | Equirect map LoD + boulders | Polar map LoD + boulders |
|---|---|---|
| 1024×768 | 3.3 | 2.1 |
| 1920×1080 | 3.4 | 2.1 |

So annotations add about 0.9 ms (equirectangular) and 0.5 ms (polar) for ~290 k line segments.

In the app (`tests-ui/src/probe-smoothness.ts`, `probe-map-pan.ts`; frames timestamped as
they arrive in the browser):

| Situation | Before phase 1.5 | With map LoD |
|---|---|---|
| Main view camera drag, no map panel | 75–87 fps | 89 fps |
| Main view camera drag, map panel visible | 35–86 fps, gaps up to 868 ms | mostly 72–85 fps, gaps ~55 ms (one 36 fps segment) |
| Panning the map | 21 fps (478×393, in PRo3D) | one frame per mouse event: 60 fps at 60 events/s (standalone) |

A render control only renders on change, so these are costs while something moves.

## Later phases

- **Annotations, next steps:**
  - picking via `pickRenderTarget` with the same stages;
  - labels;
  - chord densification: on the CPU, or with tessellation isolines, which set their subdivision
    level per segment at runtime.
- **Planets:** a per-patch double anchor, the LoD decider, planetographic/west-positive
  conventions.
