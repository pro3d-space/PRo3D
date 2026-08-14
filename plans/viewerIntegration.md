# Boolean operations in the viewer — union first, then cut

Integration plan for `RegionOps` cut/merge into PRo3D proper. Geometry design and lab:
`plans/booleanOperations.md`; testing rationale: `plans/testingStrategy.md`.

Staged deliberately: **stage 1 is union** (no new interaction machinery needed), **stage 2 is
cut** (needs a drawing-like mode). Each stage is its own PR, and each lands with its tests
before any UI exists — the same discipline that caught the `cutsThrough` and vertex-graze
defects before a user ever saw them.

## 0. Readiness audit — what exists, what is missing

Verified against the code, not the older plans:

| Needed | State | Anchor |
|---|---|---|
| 2D chart abstraction | **exists** | `SurfaceChart.fs:17-23`: `toChart : V3d -> V2d option`, `toWorld`, with `ofPlane` (`:37`), `tryOfPlane` (`:52`), `ofUpVector` (`:60`), `geographic` (`:94`) |
| Attributed regions + boolean ops | **exists** | vendored `PolyRegion<V3d>`, `RegionOps` cut (polyline strokes) / merge, 293 tests |
| Plane fitting for a common chart | **exists** | `PlaneFitting.Fit` (`CSharpUtils/PlaneFitting.cs:11`), used by `AnnotationHelpers.calculateVertexPlane:104` |
| Multi-selection of annotations | **exists** | `GroupsModel.selectedLeaves : HashSet<TreeSelection>` (`Groups-Model.fs:222`), filled by shift+click while ctrl-picking (`Drawing-App.fs:632-637`) and by the tree UI (`Drawing.UI.fs:290-296`) |
| "Exactly N selected" action precedent | **exists** | `GeologicSurfaceApp.fs:185-214` requires exactly 2 selected annotations |
| Atomic undo for a multi-leaf change | **exists** | `AnnotationsDelta.SnapshotDelta of before * after : GroupsModel` (`Drawing-Model.fs:27-30`), one message = one undo step (`Drawing-App.fs:562-573`) |
| Terrain-picked polyline drawing | **exists** | the working-annotation pipeline: `DrawingModel.working` (`Drawing-Model.fs:103`), `addPoint` (`Drawing-App.fs:141`), Enter=`Finish`, Backspace=`RemoveLastPoint`, Esc=`ClearWorking` (`Viewer.fs:428-430`) |
| Add/remove annotation leaves | **exists** | `GroupsApp.addLeafToActiveGroup:159`, `removeLeaf:211` |
| Region from chart-projected ring | **missing** | `RegionOps.ofRing` assumes z=0 ≙ world; a `SurfaceChart`-aware constructor is new (§1) |
| Common chart for several annotations | **missing** | chart choice rules exist per-annotation only (§1) |
| Union/cut at the `Annotation` level | **missing** | the pure core of both stages (§1) |
| Refusal/feedback UX | **missing** | §2/§3 |

So the geometry and the model machinery are prepared; what is genuinely new is one pure module,
two messages, and (for cut only) one interaction mode.

## 1. The pure core: `AnnotationRegionOps` — build and test this first

New file `src/PRo3D.Base/Geometry/AnnotationRegionOps.fs`, compiled after `Annotation-Model.fs`
(it needs `Annotation`; `GroupsModel` context stays out — take and return plain values):

