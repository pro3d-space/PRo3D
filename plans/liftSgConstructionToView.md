# Lift scene-graph construction out of `SurfaceModel` and into the view layer

## Goal

Stop storing rendering-pipeline values inside the domain model. Today
`PRo3D.Core.SurfaceModel` carries two pre-built scene-graph fields:

```fsharp
type SurfaceModel = {
    surfaces       : GroupsModel                              // metadata
    sgSurfaces     : HashMap<Guid, SgSurface>                 // !!! holds ISg per surface
    sgGrouped      : IndexList<HashMap<Guid, SgSurface>>      // !!! derived from the above
    kdTreeCache    : HashMap<string, ConcreteKdIntersectionTree>
    debugPreTrafo  : string
}
```

…and `SgSurface` itself owns the compiled scene graph:

```fsharp
type SgSurface = {
    surface     : Guid
    trafo       : Transformation
    globalBB    : Box3d
    sceneGraph  : ISg              // <-- compiled rendering output
    picking     : Picking          //     (Picking.KdTree | Picking.PickMesh ISg)
    opcScene    : Option<OpcScene>
    ...
}
```

Move scene-graph construction into the **view functions** (`viewRenderView`,
`viewInstrumentView`, the planned `viewEquirectView`, snapshot rendering)
so the model only carries loaded surface data, and each view builds the Sg
it needs from that data and its own view-specific configuration (LoD
decider, surface effect, …).

## Why this matters now

1. **`sgGrouped` is pure derived state, and `triggerSgGrouping` is manual
   cache invalidation.** The grouping is computed by `Groups-Model.fs:513`
   `groupSurfaces : HashMap<Guid, SgSurface> → HashMap<Guid, Surface> →
   IndexList<HashMap<Guid, SgSurface>>` — buckets surfaces by
   `Surface.priority.value`, sorts ascending. It's a pure function of two
   inputs already on the model. But because the result is stored as a
   field, every code path that mutates either input has to remember to
   call `|> SurfaceModel.triggerSgGrouping`. The 8 current call sites:

   | Site | Trigger |
   |---|---|
   | `SurfaceApp.fs:859` | `SurfaceProperties.SetPriority` |
   | `SurfaceApp.fs:868` | `RemoveSurface` |
   | `SurfaceApp.fs:947` | `GroupsAppAction.RemoveGroup` / `RemoveLeaf` |
   | `Scene.fs:190` | OPC scene import |
   | `Scene.fs:218` | OBJ import |
   | `Scene.fs:272` | scene-object import |
   | `Snapshot-Utils.fs:73` | `clearSnapshotNode` |
   | `Snapshot-Utils.fs:286` | snapshot scenegraph mutation |

   One missed call = stale grouping = a surface that doesn't render in the
   right order (or at all) until something unrelated forces a refresh.
   This is the canonical "cache that isn't pull-based" anti-pattern;
   replacing it with an adaptive computation deletes all 8 sites.

2. **`sgSurfaces` carries `ISg` per surface.** That binds the model to
   the rendering pipeline: any view that wants to render the same loaded
   data with a different LoD decider or effect chain must either build a
   parallel `sgSurfaces` field (rejected for the equirect view — see
   `plans/equirectangularMapView.md`), or accept the model's choice of
   PatchNode + `surfaceEffect`. The equirect view needs a different LoD
   decider; future projection views will likely want different effect
   chains. The right shape is: model holds the **loaded data**, view
   compiles it.

3. **Testability.** The current `SurfaceModel` cannot be unit-tested
   without `IRuntime` / `Sg` infrastructure, because constructing it
   requires building real `PatchNode`s. After this refactor `SurfaceModel`
   holds plain data and can be exercised in `Tests/`.

## What is data vs. what is view

The split that drives the design:

| Field of `SgSurface` | Nature | After refactor |
|---|---|---|
| `surface : Guid` | identity | stays on `LoadedSurface` |
| `trafo : Transformation` | per-surface user transform | stays on `LoadedSurface` |
| `globalBB : Box3d` | geometry property | stays on `LoadedSurface` |
| `picking : Picking` (the `KdTree` case) | geometry for picking | stays on `LoadedSurface` |
| `picking : Picking` (the `PickMesh ISg` case) | rendering | view-built |
| `opcScene : OpcScene option` | OPC config (paths, hierarchy refs) | stays on `LoadedSurface` |
| `sceneGraph : ISg` | rendering | view-built |
| `isObj : bool` (or equivalent discriminator) | data | stays on `LoadedSurface` |

The new type:

