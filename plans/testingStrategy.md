# Staged testing for annotation boolean operations

Research + design. Goal: get testing right *before* boolean operations are built, so the
implementation can be driven by tests rather than retrofitted with them.

## Decisions taken

- **Start with invariants + FsCheck on the pure geometry.** No new project for step 1.
- **The 2D lab gets its own minimal model** (Simple2DDrawing-style) calling the *real*
  `PolyRegion` / `PolygonFill` / `SurfaceChart`. It does not drive `DrawingApp.update`, so it
  drags in no surfaces, picking or reference systems.
- **No model goldens. Ever.** Assertions are targeted facts plus per-message invariants.
- **Replay targets the sub-app level** (`DrawingApp.update`), which needs no GL and runs in CI.

### What these decisions delete

Worth stating, because it is most of the complexity:

| Dropped | Because |
|---|---|
| id injection into `Annotation.make`, or a Guid normaliser | nothing compares whole models, so ids never matter |
| model serialization for goldens | same |
| **message serialization (FsPickler), for now** | steps 1–3 use *hand-written and generated* message sequences, not captured ones. Serialization is only needed to capture a live session from the running viewer — defer it until that is actually wanted |
| a GL context in the baseline | sub-app replay needs only a `BlockingCollection` |

So the first three steps need **no new project, no serialization and no GPU**.

---

## 0. What already exists — do not rebuild it

Two things landed on `develop` that overlap heavily with this request.

### `src/Tests/Features/` is already a message-driven app harness

Sixteen section files plus `TestHelpers.fs`, mapped to `docs/Test_Protocol`. It already:

- builds a real `Model` through `Viewer.initial` with a live `MailboxProcessor`
- exposes `update m msg = ViewerApp.updateViewer runtime signature sendQueue mailbox m msg`
  (`TestHelpers.fs:163`) — the **real** update, not a stand-in
- feeds real messages: *"each Ctrl+click is fed to the real DrawingApp.update"*
  (`Section03_DrawingAnnotations.fs:8`)
- creates a real `IRuntime` + `IFramebufferSignature` from `OpenGlApplication`, self-skipping
  when no GL context or test data is available (`Render.context`, `Render.available()`)
- drives time-dependent code with `runAnimationToCompletion`, ticking the animation clock the
  way the viewer's clock thread would

**So "apply messages to the real update" is already solved.** What is missing is *recording*,
*replay*, and *generated* message sequences — a thin layer on top, not a new harness.

### PROVEX provenance already captures state

`--enableProvenance` with `/api/v2/captureSnapshot`, `getProvenanceGraph` and
`activateSnapshot` (`src/Tests/RemoteApi.rest`, `docs/ProvenanceTracking.md`). This records
*scene states*, not message logs, but it is adjacent prior art and its snapshot format is worth
reading before inventing one.

---

## 1. The structural fact that makes replay viable

`Aardvark.UI.App` (`aardvark.media/src/Aardvark.UI/App.fs:14`):

```fsharp
type App<'model, 'mmodel, 'msg> =
    {
        initial   : 'model
        threads   : 'model -> ThreadPool<'msg>
        update    : 'model -> 'msg -> 'model
        view      : 'mmodel -> DomNode<'msg>
        unpersist : Unpersist<'model, 'mmodel>
    }
```

`update` is a plain function `'model -> 'msg -> 'model`. A replayed test is therefore just:

```fsharp
List.fold update seed messages
```

Everything else in this document is about the two things that spoil that simplicity.

**Spoiler 1 — `threads`.** Messages can arrive later, asynchronously. PRo3D uses this for
surface-sampling (`pendingIntersections : ThreadPool<DrawingAction>`). A recording must capture
thread-produced messages too, otherwise replay diverges. Recording at the `update` funnel gets
this for free, since those messages also pass through `update`.