```fsharp
module AnnotationRegionOps =

    type Refusal =
        | TooFewAnnotations
        | ChartProjectionFailed of annotationName : string
        | ResultHasHoles of holeCount : int
        | DegenerateInput of reason : string
        | StrokeDoesNotCut

    /// One chart every operand projects through (decided): a plane fitted over the
    /// concatenated points of all operands. No diverging-planes warning machinery - an operand
    /// the chart cannot cover surfaces as ChartProjectionFailed and the operation is refused
    /// with that message. SurfaceChart.tryOfPlane already rejects degenerate fits.
    val commonChart : seq<Annotation> -> Option<SurfaceChart>

    /// Chart-project a ring, keeping each vertex's world position as the region attribute.
    /// Any point the chart cannot project => None (the chart does not cover the annotation).
    val toRegion : SurfaceChart -> Annotation -> Option<Region>

    /// World-space rings of the result, one per output annotation. Union explodes into one
    /// ring per component; holes are refused (the decided policy). Vertices that survive from
    /// the inputs are *exactly* the input world points (the attribute channel); invented
    /// vertices (edge crossings) are re-projected onto the terrain through projectToSurface -
    /// the same raycast infrastructure ellipse construction uses. A vertex whose projection
    /// fails falls back to its chord blend rather than failing the operation.
    val union : projectToSurface : (V3d -> Option<V3d>) -> list<Annotation> -> Result<list<V3d[]>, Refusal>

    /// Stroke points are world positions picked on the terrain, projected through the same
    /// chart as the annotation. A stroke that does not cut is a Refusal, not a silent no-op,
    /// so the UI can say why nothing happened.
    val cut : projectToSurface : (V3d -> Option<V3d>) -> Annotation -> stroke : V3d[] -> Result<list<V3d[]>, Refusal>
```

Decisions folded in:

- **All geometry in 2D chart space, all identity in world space.** The chart is topology-only,
  exactly like the fill (`PolygonFill` §5 of `annotationPolygonFill.md`): output ring vertices
  come back as world points through the attribute channel, so a merged outline coincides with
  the drawn outlines wherever they survive. This is the reason the attributed `PolyRegion` was
  vendored — the union is where it pays off.
- **Invented vertices are re-projected onto the terrain (decided).** Where two rings cross, the
  blended vertex lies on a chord; the raycast infrastructure to fix that exists
  (`projectToSurface` as used by `constructAndSampleFromPlane`, `EllipseAnnotation.fs:62`), so
  v1 uses it rather than dodging it. The pure module takes the projection function as a
  parameter — tests stub it with identity or an analytic surface. Only vertices that do not
  match an input attribute are projected (survivors are already terrain points); a failed
  projection falls back to the blend for that vertex alone.
- **Chart choice: one plane fitted over all operands' points (decided).** "First wins" applies
  to metadata only, not the chart. No diverging-planes warning machinery: an operand the chart
  cannot cover fails with `ChartProjectionFailed` and the operation is refused with that
  message. Geographic chart stays opt-in and out of stage 1.
- **Holes refused, components exploded** — as decided in `booleanOperations.md`. The lab
  measures how often refusal actually bites; if it turns out common, the escape hatch is the
  model extension (rings list), a scene-version bump deliberately not taken now.

### Checkpoint 1 (pure, CI, no GL) — before any viewer change

- `toRegion` round-trip: plane-chart annotation → region → `outerRings` returns exactly the
  input world points (attribute fidelity, the property that pins the whole design).
- `commonChart` fallbacks: all-NaN `dnsResults`, collinear points, < 3 points.
- `union`: two overlapping synthetic rings → one ring, area = inclusion–exclusion (against
  `RegionOps.area`); disjoint → two rings; nested/U+bar → `ResultHasHoles`.
- `cut`: piece rings replayed through **the same `RegionInvariants`** the lab fixtures use —
  areas sum, containment, re-cut no-op. No new invariant definitions; one source of truth.
- FsCheck: random simple rings on a plane chart (generators already exist in
  `RegionOpsTests.fs`), lifted to annotations; union/cut invariants over 100 cases.

### Checkpoint 1 result — done, and it caught a real defect

`AnnotationRegionOps` + `AnnotationRegionOpsTests` are implemented and green (306 tests).
The tilted-chart tests immediately caught something the lab could never see, because its chart
is the identity: **after a cut, the attribute channel is contaminated.** Vertices on the stroke
blend the region's world points with the synthetic side polygon, whose "world" attributes are
chart coordinates — and such a garbage blend can even land within tolerance of a *real* input
point by accident, so attributes cannot even identify surviving vertices. `cut` therefore
matches survivors by their (exact) chart position and lifts every other vertex through
`chart.toWorld` + `projectToSurface`; `RegionOps.outerContours` exposes contours with chart
positions alongside attributes for exactly this. Union is unaffected — both operands carry
real world attributes, so its blends are legitimate chords.

## 2. Stage 1: union

### UX — two candidates, sequenced not competing