```fsharp
type LoadedSurface = {
    surface    : Guid
    trafo      : Transformation
    globalBB   : Box3d
    kdTrees    : HashMap<Box3d, KdTrees.Level0KdTree>
    opcScene   : OpcScene option
    isObj      : bool
    objLoader  : Loader.Scene option         // for Assimp-loaded OBJs
    // any other view-independent metadata
}
```

`Picking.PickMesh ISg` (the rare OBJ-picking variant) is the one mixed-
concerns case. Two options, decided per-call-site:

- **(a)** Generate the pick-mesh ISg on demand at view-build time from
  the OBJ data; `LoadedSurface` keeps just the loader output.
- **(b)** Keep a `pickMesh : ISg option` field on `LoadedSurface` as a
  pragmatic exception — it's tiny, view-independent, and the alternative
  is wiring a factory for one rarely-used picking path.

Recommendation: (a) for cleanliness, (b) only if (a) reveals a sharp edge.

## Architecture after refactor

```
SurfaceModel
 ├── surfaces        : GroupsModel                  (unchanged — metadata, tree, priorities)
 ├── loadedSurfaces  : HashMap<Guid, LoadedSurface> (replaces sgSurfaces; no ISg)
 └── kdTreeCache     : HashMap<string, ConcreteKdIntersectionTree>
 (sgGrouped is gone)

viewRenderView (and siblings)
 ├── reads m.scene.surfacesModel.loadedSurfaces : amap<Guid, AdaptiveLoadedSurface>
 ├── reads m.scene.surfacesModel.surfaces.flat
 ├── adaptively buckets by priority      → alist<amap<Guid, AdaptiveLoadedSurface>>
 ├── per surface, builds ISg via         → buildSurfaceSg loadedSurface viewCfg
 │     (PatchNode + LoD decider + effect)
 └── wraps with surfaceEffect / equirectSurfaceEffect / objEffect as appropriate
```

The factory `buildSurfaceSg : LoadedSurface → SurfaceViewConfig → ISg`
lives in `src/PRo3D.Core/Surface/Surface.Sg.fs` next to the existing
`createSgSurface`. `SurfaceViewConfig` captures the view-specific knobs:

```fsharp
type SurfaceViewConfig = {
    lodDecider  : LodDecider              // mars2 | equirect | fixed | ...
    surfaceEffect : Effect                // surfaceEffect | equirectSurfaceEffect | objEffect
    runtime     : IRuntime
    signature   : IFramebufferSignature
    // anything else PatchNode currently grabs from OpcScene that varies per view
}
```

`viewRenderView` passes the existing settings; `viewEquirectView` passes
the equirect decider + effect (per `plans/equirectangularMapView.md`).

## Milestones

### M1 — Delete `sgGrouped` from the model

**Lowest risk; visible-correctness win on its own.**

- `Groups-Model.fs` (line 462) — remove the `sgGrouped` field.
- Replace `triggerSgGrouping` with an adaptive grouping helper
  (`SurfaceModel.groupedAdaptive`) used inside the view functions:

  ```fsharp
  // pseudo
  let groupedAdaptive (sgSurfaces : amap<Guid, AdaptiveSgSurface>)
                      (surfacesFlat : amap<Guid, AdaptiveLeaf>) =
      // for each (guid, sg) pair look up the Surface's priority,
      // bucket by priority, sort ascending, emit alist<amap<Guid, _>>
      ...
  ```

  v1 implementation: coarse — `AVal.bind` on both inputs, rebuild the
  whole grouping on any change. Scenes have O(10) surfaces; this is
  cheap. Refine to per-priority incrementality only if profiling shows
  it matters.

- Update consumers to call `groupedAdaptive m.scene.surfacesModel`
  instead of reading `m.scene.surfacesModel.sgGrouped`:
  - `Viewer.fs:2358` (`viewInstrumentView`)
  - `Viewer.fs:2431` (`viewRenderView`)
  - `Viewer-Utils.fs:628, 922, 1043, 1120`
  - `SnapshotSg.fs:109, 145`
  - `Snapshot-Utils.fs:245-247` (the bbox-via-grouping path may be
    rewritten to consume `sgSurfaces` directly — grouping is irrelevant
    to the bbox).
- Remove every `|> SurfaceModel.triggerSgGrouping` call site (`Scene.fs:190,
  218, 272`, `SurfaceApp.fs:859, 868, 947`, `Snapshot-Utils.fs:73, 286`).
- Delete `SurfaceModel.groupSurfaces` and `SurfaceModel.triggerSgGrouping`.

