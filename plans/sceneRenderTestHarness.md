# Scene Render Test Harness — plan

## Goal

Catch regressions in the OPC surface pipeline that no other test sees — both the class
where **the geometry is fine and the shading is wrong on one GPU only**, and the class
where **nothing looks wrong but a stage is quietly expensive**.

CI has no GPU today, so this starts as a **deliberately-invoked gate run on team machines**.
GPU runners are expected later, so the harness must be **CI-ready from day one**: turning
it on should be a one-line change, not a redesign (see Gate 1 and "CI-readiness").

Two success criteria, both phrased as bugs that already happened:

1. **Correctness.** The `crossSectionClip` bug (Apple Silicon, `releases/6.0.0`, fixed in
   #666) would have been caught by one command on an Apple Silicon machine before release
   — and that command would print **the name of the offending shader stage**, not show a
   picture for someone to judge.
2. **Performance.** The geometry-shader cost found in the follow-up investigation would
   have been surfaced the same way. A stage that is *switched off* and still dominates
   frame time is exactly what nobody notices until someone goes looking — and it stayed
   invisible precisely because every correctness check passed.

The "name the stage" half matters as much as the detection half in both cases. A harness
that reports "these pixels differ" or "this got slower" saves little; the expensive part is
attribution across ~22 shader stages.

**Both criteria reduce to the same primitive, and that is the central design fact of this
plan**: compose a prefix of `surfaceEffect` / drop a named stage, render, then either
assert an invariant or take a measurement. One mechanism, two questions — arrived at
independently twice, once hunting a correctness bug and once attributing frame time.

## The motivating bug, stated precisely

Every design decision below follows from one of its properties.

`crossSectionClip` discarded fragments on `InsideOutsideV4 < 0`. With no cross-section
defined, that attribute is bound as a constant (`SingleValueBuffer`) whose value does not
arrive on Apple Silicon; roughly half the surface read negative and was discarded,
producing a lattice of holes.

1. **Invisible to a simple OPC render.** Geometry, LOD, textures, `stableTrafo`,
   `triangleSizeFilter` and `generateNormal` were all clean. It only appeared under the
   full viewer `surfaceEffect`. *"Clean" here means correct, not cheap* — the two
   geometry-shader stages in that list later turned out to dominate frame time. A stage
   can pass every invariant in this document and still be the performance problem.
2. **Triggered by configuration, not content.** `clippingEnabled` defaults to `true`; the
   bug needed that flag live *and* no cross-section present. No camera or dataset would
   have found it — a config combination did.
3. **Platform-specific.** Windows and Linux were correct.
4. **Not reproducible in isolation.** Four attempts at a standalone repro (many draw calls,
   pass-through geometry shader, 24k verts/draw, sibling attributes) all passed. It needs
   the real OPC LOD path.
5. **Silent.** No exception, no GL error, no log line. Only pixels.

## Design constraints these impose

| From | Constraint |
|---|---|
| (1) | Must render through the **real** `ViewerUtils.surfaceEffect` and `Surface.Sg` scene graph. A harness that reimplements the pipeline tests the reimplementation. |
| (2) | The unit of test is a **(scene × surface configuration)** pair, and configurations must be swept, not hand-picked. |
| (3) | Assertions must be **absolute**, not relative to a reference platform — there is no CI matrix to compare against. |
| (4) | Must drive the actual OPC path with real patch data, not synthetic geometry. |
| (5) | Assertions must be on **pixels**, and must not require a human to look. |

A sixth, from the session rather than the bug: **shared code, not copied code.** The
throwaway harness only stayed honest because `stableTrafo` and `triangleSizeFilter` were
moved to `PRo3D.Base` and composed from there. Any shader the harness renders must be the
same object the viewer renders.

## Core idea: invariants, not golden images

### Rejected as the gate: golden images

A reference PNG per (scene, config), compared with a tolerance.

Rejected twice over. First, the tolerance has to absorb genuine cross-driver differences —
rasterisation rules, texture filtering, FP rounding — easily a few percent of pixels on
this codebase. The `crossSectionClip` artefact was *itself* a few percent of pixels: a
threshold loose enough to avoid false positives is loose enough to miss the bug we are
building this for. Second, with no CI there is nowhere to bless or store per-platform
reference sets, and no automation to regenerate them.

Keep renders as **local failure artifacts** written next to the test output, so a human
can look after a failure. Never gate on them.

### The gate: invariants

Properties that must hold on *every* driver, which the artefact violates structurally.

For the motivating bug the invariant is one line: **with no cross-section defined, no
surface fragment may be discarded.** Render against a distinctive clear colour, count
clear-coloured pixels inside the terrain's projected silhouette, fail on any hole.
Driver-independent, no reference image, fails specifically.

In rough order of value:

- **Config neutrality** — toggling a feature that is *inactive* must not change a single
  pixel. Enabling cross-section clipping with no cross-section defined must be a no-op.
  This is the general form of the bug: it would have caught it without anyone having
  thought about cross-sections, and it generalises to every flag in `surfaceEffect`.
- **Config efficacy** — the dual, and just as necessary: turning a feature on in a state
  where it *should* act must change pixels. A feature that silently stops working is
  invisible to every other invariant here, and the #666 fix created exactly that exposure:
  the `CrossSectionDefined` guard is one boolean away from disabling cross-section
  clipping outright, and nothing in the neutrality tier would notice. Neutrality without
  efficacy tests only that features are harmless when off, never that they work when on.
- **Coverage** — fragments that should be drawn are drawn. Catches discard/clip bugs,
  over-aggressive triangle filtering, LOD holes. Note this is *expected* coverage, not
  "no holes": with an active cross-section, clipping is supposed to remove fragments. The
  invariant is that coverage matches what the configuration calls for, which is why the
  fixture must carry a cross-section whose clipped region is known.
- **Determinism** — same scene, same config, two runs, byte-identical. Catches
  uninitialised buffers and attribute-state leakage, which is the root-cause *class* here.
- **Opacity** — final alpha is 1 where the surface is drawn.

All four are absolute, which is what constraint (3) requires.

## Gates — both now resolved

### Gate 1 — CI. **RESOLVED: skipped on CI for now; GPU runners expected later.**

Rendering tests are gated out of CI until GPU runners are available. Until then the gate
is run by the team on their own machines, which is also how cross-platform coverage is
obtained (see "Cross-platform coverage" below).

Follow the existing `--skip-hera` idiom exactly (`src/Tests/HeraSpiceTests.fs`,
`runTests.sh`): a test skips when its resource is absent **or** when the skip flag is
passed, so CI skips *deterministically* rather than by accident of environment.

- Rendering tests skip if `--skip-render` is passed **or** if a GL context cannot be
  created.
- `runTests.sh` / `runTests.cmd` add `--skip-render` to their existing `--skip-hera`, so
  the CI matrix keeps passing untouched.
- A new `runRenderTests.sh` / `.cmd` opts in.

The GL-context probe is not redundant with the flag — it is what stops the suite from
erroring out on a machine without a usable context, and it is what will let CI adopt these
tests by simply dropping `--skip-render`.

### CI-readiness

Because CI adoption is expected, avoid anything that would have to be undone later:

- **No window server, no screenshots, no interaction.** Offscreen rendering only. (System
  screenshots proved the fix this session and remain useful for local debugging, but must
  never be what a test asserts on.)
- **No absolute paths, no machine-specific configuration.** Fixtures resolve relative to
  the submodule.
- **Budget runtime for CI from the start**, not just for human patience — the neutrality
  tier should stay well under a minute so a four-platform matrix stays affordable.
- **Machine-readable results.** Expecto already gives this; don't add output that only a
  human can interpret.
- **Skipping is a flag, not a `#if`.** Conditional compilation would make the eventual
  switch a code change instead of a script change.

### Gate 2 — test data. **RESOLVED: `PRo3D.Resources.TestData`.**

Test OPCs live in https://github.com/pro3d-space/PRo3D.Resources.TestData, consumed as a
git submodule mounted at `src/Tests/data/opc` (see `.gitmodules` and
[docs/tests/TestData.md](../docs/tests/TestData.md)).

The first fixture was published under `PRo3D-Testdata/` in `PRo3D.Resources.Models` and
moved to its own repository: a submodule always mounts a repository *root*, so reusing
`src/ModelViewer/resources` would have made anyone who only wants to run the tests fetch
the ModelViewer's spacecraft and terrain models too (869 MB of working tree for a 167 MB
fixture). Test data and ModelViewer data are now fetched independently.

