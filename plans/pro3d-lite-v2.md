# PRo3D Lite v2 — a minimal viewer by composition

## Context

`src/PRo3D.Lite` (2022) was meant to be an approachable, lightweight PRo3D reusing PRo3D code. It failed at reuse:
it references only `PRo3D.Base`, reimplements an orbit controller, renders OPCs with a bare `Sg.patchLod`,
picks by GPU depth readback, has no annotations/scene IO, and loads a hardcoded `I:\OPC\GardenCity` path. Since 2022 it
only received dependency ports.

Root cause: the parts a minimal viewer needs are split. Domain sub-apps (`SurfaceApp`, `DrawingApp`, `GroupsApp`,
`ReferenceSystemApp`, KdTree intersection, codecs) are reusable in `PRo3D.Core`. The **glue** that wires them is in the
`PRo3D.Viewer` exe and typed on the root `Model`/`ViewerAction`: navigation (`Navigation.fs`, `MapViewCameraController.fs`,
`NavigationGizmo.fs`), click→point routing (`matchPickingInteraction`, `Viewer.fs:314`, private), the Viewpoint/Sky `hitF`
builder (`Viewer.fs:~1668`), surface pick events (`ViewerUtils.viewSingleSurfaceSg`), `renderCommands`, process init
(`Program.fs:191-200`), surface import (`SceneLoader.import'`), annotation sidecar IO.

**v2 approach (decided):** lift that glue into a library both the Viewer and Lite use, then build Lite as a small
aardvark.media app composing Core sub-apps — the pattern `src/PRo3D.MapProjection` already proves (standalone app on
Core + `MapProjectionHost.fs` adapter in the Viewer).

## Status (2026-10-04)

P0–P8 are implemented. They land as two stacked PRs into `experimental/pro3d-lite` (not
`develop` - this is an exploration):

- **PR A** `features/composition-host-glue`: PRo3D.Composition and the Viewer delegating to it,
  no behaviour change (verified on its own: full Expecto suite green; the viewer probe's frames
  after importing the MSL OPC and drawing are pixel-identical to `develop`).
- **PR B** `features/pro3d-lite-v2`: remove the old Lite, the scene-core codec + relaxed
  `Scene.read3`, PRo3D Lite, docs.

What Lite offers: [docs/PRo3DLite-Features.md](../docs/PRo3DLite-Features.md). Beyond the
original scope it gained the Viewer's toolbars and tool strip, the camera overlay and the view
config (navigation sensitivity with Page Up/Down) - all shared through PRo3D.Composition.

**Verification:** Expecto 701 passed / 0 failed (`--skip-hera`; `CompositionTests`,
`LiteTests`); Playwright `tests-ui/tests/lite.spec.ts` 4/4.

Deviations from the plan, found while building it:

- `ReferenceSystemSync` added: a planet change must re-base surfaces and recompute every
  measurement; that was private in the Viewer's `SceneBodySync` (now delegates).
- The Viewer's "simple" surface graph (`getSimpleSingleSurfaceSg`, only used for an offscreen
  depth render) was bit-rotted: OpcViewer shaders reading a `TriangleSize` uniform and a
  `WorldPosition` attribute nobody provided, and OPC patch attributes (projected images,
  secondary texture) without a root default. `SurfaceView.simpleSurfaceSg` now sets all OPC
  attributes neutrally and uses the Viewer's own view-space shaders + `opcDiffuseTexture`.
- A host must touch PRo3D.Core before `Aardvark.Init()` (scene-graph rule registration).
- `Scene.read3` relaxed as planned; pre-change PRo3D cannot open a Lite-created scene.

Open: Lite is not part of `Build.fs` publish/Electron packaging yet; GIS placement in Lite;
namespace cleanup of the moved files.

## Scope

Core (requested): load & view OPC surfaces · free-fly + orbit (ArcBall) navigation · annotations (draw, select, edit
properties, delete).

Core (added, agreed):
- save/load annotations as `.pro3d.ann` (compatible with full PRo3D)
- minimal scene file = readable subset of `.pro3d` v3 + annotation sidecar
- planet / reference system selection so up vector and measurement results (altitude, slope, dip/strike, …) are correct; results panel
- undo/redo for annotations (existing `DrawingModel` undo stack)
- surface list with visibility, fly-to-surface and home