**(B) Operate on the existing selection — build first.** Annotations are already
multi-selectable (shift+click during ctrl-pick, or in the annotations tree). Add one action,
"Union selected annotations", in the annotations panel next to the existing bulk actions,
enabled when ≥ 2 annotations are selected — the exact pattern `GeologicSurfaceApp` uses for
"make surface from 2 selected". No new interaction mode, no gating changes, works from the tree
alone (no 3D picking needed at all).

**(A) A dedicated "union annotations" click-through mode — add second, as sugar.** The
requested flow: enter the mode, click annotations one after another, each click toggles
membership in the union set (highlighted), Enter applies, Esc cancels. Genuinely nicer for
picking many annotations in the viewport. Costs, stated honestly:

- a new `Interactions` case (the enum at `Model.fs:5-26` already has 21 cases) plus its
  `hideSet` entry, dropdown text, and gating: `allowAnnotationPicking` (`Viewer.fs:2248-2256`)
  must include the new mode or clicks select nothing;
- the ctrl-held-to-pick convention (`StartPicking`, `Viewer.fs:431-435`) either applies — one
  more thing to hold — or the mode picks without ctrl, diverging from `PickAnnotation`;
- highlight state for "in the union set" — reusable from `selectedLeaves` if the mode simply
  drives the *existing* selection, which is the trick that keeps (A) cheap: **the mode is then
  only an input affordance over (B)'s selection set**, and Apply is the same message.

Recommendation: ship (B) in the union PR; add (A) in a follow-up sized commit once (B)'s
semantics are proven, implementing it as selection-driving sugar so there is exactly one code
path. If (A) is wanted immediately, it still sits on top of (B)'s message — the order does not
change the architecture, only the PR size.

### Message flow

```
DrawingAction.UnionSelectedAnnotations
  -> leaves = selectedLeaves resolved via flat        (Groups-Model.fs:274-291)
  -> AnnotationRegionOps.union annotations
  -> Error r  -> status/log message, model unchanged
  -> Ok rings -> before = model.annotations
                 remove originals (GroupsApp.removeLeaf), add one leaf per ring
                   (Annotation.make + metadata policy below, GroupsApp.addLeafToActiveGroup)
                 push SnapshotDelta(before, after)     -- one undo step, already supported
```

- **Metadata policy**: the result copies colour, thickness, projection, semantic and group
  placement from the *first-selected* operand (`lastSelectedItem` order is not stable across
  the set — use the tree order of `selectedLeaves` and document it). `dnsResults` and other
  derived values are recomputed by routing the new leaves through the existing
  `RecalculateMeasurements` path (`Drawing-App.fs:589-601`).
- **Key dangling**: deleting originals leaves their ids in — audit result:
  sequenced-bookmark scene states (`SequencedBookmarks-Model.fs:203`, full `GroupsModel`
  snapshots — restoring an old bookmark resurrects the pre-union annotations, which is
  *snapshot semantics, not a bug*, but document it), provenance snapshots
  (`ProvenanceModel.fs:50`, same semantics), comparison measurements
  (`Comparison-Model.fs:36` — verified harmless: measurements are rebuilt from the *current*
  annotations via `tryHead`/`Option.bind`, the stored `annotationKey` is write-only output),
  correlation-panel `ContactId`s (latent — the wiring is commented out at
  `Viewer.fs:611-632`), and `Annotation.bookmarkId` points the *other* way (safe). None block
  stage 1, and none needs code changes.

### Checkpoint 2 (sub-app messages, CI, no GL)

The existing `src/Tests/Features` harness drives the real `ViewerApp.updateViewer`
(`TestHelpers.fs:163`) and already creates annotations by feeding picked points directly
(`Section03_DrawingAnnotations.fs`). Add `Section20_BooleanOperations.fs`:

- draw two overlapping polygons via `AddPointAdv` + `Finish` (points supplied directly, no GL)
- select both (`SingleSelectLeaf` + `AddLeafToSelection`)
- send `UnionSelectedAnnotations` → assert: one annotation, expected area (vs
  `calculatePolygonArea`), originals gone, group placement, metadata copied
- `Undo` → both originals back, selection sane; `Redo` → union again
- refusal case (U + bar shapes) → model unchanged, no undo step pushed
- comparison module holding a deleted key → no crash