**Spoiler 2 — impure edges.** `ViewerApp.updateViewer` takes `runtime`, `signature`,
`sendQueue`, `mailbox`. It touches GL, IO and a mailbox, so it is not pure. This is exactly the
problem [elm-program-test](https://elm-program-test.netlify.app/cmds.html) solves by making
effects an explicit data type with a simulated interpreter: *"Cmd values cannot be inspected"*,
so you introduce an `Effect` type and a `withSimulatedEffects` interpreter.

We do not need to go that far, because of the tiering below.

---

## 2. Tiers

| Tier | Subject | Needs | Speed |
|---|---|---|---|
| 1. Unit | pure geometry (`PolygonFill`, `SurfaceChart`, `PolyRegion`) | nothing | ms |
| 2. Generated | invariants over random message/edit sequences | nothing | ms |
| 3. 2D lab | real geometry + real update + SVG view | nothing | ms |
| 4. Sub-app replay | `DrawingApp.update` on recorded logs | nothing | ms |
| 5. Viewer replay | `ViewerApp.updateViewer` on recorded logs | GL context | s |
| 6. Image golden | rendered output vs reference | GL + tolerance | s |

Tiers 1–4 need no GPU and belong in CI. Tiers 5–6 self-skip, as the existing suite already does.

**Do tier 2 first.** For boolean operations it is worth more than tiers 4–6 combined, and costs
almost nothing — see §4.

---

## 3. Tier 3: the 2D lab — real code, degenerate chart

Inspired by `aardvark.media/src/Examples (dotnetcore)/07 - Simple2DDrawing` (60-line `Model.fs`,
159-line `App.fs`): polygons as point lists, a working polygon, a cursor, undo/redo via
`past`/`future` fields marked `[<TreatAsValue>]`, and an **SVG** view.

**The key design decision: the 2D lab must not reimplement the geometry.** Its whole value is
exercising the code that ships. That is already possible without any abstraction:

```fsharp
// the XY plane as a chart makes 2D points ordinary 3D points with z = 0,
// and the production pipeline runs unchanged
let chart2d = SurfaceChart.ofPlane (Plane3d(V3d.OOI, V3d.Zero))
```

So the lab is: real `PolygonFill`, real `SurfaceChart`, real `PolyRegion<'a>` boolean ops, with
an SVG view instead of a 3D one. No terrain, no OPC, no GL, no picking — the parts that make
tier 5 slow and flaky are simply absent, while the geometry under test is identical.

Messages: `AddPoint`, `ClosePolygon`, `MoveCursor`, `Select`, `Union`, `Difference`,
`Intersect`, `Xor`, `Undo`, `Redo`.

It doubles as the UX prototype for boolean operations: what does "select two annotations and
union them" *feel* like, and what happens when the result has a hole or splits in two — the
model question section 10 of `annotationPolygonFill.md` leaves open. Answering that in a
219-line SVG app is far cheaper than answering it in the viewer.

---

## 4. Tier 2: invariants and generated sequences

The highest-value tier for boolean ops, and the cheapest.

**Invariants** — properties that must hold after *every* message, checked by folding a sequence
and asserting at each step:

- no annotation's `points` contains consecutive duplicates
- a filled annotation's triangulated area equals `Calculations.calculatePolygonArea`
  (already asserted once in `PolygonFillTests`; make it universal)
- `Undo >> Redo` returns an equal model
- union area ≥ each operand's area; intersection area ≤ each operand's; `A ∪ B` and
  `(A ∪ B) \ B ∪ B` agree within tolerance
- every vertex of a fill lies on its chart surface

**Generation.** [FsCheck](https://fscheck.github.io/FsCheck/StatefulTestingNew.html) has
model-based state-machine testing in `FsCheck.Experimental`: define `Pre` (precondition), `Run`
(apply to the model), `Check` (apply to the real system and compare), and it generates random
command sequences and shrinks failures to a minimal case. That shrinking is the part worth
having — a random 200-message failure reduced to three messages is the difference between a
usable bug report and a shrug.

For boolean operations the model can be deliberately naive (e.g. a rasterised occupancy grid at
some resolution) and the SUT is `PolyRegion`; agreement within tolerance is the property.

---

## 5. Tier 4/5: record and replay

### The recorder is one function

`update` is the single funnel every message passes through, including thread-produced ones:

```fsharp
let recording (sink : 'msg -> unit) (update : 'm -> 'msg -> 'm) : 'm -> 'msg -> 'm =
    fun model msg -> sink msg; update model msg
```

Wrap the app's `update` at composition time. That is the entire recording mechanism.

### Starts

Two seeds, both worth having:

| Seed | What | Trade-off |
|---|---|---|
| `Empty` | `Viewer.initial` / `DrawingModel.initialdrawing` | deterministic, no data dependency, CI-safe, fast. **Default.** |
| `Scene of path` | load a `.pro3d` first | realistic, catches interaction with real surfaces; depends on data → self-skip when absent, as the suite already does for `C:\pro3ddata` |

`Empty` should be the default because a test that cannot run in CI is a test that rots.
`Scene` earns its place for anything involving picking, surfaces or reference systems.

### Ends

Ranked by how well the failure reads:

1. **Targeted assertions** — after replay, assert specific facts (annotation count, geometry,
   area). What `Section03` already does. Best failure messages, most maintenance.
2. **Invariants** (§4) — assert after every message rather than at the end. Best
   value-per-line, and localises failures to the offending message.
3. **Model golden** — serialize the model and diff against a committed reference. Cheapest to
   write, worst to read when it fails, and brittle against unrelated model changes. Useful only
   as a coarse "did anything change" net.

Recommend **1 + 2**, with 3 reserved for a small number of end-to-end scenarios.

### Serialization

`MBrace.FsPickler` is already a dependency (`src/PRo3D.Base/paket.references`, used in
`Annotation-Model.fs`). It pickles a DU with no per-case code, so a recording is
`{ seed; messages }` pickled directly — no Chiron boilerplate for the enormous `ViewerAction`
DU. The cost is format fragility: renaming a message case invalidates old recordings. For test
fixtures that is acceptable — regenerate them. Revisit only if recordings must outlive
refactors.

### Where to replay

Prefer the **sub-app** level. `DrawingApp.update` takes
`referenceSystem, config, _, sendQueue, view, shiftFlag, model, action` — the only awkward
argument is a `BlockingCollection`, trivially supplied. No GL, no mailbox, milliseconds.
Reserve full-viewer replay for cases that genuinely need it.

### Determinism hazards — the things that will actually break replay

- **`Guid.NewGuid()` in `Annotation.make`.** Every replay produces different annotation keys, so
  any golden comparison fails immediately. Either inject an id source or normalise ids before
  comparison. This is the single most likely thing to sink a naive implementation.
- **`DateTime.Now`** in bookmarks / sequenced bookmarks.
- **Async intersections** (`PRo3D.Config.useAsyncIntersections`) — nondeterministic message
  ordering. Force the synchronous path in tests; the existing harness sidesteps it by supplying
  picked points directly.
- **Process-global SPICE kernel state** — already documented in `src/Tests/Program.fs`, and it
  is why the geographic-chart test self-skips in the full suite but passes standalone.

---

## 6. Attaching rendering later

Most of this already exists — do not build it twice.

- `TestHelpers.Render.context` already creates an `OpenGlApplication`, an `IRuntime` and an
  `IFramebufferSignature`, and self-skips when unavailable.
- `PRo3D.Snapshots` already renders scenes headlessly to PNG, and it is the branch of
  `DrawingApp.view` that uses `usePackedAnnotationRendering = false`.

So image-golden testing is: replay a recording → render offscreen → compare to a reference with
a perceptual tolerance. Keep it opt-in and last. Image goldens are the most environment-sensitive
tests there are (driver and GPU differences produce sub-pixel diffs), so they need a tolerance
and a documented reference machine, or they will be disabled within a month.

---

## 7. Proposed layout

Only one new project is needed up front:

| Project | Contains | When |
|---|---|---|
| `src/PRo3D.GeometryLab` | 2D SVG app: minimal model, update, view over the real geometry | step 3 |
| `src/PRo3D.Testing` | recorder wrapper, replay fold, invariant runner | step 4, **only if** capturing live sessions is wanted — the replay fold itself is three lines and can live in `src/Tests` until then |

Invariants and FsCheck properties (steps 1–2) go straight into `src/Tests`, next to
`PolygonFillTests.fs`. FsCheck is a new test-only dependency.

## 8. Order of work

1. **Invariants** over the existing geometry (§4) — no new project, no dependency beyond
   Expecto. Universal versions of the area/duplicate/chart properties already asserted
   one-off in `PolygonFillTests`.
2. **FsCheck model-based properties** for boolean ops — the shrinking is the point: a failing
   200-message sequence reduced to three messages.
3. **`PRo3D.GeometryLab`** — settles the boolean-op UX *and* the holes / disjoint-components
   model question that `annotationPolygonFill.md` §10 leaves open.
4. **Recorder + sub-app replay** — only once there is something worth capturing.
5. Full-viewer replay, then image goldens — only if 1–4 leave a real gap.

Boolean operations get written against 1–3 before any viewer integration exists.

## 9. Open question deliberately left for step 3

Whether a merged region that has holes or splits into disjoint components should be *refused*,
*exploded into several annotations*, or *represented* by extending `Annotation` to multiple
rings (a format change). The 2D lab is the cheapest possible place to answer this, because the
answer is a UX judgement as much as a modelling one.

## Sources

- [avh4/elm-program-test](https://github.com/avh4/elm-program-test) and
  [Testing programs with Cmds](https://elm-program-test.netlify.app/cmds.html) — program-level
  testing philosophy, and the explicit-`Effect` pattern for uninspectable side effects
- [FsCheck model-based testing](https://fscheck.github.io/FsCheck/StatefulTestingNew.html) —
  `Pre`/`Run`/`Check` state machines, generation and shrinking
- [Stack Builders, Testing Elm Applications: the Test Trophy](https://www.stackbuilders.com/insights/testing/)
  — weighting integration over unit for TEA apps
- `aardvark.media/src/Examples (dotnetcore)/07 - Simple2DDrawing` — the 2D drawing template