UI: Golden Layout panels (aardvark.media `Golden`), fixed lite panel set.

Out of scope: GIS/SPICE observation placement, image projection, shadows, traverses, view plans, bookmarks, scale bars,
provenance, remote API, layout library/sidecars, MapView gizmo extras beyond what moves for free.

Old `src/PRo3D.Lite` is **removed**.

## Architecture

```
PRo3D.Base ← PRo3D.Core ← PRo3D.Composition (new lib, no [<ModelType>], no Adaptify)
                               ↑                ↑
                         PRo3D.Viewer      PRo3D.Lite (new exe, from scratch)
```

**Why a new library, not Core:** Composition is host glue (how a host wires sub-apps) that will churn during Lite work;
Core is the expensive rebuild/adapt project. Keeping Composition free of model types means it never needs Adaptify.
Moved files **keep their namespaces** (`PRo3D`, `PRo3D.Navigation2`, `PRo3D.Viewer`, `PRo3D.Core`) so the Viewer's `open`s
compile unchanged and move PRs stay mechanical; renaming is a later cleanup.

`src/PRo3D.Composition` compile order:

| File | Content |
|---|---|
| `Config.fs` | moved from Viewer (`namespace PRo3D.Core`, `Config.*` mutables read by surface sg/picking) |
| `MapViewCameraController.fs`, `Navigation-Model.fs`, `Navigation.fs`, `NavigationGizmo.fs` | moved from Viewer (depend only on Base/Core) |
| `HostConfigs.fs` | `navConf`, `drawingConfig`, `mdrawingConfig`, `mrefConfig`, `referenceSystemConfig` (from `Viewer.fs:188-207`, `Models/Lenses.fs:11`) |
| `ProcessInit.fs` | `initSerialization ()` (wraps `HeadlessPicking.initKdTreeLoading`, run-once), `initRuntime runtime opts` (SuppressSparseBuffers, `Sg.hackRunner`, `DrawingApp.usePackedAnnotationRendering`) |
| `SurfaceLoading.fs` | `prepareSurfaceModel`, `importSurfaces` (from `Scene.fs:156/245`, typed on `SurfaceModel`), `Result` around throwing loads |
| `AnnotationFiles.fs` | sidecar path, `tryLoad : string -> Result<…>` (wraps throwing `DrawingUtilities.IO.loadAnnotationsFromFile`), `save` (`Drawing.IO.saveVersioned`) |
| `SurfacePicking.fs` | `KdTreeCache` object (replaces module-level `ViewerApp.cache`), `Observation` (`none` for Lite), `skyRay`, `projectionRay`, `intersect`, `hitFunction`, `toLocal`, `pickSurface → Option<PickResult>` (total: no `FastRay3d()` fallback) |
| `PickRouting.fs` | `routePick : PickContext -> PickInputs -> PickOutcome` on sub-models: `DrawingChanged \| SurfacesChanged \| RefSystemChanged \| NavigationChanged \| Unhandled`; `stash`/log/`updateCameraUp` stay with caller |
| `SurfaceView.fs` | `pickEvents` (generic over `'msg`), `leanSurfaceSg` (= `getSimpleSingleSurfaceSg` + events + selection box; local space + CPU-double `stableTrafo`), `surfacesSg`, generic `renderCommands` |
| `NavigationView.fs` | `controllerAttributes`, `threads` — exhaustive, replacing the `failwith`s in `Viewer.fs:2507/3015` |
| `LiteScene.fs` | subset `.pro3d` codec (see Scene format) |

**Not lifted:** `createGroupedSgs` and `surfaceEffectPool` (pull in GIS, cross sections, outcrop, shadows, image
projection) — Lite uses the lean surface effect. `LayoutApp`/`LayoutOps` (hardwired to `LayoutPanels.all`/`DashboardModes`) —
Lite uses `Golden` directly; parameterizing the panel registry is a later step.

