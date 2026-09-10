# PRo3D AI Documentation Index

AI-agent documentation for **PRo3D** (Planetary Robotics 3D Viewer). PRo3D is an [aardvark.media](https://github.com/aardvark-platform/aardvark.media) application; these docs cover **PRo3D-specific** architecture and domain. For the underlying framework, see [aardvark.media's AI docs](https://github.com/aardvark-platform/aardvark.media/tree/main/ai).

Start at [../AGENTS.md](../AGENTS.md) for build/dependency/project-structure basics.

## Task-Based Lookup

| Task | Document |
|------|----------|
| Write idiomatic adaptive/F# code (must-read conventions) | [CONVENTIONS.md](CONVENTIONS.md) |
| Choose a model collection type (`IndexList`/`HashMap` vs `aval<…>`) | [CONVENTIONS.md](CONVENTIONS.md#5-model-collection-types-decide-the-adaptive-mapping--choose-deliberately) |
| Understand the root model & how sub-apps compose | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Add or change a domain feature (surfaces, annotations, bookmarks…) | [DOMAIN.md](DOMAIN.md) |
| Wire a new sub-app into the viewer's update/view | [ARCHITECTURE.md](ARCHITECTURE.md#sub-app-composition) |
| Save/load scenes, handle schema changes | [ARCHITECTURE.md](ARCHITECTURE.md#scene-persistence--versioning) |
| Work with OPC terrain data / level-of-detail | [RENDERING.md](RENDERING.md#opc-ordered-point-clouds) |
| Build the scene graph for a surface | [RENDERING.md](RENDERING.md#scene-graph-construction) |
| Implement or debug picking / ray intersection | [RENDERING.md](RENDERING.md#picking) |
| Add a custom shader/effect (outline, multitexture) | [RENDERING.md](RENDERING.md#shaders--effects) |
| Coordinate transforms / planetary reference systems | [DOMAIN.md](DOMAIN.md#reference-systems--spice) |
| GIS entities / SPICE observation setup | [DOMAIN.md](DOMAIN.md#gis) |
| Verify a change (Expecto unit tests, Playwright UI/rendering tests) | [TESTING.md](TESTING.md) |
| Drive the real app's UI in a test / take verified screenshots | [TESTING.md](TESTING.md) + [../tests-ui/README.md](../tests-ui/README.md) |
| Drive PRo3D from the command line | [AUTOMATION.md](AUTOMATION.md#command-line) |
| Control PRo3D remotely (HTTP API) | [AUTOMATION.md](AUTOMATION.md#remote-api) |
| Headless / batch rendering (snapshots) | [AUTOMATION.md](AUTOMATION.md#snapshots--headless-rendering) |
| Record/replay provenance of interactions | [AUTOMATION.md](AUTOMATION.md#provenance-tracking) |

## Type-to-Document Mapping

| Type / Module | Document |
|---------------|----------|
| `Model` (root), `ViewerAction`, `ViewerAnimationAction` | [ARCHITECTURE.md](ARCHITECTURE.md#the-root-model) |
| `Scene` (+ versioning) | [ARCHITECTURE.md](ARCHITECTURE.md#scene-persistence--versioning) |
| `SurfaceModel`, `Surface`, `SgSurface` | [DOMAIN.md](DOMAIN.md#surfaces) / [RENDERING.md](RENDERING.md) |
| `GroupsModel`, `Node`, `Leaf` | [DOMAIN.md](DOMAIN.md#groups-the-shared-hierarchy) |
| `DrawingModel`, `Annotation`, `Geometry`, `DipAndStrikeResults` | [DOMAIN.md](DOMAIN.md#drawing--annotations) |
| `ReferenceSystem`, `Planet`, `SphericalCoo`, `CooTransformation` | [DOMAIN.md](DOMAIN.md#reference-systems--spice) |
| `GisApp`, `Entity`, `ReferenceFrame`, `ObservationInfo` | [DOMAIN.md](DOMAIN.md#gis) |
| `Bookmark`, `SequencedBookmarks`, `SceneState` | [DOMAIN.md](DOMAIN.md#bookmarks--sequenced-bookmarks) |
| `ScaleVisualization`, `GeologicSurface`, `SceneObject`, `TraverseModel` | [DOMAIN.md](DOMAIN.md#other-domain-modules) |
| `TransformationApp`, `VisualizationAndTFApp`, `FalseColorsModel` | [DOMAIN.md](DOMAIN.md#transformations--false-color) |
| `Level0KdTree`, `LazyKdTree`, `InCoreKdTree` | [RENDERING.md](RENDERING.md#kdtrees) |
| `QueryResult`, `QueryFunctions` | [DOMAIN.md](DOMAIN.md#queries) |
| `ProvenanceModel`, `ProvenanceApp` | [AUTOMATION.md](AUTOMATION.md#provenance-tracking) |
| `StartupArgs`, `CommandLine` | [AUTOMATION.md](AUTOMATION.md#command-line) |

## Document Overview

- **[CONVENTIONS.md](CONVENTIONS.md)** — Coding conventions agents **must** follow: FSharp.Data.Adaptive usage rules (no `AVal.force` inside adaptive computations; adaptive-collection vs `aval<collection>` trade-offs; the Adaptify model-collection mapping), total-functions-only, prefix generic syntax, and performance/data-structure rules.
- **[ARCHITECTURE.md](ARCHITECTURE.md)** — How PRo3D layers on aardvark.media's ELM pattern: the root `Model`/`Scene`, the nested `ViewerAction` message tree, sub-app composition via lenses, app startup & hosting, the provenance-wrapped update, and versioned scene persistence.
- **[DOMAIN.md](DOMAIN.md)** — The planetary-science domain: surfaces, the shared groups hierarchy, drawing/annotations, reference systems & SPICE coordinate transforms, GIS, bookmarks, scale bars, geologic surfaces, scene objects, traverses, transformations/false-color, and queries.
- **[RENDERING.md](RENDERING.md)** — OPC terrain data and level-of-detail, scene-graph construction for surfaces, custom shaders/effects, and KdTree-based picking/intersection. Points at the external `Aardvark.GeoSpatial.Opc` / `OPCViewer.Base` repos.
- **[AUTOMATION.md](AUTOMATION.md)** — Non-interactive use: command-line arguments, the remote HTTP API, headless snapshot/batch rendering, and provenance recording/replay.
- **[TESTING.md](TESTING.md)** — How to verify changes: the Expecto suite (`src/Tests`) for models/math, and the Playwright harness (`tests-ui/`) that drives the real viewer and judges rendered screenshots — use it for any viewer-behavior change instead of declaring victory from a green build.

## See Also

- [../AGENTS.md](../AGENTS.md) — build, dependencies, project structure, framework rules
- [../docs/](../docs/) — long-form human feature docs (`ProvenanceTracking.md`, `KdTrees.md`, `spice.md`, `Transformations.md`, `Feature-Queries.md`, `Feature-Multitexture.md`, …)
- aardvark.media AI docs — base framework: https://github.com/aardvark-platform/aardvark.media/tree/main/ai
