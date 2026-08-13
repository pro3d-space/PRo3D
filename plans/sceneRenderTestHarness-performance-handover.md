# Handover — folding performance attribution into the scene render test harness

To the session that wrote [`plans/sceneRenderTestHarness.md`](sceneRenderTestHarness.md).

This session was asked to investigate why the view-space triangle-size filter costs frame
time on Apple Silicon. It used your harness to do it, found something much larger than the
question assumed, and in the process produced two things you need: a **direct answer to your
open question 1**, and evidence that your step 4 (the named stage list) is load-bearing for
more than correctness.

Written from measurements on an Apple M1, OpenGL 4.1 / GLSL 4.1 Core.

---

## 0. Logistics first — your harness code is not where you left it

**The harness no longer exists in `/Users/hs/pro3d/PRo3D`.** `src/OpcViewer/ScreenshotViewer.fs`,
the `Program.fs` CLI, `docs/OpcViewer-Screenshot-Harness.md` and the `Viewer-Utils.fs` stage-list
changes were uncommitted working-tree edits there, and that tree has since moved to
`c6c8a292` without them.

They survive only on branch **`claude/objective-babbage-6786fc`** (worktree
`/Users/hs/pro3d/PRo3D/.claude/worktrees/objective-babbage-6786fc`):

| commit | contents |
|---|---|
| `57425cdc` | your harness exactly as it was in the working tree, committed unmodified as a baseline |
| `95eb806b` | this session's additions: `--benchmark`, `--repeats`, `--drop` |

That branch is based on `5b18190e` and is therefore **behind** your current `c6c8a292`,
which has test infrastructure my base lacks. **Cherry-pick, do not merge:**

```bash
git cherry-pick 57425cdc 95eb806b
```

Recover the harness before anything else in this document is actionable.

---

## 1. What was measured

`--benchmark <frames>` settles the LOD tree the way a screenshot does, then renders batches
of frames from the fixed camera and reports ms/frame, syncing once per batch rather than per
frame (GL queues commands; timing a single `task.Run` measures CPU submission, not GPU work).
Batches are reported individually. Within-configuration spread is 1–3%, so every gap below is
far outside noise.

`--drop <names>` removes named stages from the composition — the same names as the env-var
interface, reached from the CLI instead.

Dataset `victoria`, offscreen, median of 5 × 60 frames.

### The geometry-shader *stage* is ~92% of frame time

Cleanest isolation, where the only difference is `triangleSizeFilter`:

| config | ms/frame | fps |
|---|---|---|
| `--stack minimal` (no geometry shader) | **6.86** | 146 |
| `--stack filter` (+ `triangleSizeFilter`, **disabled by uniform**) | **81.98** | 12 |
| `--stack filter --drop triangleSizeFilter` (sanity: reproduces `minimal`) | 6.70 | 149 |

**+75 ms/frame for a geometry shader that is switched off.** Resolution-independent
(+71.8 ms at 320×200, +67.5 ms at 1920×1200), so it is geometry-stage cost, not fragment work.

On the full `color` stack:

| config | ms/frame |
|---|---|
| default — *what the viewer does today* | 64.12 |
| `--triangle-filter 5` (filter actually **enabled**) | 61.40 |
| `--drop triangleSizeFilter` (`generateNormal` remains) | 61.90 |
| `--drop triangleSizeFilter,generateNormal` (**no GS at all**) | **4.61** |

`--dump-glsl` shows the mechanism: the `color` stack emits **one** `#ifdef Geometry` block.
FShade **merges** `triangleSizeFilter` and `generateNormal` into a single stage, so dropping
either alone leaves the stage — and its cost — in place. Dropping both is **13.9×**.

Two results worth carrying forward as warnings:

- Enabling the filter is *faster* than leaving it composed-but-disabled (61.40 vs 64.12), because
  it emits fewer triangles. The filtering work is free; the stage is the entire cost.
