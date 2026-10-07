# Long stalls in PRo3D, and what to show the user while they last

## Why this is not just "add a spinner"

PRo3D's UI and its 3D view both hang on **one lock**. In aardvark.media:

- `App.start`'s update thread runs `doit`, which takes `lock l` and wraps **the whole
  drained message queue** in a single `transact`
  (`aardvark.media/src/Aardvark.UI/App.fs:66-99`).
- The DOM-diff thread that pushes HTML patches to the browser takes the *same* lock
  (`MutableApp.fs:195-200`).
- The render service is handed the same lock too
  (`MutableApp.fs:332`: `Aardvark.Service.Server.toWebPart app.lock renderer`).

So while one `update` call is slow, **the DOM cannot change and the 3D image stream
stops**. Three consequences drive the whole plan:

1. **No model-driven indicator can appear during a stall.** Anything written into the
   model is only rendered *after* the update that wrote it returns. A "please wait"
   set in the same update as the work is a "that was slow" message.
2. **`doit` batches.** Two messages emitted back-to-back (the trick used by
   `ImportDiscoveredSurfacesThreads`, below) can land in one transaction, in which case
   the text and the work share a transaction and the user sees neither until both are
   done. Whether they split is a scheduling race.
3. **Only the client keeps running.** CSS animations, JS timers and anything served by a
   Suave route that does *not* take `app.lock` are alive throughout. That is the only
   place a truthful "busy" signal can come from without moving work off the update thread.

## The feedback we have today

`UserFeedback<'a>` — `src/PRo3D.Viewer/Viewer/Viewer.fs:55-83`, rendered by
`ViewerGUI.textOverlaysUserFeedback` (`ViewerGUI.fs:321`) as white text in the top-right
corner of the render view.

```fsharp
type UserFeedback<'a> = { id : string; text : string; timeout : int; msg : 'a }

let createWorker (feedback : UserFeedback<ViewerAction>) =
    proclist {
        yield UpdateUserFeedback ""
        yield UpdateUserFeedback feedback.text
        yield feedback.msg                    // <- the actual work, piggy-backed
        do! Proc.Sleep feedback.timeout
        yield ThreadsDone feedback.id
    }
```

It is a **transient toast**, and for what it is mostly used for — "scene saved"
(`Viewer.fs:1860`), "Screenshot saved" (`:2285`), "Snapshot generation started" (`:1235`)
— it is fine. It is the wrong tool for a stall, for five separate reasons:

1. **It is post-hoc by construction.** See point 1 above. The one place it tries to be
   pre-hoc is `ImportDiscoveredSurfacesThreads` (`Viewer.fs:1376`), which sets
   `text = "Importing OPCs..."` and `msg = DiscoverAndImportOpcs sl` so that the text
   message is processed before the import message. That is the race from point 2 — and
   even when it wins, the text then sits there for the *remainder* of its 5 s timeout,
   which for a two-minute import means it vanishes long before the import ends.
2. **`msg` is a work channel disguised as a feedback field.** Feedback text and "run this
   expensive action" are unrelated concerns welded into one record.
3. **One slot, and clearing is not scoped.** `userFeedback` is a single `string`, and
   `ThreadsDone id` (`Viewer.fs:2054`) unconditionally sets it to `""`. Two overlapping
   toasts clobber each other: the older one's timeout wipes the newer one's text.
4. **It lives in the persisted model.** `userFeedback : string` and
   `feedbackThreads : ThreadPool<ViewerAction>` are fields of `Scene`
   (`Viewer-Model.fs:241-242`) — the record that defines the `.pro3d` save format. They
   are not written to JSON, but every `Scene.readN` has to initialise them and every
   reader of the type has to know they are transient.
5. **No busy state at all.** There is no "still working", no elapsed time, no progress, no
   cancel, and nothing is wired to the operations that actually hang.

There *is* good infrastructure next to it, already used correctly once: the background
preview pick (`Viewer.fs:1550-1581`) — `ConsumableAsyncValue` (an MVar) in the model, a
`proclist` worker on `Async.SwitchToThreadPool()`, results posted back through
`messagingMailbox`. That is the framework's own canonical pattern (cf.
`aardvark.media/src/Examples (dotnetcore)/25 - BackgroundOperation`). It is the thing to
generalise.