**Validation gate for M1 alone:** scene loads, surfaces render in priority
order, removing/adding/reprioritizing a surface reflects in the view
without manual refresh, instrument view still works, snapshots still
render correctly.

### M2 — Split `SgSurface` into `LoadedSurface` (data) + view-time `ISg`

**The architectural fix.**

- `Surface-Model.fs:1024-…` — introduce `LoadedSurface`; keep `SgSurface`
  as a deprecated alias during transition (or rename in one go — see
  "Big-bang vs. staged" below).
- Move `sceneGraph : ISg` and `Picking.PickMesh ISg` (if option (a)) out
  of the type.
- `SurfaceModel.sgSurfaces` → `loadedSurfaces : HashMap<Guid, LoadedSurface>`.
- The surface-loading pipeline produces `LoadedSurface` values:
  - `Surface.Sg.createSgSurface` and friends — split into
    `Surface.Loader.loadSurface : ... → LoadedSurface` (data) and
    `Surface.Sg.buildSurfaceSg : LoadedSurface → SurfaceViewConfig → ISg`
    (view).
  - Sites that currently mutate `sgSurfaces` (`SurfaceApp.fs:863, 867,
    881, 944, 962, 977, 1031, 1056, 1082`; `Scene.fs:185, 214, 257, 294`;
    `Snapshot-Utils.fs:71, 283`) start producing `LoadedSurface` values
    instead of `SgSurface` values.
- View functions call `buildSurfaceSg` adaptively per `LoadedSurface`,
  inside the `AMap.map` body that today reads `surface.sceneGraph`
  (`Viewer-Utils.fs:1053-1071`, `Viewer-Utils.fs:933-959`).

Adaptive caching: the existing pattern (`amap → AMap.map → ASet → Sg.set`)
caches per-key. The `ISg` factory is invoked once per surface load, not
per frame, because the keys (Guids) are stable. Aardvark's incremental
machinery already handles this — no manual caching needed.