Consequences to design for:

- The submodule is **not** initialised by default. Rendering tests must skip cleanly when
  the OPC fixture is absent, same idiom as above — never fail with a path error.
- No git-lfs: the largest file is ~5.5 MB, well inside plain-blob territory, and LFS
  bandwidth is a metered org-wide quota while plain blobs are not.
- Keep fixtures at the repository root, one directory per surface, importable as-is.

**What to ask for in the fixture** (the data does not exist yet, so this is the moment to
specify it):

- One small OPC, **at least two LOD levels** — a single-level patch would not exercise the
  LOD streaming path, which is exactly where the constant-attribute bug lived.
- Real textures, since the texture path is part of what is under test.
- **A defined cross-section** — a stored annotation/polygon over the fixture, so the
  *active* clipping path is exercised, not only the empty one. This is the branch where
  `InsideOutsideV4` carries a real `ArrayBuffer`; #666 only fixed the branch where it does
  not, and that branch has never been under test. Required for both the efficacy invariant
  and expected-coverage.
- **A secondary texture plus a transfer function** — so `secondaryTexture`, `contourLines`
  and the `TextureCombiner` modes (Primary/Secondary/Multiply/Blend) are driven rather than
  skipped. These are fragment stages that can silently degrade to passthrough; without data
  they are only ever tested in their inactive state.