### Checkpoint 2 result — done

`DrawingAction.UnionSelectedAnnotations of Option<V3d -> Option<V3d>>`: the panel button (next
to the recalculate icon, `Drawing.UI.fs`) sends `None`; the viewer's `DrawingMessage` handler
enriches it with the sky-direction kd-tree raycast and re-dispatches — the same enrichment
pattern as `AddPointAdv`. The handler resolves the selection in depth-first tree order (the
selection set is unordered, so this is what makes "first wins" deterministic), calls
`AnnotationRegionOps.union`, replaces the operands atomically and pushes one
`SnapshotDelta(before, after)`. Refusals log and change nothing — no empty undo step.
`Section20_BooleanOperations.fs` covers union area, component explosion, one-step undo/redo,
hole refusal and the <2-selected case (311 tests green). User docs:
`docs/AnnotationBooleanOps.md`.

### Checkpoint 3 findings — two defects from the first real-scene union (unionFail.pro3d)

The first union on a real Jezero scene rendered with missing edges. Two independent causes,
both now fixed and regression-tested with the scene's exact rings:

1. **Result rings were stored open.** Drawn polygons store their ring *closed* (`closePolyline`
   appends the first point) and a segment-less annotation renders as an open polyline between
   consecutive points — so the union result lost its closing edge on screen. Result rings are
   now closed at annotation construction.
2. **A self-intersection sliver became its own annotation.** One input ring folded over itself
   in projection (a hand-drawn spike); EvenOdd resolves the fold into a 0.7 m² extra contour,
   which the union faithfully exploded into a second, absurd annotation. Since every legitimate
   union component contains at least one whole operand, components smaller than the smallest
   operand are artifacts by definition — `union` now drops them, and fills micro-holes from the
   same folds (< smallest operand × 1e-3), keeping the area error orders below the invariant
   tolerances. Substantial holes still refuse.

### Checkpoint 3 (manual, viewer) — passed 2026-08-14

Union of two overlapping polygons on the Jezero scene (`unionFail.pro3d`), after the two fixes
above: one closed outline replacing both originals, confirmed working by the user. Still worth a
look when convenient: fill rendering on a union result, save → reload round-trip, and a
sequenced bookmark from before a union restoring its own state.

## 3. Stage 2: cut

### UX — select, then draw the stroke

Requested flow, assessed: select one annotation → enter a "cut annotation" mode → click a
polyline stroke on the terrain (exactly like drawing an annotation) → Enter applies, Esc
cancels. **Verdict: possible without new machinery, and it does not escalate** — provided the
stroke reuses the working-annotation pipeline rather than growing its own:

- stroke points come from the same `addPoint` sampling (`Drawing-App.fs:141`) — terrain
  picking, projection handling and multi-surface sampling come for free;
- the stroke renders through `Sg.drawWorkingAnnotation` (`Drawing.Sg.fs:282-313`) with a
  distinct colour — no new scene-graph code;
- Enter/Backspace/Esc already route to `Finish`/`RemoveLastPoint`/`ClearWorking`
  (`Viewer.fs:428-430`) — the cut mode reinterprets `Finish` as "apply the cut".

Drawbacks, thought through:

1. **Mode-matrix growth.** One more `Interactions` case (`CutAnnotation`), its gating in
   `matchPickingInteraction` (`Viewer.fs:279`), and mutual exclusion with picking. Bounded:
   the mode is a thin router; all geometry stays in `AnnotationRegionOps`. The enum is UI
   state on `Model`, not persisted scene state — no version bump.
2. **The precondition is invisible.** A stroke cuts only if both *ends* project outside the
   ring in chart space — the user cannot see the chart. Mitigation: live feedback — after each
   added stroke point run `AnnotationRegionOps.cut` in dry-run (it is pure and cheap at these
   sizes) and colour the stroke green/red for "would cut / would not". This turns the
   invisible rule into an affordance and costs one `aval`.
3. **Projection surprises on rugged terrain.** A stroke sensible in 3D can self-intersect
   once flattened; behaviour is then winding-resolution (pinned by tests, not undefined), but
   the result may surprise. The green/red feedback covers the "does not cut" half; a status
   line ("stroke crosses itself in projection") covers the rest.