There is also an unused out-of-band channel: `sendQueue : BlockingCollection<string>` plus
the `/websocket` route (`Program.fs:215-225, 376`), which is *not* behind `app.lock`.

---

## Inventory of stalls

Ranked by (how long) x (how often a user hits it). "UI thread" = inside `updateViewer`,
i.e. holding `app.lock`.

### 1. First pick per patch — UI thread, 0.2–3 s, constantly

`Picking.pickRay` runs synchronously in the update for every real click:
`Viewer.fs:1594, 1623, 1629` (`PickSurface`), and — easy to miss — `Viewer.fs:727`,
where the **arcball's** `pickingFunction` picks the new orbit centre, so *navigation*
can stall too.

The cost is `DebugKdTreesX.loadObjectSet` (`src/PRo3D.Core/Surface.fs:126`) on a cache
miss: read the `.kdtree`, then `loadTriangles'` (`Surface.fs:107`) reads the patch's
`.aara` position grid, transforms every vertex, and builds a `TriangleSet`. For a
1032² patch that is ~4 MB of positions and millions of triangles. Two avoidable costs
inside it:

- `getInvalidIndices3f` (`Surface.fs:84`) does `positions |> List.ofArray |> List.mapi …
  |> List.choose id` — an F# list of the entire position array, allocated per patch. A
  plain array loop is the same result at a fraction of the cost. (This also violates the
  complexity rule in CLAUDE.md.)
- `getTriangleSet` (`Surface.fs:96`) builds the triangles through a `Seq.map |>
  chunkBySize |> map |> filter |> toArray` pipeline over millions of elements.

The *hover preview* pick is already on a background thread. The click is not, so the
first click on a cold patch is exactly the "long hang" being reported.

`Picking.cache` (`Picking.fs:25`) is a `mutable` global `HashMap` with **no bound** —
every patch ever picked keeps its triangle set alive.

### 2. Color by Category → *resample surface* — UI thread, seconds to minutes

`sampleSurfaceForCbc` (`Viewer.fs:134-172`), triggered from `Viewer.fs:926`. One ray cast
per **control point of every annotation**, each a full `doKdTreeIntersection`, each cold
patch paying cost #1. `docs/ColorByCategory.md:74` already says it runs on the UI thread.

It has a pathological amplifier: the per-patch triangle→grid mapping cache
(`ProfileAttributeExtraction.fs:107-130`) holds 32 patches and, when full, **`Clear()`s
the whole dictionary** instead of evicting one entry. Annotations are iterated in
`HashMap` order, so points hop between patches; past ~32 patches the cache is repeatedly
emptied and the ~4 MB mappings are rebuilt over and over. Worst case this turns a linear
pass into a quadratic one.

### 3. Color by Category → every panel edit — render thread, seconds per edit

`PackedRendering.linesNoIndirect` (`PackedRendering.fs:654`), `.fills` (`:855`) and
`.points` (`:1051`) are each one `AVal.custom` that packs **geometry and colour into the
same buffers**, with `cbcSettings` read at the top of the closure (`:663`, `:867`,
`:1061`). Any CbC change — dragging a colour picker, moving the min/max, switching
attribute — invalidates the whole thing, so every annotation's `getPolylinePointsAt`
(`Drawing.Sg.fs:198`, which walks every segment and every sampled point) re-runs and all
vertex buffers are re-uploaded. On a scene with densely sampled polylines that is a
visible hitch *per mouse-move of a colour slider*.

This one is not an `update` stall, so a busy overlay is the wrong fix — splitting the
colour buffer from the geometry buffer is.

### 4. OPC import / scene load — UI thread, 10 s to several minutes

`DiscoverAndImportOpcs` (`Viewer.fs:1348`) → `Files.superDiscoveryMultipleSurfaceFolder`
(a recursive directory walk, often over a network share) → `SceneLoader.import'` →
`Surface.Sg.fs:421` `KdTrees.loadKdTrees` per hierarchy. If the `.kdtree` files are
missing it *builds* them (`KdTrees.fs:270-360`) — that is minutes, and it is the one place
that already calls `Report.Progress` (`KdTrees.fs:355`), into the log, where no user
looks. Loading a scene (`Viewer.fs:1776`) goes down the same path.