- A total size budget worth agreeing up front; a few MB, not a few hundred.
- Ideally a second fixture containing **invalid/NaN vertices**, which is what the triangle
  filter exists for and is otherwise untested. Note the filter is **off by default**
  (`SurfaceApp.mk`), so this exercises a non-default path — while the measurements above
  say the default path pays for the filter's geometry stage regardless.

The cross-section and secondary-texture assets are the part that does not exist anywhere
today; the OPC itself is the easy half. Worth specifying them in the same conversation
rather than discovering later that the fixture only supports half the tiers.

## Shape of the thing

### Where it lives

Extend `src/Tests` (Expecto, already cross-platform, already has the skip idiom) rather
than adding a project.

### Test case shape

```
scene fixture  ×  surface configuration  ×  camera  →  render  →  invariants
```

A configuration is a declarative record of the surface/config flags that feed uniforms:
cross-section clipping, triangle filter + size, contour lines, secondary texture /
transfer function, footprint, colour adaption, radiometry, false colour, LOD colouring,
MSAA samples, near/far. Each maps to something `ViewerUtils` already binds.

Two of these must be driven in their **active** state, not merely toggled: cross sections
(clipping against a real polygon) and secondary textures (with a transfer function, across
the `TextureCombiner` modes). Both are fragment paths that degrade silently to passthrough,
and both are otherwise only ever seen switched off.

The full cartesian product is unaffordable. Three tiers:

- **Neutrality tier** — for each flag independently, render with it off, and with it on in
  a state where it should have no visible effect. Assert pixel-identical. Linear in the
  number of flags. This is the tier that catches the motivating bug.

  **Both arms must be composition-identical: the same stages composed, differing only in
  uniform values.** This is not a detail — it is what keeps the exact comparison viable.
  A geometry shader that passes a triangle through unchanged does *not* reproduce the
  rasteriser's input bit-for-bit; measured on `minimal` vs `filter`, 0.4% of bytes differ,
  82% of them by exactly ±1. That is re-emission noise, not a bug, and it would make an
  exact assertion fail forever for the wrong reason.

  So neutrality is a claim about a stage's **output**, never about its **presence**.
  Comparing "stage absent" against "stage present but disabled" is a different question,
  belongs to the performance tier, and must never be asserted pixel-exact. Keeping that
  line sharp is also what makes the performance problem expressible: *disabled* and *not
  composed* are different things, and the whole geometry-shader cost lives in the gap.
- **Efficacy tier** — for each feature that can be *made* to act, drive it with real data
  and assert it changes pixels, deterministically and in the expected direction. Cross
  section: clipping with a defined polygon removes a non-empty, stable set of fragments,
  and disabling it restores full coverage. Secondary texture: each `TextureCombiner` mode
  produces a distinct result, and the transfer function maps a known input to a known
  output band. Composition-identical arms, same as neutrality.
- **Smoke tier** — a handful of realistic combinations, asserting coverage and opacity only.

### Determinism

Screenshots must not race the loader. Known-good from this session: build patch nodes with
`asyncLoading = false`, then render until two consecutive frames are byte-identical, with a
cap. Without it the harness produces flaky coarse-LOD frames that read as rendering
differences. Render offscreen (`CompileRender` → `Download`), as
`PRo3D.ProjectionTestbed/Offscreen.fs` already does — no window, no window-manager
dependency.

(The in-viewer snapshot feature is **broken on `releases/6.0.0`**, observed this session.
Separate bug, out of scope. System screenshots test what the user actually sees and were
what proved the fix, but need a window server and Screen Recording permission — keep as a
local debugging mode only.)

### Bisect as a first-class feature

When a case fails, the harness must report *which stage* by composing `surfaceEffect`
progressively and naming the first stage at which the invariant breaks. This is precisely
what found the bug by hand over several rounds; automating it is the difference between a
useful gate and a screenshot to squint at. With no CI history to bisect against, this is
the only bisect available.

