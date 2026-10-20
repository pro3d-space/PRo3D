# Rendering, OPC & Picking

How PRo3D turns planetary surface data into a scene graph, renders it with level-of-detail, and picks against it.

> **Prerequisite:** aardvark.media's rendering model — `RenderControl`, `RenderTask`, the adaptive scene graph (`ISg`), FShade shaders — see [aardvark.media/ai/RENDERING.md](https://github.com/aardvark-platform/aardvark.media/tree/main/ai). This document covers the OPC-specific pipeline PRo3D builds on top.

---

## Where the OPC code actually lives

Much of the heavy lifting is **not** in this repo — it's in shared Aardvark platform packages (consumed via Paket). When you need to understand or fix OPC loading/rendering, go to the right repo:

| Concern | Package / repo |
|---------|----------------|
| **OPC data loaders** (read patch hierarchies, `.aara` vertex/attribute files, patch file info, paths) | `Aardvark.Data.Opc` — https://github.com/aardvark-platform/aardvark.data/tree/master/src/Aardvark.Data.Opc |
| **Level-of-detail rendering** (`Sg.patchLod` nodes, LoD metrics, streaming) | `Aardvark.GeoSpatial.Opc` — https://github.com/aardvark-platform/aardvark.geospatial/tree/master/src/Aardvark.GeoSpatial.Opc |
| **Shared OPC viewer base** (camera, picking helpers, view infrastructure reused by PRo3D) | `OPCViewer.Base` — https://github.com/aardvark-platform/OPCViewer.Base |
| **Ray/triangle intersection** | `Aardvark.Geometry.Intersection` (`ConcreteKdIntersectionTree`, `RayHit3d`) |

In this repo, the `OpcViewer` project and `src/PRo3D.Core/Surface/` adapt these into PRo3D's model.

---

## OPC (Ordered Point Clouds)

OPC is the patch-based, hierarchical terrain format PRo3D is built around. An OPC dataset is a directory containing `Images/` and `Patches/` plus a `patchhierarchy.xml`. Each patch holds geometry (`.aara` binary vertex/coordinate arrays), texture coordinates, and textures (`.dds`/`.tif`/`.exr`), arranged in a level-of-detail tree so only the needed resolution is loaded and drawn.

- **Loading**: `Aardvark.Data.Opc` parses the hierarchy and patch files; PRo3D discovers/validates dataset folders in `src/PRo3D.Core/Surface/` (folder discovery, `isOpcFolder`/`isSurfaceFolder`).
- **LoD rendering**: `Aardvark.GeoSpatial.Opc` provides `Sg.patchLod` (and friends), which builds an adaptive scene-graph node that streams and swaps patch levels based on screen-space error / distance metrics.
- **`opc-tool`** (`src/opc-tool/`) is the offline companion CLI: validate a dataset, resize/convert patch textures (to `.dds`), and **pre-build KdTrees** for picking.
- **Attribute layers** (elevation, slope, gravity, …) come in two forms. Every OPC has them as *texture layers* under `Images/<Layer>/`, declared in the dataset's `*.opcx`. Newer exports additionally ship them as *per-vertex* `.aara` grids inside each patch directory, listed in `patch.xml`'s `<Attributes>`. Reading values at a picked point prefers the per-vertex form (three small random-access reads per layer) and falls back to decoding the texture. **Beware two traps**: the per-vertex grid is *smaller* than the position grid (a centred skirt, `off = (posSize − attrSize) / 2`), and texture layers store values *normalised into the layer's `ChannelsDefinedRange`* while per-vertex layers hold physical values. See [docs/VertexAttributes.md](../docs/VertexAttributes.md) and `src/PRo3D.Core/VertexAttributes.fs`.

---

## Scene-Graph Construction

PRo3D assembles each surface's `ISg` (stored on its `SgSurface`, see [DOMAIN.md](DOMAIN.md#surfaces)) and combines them into the final render task.