4. **Which annotation is being cut** must be unambiguous: entering the mode requires exactly
   one selected annotation (`tryGetSelectedAnnotation`, `Groups-Model.fs:312`); selection is
   frozen while the mode is active (picking is off in draw-like modes anyway, the same
   mutual exclusivity noted in `annotationPolygonFill.md` §8).
5. **Mid-cut state on mode switch / scene load.** The stroke lives in `DrawingModel` next to
   `working` (`cutStroke : Option<...>`); leaving the mode clears it, and it is *not*
   persisted — same policy as `working`.

Alternative considered and rejected for v1: cutting *all* shapes the stroke crosses (the lab's
behaviour, no selection needed). Cheaper to trigger but dangerous in a scene with many
annotations — an unlucky stroke cuts things off-screen. The lab keeps that behaviour for
experimentation; the viewer cuts the selected annotation only.

### Message flow

```
enter CutAnnotation (requires exactly 1 selected, else status message)
AddPointAdv (routed to cutStroke, not working)
Finish -> AnnotationRegionOps.cut annotation strokeWorldPoints
       -> Error  -> status message, stroke kept for correction
       -> Ok rings -> SnapshotDelta undo, remove original, add piece per ring
                      (metadata copied to every piece, per the decided design)
```

### Checkpoint 4 result — done

`Interactions.CutAnnotation` (enum case 21) routes viewport picks to `AddCutStrokePoint`;
Enter/Backspace/Escape reroute per-mode in `getDrawingActionForKey` to
`ApplyCutStroke`/`RemoveLastCutPoint`/`ClearCutStroke`. The stroke lives in
`DrawingModel.cutStroke : Option<Annotation>` so `Sg.drawWorkingAnnotation` renders it
unchanged, and its colour flips green/red with a dry-run `AnnotationRegionOps.cut` after every
point — the invisible ends-outside precondition made visible. Apply removes the target via the
new path-independent `GroupsApp.removeLeafById` (viewport picks store the root path in
`TreeSelection` regardless of the leaf's group, so path-based removal is a trap), adds one
closed polygon per piece and pushes one `SnapshotDelta`. Raycast enrichment shares the union's
viewer branch. Section 20 covers: cut with areas summing and closed piece rings, one-step undo,
dry-run colours, refusal keeping the stroke, and no-selection refusal (316 tests green).

### Checkpoints 4 + 5

Mirror checkpoints 2 + 3: `Section20` grows message tests (draw polygon → select → cut-mode
messages → stroke points → `Finish` → two annotations with areas summing to the original;
undo restores; non-cutting stroke → refusal, model unchanged, stroke retained). Manual: cut a
polygon along a terrain feature with a multi-point stroke — the polyline cut exists precisely
so this follows the feature rather than chording it.

## 4. Order of work

1. `AnnotationRegionOps` + checkpoint 1 tests (pure; the union PR's first commits)
2. `UnionSelectedAnnotations` message + panel button (UX B) + checkpoint 2 tests
3. manual checkpoint 3 on a real scene (comparison hardening turned out unnecessary — verified)
4. *(optional, follow-up)* click-through union mode (UX A) as selection-driving sugar
5. `CutAnnotation` mode + `cutStroke` + dry-run feedback + checkpoint 4 tests
6. manual checkpoint 5; `docs/AnnotationBooleanOps.md` for both stages

## 5. Decisions — all three closed (2026-08-14)

- (i) **union metadata: first-selected wins.** Metadata means everything beside the geometry:
  appearance (`color`, `thickness`, fill fields, `textSize`), classification (`semantic`,
  `text`, `projection`), and group placement. The result copies all of them from the
  first-selected operand; `key` is always new, `dnsResults`/measurements always recomputed.
- (ii) **chart: one plane fitted over all operands' points** — "first wins" was metadata only.
  No warn/refuse threshold; an operand the chart cannot cover is a `ChartProjectionFailed`
  refusal (§1).
- (iii) **invented vertices are terrain-projected in v1** via the existing `projectToSurface`
  raycast (§1); a failed projection falls back to the chord blend per vertex.