Comparison across rungs must be on **invariants (coverage), never on pixels** — rungs
differ in composition, so the ±1 re-emission noise above applies. Bisect asks "at which
stage does the invariant break", not "at which stage do the pixels change".

That requires `surfaceEffect` to be an inspectable, filterable list of named stages with
declared inter-stage dependencies — a fragment stage that reads a varying needs the vertex
stage that writes it, or the GL backend throws `Could not get attribute`. A working
version exists on `bugs/apple-silicon-crosssection-clip` (commit `890cf57a`), written under
time pressure with an environment-variable interface. It should be **redesigned properly**:
the stage list and its dependency graph are part of the rendering architecture, not test
scaffolding, and belong in their own PR with their own review. The env-var interface should
not survive.

**Stages are not independently droppable, and the design must not pretend otherwise.**
FShade merges adjacent shaders of the same kind into one GLSL stage — `triangleSizeFilter`
and `generateNormal` become a single geometry stage. Dropping either alone was measured at
~3%; dropping both, 13.9×. Worse, dropping `generateNormal` *alone* measured **slower** than
composing both, which is unexplained. Consequences:

- Attribution may only ever claim *"dropping X saves Y"*, never *"X costs Y"*.
- A bisect that assumes additive, independent stages will mislead. Report the prefix that
  changed behaviour, not a per-stage cost table.

## Performance tier

Same primitive, different reduction: instead of asserting an invariant on the rendered
image, time it.

- **Budget** — total frame time for a reference (scene, config, camera) against a recorded
  threshold. Catches "everything got slower" without attributing it.
- **Attribution** — for each stage, drop it and re-measure, reporting the delta. Subject to
  the non-additivity caveat above: this ranks candidates, it does not decompose a total.

Both need far more care about measurement noise than the correctness tiers, and neither
should gate a release until it has a replicated baseline. Start informational.

### Known measurements, unreplicated

From the follow-up investigation, recorded so the numbers are not re-derived — and flagged
because they are **one machine, one dataset, one camera, via a machine-specific
`--data-root`**, which the CI-readiness rules forbid and open question 3 must fix:

- The geometry-shader *stage* costs ~92% of frame time, resolution-independent, while
  `FilterTriangleEnabled = false`.
- The **viewer itself was never benchmarked.** That `ViewerUtils.surfaceEffect` pays the
  same cost is an inference from code reading (it composes both stages unconditionally),
  not a measurement. Verify before anyone acts on it.
- The originally reported symptom — `--triangle-filter 5` feeling slower interactively —
  **did not reproduce offscreen**; it measured slightly *faster*. The cost was present in
  both arms of the reported comparison.

No fix has been attempted. The shader-vs-CPU direction was put to the user per
`ai/CONVENTIONS.md` and deferred; it remains open and is not this plan's to close.

## Cross-platform coverage without a CI matrix

The bug was platform-specific, so coverage across platforms is the whole point — and until
GPU runners exist it comes from team members running the gate on the machines they have.
That is workable but has a failure mode CI does not: **nobody can tell which platforms were
actually covered before a release.** "It passed for me" on one machine is not the same
claim as a green matrix, and it is easy to mistake one for the other.

Minimum viable answer: the release checklist names the platforms that must be exercised
(at least Apple Silicon and Windows — the pair that differed here), and the run's summary
output is pasted into the release PR or notes. Cheap, and it makes a gap visible rather
than silent.

## The dominant risk: a suite nobody runs

Nothing forces this to run until CI gets GPUs. An unrun test suite rots into a liability —
it fails for unrelated reasons, nobody trusts it, it gets deleted. This is a bigger threat
to the work than any technical gate, and it needs a deliberate answer:

- **Wire it into the release process.** Running it on Apple Silicon and Windows should be a
  checklist item alongside `PRODUCT_RELEASE_NOTES.md`, not folklore.
- **Keep it fast.** Well under a minute for the neutrality tier. 256×256 targets are ample
  for coverage statistics.
- **Make failure output good enough that people want to run it** — a named stage and a
  written-out PNG, not a bare assertion failure.
- **Adopt CI the moment GPU runners exist.** That is the real fix, and the CI-readiness
  rules above exist to keep the switch to a one-line change. Until then, do not treat the
  manual gate as equivalent to automation.

## Decisions taken

- Invariants are the gate; renders are failure artifacts only.
- Offscreen rendering, not window capture — required for the eventual CI switch.
- Extend `src/Tests`; skip via the `--skip-hera` idiom (`--skip-render` + GL probe +
  fixture-absent). Skipping is a runtime flag, never conditional compilation, so CI
  adoption is a script change.
