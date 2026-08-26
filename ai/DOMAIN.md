# Domain Modules

The planetary-science domain of PRo3D, expressed as ELM sub-apps. Each section gives the concept, its model/app files, and the main types. Composition into the viewer is covered in [ARCHITECTURE.md](ARCHITECTURE.md#sub-app-composition); the shared rendering/picking machinery is in [RENDERING.md](RENDERING.md).

Most models live in `src/PRo3D.Core` (with foundational types in `src/PRo3D.Base`), follow the `*-Model.fs` + generated `*-Model.g.fs` convention, carry a `version` field, and serialize via Chiron.

---

## Surfaces

The 3D data being visualized — primarily **OPC** (Ordered Point Cloud) terrain patch hierarchies, and meshes (OBJ/glTF/PLY/etc.).

| File | Purpose |
|------|---------|
| `src/PRo3D.Core/Surface-Model.fs` | `Surface`, `SurfaceModel`, `SgSurface`, layer types |
| `src/PRo3D.Core/Surface/SurfaceApp.fs` | Update/view for the surfaces panel |
| `src/PRo3D.Core/Surface.fs` | Intersection (`SurfaceIntersection`), triangle/object-set loading |
| `src/PRo3D.Core/Surface/` | Scene-graph construction, properties, utils |
| `src/PRo3D.Core/SurfaceTrafoImporter.fs` | Import per-surface transforms |

Key types:
- **`Surface`** — one logical surface: `guid`, `name`, `importPath`, the contained OPC names/paths, visibility/active flags, quality & render priority, texture layers, scalar layers, color correction, radiometry, and a `preTransform` / `transformation`.
- **`SurfaceType`** (`SurfaceOPC` | `Mesh`), **`MeshLoaderType`** (Assimp/GlTf/Wavefront/Ply/…).
- **`ScalarLayer`** — a scalar attribute (e.g. elevation, a measurement) with a `FalseColorsModel` legend (see [Transformations & False-Color](#transformations--false-color)).
- **`TextureLayer`**, **`ColorCorrection`** (contrast/brightness/gamma/grayscale), **`Radiometry`**, **`TransferFunction`**.
- **`SgSurface`** — the rendering-side companion: `guid`, `trafo`, global bounding box, the `ISg` scene graph, and picking info. Built from a `Surface`; cached in the model.
- **`SurfaceModel`** — container: a `GroupsModel` hierarchy of surfaces (see below), an `sgSurfaces : HashMap<Guid, SgSurface>`, a KdTree cache, and surfaces grouped by render priority.

Surfaces are organized hierarchically through the shared groups mechanism, so much of "surface management" is really `GroupsApp`.

---

## Groups (the shared hierarchy)

A generic tree used uniformly for **surfaces, annotations, and bookmarks**. File: `src/PRo3D.Core/Groups-Model.fs`, app `src/PRo3D.Core/GroupsApp.fs`.

- **`Node`** — a group: `key : Guid`, `name`, `leaves : IndexList<Guid>`, `subNodes : IndexList<Node>`, plus `visible`/`expanded`.
- **`Leaf`** — a tree item, a DU over the kinds it can hold: `Surfaces` | `Bookmarks` | `Annotations`, each exposing `id`/`visible`.
- **`GroupsModel`** — `rootGroup`, active group/child (`TreeSelection`), a `flat : HashMap<Guid, Leaf>` for O(1) lookup, a groups lookup, and selection state (`selectedLeaves`, `singleSelectLeaf`).
- Helpers: `Group.flatten`/`flatNodes`, `Leaf.toggleVisibility`/`setName`/`toSurfaces`/`toAnnotations`, `GroupsModel.tryGetSelectedAnnotation`.

When you work with surfaces or annotations, remember the *content* lives in the `flat` map keyed by `Guid` while the *tree* (`Node`s) only carries `Guid` references.

---

## Drawing & Annotations

Interactive measurement/markup placed on surfaces.

| File | Purpose |
|------|---------|
| `src/PRo3D.Base/Annotation/Annotation-Model.fs` | The `Annotation` data model and result types |
| `src/PRo3D.Core/Drawing/Drawing-Model.fs` | `DrawingModel` (live drawing + annotation groups) |
| `src/PRo3D.Core/Drawing/Drawing-App.fs` | Drawing update/view, measurement computation |
| `src/PRo3D.Core/Importers/AnnotationGroupsImporter.fs` | Import annotations (PRo3D v1 XML groups, plus `importSbmt`) |
| `src/PRo3D.Core/Importers/SbmtImporter.fs` | Import SBMT (Small Body Mapping Tool) structure files — tab-separated point/ellipse catalogs, **not** GIS shapefiles. See `docs/SbmtImport.md`. |

Key types:
- **`Geometry`** — `Point` | `Line` | `Polyline` | `Polygon` | `DnS` (dip & strike) | `TT` (true thickness) | `Ellipse` | `AxisEllipse` | `Axis4PEllipse`.
- **`Projection`** — `Linear` | `Viewpoint` | `Sky` | `Bookmark` (how sampled points are projected).
- **`Semantic`** — geological semantics (`Horizon0..4`, `Crossbed`, `GrainSize`, `None`).
- **`Segment`** — a span between two picked points plus intermediate sampled points (`IndexList<V3d>`).
- **`Annotation`** — the full markup: geometry, segments, style (color/thickness), projection/semantic, and computed results.
- **Results**: `AnnotationResults` (height, length, bearing, slope, area, true thickness, …) and `DipAndStrikeResults` (fitted plane, dip/strike angles & directions, azimuth, center of mass, error stats), plus `Statistics`.
- **`DrawingModel`** — the live working annotation (`Option<Annotation>`), the annotation `GroupsModel`, current color/thickness/projection/geometry/semantic, and sampling settings. Undo/redo lives on the root `Model` (`past`/`future`), not here.

Annotations can be exported to GeoJSON (see [AUTOMATION.md](AUTOMATION.md#remote-api)). See also `docs/AdvancedAnnotations.md` and `docs/SbmtImport.md`.

---

## Reference Systems & SPICE

Planetary coordinate frames and the up/north directions everything else is expressed against.

| File | Purpose |
|------|---------|
| `src/PRo3D.Core/ReferenceSystem-Model.fs` | `ReferenceSystem` (origin, up/north, planet, scale chart) |
| `src/PRo3D.Base/CooTransformation.fs` | `Planet`, `SphericalCoo`, coordinate conversions (P/Invoke to native SPICE) |
| `src/PRo3D.Base/SpiceInterfacing.fs` | Loading SPICE kernels, native call wrappers |
| `src/PRo3D.Base/GisModels.fs` | `EntitySpiceName`, `FrameSpiceName`, `Entity`, `ReferenceFrame` |

Key types & functions:
- **`Planet`** — `Earth` | `Mars` | `Moon` | `Phobos` | `Deimos` | `Didymos` | `Dimorphos` | `JPL` | `ENU` | `None`.
- **`ReferenceSystem`** — `origin : V3d`, `north`/`up` (as `V3dInput` for UI editing), a north-offset angle, `planet`, visibility/size, and a selectable scale chart.
- **`SphericalCoo`** — longitude/latitude/altitude (+ radian flag).
- **`CooTransformation`** — conversions `getLatLonAlt`, `getXYZFromLatLonAlt`, `getUpVector`, `getAltitude`/`getElevation'`/`getHeight`, and lifecycle `initCooTrafo`/`deInitCooTrafo`. These wrap the native `CooTransformation` library and are **thread-locked**; they require initialization at startup (done in `Program.fs`).
- `Planet.inferCoordinateSystem` / `suggestedSystem` heuristically infer the planet from a point's distance from origin.

See `docs/spice.md` and `docs/Transformations.md`.

---

## GIS

Geospatial features driven by SPICE bodies/frames and observation geometry. The interactive sub-app is in `PRo3D.Core/GisApp`; the heavier tooling (image projection, TIFF, instrument metadata) is the separate `src/PRo3D.GIS` project.

| File | Purpose |
|------|---------|
| `src/PRo3D.Core/GisApp-Model.fs` | `GisApp` model |
| `src/PRo3D.Core/GisApp/Entity.fs`, `ReferenceFrame.fs`, `ObservationInfo.fs` | Entity/frame/observation pieces |
| `src/PRo3D.GIS/GisApp.fs` | Geospatial tooling |

Key types:
- **`Entity`** — a SPICE body: `spiceName : EntitySpiceName`, label, color, radius, trajectory length, draw/trajectory toggles, default frame.
- **`ReferenceFrame`** — a SPICE frame: label, description, `spiceName : FrameSpiceName`.
- **`ObservationInfo`** — `target`, `observer`, `time` (calendar), `referenceFrame` — the observation geometry used to place/orient things.
- **`GisSurface`** — binds a `SurfaceId` to an optional entity + reference frame.
- **`GisApp`** — default observation info, `entities`/`referenceFrames`/`gisSurfaces` maps, the active `spiceKernel`, a projected-image list, marker visibility, and mission-time entries (`MissionTimeEntry` for rover ops timelines).

Actions cover assigning bodies/frames to surfaces, observing (positioning at a time), setting the SPICE kernel, and toggling the camera into an observer frame.

---

## Bookmarks & Sequenced Bookmarks

| Concept | File | Notes |
|---------|------|-------|
| Bookmarks | `src/PRo3D.Core/Bookmark-Model.fs` | Saved camera views |
| Sequenced bookmarks | `src/PRo3D.Core/SequencedBookmarks/SequencedBookmarks-Model.fs` | Animated tours |
| Animations | `src/PRo3D.Core/SequencedBookmarks/BookmarkAnimations.fs` | Easing/interpolation between bookmarks |

- **`Bookmark`** — `key : Guid`, `name`, `cameraView`, `exploreCenter`, `navigationMode`. Stored in a `GroupsModel` (the `bookmarks` field of `Scene`).
- **Sequenced bookmarks** add per-bookmark `delay`/`duration`, an `AnimationLoopMode` (`NoLoop`/`Repeat`/`Mirror`), global animation settings, and a captured **`SceneState`** (camera + view config + reference system + annotation/geologic-surface states). Playing a sequence interpolates camera and restores scene state, and can drive batch snapshot/panorama rendering (see [AUTOMATION.md](AUTOMATION.md#snapshots--headless-rendering)).

---

## Transformations & False-Color

- **`src/PRo3D.Core/TransformationApp.fs`** — per-object placement: translation, yaw/pitch/roll with a selectable `EulerMode` (6 orderings), scaling, and a `PivotMode` (`NoPivot`/`BBCenter`/`PickPivot`). Computes the full `Trafo3d` from a reference-system basis: `getReferenceSystemBasis_global`/`_local`, `calcFullTrafo`, `getNorthAndUpFromPivot`. Used by surfaces, scene objects, scale bars.
- **`src/PRo3D.Core/VisualizationAndTFApp.fs`** + **`src/PRo3D.Base/FalseColors/`** — false-color legends and transfer functions applied to scalar layers (`FalseColorsModel`: interval, color scheme, legend). See `docs/Contour-Lines.md` and `docs/Feature-Multitexture.md`.

---

## Other Domain Modules

| Domain | Model file | What it is |
|--------|-----------|------------|
| **Scale bars** | `src/PRo3D.Core/ScaleBars-Model.fs` | `ScaleVisualization` — camera- or planet-aligned scale bars / coordinate frames with units (mm…km), subdivisions, orientation, pivot. |
| **Geologic surfaces** | `src/PRo3D.Core/GeologicSurface-Model.fs` | `GeologicSurface` — a mesh built between two point sets (`points1`/`points2`) to visualize a geologic boundary/cross-section; color, transparency, thickness, mesh inversion. |
| **Scene objects** | `src/PRo3D.Core/SceneObjects-Model.fs` | `SceneObject` — external mesh/model placed in the scene with a full transformation; rendered via a cached `SgSurface`. |
| **Traverses** | `src/PRo3D.Core/Traverse-Model.fs` | Rover paths and related data. `TraverseType` = `Rover` (SLAM/RMC) \| `Rimfax` (ground-penetrating radar surfaces) \| `WayPoints`. Carries per-sol metrics, distances, and (for RIMFAX) image-mode surfaces. See `docs/RIMFAXTraverse.md`, `docs/TraversePriorities.md`. |

---

## Queries

Spatial/attribute extraction from OPC patches, used by the remote API and analysis workflows. File: `src/PRo3D.Core/Queries/` (e.g. `AnnotationQuery.fs`).

- **`QueryFunctions`** — predicates `boxIntersectsQuery : Box3d -> bool` and `globalWorldPointWithinQuery : V3d -> bool`. Built from an annotation polygon (`queryFunctionsFromAnnotation`) or points-on-plane within a height range (`queryFunctionsFromPointsOnPlane`).
- **`QueryResult`** — extracted `attributes : Map<string, QueryAttribute>` (channels + raw array), global/local positions, indices, and the source patch path.
- `handlePatch` extracts a patch's vertices/attributes that fall inside the query region; quadtree traversal (`QTree.foldCulled`) applies frustum/region culling.

See `docs/Feature-Queries.md` and the `notebooks/Query.ipynb` example.

---

## See Also

- [ARCHITECTURE.md](ARCHITECTURE.md) — how these sub-apps compose into the viewer
- [RENDERING.md](RENDERING.md) — surfaces' scene graphs, OPC data, KdTree picking
- [AUTOMATION.md](AUTOMATION.md) — querying/controlling these features programmatically
- `../docs/` — feature deep-dives (annotations, transformations, queries, SPICE, traverses, contour lines)
