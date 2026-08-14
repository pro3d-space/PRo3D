# Architecture

How PRo3D is structured on top of aardvark.media's ELM architecture.

> **Prerequisite:** This document assumes the aardvark.media model — `App<'model,'mmodel,'msg>`, `Unpersist`, `[<ModelType>]`, the adaptive (`aval`/`aset`/`alist`/`amap`) update model, `ThreadPool`/`proclist`, and Giraffe/`MutableApp.toWebPart` hosting. See [aardvark.media/ai/ARCHITECTURE.md](https://github.com/aardvark-platform/aardvark.media/tree/main/ai). Here we only cover what PRo3D adds.

---

## The Big Picture

PRo3D is one large aardvark.media app whose model is composed of many **domain sub-apps**. Each sub-app (surfaces, drawing, bookmarks, GIS, …) is itself an ELM unit with its own `Model`/`Action`/`update`/`view`, living in `PRo3D.Core` or `PRo3D.Base`. The **viewer** in `PRo3D.Viewer` is the orchestrator: it owns the root model, routes messages to sub-apps, recombines their results, and builds the dockable UI.

```
                 PRo3D.Viewer  (root app)
   Model ── Scene ──┬── SurfaceModel        (SurfaceApp)
     │              ├── GroupsModel          (bookmarks / GroupsApp)
     │              ├── ScaleBarsModel       (ScaleBarsApp)
     │              ├── TraverseModel         (TraverseApp)
     │              ├── ViewPlanModel         (ViewPlanApp)
     │              ├── SceneObjectsModel     (SceneObjectsApp)
     │              ├── GeologicSurfacesModel (GeologicSurfaceApp)
     │              ├── SequencedBookmarks    (SequencedBookmarksApp)
     │              ├── GisApp                (Gis.GisApp)
     │              ├── ReferenceSystem / ViewConfigModel / ...
     ├── DrawingModel        (DrawingApp)
     ├── NavigationModel     (Navigation)
     ├── AnimationModel      (animation system)
     ├── ProvenanceModel     (ProvenanceApp)
     └── ...
   ViewerAction  ──(nested union)──▶ updateViewer ──▶ each sub-app update
```

---

## The Root Model

Defined in `src/PRo3D.Viewer/Viewer-Model.fs`.

Two layers matter:

1. **`Scene`** (`Viewer-Model.fs:211`) — the **persisted** part of the application: everything that is saved to a `.pro3d` scene file. It carries a `version : int` for migration and aggregates the persisted sub-models:

   ```fsharp
   type Scene = {
       version           : int          // schema version, see below
       cameraView        : CameraView
       navigationMode    : NavigationMode
       exploreCenter     : V3d
       interaction       : InteractionMode
       surfacesModel     : SurfaceModel
       config            : ViewConfigModel
       scenePath         : Option<string>
       referenceSystem   : ReferenceSystem
       bookmarks         : GroupsModel
       scaleBars         : ScaleBarsModel
       traverses         : TraverseModel
       viewPlans         : ViewPlanModel
       dockConfig        : DockConfig
       // ...
       sceneObjectsModel     : SceneObjectsModel
       geologicSurfacesModel : GeologicSurfacesModel
       sequencedBookmarks    : SequencedBookmarks
       screenshotModel       : ScreenshotModel
       gisApp                : PRo3D.Core.Gis.GisApp
   }
   ```

2. **`Model`** (`Viewer-Model.fs:584`) — the **full runtime** model. It embeds `scene : Scene` plus transient/runtime-only state that is *not* persisted: the live `drawing : DrawingModel`, `navigation : NavigationModel`, `animations`/`animator`, `provenanceModel`, picking flags (`picking`, `pivotType`, `surfaceIntersection`), the `messagingMailbox`, undo/redo (`past`/`future`, marked `[<TreatAsValue>]`), background thread pools (`snapshotThreads`, `backgroundPicking`), and so on.

   Fields annotated `[<NonAdaptive>]` (e.g. `animator`) are excluded from the adaptive model; `[<TreatAsValue>]` fields (e.g. undo history) are treated as opaque values rather than adaptified deeply.

### Messages: `ViewerAction` and `ViewerAnimationAction`

`ViewerAction` (`Viewer-Model.fs:85`) is a large discriminated union whose cases are mostly **wrappers around sub-app messages**:

```fsharp
type ViewerAction =
    | NavigationMessage       of Navigation.Action
    | DrawingMessage          of Drawing.DrawingAction
    | SurfaceActions          of SurfaceAppAction
    | BookmarkMessage         of ...
    | SequencedBookmarkMessage of ...
    | ViewPlanMessage         of ViewPlanApp.Action
    | SceneObjectsMessage     of ...
    | TraverseMessage         of TraverseAction
    | GisAppMessage           of Gis.GisAppAction
    // ... plus many viewer-level actions (imports, picking, config, key/mouse, IO)
```

The app is actually started with a slightly wider message type that layers animation + provenance on top of `ViewerAction` (`Viewer-Model.fs:666`):

```fsharp
type ViewerAnimationAction =
    | ViewerMessage     of ViewerAction
    | ProvenanceMessage of ProvenanceApp.ProvenanceMessage
    | AnewmationMessage of AnimatorMessage<Model>   // (sic)
```

So the started app is effectively `App<Model, AdaptiveModel, ViewerAnimationAction>`.

---

## Sub-App Composition

The pattern: a sub-app exposes `update : SubModel -> SubAction -> SubModel` (and a `view`). The viewer's root `updateViewer` matches a `ViewerAction` case, calls the sub-app's update on the corresponding field of the model, and writes the result back — typically through an **Aether/optics lens** (the `Lenses` helper in `PRo3D.Base/Utilities.fs` wraps `get`/`set`/`update`).

```fsharp
// conceptual shape inside updateViewer (src/PRo3D.Viewer/Viewer/Viewer.fs)
| SurfaceActions a ->
    let surfaces' = SurfaceApp.update model.scene.surfacesModel a ...
    Optic.set _surfacesModel surfaces' model
| DrawingMessage a ->
    let drawing' = DrawingApp.update model.drawing a ...
    { model with drawing = drawing' }
```

The dispatch chain in `src/PRo3D.Viewer/Viewer/Viewer.fs`:

| Function | Role |
|----------|------|
| `updateViewer` | The master `match` over `ViewerAction`, routing to sub-app updates |
| `updateInternal` | Thin wrapper that invokes `updateViewer` |
| `updateWithProvenanceTracking` | Top-level `update` actually wired into the app; optionally records provenance around `updateInternal` |

**To add a new sub-app:**
1. Define its `Model`/`Action`/`update`/`view` (usually in `PRo3D.Core`), with `[<ModelType>]` on the model.
2. Embed the model in `Scene` (if persisted) or `Model` (if transient).
3. Add a wrapper case to `ViewerAction`.
4. Handle that case in `updateViewer`, delegating to the sub-app and writing back.
5. Mount the sub-app's `view` in the dock layout (see below) and surface it in a `DockConfig`.
6. Run `adapt.cmd` to regenerate `*.g.fs`, and extend `Scene` versioning if persisted.

---

## App Startup & Hosting

The app record is assembled and started in `ViewerApp.start` (`src/PRo3D.Viewer/Viewer/Viewer.fs:2634`):

```fsharp
let app = {
    unpersist = Unpersist.instance
    threads   = threadPool
    view      = view runtime
    update    = updateWithProvenanceTracking runtime enableProvenance signature sendQueue messagingMailbox
    initial   = m
}
app.startAndGetState()   // -> (AdaptiveModel, MutableApp<...>)
```

The initial model `m` is built from `Viewer.initial` and then, depending on `StartupArgs`, optionally pipelined through scene loaders (`ViewerStartupLoad`, `Viewer.fs:2629`):
`SceneLoader.loadSceneFromFile` → `loadRoverData` → `loadAnnotations` → `loadCorrelations` → `loadSequencedBookmarks` → `addScaleBarSegments` → `addGeologicSurfaces`.

Process startup, backend selection, and SPICE init happen in `src/PRo3D.Viewer/Program.fs` (`[<EntryPoint; STAThread>]`):
- Set up `%APPDATA%/PRo3D` paths and logging.
- `Aardvark.Init()`, create `OpenGlApplication` (Vulkan optionally), apply GPU workarounds.
- Initialize SPICE coordinate transforms (`CooTransformation.initCooTrafo`).
- Parse command line (`CommandLine.parseArguments`, see [AUTOMATION.md](AUTOMATION.md)).
- Host via Giraffe `MutableApp.toWebPart' runtime false mainApp` (`Program.fs:373`, `open Aardvark.UI.Giraffe`), optionally mounting the remote API and remote-control app. UI is shown through **Aardium** (the Aardvark Electron-style shell), or headless in server mode.

### Dockable UI

The window layout is data-driven via `Aardvark.UI.Primitives` docking:
- `src/PRo3D.Viewer/DockConfigs.fs` defines named layouts (e.g. `full`, `gis`, `comparison`, `core`, `renderOnly`, `provenance`, view-planner / M2020 modes) as nested horizontal/vertical splits with stacked, named panels (`"render"`, `"surfaces"`, `"annotations"`, `"config"`, `"bookmarks"`, …).
- `src/PRo3D.Viewer/DashboardModes.fs` bundles a `DockConfig` with a name into selectable dashboard presets. The current one is stored as `Model.dashboardMode` / `Scene.dockConfig`.

The root `view` renders the 3D `RenderControl` plus the panel for each dock element, dispatching each panel's messages back through the corresponding `ViewerAction` wrapper.

---

## Threads, Messaging & Async

PRo3D uses aardvark.media's `ThreadPool<'msg>`/`proclist` for background work that yields messages (see upstream docs). PRo3D-specific pieces:

- **`messagingMailbox` / `MessagingMailbox`** — an async mailbox the viewer uses to inject messages from outside the normal update loop (e.g. remote API, async load completions). Carried on the root `Model`.
- **`snapshotThreads` / `backgroundPicking`** — dedicated `ThreadPool<ViewerAction>` fields for batch snapshot rendering and background surface picking, kept separate so they can be started/stopped independently.
- **Async picking** — surface picking results arrive via async values (`pickPreviewRequested : ConsumableAsyncValue<...>`); picking work is only spawned when an interaction actually requires it (see [RENDERING.md](RENDERING.md#picking)).

---

## Scene Persistence & Versioning

Scenes are saved as `.pro3d` files using **Chiron** JSON codecs (with `PRo3D.Base/Serialization.fs` providing the serializers; see [RENDERING.md](RENDERING.md#serialization) for the binary/JSON split). The key pattern is **explicit versioning**:

- `Scene.version` holds the schema version; `Scene.current = 3` (`Viewer-Model.fs:247`).
- Per-version reader functions `read0`, `read1`, `read2`, `read3` deserialize the corresponding on-disk schema, filling missing fields with defaults so **older scenes load into the current model**.
- Version history is noted in comments, e.g. v2 added traverses/sequenced-bookmarks/comparison, v3 added view plans.

**Adding a field does NOT require a version bump** — as long as the reader uses **`Json.tryRead`** (which yields `Option`) for that field and a sensible **default** exists for when it's absent. This is the common case and the codebase relies on it heavily (`Viewer-Model.fs` reads `comparisonApp`, `sequencedBookmarks`, `gisApp`, … via `Json.tryRead` and defaults them). So:

```fsharp
// additive change — no version bump needed
let! newThing = Json.tryRead "newThing"            // Option<_>
// ...
newThing = newThing |> Option.defaultValue Defaults.newThing
```

**Bump `current` and add a new `readN` only for changes a `tryRead`+default can't handle gracefully**: removing/renaming a field, changing a field's type or units, or otherwise changing the *meaning* of existing data so an old file would deserialize incorrectly. Never silently change the semantics of an existing field without a version bump — old scenes would mis-deserialize.

Sub-models that are persisted (e.g. `SurfaceModel`, `GroupsModel`, `ScaleBarsModel`, `GisApp`) typically also carry their own `version` field and `FromJson`/`ToJson` codecs, following the same discipline (additive via `tryRead`+default; bump only for breaking changes).

---

## See Also

- [DOMAIN.md](DOMAIN.md) — the sub-apps referenced here, in detail
- [RENDERING.md](RENDERING.md) — surface scene graphs, OPC, picking, serialization internals
- [AUTOMATION.md](AUTOMATION.md) — command line, remote API, snapshots, provenance
- [../docs/ProvenanceTracking.md](../docs/ProvenanceTracking.md), [../docs/ModelTypes.md](../docs/ModelTypes.md)
- aardvark.media: [ARCHITECTURE.md](https://github.com/aardvark-platform/aardvark.media/tree/main/ai) — App type, Unpersist, ThreadPool, MutableApp hosting
