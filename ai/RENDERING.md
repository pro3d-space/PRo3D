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