- `--drop generateNormal` alone measured **146.21 ms** — 2.3× *slower* than composing both.
  Removing a shader made it slower. Unexplained; likely varying count in the merged-vs-single
  GS. Flagging it because it means **stage cost is not additive**, which constrains how a perf
  tier may attribute time (see §3).

---

## 2. Direct answer to your open question 1

> *Do the config-neutrality pairs actually hold today? If some flags already perturb pixels
> while nominally inactive, that is either a second bug or a modelling error in the invariant.
> Expect to find at least one.*

**They do not, and here is the first one.** `minimal` vs `filter` — identical geometry,
`FilterTriangleEnabled = false`, a pure pass-through — are **not pixel-identical**:

- 0.4% of bytes differ
- 82% of those differ by exactly ±1 (mean 1.48, max 54)

No missing triangles; this is interpolation noise from the geometry shader **re-emitting
vertices**. A GS that passes a triangle through unchanged does not reproduce the rasteriser's
input bit-for-bit.

This matters directly to your design, because your neutrality tier is specified as
*"Assert pixel-identical"* and you explicitly rejected tolerances on the grounds that the
`crossSectionClip` artefact was itself only a few percent of pixels:

> *a threshold loose enough to avoid false positives is loose enough to miss the bug we are
> building this for*

That reasoning still holds, and this finding does not overturn it — but it does mean the
neutrality tier **cannot be uniformly exact**. `triangleSizeFilter` off-vs-dropped will fail an
exact comparison forever, for a reason that is not a bug. Options, none free:

1. **Exact by default, per-pair declared exceptions.** Keep pixel-identical as the rule; this
   pair carries an explicit, justified exception. Preserves the tier's strength everywhere else;
   an exception list is a place bugs can hide.
2. **Magnitude-aware comparison.** Fail on any pixel differing by more than ±1, rather than on
   any pixel differing at all. Would still catch `crossSectionClip` (discarded fragments are a
   whole clear-colour delta, not ±1) and absorbs re-emission noise. Weakens the assertion
   uniformly, including where it did not need weakening.
3. **Neutrality applies to uniform toggles only, never to composition changes.** Arguably the
   honest framing: a stage that is composed is in the pipeline, and "inactive" is a claim about
   its *output*, not its *presence*. This is also exactly the distinction that made the
   performance bug invisible.

I did not pick one — it is a design decision on your plan, not mine. **Option 3 is the one I would
argue for**, because it is the framing that makes the performance finding expressible: the whole
point is that "disabled" and "not composed" are different things.

---

## 3. What consolidation actually buys

Your plan and this investigation converge on the same object: **`surfaceEffect` as an
inspectable, filterable list of named stages** (your step 4). You justified it as the
prerequisite for automatic bisect on correctness failure. This session used exactly that
machinery for something you did not plan for, and it worked unmodified.

That is the consolidation argument, and it is stronger than "two tools share a helper":

- **Same primitive, two questions.** Correctness bisect asks *at which stage do the pixels go
  wrong*; performance attribution asks *at which stage does the time go*. Both are "compose a
  prefix / drop a stage, render, measure" — the measurement differs, the machinery does not.
- **It promotes step 4 from leverage to foundation.** Your staging puts it at 4 of 8, after the
  value is already delivered, on the grounds that 1–3 are "the whole value". A second
  independent consumer changes that calculus — though see the caveat in §5.
- **It retires the env-var interface with a real replacement.** You wanted
  `PRO3D_SURFACE_EFFECT_DROP` gone. `--drop` in `95eb806b` is what the CLI shape looks like,
  including rejecting unknown stage names rather than ignoring them — a typo that silently
  measures the unmodified effect reports "no difference", which is the worst available failure
  mode for either tool.

### A performance tier, if you want one

Cheap given the above, and it fits your existing tier structure:

- **Budget tier** — assert the full `color` stack renders under a per-platform ms/frame budget.
  Absolute, like your other invariants; no reference platform needed. Catches "someone composed
  a geometry shader into the OPC path" — which is the bug this session actually found.
- **Attribution mode** — on failure, drop stages one at a time and report cost. This is the perf
  analogue of your bisect, and it is **where the `generateNormal` anomaly bites**: cost is not
  additive, and merged stages do not decompose. Attribution must report *"dropping X saves Y"*,
  never *"X costs Y"*. Those are different claims and only the first is measurable here.

Timing on a shared machine is noisier than pixel assertions. Your "dominant risk: a suite nobody
runs" applies with extra force — a flaky perf gate is deleted faster than a flaky correctness
gate. If it goes in, min-of-N and a generous budget, not a tight one.

---

## 4. Corrections to specifics in your plan

- Your §"The motivating bug" lists `triangleSizeFilter` and `generateNormal` among the stages that
  "were all clean". True for correctness, and it is how the perf problem stayed invisible —
  worth a footnote so the next reader does not take "clean" as "cheap".
- Your fixture spec asks for *"a second fixture containing invalid/NaN vertices, which is what
  the triangle filter exists for and is otherwise untested"*. Still right. Note the filter is
  **off by default** (`SurfaceApp.mk`), so such a fixture tests a non-default path — and the
  measurements above say the default path pays for the filter anyway.
- Your CI-readiness rule forbids absolute paths. Every measurement here used
  `--data-root /Users/hs/Downloads/VictoriaCrater`, machine-specific and not reproducible by
  you. Nothing above is re-measurable until the fixture question (your open question 3) lands.
  **Treat these numbers as one machine's, unreplicated.**

---

## 5. What I did not establish

Stated plainly, because the numbers above are strong enough to be over-read:

- **The viewer itself was never benchmarked.** All measurements are the OpcViewer harness. The
  inference that the viewer pays the same cost is from code reading — `ViewerUtils.surfaceEffect`
  composes both `triangleSizeFilter` and `generateNormal` unconditionally — not from measurement.
  It is an inference. Verify before anyone acts on it.
- **One machine, one dataset, one camera.** No Windows or Linux comparison, so "geometry shaders
  are slow on Apple's GL-over-Metal" is consistent with the data but not demonstrated by it.
- **The originally reported symptom did not reproduce.** The human reported
  `--triangle-filter 5` as noticeably slower in `--interactive`; offscreen it is *faster*
  (61.40 vs 64.12). The reported comparison is not the real effect — the cost was in both of
  their runs. `--interactive` adds async loading and a moving camera and was not measured.
- **No fix was attempted.** The user was asked to choose a direction (vertex-stage degenerate
  emission, CPU-side filtering at patch build, or conditional composition) per
  `ai/CONVENTIONS.md`'s rule that shader-vs-CPU trade-offs are discussed, not picked silently.
  They deferred. **The question is still open and is not yours to close** unless it is handed to
  you — but note it is now a different question than when it was asked: conditional composition
  of `triangleSizeFilter` alone is worth ~3%, not the 13.9×.

---

## 6. Suggested amendments to your staging

Minimal, in your existing numbering:

- **Step 3** — when validating invariants against a reverted #666, also record the frame time.
  Free at that point, and it establishes the budget-tier baseline before anyone needs it.
- **Step 4** — reweight. Two consumers, and it is the only thing that makes either bisect
  possible. Still its own PR and its own review, as you said.
- **New step 4b** — budget tier + attribution mode, directly after the stage list.
- **Step 5** — resolve the neutrality-exactness question in §2 *before* building the tier, or the
  first flag you test decides it for you by accident.

Your judgement that "steps 1–3 are the whole value" should probably survive this. A perf tier is
worth less than a correctness gate that runs, and §3's argument for promoting step 4 is an
argument about foundations, not about shipping order. If the work stops early it should still
stop after 3.