This is the only stall with any feedback today, and it is the racy one from above.

### 5. Annotation export with *coordinates from file* or *surface properties* — UI thread, seconds to minutes

`Viewer.fs:1087-1107` calls `AnnotationExportViewer.export` synchronously.
`docs/AnnotationExport.md:303` and `:438-445` both state outright that "PRo3D is
unresponsive while the export runs" — a ray cast (plus an image decode per layer) per
exported point, and with *include sampled segment points* on, a polyline easily has
thousands.

### 6. Drawing a long segment — UI thread, per click

`Drawing-App.fs:58` `resampleSegment` walks `a → b` in `samplingDistance` steps and picks
the surface at each step, on every added point (`:284`) and on every moved vertex (`:151`).
A long segment with a small sampling distance is hundreds to thousands of picks *inside
the click*.

### 7. `RebuildKdTrees` — UI thread, minutes

`SurfaceApp.fs:930-955`: enumerate directories, `PatchHierarchy.load` each, build every
KdTree. Explicitly user-invoked, so expectations are lower, but it is entirely silent.

### 8. Shader link on a cold cache — ~15 s, first start only

`ViewerUtils.SharedEffectPool` (`Viewer-Utils.fs:1131-1140`). Already exposes
`ready : cval<bool>` to the DOM as `data-surface-shaders` **for the UI tests**
(`ViewerGUI.fs:337-341`) — and shows the user nothing. Cheapest possible win.

### 9. Expanding a large annotation group — browser side, seconds

Noted in `docs/SbmtImport.md:30`: the tree materialises a DOM node per leaf on expand;
4,800 rows visibly stalls the UI. Mitigated by bucketing into sub-folders, not fixed.

---

## Plan

Four phases. Phase 1 alone makes every stall in the list visible, including ones not
listed, and touches none of the stalling code.

### Phase 1 — a busy indicator that works *during* a stall

The indicator has to come from outside `app.lock`. Two pieces:

**Server:** a `Busy` module holding a plain mutable cell (`opName : string`,
`startedUtc : DateTime`), set by a wrapper around `updateViewer` in
`ViewerApp.updateInternal` (`Viewer.fs:2444`) — one `try/finally` around the existing
call, plus a label derived from the `ViewerAction` case. Expose it on a new Suave route
(`/busy`) added next to the existing ones in `Program.fs:374-383`. The route handler reads
the cell and returns JSON. It **must not** touch the adaptive model or take `app.lock` —
that is the whole point, and the existing `/api` remote-api routes are the precedent for
adding routes here.

**Client:** a small script (an `onBoot` on the viewer's root element, like
`AnnotationExportApp.viewModal` does at `AnnotationExportApp.fs:441`) polling `/busy`
every ~200 ms. When an operation has been running for more than ~400 ms it fades in a
CSS-animated overlay: spinner, the operation label, and a live elapsed-time counter.
Because it is CSS plus `setInterval` in the browser, it keeps animating while the server
is frozen solid.

Deliberately *not* a modal that blocks input: it is an indicator, not a dialog.
Threshold and poll interval go behind `Config` so they can be tuned without a rebuild.

**Labels.** A `ViewerAction -> Option<string>` function, `None` for everything cheap.
Start with: `PickSurface` / `NavigationMessage` → "picking", `DiscoverAndImportOpcs` /
`ImportSurface` → "importing surfaces", `LoadScene` → "loading scene",
`AnnotationExportMessage (Export _)` → "exporting annotations",
`ColorByCategoryMessage ResampleSurface` → "sampling surface", `RebuildKdTrees` →
"building KdTrees", `DrawingMessage` → "sampling segment".