**Validation gate for M2:** same as M1, plus: picking still works (the
KdTree path didn't depend on ISg); selected-surface render still works
(`Surface.Sg.fs:619` reads `surface.sceneGraph` — needs to call
`buildSurfaceSg` instead); comparison/measurement tools still work
(`AreaSelection.fs:56-58`, `SurfaceMeasurements.fs:50, 89`).

### M3 (deferred, optional) — Reconsider `loadedSurfaces` as model state

After M2 the model holds a `HashMap<Guid, LoadedSurface>`. That's
still effectively an asset cache. A purer split would move the loaded-
asset cache out of the model entirely (e.g. into an `ISurfaceService`
keyed by Guid), leaving the model with just `surfaces : GroupsModel` and
the user's intent. Out of scope; flagged for completeness.

## Big-bang vs. staged

M1 and M2 are **independent commits** and should be reviewed separately:

- M1 changes one record field (delete) + ~10 call sites + adds one
  adaptive helper. Reversible. The semantic change is "grouping is now
  always fresh."
- M2 changes a record type (`SgSurface` → `LoadedSurface` + factory),
  every surface-load call site, and every render path. Larger and harder
  to review piecemeal. Worth doing in one PR after M1 has settled.

Doing M2 without M1 is wasted work — you'd refactor `sgGrouped` consumers
twice. Do M1 first.

## Sites to modify

### M1
- `src/PRo3D.Core/Groups-Model.fs:457-527` — drop `sgGrouped` field,
  delete `groupSurfaces`, `triggerSgGrouping`; the generated
  `Groups-Model.g.fs` regenerates from the type and follows.
- `src/PRo3D.Core/Groups-Model.g.fs:233-242, 263, 272` — regenerated;
  no manual edit (regenerated by Adaptify if the project uses it; if it's
  hand-maintained, update these spots too).
- `src/PRo3D.Core/Surface/SurfaceApp.fs:859, 868, 947` — drop the
  `|> SurfaceModel.triggerSgGrouping` tail calls.
- `src/PRo3D.Viewer/Scene.fs:190, 218, 272` — same.
- `src/PRo3D.SimulatedViews/Snapshots/Snapshot-Utils.fs:73, 286` — same.
- Add `SurfaceModel.groupedAdaptive` (new helper, `Groups-Model.fs`).
- `src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:628, 922, 1043, 1120` — replace
  `m.scene.surfacesModel.sgGrouped` with `groupedAdaptive
  m.scene.surfacesModel`.
- `src/PRo3D.Viewer/Viewer/Viewer.fs:2358, 2431` — same.
- `src/PRo3D.Viewer/Viewer/SnapshotSg.fs:109, 145` — same (signature of
  `createSceneGraph` already takes `sgGrouped` as a parameter; just change
  what's passed at the call site).
- `src/PRo3D.SimulatedViews/Snapshots/Snapshot-Utils.fs:245-247` — bbox
  path likely simplifies to a direct fold over `sgSurfaces`.

### M2
- `src/PRo3D.Core/Surface-Model.fs:1024-…` — introduce `LoadedSurface`,
  remove `sceneGraph` from `SgSurface` (or rename outright).
- `src/PRo3D.Core/Surface/Surface.Sg.fs` — split `createSgSurface` into
  `Surface.Loader.loadSurface` (returns `LoadedSurface`) and
  `Surface.Sg.buildSurfaceSg : LoadedSurface → SurfaceViewConfig → ISg`.
  The `LodDecider` selection at line 436 (`cleanedOldLegacyLoD`) becomes
  a `SurfaceViewConfig.lodDecider` argument.
- `src/PRo3D.Core/Groups-Model.fs` — rename `sgSurfaces` →
  `loadedSurfaces`, retype to `HashMap<Guid, LoadedSurface>`.
- `src/PRo3D.Viewer/ViewerLenses.fs:28` — rename
  `_sgSurfaces` → `_loadedSurfaces`.
- Producer sites (now produce `LoadedSurface`):
  `src/PRo3D.Core/Surface/SurfaceApp.fs:863-1082` (every line that today
  writes `sgSurfaces`); `src/PRo3D.Viewer/Scene.fs:185, 214, 257, 294`;
  `src/PRo3D.SimulatedViews/Snapshots/Snapshot-Utils.fs:71, 283`.
- Consumer sites that read `.sceneGraph` directly:
  `src/PRo3D.Core/Surface/Surface.Sg.fs:605` (selected-surface render path
  reads `surface.sceneGraph` — must call `buildSurfaceSg` instead).
- Consumer sites that read other `SgSurface` fields are unaffected — the
  fields move onto `LoadedSurface` with the same names.
- View functions: `Viewer-Utils.fs:935-959, 1048-1088` — the per-surface
  `AMap.map` body builds the Sg from `LoadedSurface` + view config.

## Verification

After **M1**:
1. `dotnet build` clean.
2. Open `testdimo.pro3d` (or any scene with ≥ 2 surfaces at different
   priorities). Surfaces render in priority order. Confirmed by toggling
   `Surface.priority` on one of them via the UI — render order updates
   without scene reload.
3. Add a new surface via Import OPC: it appears in the render view.
4. Remove a surface: it disappears.
5. Instrument view renders identically to before.
6. Snapshot tool produces the same output as a pre-refactor build (binary
   comparison on a fixed scene, or visual diff).

After **M2**:
7. All of (1)–(6).
8. Picking via mouse hover/click still hits the right surface (KdTree
   path unchanged conceptually; verify regression).
9. Surface comparison tool (`AreaSelection`) still works.
10. Selected-surface highlight (the `Surface.Sg.fs:605` path) still
    renders.
11. Equirect view (`plans/equirectangularMapView.md`) can now be
    implemented by passing a different `SurfaceViewConfig` to
    `buildSurfaceSg`, with no new fields added to `SurfaceModel`.

## Open questions

1. **Adaptify regeneration.** If `Groups-Model.g.fs` is auto-generated
   from `Groups-Model.fs` via Adaptify, M1 is trivial — change the source
   type, regenerate. If it's hand-maintained, M1 also touches `*.g.fs`
   explicitly. Verify before starting.
2. **`Picking.PickMesh ISg` path.** Unknown how widely used — grep usage
   before deciding (a) vs (b). If only OBJ surfaces use it and OBJ
   loading already retains the Assimp scene, (a) is free.
3. **`SnapshotSg.createSceneGraph` signature** already takes `sgGrouped`
   as a parameter, so consumers there aren't reading model state directly
   — the call sites at `SnapshotSg.fs:145` and `Snapshot-Utils.fs:245`
   are where the model is read, and those are the only spots that need to
   switch to `groupedAdaptive`.

## Out of scope

- `kdTreeCache` (still a sensible cache on the model — it's a string-keyed
  disk-cache for serialized KdTrees, not a rendering value).
- `debugPreTrafo` (debug string, harmless).
- `SgSurface.opcScene` — stays as-is on `LoadedSurface`; it's
  configuration data, not rendering output.
- The equirect view itself — covered by
  `plans/equirectangularMapView.md`. This refactor is the precondition
  that lets the equirect view build its Sg in `viewEquirectView` without
  adding `sgGroupedEquirect` to the model.