| File | Role |
|------|------|
| `src/PRo3D.Core/Surface/` (`Surface.Sg.fs` etc.) | Build a surface's scene graph: `patchLod` geometry + textures + transforms + shaders |
| `src/PRo3D.Core/Sg.fs` | Reference-system / orientation visualization (north/east/up markers, scale chart) |
| `src/PRo3D.Base/PatchOverrides.fs` | Override patch materials/shaders for specific patches |
| `src/PRo3D.Base/Multitexturing.fs` | Multi-layer texture blending (`TextureCombiner`: Primary/Secondary/Multiply/Blend) |
| `src/PRo3D.Base/OutlineEffect.fs` | Stencil-buffer outlines for selection highlighting |
| `src/PRo3D.Base/Utilities.fs` (`OPCFilter`) | FShade shader snippets (diffuse+color blend, patch-border marking) |

The pipeline is standard Aardvark: an adaptive `ISg` parameterized by `aval` model values, FShade shader composition, and "stable" transforms (`stableTrafo`) for large planetary world coordinates to avoid float precision loss.

### Precision: local space → view space (critical)

PRo3D's coordinates are planetary-scale (millions of metres), so the pipeline never feeds world-space positions through a `float32` shader transform. Instead:

- Geometry is kept in a **local space** — OPC patches in their own patch-local space; **annotations** in a space translated from `(0,0,0)` to the annotation's first point — and positioned with `Sg.trafo`.
- The model-view-projection matrix is composed **on the CPU in `double`** (`DefaultSurfaces.stableTrafo`) and the shader transforms **local → view space directly**, skipping a global float world space.
- **Lighting** and **large-triangle filtering/culling** are done in **view space**; world space would suffer numerical problems at this scale.