Free extra in the same overlay, since it is client-side anyway: show
`SharedEffectPool.ready = false` as "compiling shaders" (#8) — that one can piggy-back on
the `data-surface-shaders` attribute that already exists.

### Phase 2 — get the worst offenders off the update thread

Generalise the background-pick pattern (`Viewer.fs:1550-1581`) into a reusable `Jobs`
module rather than open-coding a `proclist` per operation:

```fsharp
type JobId = JobId of string
type JobState = {
    name       : string
    progress   : Option<float>
    startedUtc : DateTime
    cancel     : CancellationTokenSource
}
```

- `jobs : HashMap<JobId, JobState>` goes in the **runtime `Model`**, not in `Scene` — it
  is transient, like `backgroundPicking` (`Viewer-Model.fs:679`) already is.
- A job is started from `update` by putting its input into an MVar and returning a model
  with one more entry in `jobs`; the worker runs on `Async.SwitchToThreadPool()` and posts
  `JobProgress (id, p)` / `JobFinished (id, result)` back through `messagingMailbox`, the
  same route the preview pick already uses.
- Progress then renders through the ordinary adaptive path, and the Phase-1 overlay is no
  longer needed for that operation — it degrades into a proper progress bar with a cancel
  button.

Port in this order, easiest and most valuable first:

1. **CbC resample (#2).** Already isolated in one function that only *reads* the model.
   Progress is "annotation i of n". Lowest risk, highest visible payoff.
2. **Annotation export (#5).** Same shape — reads the model, writes a file.
3. **OPC import and scene load (#4).** Biggest win, biggest change: the result mutates the
   surface model and allocates GPU resources, so the *result application* has to come back
   as a message on the update thread while only the loading runs in the job. Replaces the
   `ImportDiscoveredSurfacesThreads` racy trick, which then gets deleted.
4. **Click pick (#1).** Reuse the existing preview-pick channel rather than a job: the
   machinery is already there and already warms the same `Picking.cache`. Needs care —
   the click must not be *dropped* while the pick is in flight, and the interaction it
   feeds (`matchPickingInteraction`) has to run on the update thread once the hit arrives.

Segment resampling (#6) stays synchronous for now — it is per-click and interactive; the
right fix there is to bound the sample count, not to make drawing asynchronous.

### Phase 3 — the stalls that should not exist

Independent of the feedback work, each small and self-contained:

- **LRU instead of `Clear()`** in `ProfileAttributeExtraction.fs:126-129`. Evicting the
  oldest entry instead of emptying the cache removes the quadratic blow-up in #2.
- **`getInvalidIndices3f`** (`Surface.fs:84`) → array loop, no intermediate list.
  **`getTriangleSet`** (`Surface.fs:96`) → array loop instead of the `Seq` pipeline.
- **Split colour from geometry** in `PackedRendering` (#3): keep the packed positions in
  one `AVal.custom` and the per-vertex colours in a second one that depends on the CbC
  settings and the annotation colours but *not* on the geometry. A colour-picker drag then
  re-uploads a colour buffer instead of re-walking every polyline.
- **Bound `Picking.cache`** (`Picking.fs:25`) the way the triangle-grid cache is bounded
  (with the LRU from above), so long sessions stop growing without limit.

### Phase 4 — clean up `UserFeedback` itself

Once jobs exist, the toast can go back to being only a toast:

- Move `userFeedback` and `feedbackThreads` out of `Scene` and into `Model`. This is a
  *removal* of fields from the persisted record, so per CLAUDE.md it needs a `Scene`
  version bump and a new `readN`.
- Replace the single `string` with `IndexList<Toast>` (id, text, expiry) so
  `ThreadsDone id` removes only its own entry (`Viewer.fs:2054`) and overlapping toasts
  stop clobbering each other.
- Delete the `msg` field from `UserFeedback` and the `ImportDiscoveredSurfacesThreads`
  detour with it.

## Docs

Phase 1 gets `docs/BusyIndicator.md` (what it watches, thresholds, why it is client-side).
Phase 2 gets `docs/BackgroundJobs.md` (how to add a job, what may not run in one).
`docs/ColorByCategory.md:74` and `docs/AnnotationExport.md:303,438` currently document the
UI-thread behaviour as a caveat and need updating as each is ported.

## Suggested first cut

Phase 1 plus the two small items from Phase 3 (`Clear()` → LRU, and the
`getInvalidIndices3f` array loop). That is a self-contained PR that makes every hang
visible and makes the two worst ones measurably shorter, without restructuring anything.
