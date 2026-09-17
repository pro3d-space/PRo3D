# Map Projection View

Synopsis: a panel showing a small body's OPC surfaces as a 2D map, **equirectangular** or
**polar stereographic**, with a lat/lon graticule. It runs standalone too.
Issue: [#772](https://github.com/pro3d-space/PRo3D/issues/772).
Interacts with: [LatLon Shader](LatLon-Shader.md), [Scene Body](SceneBody.md), [Window Layouts](WindowLayouts.md).

> **Phase 1 (proof of concept).** OPC surfaces with their primary texture, plus the
> graticule, pan and zoom. Not yet: annotations (phase 2), a map LoD decider (phase 1.5),
> picking or cursor readout, OBJ meshes, planets.

## Using it

- **In PRo3D:** *Layout → Reopen Panel → Map Projection*, or open `?page=mapprojection`.
- **Standalone:**

  ```
  PRo3D.MapProjection.exe --opc <dir> [--opc <dir> ...] [--planet Dimorphos] [--port 4330] [--server]
  ```

  `--opc` takes an OPC directory (its patch hierarchies are found below it) or a single
  hierarchy. Without `--server` it opens an Aardium window. With `--server` it only serves
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
     - Unwraps each triangle's longitudes relative to its first corner.
     - A triangle crossing ±180° is drawn unwrapped, plus a copy shifted by 2π; the viewport
       clips the overhang.
     - A triangle **containing a pole** (its longitudes wind once around) is drawn as a
       strip from its corners to the pole row, plus its shifted copy.
     - The cap is written out twice instead of looping: FShade does not generate a geometry
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
the LoD decider is a constructor argument, and the map needs a different one.
- **Phase 1** uses `MapSg.finestLod` (always refine), because level 0 of a small body fits
  in memory: the Dimorphos test OPC has 6 leaf patches.
- **Cost:** opening the panel loads the patches a second time.

The host places each surface with `SunShadowMap.surfacePlacement`, the same placement the
main render uses. It passes only OPC surfaces of the scene body, so a Didymos surface in a
Dimorphos scene is left out.

## Tests

A testing ladder: each rung is green before the next.

| Rung | Where | What |
|---|---|---|
| 1. CPU math | `src/Tests/MapProjectionMathTests.fs` | Round trips on every map, polar orientation, seam/pole classification, view fit and aspect, graticule |
| 2. Shader codegen | `src/Tests/MapProjectionShaderTest.fs` | Every map effect generates GLSL with its geometry stage and reads only attributes an OPC patch has. No GPU |
| 3. Headless render | `src/Tests/MapProjectionRenderTest.fs` | The Dimorphos OPC rendered through `MapSg`. For every pixel, the interpolated surface position must lie at the lon/lat the pixel means (independent of the shader's own lon/lat) |
| 4. Benchmark | `Tests.dll --bench-map` (`src/Tests/MapProjectionBenchmark.fs`) | Frame time per projection × LoD (finest vs root) × size, on the #719 harness |
| 5. Playwright | `tests-ui/tests/map-projection.spec.ts` | Standalone app: the surface texture is drawn, the graticule sits where the projection says, drag moves the map by exactly the drag, polar north puts the prime meridian below the pole, a planet gets the hint. PRo3D's `mapprojection` page shows the same map for a body-fixed Dimorphos scene, and the hint for the J2000 test scene |

Notes on rung 3:
- It runs a flipped control: with mirrored rows the error must be large, proving the metric
  can fail.
- The textured case only checks that texture reaches the map. The test OPC's default layer
  (`DRACO_1`) is the raw DRACO frame stored as a 2:1 map raster, so the correct map shows that
  photo undistorted.

### Benchmark (phase 1, Windows desktop GPU)

Median GPU ms per frame, Dimorphos test OPC (`g_01960mm`), zoom 1:

| Size | Equirect finest | Equirect root | Polar finest | Polar root |
|---|---|---|---|---|
| 1024×768 | ≈ 22 | 3.9 | 9.8 | 1.6 |
| 1920×1080 | 22.8 | 4.0 | 9.5 | 1.8 |

Full detail costs about 6× the root level, which is the most a map LoD decider (phase 1.5)
could save on this dataset.
- The first arm of a run includes warm-up (37 ms once); the steady value is ≈ 22 ms.
- A render control only renders on change, so this is the cost of a pan or zoom frame, not
  a continuous load.

## Later phases

- **1.5 — map LoD decider**, time-boxed:
  - Cull a patch's conservative lon/lat rectangle against the map window.
  - Refine by projected size in map pixels.
  - Always refine patches that cross the seam or contain a pole.
- **2 — annotations:**
  - The packed annotation buffers already take a view matrix, so call them with identity to
    get body-centred positions.
  - Parameterise their shaders with the map vertex/geometry stages (lines through a
    `mapThickLine` that densifies and seam-splits).
  - Picking reuses `pickRenderTarget` with the same stages.
- **Planets:** a per-patch double anchor, the LoD decider, planetographic/west-positive
  conventions.