- Build CI-ready from day one even though CI is deferred.
- Fixtures come from `PRo3D.Resources.Models` via submodule; tests skip when uninitialised.
- Compose the viewer's real shaders and scene graph. No reimplementation, no copies.
- `asyncLoading = false` plus frame-stability convergence.
- The stage list / bisect machinery is production code in `ViewerUtils`, designed and
  reviewed on its own terms, with the harness as one consumer.

## Open questions

1. ~~Do the config-neutrality pairs hold today?~~ **Partly answered.** A composition change
   (`minimal` vs `filter`) is *not* pixel-identical — ±1 re-emission noise, not a bug. That
   was a different comparison from the one this tier specifies, and the resolution is the
   composition-identical rule above rather than a tolerance. **Still open:** whether any
   genuine *uniform* toggle perturbs pixels while nominally inactive. Resolve before
   building the tier, or the first flag tested settles it by accident.
2. How much of `Surface.Sg`'s per-patch attribute plumbing can be driven without the full
   viewer model? `applyFootprint` / `applyCrossSection` / `applySecondaryTextureId` are Ag
   attributes whose absence throws at `CompileRender`
   (`PRo3D.ProjectionTestbed/Program.fs` documents this). Needs a small scene-graph
   construction helper — shared with the testbed, not duplicated.
3. Exact fixture contents and size budget — needs agreeing with whoever populates
   `PRo3D.Resources.Models`. Blocking for Phase 1 steps 4–5. The OPC is the easy half; the
   cross-section polygon and the secondary texture + transfer function do not exist
   anywhere today and are what the efficacy tier depends on.
4. Should this subsume `PRo3D.ProjectionTestbed`? Both render OPCs offscreen and compare.
   Not now — but do not build a third offscreen-render helper; factor the existing one.

## Risks

- **Nobody runs it.** See above. The main risk.
- **Invariants too weak** — a coverage rule that passes on a subtly wrong image.
  Mitigation: validate each invariant against a *known-bad* build. Concretely: revert #666
  locally and confirm the harness fails. An invariant never validated against the bug it
  claims to catch is decoration.
- **Flakiness** poisoning trust. Mitigation: determinism is itself an asserted invariant,
  so flakiness surfaces as a specific failure rather than noise.
- **Scope creep** into a general rendering-test framework. The success criterion at the top
  is the scope.

## Suggested staging

### Phase 1 — a gate that works

Everything here is reachable **without** the named stage list: these tiers toggle uniforms
and swap fixture data, they do not recompose the effect.

1. Offscreen render helper + deterministic settling, factored from
   `ProjectionTestbed/Offscreen.fs`. Skip idiom wired up (`--skip-render`, GL probe,
   fixture-absent). One test: renders the fixture, asserts non-empty.
2. Agree and land the fixture in `PRo3D.Resources.Models` — OPC **plus** the cross-section
   and secondary-texture assets (open question 3). Blocking for 4 and 5.
3. Coverage + determinism invariants. **Validate against a reverted #666** — this is the
   step that proves the harness works, and nothing after it is worth doing until it passes.
   Record frame time while here; it is free at this point and establishes the performance
   baseline before anyone needs it.
4. Config neutrality tier over the flags in `ViewerUtils`. Settle open question 1 first.
5. Config efficacy tier — cross sections and secondary textures driven with real data.
6. Smoke tier + release-checklist entry naming the platforms to cover.

### Phase 2 — attribution

Deferred. Phase 1 tells you *that* something broke or slowed; this phase tells you *which
stage*.

7. `surfaceEffect` as a designed, named, dependency-aware stage list (own PR, own review).
   **Foundation, not leverage** — two independent consumers (correctness bisect and
   performance attribution), and the only thing that makes either possible. That it is
   foundational is an argument about architecture, not about shipping order.
8. Automatic stage bisect on failure — invariant-based, not pixel-based.
9. Performance tier — budget + attribution mode on top of the stage list. Informational
   until a baseline is replicated on a second machine.

### Phase 3 — automation

10. *(when GPU runners exist)* Drop `--skip-render` from `runTests` and add the matrix.
    Nothing else should need to change; if it does, the CI-readiness rules were violated.

Steps 1–3 are still the whole value. If the work stops early it should stop after 3 with
something real, not halfway through 4. A performance tier is worth less than a correctness
gate that actually runs, which is why Phase 2 sits behind a working Phase 1 even though the
stage list is architecturally the foundation.