This is a hard rule for any new geometry/shader work. The full statement and the other scene-graph optimization rules (instancing via `Sg.instanced`, effect caching, shader-vs-CPU trade-offs, update-don't-rebuild) are in [CONVENTIONS.md → Scene Graph & Rendering](CONVENTIONS.md#scene-graph--rendering-aardvarkrendering). Follow them when touching this pipeline. Per-surface `preTransform`/`transformation` (and SPICE observation transforms, see [DOMAIN.md](DOMAIN.md#reference-systems--spice)) position the surface in world space.

### Shaders & Effects

PRo3D mixes Aardvark `DefaultSurfaces` with custom FShade effects. Notable PRo3D-specific ones: false-color/transfer-function shading for scalar layers (see [DOMAIN.md](DOMAIN.md#transformations--false-color)), multitexturing, outline/selection via stencil modes, and contour-line shading (`docs/Contour-Lines.md`, `docs/Feature-Multitexture.md`).

### OPC surface effect variants (#719)

A stage that is switched off by a uniform still costs if its *kind* is expensive. Two parts of the OPC surface effect are therefore composed only when a surface needs them (`ViewerUtils.surfaceEffectVariant geometryStage crossSectionClip`):

- **`geometryStage`**: `triangleSizeFilter` + `generateNormal`, the only geometry shaders. FShade merges them into one stage, and that stage alone dominated OPC frame time on Apple Silicon (#719). It is composed when a surface actually has a reader of its face normal (`LocalNormal`) — a projection stack, a hover footprint, sun shading or shadows — or needs triangle-size / distance filtering, and is switched in at runtime as soon as one of those appears (`SurfaceEffectSwitchTest`). A surface that gets one later is not a special case: `SharedEffectPool` prepares every render object against the full variant's interface first (see the bootstrap there), which is what makes the switch-up actually show. Without the stage, `ImageProjection.Shaders.noFaceNormal` writes a constant `LocalNormal` so it never becomes a vertex attribute.
- **`crossSectionClip`**: the stack's only `discard`, which defeats hidden-surface removal on tile-based GPUs. Composed while clipping is enabled and a cross-section exists.

Each surface picks its variant through `ViewerUtils.surfaceEffectPool`, an Aardvark `Surface.Dynamic`: switching swaps the GL program without rebuilding render objects. All variants share one FShade input layout, and `Sg.effectPool` builds it by **linking every variant**, on every start and once per surface. `SharedEffectPool` builds it from the **last variant alone** — the one that uses everything the others use, which `SurfaceEffectVariantTest` pins — and shares it per (framebuffer layout, topology). The other variants stay lazy: applying a layout does not link, and GL finds their programs on disk by effect id + layout hash.

That one link costs ~6 s, and the layout is exactly what GL keys its on-disk programs by, so it is stored next to the shader cache (`<ShaderCachePath>/PRo3D.EffectInputLayouts`). A **cold** start (first after a shader change) links once; a **warm** start links nothing at all.

### OPC surface effect bands

The fragment stages of `surfaceEffectVariant` are ordered in three bands, so a stage's position follows from what it is:

1. **Surface colour** — what the terrain *is*: texture, LoD/radiometry, secondary texture, contour lines, the projection stack and its coverage view. Lighting applies to it.
2. **Lighting** — `surfaceColor` keeps the unlit colour in the `SurfaceColor` semantic, then `solarShadingLS`, `terrainSunShadow`.
3. **Overlays** — drawn *on* the terrain: unlit, so they stay readable in shadow. Bottom → top: `depthCalculation2` (tints `SurfaceColor`, not the lit colour), `latLonLines`, `outcropTrace`, `footPrintF`, `hoveredProjectionOutline`, `donutFragment` (the cursor is always on top).

`crossSectionClip` comes **after all three**: after a `discard`, derivatives in its pixel quad are undefined, and the grid, the outcrop trace and every implicit-LOD texture sample take them. Its tile-GPU cost comes from being composed at all, not from its position.

**Adding a stage to the surface effect:** pick its band first. If it reads `LocalNormal`, it only works on surfaces that keep the geometry stage. If it adds a new vertex input, check `SurfaceEffectVariantTest`.

### One return per fragment stage

**Write every fragment stage with a single `return` and a mutable accumulator**, never `if cond then return a else return b`:

```fsharp
fragment {
    let mutable c = v.c
    if uniform.SomethingEnabled then
        c <- ...
    return c
}
```

FShade inlines the *rest of the effect* once per return path of a stage, so the generated GLSL is the **product** of the return counts down the stack, not their sum ([krauthaufen/FShade#39](https://github.com/krauthaufen/FShade/issues/39)). The OPC surface effect has ~28 stages; with a handful of two-return stages it generated **21971 lines with 861 copies of the last stage**, taking tens of seconds per variant to compile and producing a fragment shader no driver is happy with. Rewriting every stage to a single return brought the same effect to **~1000 lines**, cold codegen for all four variants from minutes to **8 s**, and GL compile to **3 s** (warm: 0.1 s).

`SurfaceEffectVariantTest` pins the GLSL size so a reintroduced second return fails a test instead of quietly halving start-up speed.

---

## Picking

PRo3D picks by casting a ray against per-patch **KdTrees** (precomputed triangle acceleration structures), not against the live LoD geometry.

Entry points:
- `src/PRo3D.Viewer/Viewer/Picking.fs` — high-level `pickRay` from the viewer; visualizes the hit (cone + normal cylinder preview) and feeds annotation/measurement workflows.
- `src/PRo3D.Core/Surface.fs` — `SurfaceIntersection.doKdTreeIntersection`: the core intersection.

Flow:
1. Filter to active + visible surfaces.
2. For each surface, get its KdTree(s) from the cache (loading lazily if needed).
3. Transform the ray into the surface's local space (accounting for `preTransform`, pivot rotation, scale, and any SPICE/observation transform).
4. Reject via transformed bounding boxes, then intersect with `ConcreteKdIntersectionTree.Intersect` (from `Aardvark.Geometry.Intersection`), applying a triangle filter.
5. Return the closest hit (`t`, triangle, surface reference).

**Performance note:** picking is only spawned when the current interaction actually needs it (recent change "Only spawn picking when necessary", commit `14583377`) — surface picking message spawning was previously over-eager. Results may be delivered asynchronously (`Model.pickPreviewRequested`, `backgroundPicking` thread pool; see [ARCHITECTURE.md](ARCHITECTURE.md#threads-messaging--async)).

### Annotation picking — a separate, GPU-based system

Annotations are **not** picked with the KdTrees. `PackedRendering.pickRenderTarget` rasterizes them into an offscreen `Rgba32f` buffer and the readback (`Drawing-App.fs`, in the packed branch of `DrawingApp.view`) downloads the single pixel under the cursor on every mouse move.

Channel meanings, cleared to `(0,0,0,-1)`:

| channel | meaning |
|---|---|
| **alpha** | packed object id — the index into `PackedRendering.orderedAnnotations`. `-1` = miss. |
| **red** | sub-index within that object: the control point index for vertex-handle fragments, `-1` for lines and fills. `Picking.pickId` writes `-1`, `Picking.pickVertexId` writes the index. |
| green, blue | fragment colour; read by nothing but the debug lens overlay in `OpcViewer/AnnotationViewer.fs`. |

`orderedAnnotations` is the single cached ordering — **every packed draw that writes `ObjId` must take its ids from it**, or a click selects a different annotation than the one under the cursor. It is `HashSet.toArray` inside an `AVal.custom`, so the int id space can permute whenever the annotation set changes: capture the `Guid`, never the int, if you need to hold onto a pick.

Draw order inside the pass is significant — fills, then lines, then vertex handles — so an outline wins over its own fill and a handle wins over the outline through it. Each also gets a progressively larger `DepthOffset` (`fillDepthOffsetFactor`, `handleDepthOffsetFactor`). The pick pass draws the fill's **flat cap** (`FillGeometry.caps`); only the visible pass uses the prisms below.

### Surfaces offscreen (#841)

The main and instrument views render their surfaces **offscreen** (`OffscreenSurfaces`, `ViewerUtils.offscreenScene`): the priority groups go into a multisampled colour + depth target, and a per-sample fullscreen blit copies **colour and depth** back into the render control — depth everywhere, depth 1 included; colour premultiplied (see below). The depth is cleared before each group, as before, so it only holds the last group; earlier groups keep their colour at depth 1 exactly as when drawn into the view directly (and fills cannot land on them, also as before). Everything after it (depth-tested annotations, traverses, overlay) depth-tests against the surfaces exactly as before — but the surfaces' depth now also exists as a texture (`OffscreenSurfaces.SurfaceDepth`).

- **Picking** stays on scene events: the grouped surface scene graphs are added to the control a second time through `OffscreenSurfaces.pickOnly` — a typed applicator whose `RenderObjects` rule returns nothing, so their `Sg.pickable'`/`withEvents` still fire.
- **Per render client — size, samples, trafos:** the views pass `DomNode.RenderControl` a `RenderClientValues -> ISg` function, and `offscreenScene` builds the offscreen targets and the fill accumulation from `values.size`, `values.viewTrafo`, `values.projTrafo`. aardvark.media compiles one scene per (scene, sample count) and, before every frame it renders — a live frame or a server screenshot (`/rendering/screenshot?w=&h=&samples=`) — sets these values in a transaction (`ClientRenderTask.Run` → `ConcreteScene.Apply`). So a screenshot of any size or aspect ratio gets surfaces rendered for exactly that image; one with the live view's sample count shares its compiled scene, and the targets resize to the screenshot and back. Do **not** derive the size from `viewPortSizes` — that is the page's reported size, which a screenshot does not change. Only the targets are per client: `createGroupedSgs`, the overlay, the depth-tested scene and the fill geometry are built once outside the function and shared, so the surfaces' and annotations' GPU data are not duplicated per compiled scene.
- **Sample count:** the offscreen targets always have the viewer's (`dataSamples`), never the target's. aardvark.geospatial prepares each OPC patch once, for the framebuffer signature its node was built with (`runtime.PrepareRenderObject(n.Signature, ro)`, RenderingV2.fs); a task of another sample count rejects the prepared patches and the surfaces come out black. The blit converts instead (`TargetSamples`): fewer target samples get the mean colour of the offscreen samples they cover and the first one's depth, more get them repeated.
- **Background:**
 the surfaces are rendered over transparent black, which leaves premultiplied colour (`BlendMode.Blend` accumulates alpha with `One`/`InvSourceAlpha`), and the blit blends with `One`/`InvSourceAlpha` (`premultipliedOver`). Where no surface is, the target keeps its own clear colour — the control's `#222222`, a screenshot's `background=` — and translucent surfaces come out as if drawn into the target directly.
- **Lifetime:** the offscreen tasks are compiled inside an `AdaptiveResource` (`OffscreenSurfaces.renderTargets`) — on first acquire, disposed on last release. The render control's per-client scene task acquires them, so they live exactly as long as a page shows them. Compiled eagerly in the view function they leaked a full copy of the surfaces' GPU data per page load (view functions run again per `?page=` request).
- **Memory:** a large screenshot allocates the targets at its size — multisampled RGBA8 + D24S8 plus the RGBA32F fill accumulation, e.g. ~3 GB for 8k × 8k at 4 samples — until the next live frame resizes them back.

**Known limitations:**
- Server screenshots with a `samples=` other than the viewer's (4) come out without surfaces: the render server compiles a separate scene for that sample count, and its single frame has no OPC patches. The same on develop (checked with the 2026-09-24 develop build), so not caused by the offscreen surfaces; PRo3D's own screenshot clients send `samples=4`. Unverified guess: the OPC LoD of a freshly compiled scene has not streamed anything yet.
- After a server screenshot of another size (same sample count, so sharing the live view's compiled scene), the live view stops showing model changes until the camera moves - an invalidation lost in aardvark.media 5.7.3 (`ClientRenderTask.Run` sets `OutOfDate` by hand before applying the screenshot's values). The same on develop. `tests-ui/tests/annotation-fill.spec.ts` therefore takes its server screenshots last.

- The fill accumulation is single-sampled (depth sample 0); the composite writes one value per pixel, so fill edges at terrain silhouettes are not antialiased.
- The stencil fallback (Snapshots) rebuilds its batches whenever the batch count changes.
- Snapshots (`SnapshotSg`) render surfaces directly and use the stencil fill fallback.
- Instrument view: verified by screenshot (headless probe, `fillProblem` scene) that surfaces show; fills there not checked separately.

- GPU memory grows while the camera moves — on develop as much as here (OPC LoD caching), pre-existing.
- The closing edge of Sky-projected polygon outlines is not drawn — pre-existing, unrelated to fills.

### Annotation fills — against the surface depth (#841)

Visible fills are **not** a mesh. A flat cap between the outline points only touches the terrain at its rim, so relief inside the outline cuts through it (z-fighting). Instead:

1. `FillVolume.tryCompute` (`PRo3D.Base`, pure, unit-tested) builds a **closed prism** per fill: the `PolygonFill` triangulation as top/bottom caps, offset ±h along the outline's **fitted-plane normal** (works on vertical rock faces; no per-vertex `getUpVector`/SPICE), plus a wall on every *boundary edge of the triangulation* — so tessellator-invented vertices and holes close by construction. h = the ring's spread along the axis + the user's **fill reach** (`FillReach`: Auto = half the outline size capped per body by `FillReach.maxAuto`, or metres; `ViewConfigModel.fillReachAuto` / `fillReach`).
2. `PackedRendering.DeferredFills`: **all prisms in one draw** into an RGBA32F accumulation target. A face fragment *behind* the surface depth adds its fill's premultiplied colour, +1 for back faces, −1 for front faces — the z-fail count per fill, in colour, order-independent. Inside a prism the sum is ±1×colour, outside 0; prisms in front of the terrain contribute nothing. Depth clamp keeps faces beyond near/far; the shader clamps `gl_FragCoord.z` itself and skips pixels without surface.
3. A fullscreen composite (in `fillPass`, before `main`, so outlines draw on top) turns the sums into straight-alpha colour; `abs` makes it independent of the winding convention.

Cost: 10240 fills ≈ 0.06-0.15 ms/frame with a moving camera (RTX 500 Ada), offscreen + blit ≈ 0.04-0.05 ms. Translucent fills overlapping *on the terrain* blend as a weighted average with alpha min(1, Σα).

**Why not the stencil** (kept as fallback where no surface depth exists — Snapshots): a shared stencil mark cannot say *which* prism contains the terrain, so the colour pass painted a pixel from whichever prism face reached it first — a near prism's wall took a far fill's terrain (straight cuts in oblique views). One stencil bit per fill, eight per ordered batch (`packedFillRender`, the Snapshots fallback), is correct but costs ~10-23 ms at 10k fills (varies between runs); a render command per fill ~54 ms.

The pick pass draws the flat caps (`FillGeometry.caps`); the 2D map view (`MapAnnotations`) has no terrain and draws the caps too.

GPU tests and benchmark: `src/Tests/FillRenderTest.fs`. Playwright: `tests-ui/tests/annotation-fill.spec.ts`. User docs: [docs/AnnotationFill.md](../docs/AnnotationFill.md).

Gates: `ViewerApp.allowAnnotationPicking` decides whether the readback is honoured at all; `ViewerApp.allowVertexEditing` additionally gates the handles. `Interactions.EditAnnotation` is the one mode where annotation picking *and* KdTree surface picking are both live — see [docs/AnnotationVertexEditing.md](../docs/AnnotationVertexEditing.md) for why that ordering hazard needs `VertexGrab.movedSinceGrab`.

### KdTrees

File: `src/PRo3D.Base/KdTrees.fs`. See also `docs/KdTrees.md`.

- **`Level0KdTree`** — DU per patch: `LazyKdTree` | `InCoreKdTree`.
- **`LazyKdTree`** — holds *paths* (kdtree, object-set, coordinates, texture) plus the patch affine `Trafo3d` and bounding box; loads the actual tree on first use to keep memory bounded.
- **`InCoreKdTree`** — already-loaded tree + bounding box.
- Helpers: `loadKdtree`/`saveKdTree` (binary, via FsPickler), `expandKdTreePaths` (resolve a hierarchy's per-patch trees into `HashMap<Box3d, Level0KdTree>`), `tryFixPatchFileIfNeeded` (case-sensitivity path repair for cross-platform datasets), and `loadKdTrees'` (bulk loading with caching).

KdTrees are generated offline by `opc-tool`; if a surface has none, picking will silently find nothing.

---

## Serialization

`src/PRo3D.Base/Serialization.fs` provides two serializers, initialized once via `Serialization.init()`:

- **Binary** (MBrace.FsPickler): `save`/`loadAs<'a>` — used for KdTrees and other heavy/internal data.
- **JSON** (Chiron, with `ChironExt.fs` extensions): `saveJson`/`loadJsonAs<'a>` plus low-level string I/O — used for `.pro3d` scenes and sub-model state. Scene JSON is versioned (see [ARCHITECTURE.md](ARCHITECTURE.md#scene-persistence--versioning)).

---

## Native & Platform Notes

- SPICE coordinate transforms call native libraries through `CooTransformation`/`SpiceInterfacing` (see [DOMAIN.md](DOMAIN.md#reference-systems--spice)); these are thread-locked and must be initialized at startup.
- Native libs are embedded as `native.zip` resources and unpacked at build time (`UnpackNativeDependencies.fs`); instrument wrappers come from `lib/JR.Wrappers.dll` + `lib/Native/JR.Wrappers/<platform>/`.
- A GPU workaround disables multisampling on Intel Iris Xe (`Program.fs`).

---

## See Also

- [DOMAIN.md](DOMAIN.md#surfaces) — the `Surface`/`SgSurface`/`SurfaceModel` data side
- [ARCHITECTURE.md](ARCHITECTURE.md) — where rendering/picking results flow in the update loop
- `../docs/KdTrees.md`, `../docs/Pro3DTool.md`, `../docs/Feature-Multitexture.md`, `../docs/Contour-Lines.md`
- External: [Aardvark.Data.Opc](https://github.com/aardvark-platform/aardvark.data/tree/master/src/Aardvark.Data.Opc), [Aardvark.GeoSpatial.Opc](https://github.com/aardvark-platform/aardvark.geospatial/tree/master/src/Aardvark.GeoSpatial.Opc), [OPCViewer.Base](https://github.com/aardvark-platform/OPCViewer.Base)