### Lite app (`src/PRo3D.Lite`)

- `Program.fs` modeled on `src/PRo3D.MapProjection/Program.fs`: `Aardvark.Init`, `ProcessInit.*`, `initCooTrafo`,
  Giraffe `Server.startLocalhost` on its own port, Aardium window, `--server` mode for Playwright.
- `Lite-Model.fs` (`[<ModelType>] LiteModel`): `surfaces : SurfaceModel`, `drawing : DrawingModel`,
  `navigation : NavigationModel`, `refSystem : ReferenceSystem`, `config : ViewConfigModel`, `userPrefs`, `interaction`
  (restricted: DrawAnnotation, EditAnnotation, PickAnnotation, PickSurface, PickExploreCenter), ctrl/shift flags,
  `scenePath`, `animations`, `golden`, `[<TreatAsValue>] sceneExtras : Option<Json>`, `[<NonAdaptive>] kdCache`.
- `LiteAction`: wrappers `Navigation | Surfaces | Drawing | RefSystem | Animation | Golden`, plus `PickSurface`,
  `SetInteraction`, keys, `ImportOpcs`, `FlyToSurface`, `Home`, `OpenScene`/`SaveScene`/`SaveSceneAs`,
  `LoadAnnotations`/`SaveAnnotations`, `Undo`/`Redo` (→ `DrawingAction.Undo/Redo`).
- Panels (`?page=`): `render`, `surfaces` (`SurfaceApp` group/property views), `annotations` (`Drawing.UI` groups + toolbar),
  `properties` (`AnnotationProperties.view` + `viewResults`), `scene` (`ReferenceSystemApp` planet choice, small config).
  Never replace the `GoldenLayout` record at runtime.
- Fly-to / home via `CameraAnimations` + `AnimationApp` as in `addFlyToSurfaceAnimation` (`Viewer.fs:561`).

## Scene format

**Subset of `.pro3d` v3, not a new format.**
- Lite writes `version=3`, `cameraView`, `navigationMode`, `exploreCenter`, `surfaceModel`, `config`, `scenePath`,
  `referenceSystem` with existing Core codecs; annotations go to `<scene>.pro3d.ann` like the Viewer.
- `Scene.read3` (`Viewer-Model.fs` ~418) relaxes `bookmarks`, `viewPlans`, `scaleBars`, `sceneObjectsModel`,
  `geologicSurfacesModel`, `interactionMode`, `scenePath` to `Json.tryRead` + defaults (additive, no version bump).
  Cost: PRo3D builds older than this change cannot open a Lite-created scene.
- **Passthrough:** `LiteScene.read` keeps the full JSON object in `sceneExtras`; save overwrites only Lite's keys, so a full
  scene opened and saved in Lite keeps bookmarks, GIS, view plans, etc.

## Roadmap (one PR each, `features/<issue#>_…` branches)

| # | PR | Checkpoint |
|---|---|---|
| P0 | Remove `src/PRo3D.Lite` (folder, `PRo3D.sln` line 20, `docs/ModelTypes.md:37`, `AGENTS.md` tree); add empty `PRo3D.Composition` to sln, reference from Viewer; Composition section in `ai/ARCHITECTURE.md` | `build.cmd`, `runTests.cmd` green |
| P1 | Move navigation stack + `Config.fs` + `HostConfigs` (pure move; Viewer `navConf` etc. become aliases) | build + tests; tests-ui navigation/gizmo specs; manual FreeFly/ArcBall/MapView + Direct Tool Mode |
| P2 | `ProcessInit`, `SurfaceLoading`, `AnnotationFiles`; Viewer `Program.fs`, `SceneLoader`, `ViewerIO` forward to them | Section01 scene-load tests; `.pro3d.ann` round-trip test |
| P3a | **Characterization first:** `matchPickingInteraction` internal + `InternalsVisibleTo` Tests; tests for Draw (identity `hitF`), EditAnnotation drop, PickSurface select, PlaceCoordinateSystem, PickExploreCenter on `Viewer.initial` + loaded OPC (`TestHelpers.context`) | green on unchanged code |
| P3b | `SurfacePicking` + `PickRouting`; Viewer `PickSurface` branch and `matchPickingInteraction` delegate (keep `lastHash` dedupe, Ellipse branch, Viewer-only cases under `Unhandled`); unit tests for `skyRay`/`projectionRay` | P3a tests unchanged; tests-ui drawing specs |
| P4 | `SurfaceView` + `NavigationView`; `viewSingleSurfaceSg` uses `pickEvents`, `renderCommands`/`renderControlAttributes`/`threadPool` delegate | tests-ui screenshot specs unchanged; manual pick/preview |
| P5 | `LiteScene` codec + `read3` relaxation | Lite-written scene → `Scene.FromJson`; full scene → LiteScene → save → `Scene.FromJson` keeps all extras |
| P6 | Lite skeleton: host, model, Golden with `render` + `surfaces`, import OPC, visibility, fly-to/home, navigation; `docs/PRo3DLite.md`; `AGENTS.md` tree entry | headless `LiteApp.update` tests (import, visibility, Home moves camera); manual run on a real OPC |
| P7 | Annotations: draw/select/edit/delete via `routePick` + `DrawingApp.view` (port `allowAnnotationPicking`, `Viewer.fs:2599`); properties + results panel; undo/redo; `.pro3d.ann` load/save | headless: pick → point, undo/redo, delete, save→load equal; Lite `.ann` opens in full PRo3D |
| P8 | Scene open/save, planet selection; Playwright spec for Lite in `--server` mode | Lite scene ↔ Viewer both directions, manual + spec |

Viewer behaviour must not change in P0–P5; every lift PR is checked by the existing Features tests and tests-ui.

## Risks

- **GIS-placed surfaces** sit wrong in Lite (`Observation.none`) — warn on opening such scenes; reading the observer system later is possible.
- **Look differs** from the Viewer (lean effect: no SPICE lighting/image projection/shadows). Any added shading (e.g. view-space lighting) is a shader-vs-CPU decision to discuss first.
- **Process-global mutables** (`Sg.hackRunner`, `Config.*`, `usePackedAnnotationRendering`, `lastHash`, `Picking.cache`): fine with one app per process; tests hosting both apps must use identical values.
- **`DrawingApp.view` outside the Viewer** may rely on Viewer render-pass order (packed rendering, overlay vs depth-tested, GPU pick target acquired without release) — verify early in P7.
- **Undo coverage:** some property/group edits may not push undo — check before promising undo beyond add/remove/geometry.
- **`hitF` totality change** (`None` instead of `FastRay3d()` for unset projection) — covered by P3a tests.
- **Adaptify:** ensure `utilities/Adapt.fsx` picks up the new Lite project; keep Composition free of `[<ModelType>]`.

## Open points

- Project name: recreate as `src/PRo3D.Lite` (old one deleted in P0) vs. `PRo3D.LiteV2` — default `PRo3D.Lite`.
- Later: lift `LayoutApp` with an injected panel registry; namespace cleanup of moved files; SPICE observation support in Lite.

## Critical files

- `src/PRo3D.Viewer/Viewer/Viewer.fs` — `matchPickingInteraction` 314, `PickSurface`/`hitF` 1609–1741, `renderControlAttributes` 2507, `allowAnnotationPicking` 2599, `threadPool` 3015
- `src/PRo3D.Viewer/Viewer/Viewer-Utils.fs` — `viewSingleSurfaceSg` 265, `getSimpleSingleSurfaceSg` 620, `renderCommands` 1919
- `src/PRo3D.Viewer/{Navigation.fs, MapViewCameraController.fs, NavigationGizmo.fs, Navigation-Model.fs, Config.fs, Program.fs, Scene.fs, Viewer-Model.fs}`
- `src/PRo3D.Core/{Surface/SurfaceApp.fs, Surface/Surface.Sg.fs, Surface.fs, HeadlessPicking.fs, Drawing/Drawing-App.fs, Drawing/DrawingUtilities.fs, ReferenceSystem.fs}`
- `src/PRo3D.MapProjection/Program.fs` (host template), `src/Tests/Features/TestHelpers.fs` (headless harness)
