# Testing PRo3D

Two complementary mechanisms. Use **both** when a change touches viewer
behavior: Expecto proves the model/math, Playwright proves the pixels.

| script (`.cmd` / `.sh`) | runs |
|---|---|
| `runTests` | Expecto, kernel-independent subset (`--skip-hera`) — what CI runs |
| `runAllTests` | the whole Expecto suite, HERA kernel tests included |
| `runUiTests` | builds viewer + pro3d-tool, then the Playwright specs in `tests-ui/` (needs a GPU, `PRO3D_TEST_DATA`, `PRO3D_SPICE_KERNELS`); extra args go to Playwright, e.g. `runUiTests projection-e2e` |
| `runFullTests` | `runAllTests`, then `runUiTests`; non-zero if either fails |

## Expecto (`src/Tests`)

Unit and integration tests over models, update functions, SPICE math, parsers,
exporters. Run from the Release build the FAKE script produces:

```
dotnet bin/Release/net9.0/Tests.dll                       # whole suite (~10 s + fixtures)
dotnet bin/Release/net9.0/Tests.dll --filter-test-list <substring>
```

- The suite is **sequenced** (SPICE kernel state is process-global); new test
  lists are registered in `src/Tests/Program.fs` — read its ordering comments
  before placing anything that loads kernels.
- Kernel-, GPU- or fixture-dependent tests **self-skip** when prerequisites
  are missing; pure model tests always run.
- Pattern for update-function tests: build models directly
  (`{ SomeApp.initial with … }`), call `update`, assert on the record — see
  `ProjectedImageStackTest.fs`.

## Playwright (`tests-ui/`) — drive the real app

End-to-end tests that launch the real viewer (`--server` mode), operate its
browser UI, and verify results with **screenshots of the rendered 3D view**.
**Do not be shy about using this.** A green build plus theory is not evidence
that a viewer change works; a Playwright run is. It is the fastest way to
answer "does the surface still render", "does my UI action reach the model",
"does this effect actually draw" — and failures come with screenshots you can
look at instead of speculating.

Read **[../tests-ui/README.md](../tests-ui/README.md)** before writing or
debugging a spec — it documents the mechanics that are NOT guessable:

- server mode exits when stdin closes (the launcher keeps a pipe open);
- every docking panel is its own page (`?page=gis`, `?page=render`);
- Electron file dialogs must be stubbed on `window.aardvark.dialog`;
- click via single-shot DOM `evaluate` (Playwright's actionability loop
  starves against the incremental UI);
- screenshot gates: the loading splash is rendered *into* the stream
  (`streamLive`), overlays pollute naive brightness checks (`litFraction`),
  and an empty view is perfectly "stable";
- **a changed surface shader recompiles for minutes on first start, during
  which surfaces are absent with no log output** — budget for it, and do not
  misdiagnose it as "my shader broke rendering".

**Touched image projection** (shaders, projector matrices, winding correction, fly-to,
sidecar parsing)? Run `npm run test:projection` in `tests-ui`. It generates a
frame with its sidecar, projects it through the UI and requires the render to
reproduce the source at zero shift — see the `projection-e2e` section of the
README for prerequisites (`PRO3D_TEST_DATA`, tool build, Python, a kernel tree
matching its epoch).

For quick one-off questions (what does this page's DOM look like? what does
the view show right now?), write a **probe** (`tests-ui/src/probe-*.ts`,
`npx tsx src/<probe>.ts`) instead of a spec — same launcher, no test
ceremony. Screenshots land in `tests-ui/artifacts/`; read the images yourself
before drawing conclusions from pixel statistics.

Tests are machine-local (GPU + local datasets, `PRO3D_*` env vars); they are
not run in CI, which makes running them locally the only line of defense.

### What a probe like that CANNOT see

A probe that sets values by `evaluate`-ing JS against the DOM never *uses* the
UI — it reaches into it. That is fast and stable, and it is why the projection
probes ran green for days while a user could not perform the workflow at all.
Everything below was invisible to them and was found only by a human:

- **Broken widgets.** A probe that sets a `<select>` never clicks the accordion
  that contains it, so an accordion that refuses to open is not a failure, it is
  simply not exercised. A regression made *Orientation Source* unreachable — the
  one setting the whole projection depends on — and every probe still passed.
- **Slowness.** Probes settle by waiting for the frame to stop changing, with
  timeouts in the hundreds of seconds. A UI crawling at 2 fps and one at 60 fps
  both "pass". Perf regressions are structurally invisible.
- **Anything only the GUI does.** Probes run `--server` headless with no Aardium.
  The GIS entity panel, which is what hammered SPICE every frame, does not exist
  there.
- **Setup.** A probe that loads a scene file with the surface already imported,
  the GIS binding already made and the epoch already right is testing the last
  10% of the workflow. Start from an empty scene at least once.

So: **for anything a user touches, click it.** `tests-ui/src/probe-accordion.ts`
clicks a title and then asserts the content is actually visible afterwards, which
is the shape to copy.

### Mechanics that cost hours here

- **`--scene` omitted does NOT give an empty viewer.** PRo3D reloads the last
  scene from `userPreferences.json`. To start empty you need an explicitly empty
  scene file.
- **Two dropdown helpers, two behaviours.** `Html.SemUi.dropDown` acts on a
  hand-dispatched `change` event; **`UI.dropDown''` does not** — it reported
  "set Slope" while the render did not move a pixel. Use Playwright's
  `selectOption` for those.
- **Wait for `attached`, not `visible`.** Config rows live inside collapsed
  accordions and arrive over the incremental DOM channel *after* `networkidle`.
  They are settable while never being visible.
- **The surfaces panel only renders properties for the SELECTED surface**, and
  the tree labels entries `0|Dimorphos`, not `Dimorphos`.
- **No JS error does not mean it worked.** `$(sel).accordion(…)` on an *empty*
  jQuery set is a silent no-op. Two attempted fixes "succeeded" while leaving the
  widget dead, with a clean console. Listen to console/pageerror/requestfailed
  and print what jQuery actually matches -- that is what ended the guessing.
- **Semantic UI accordions must not be nested-and-initialised.** Its selectors
  (`.title`, `.content`) resolve across *all* descendants, so an outer accordion
  also owns an inner one's titles and the inner panel never opens. Only
  initialise the outermost; overriding the selectors with child combinators does
  not work.
- **Never `locator.click()` on the 3D view — it hangs forever.** The render
  control's parent (`div.mainrendercontrol.aardvark`) intercepts pointer events,
  so Playwright's actionability check never settles, and `click()` has *no*
  default timeout: it retries until the whole spec times out, with a call log
  that says "element is visible, enabled and stable" each time. Use raw
  `page.mouse` (as `scene-body.spec.ts`'s `drag()` does).
- **Ctrl+click needs a beat after the mouse move.** PRo3D reads the modifier from
  *key* events on the focused element, not `MouseEvent.ctrlKey`, and the control
  focuses itself on `mouseenter`. Pressing Control in the same tick as the move
  sends the keydown to whatever was focused before (typically the other page), so
  the click is taken as navigation: no pick, no annotation, no error anywhere.
- **Neither `litFraction` nor `streamLive` rejects the loading splash.**
  `litFraction` measures the centre of the frame, which is exactly where the bright
  AARDVARK banner sits. `streamLive` samples three corners expecting the splash to
  be pure black, but the render div's CSS background is `#222222` (`Viewer.fs:2530`)
  = 34, above its threshold of 15 — so it reports "live" as soon as the DOM exists,
  whatever the stream shows. An unloaded view therefore scores as "lit and stable"
  and the spec reads a half-loaded model. If a spec waits for the surface, gate on
  the splash colour too (`annotation-profile-export.spec.ts:splashFraction`).
  (`image.ts` says the viewer clears to `#2A2A2A`; measured it is `#222222`.)
- **Don't pace picking with fixed sleeps.** The first pick on a patch loads its
  KdTree from disk (~4.5 s each), later ones hit the cache and cost milliseconds.
  PRo3D ignores input while intersecting, so a click sent too early is silently
  dropped and the annotation simply never completes. Wait for the log to go quiet
  (`annotation-profile-export.spec.ts:awaitIdle`).
- **Address tool-strip buttons by `title`, not by icon.** `wrapToolTip` puts the
  tooltip on the button div itself, and icons repeat — `mouse pointer` is both
  *Select annotation* and *Select surface*. `.pro3d-tool[title="Draw annotation"]`
  is exact. It does depend on tooltips being on: `UI.wrapToolTip` returns the node
  unchanged when `UI.enabletoolTips` is false, so a viewer launched with
  `-notooltips` (`Program.fs:232`) has no `title` at all and every such locator
  fails with "not found".
- **Playwright's text engine cannot see `<option>` text.** `filter({ has:
  'option:text-is("Sky")' })` matches nothing. Find the select's index in an
  `evaluate` and then drive it with `selectOption`, which sends the real events.
- **The shared test scene carries the HERA AFC *instrument* camera — override it
  unless you are testing projection.** `fixture.sceneTemplate` is
  `AFC_2027-03-21/ProjectionTest.pro3d`, and `sceneFor` only re-points its paths,
  so every spec silently inherits `config.frustumModel.focal = 122.563`, i.e.
  **hfov 5.53°** instead of PRo3D's 60°. `Scene.applyScene` recomputes the frustum
  unconditionally from `focal`/`nearPlane`/`farPlane`/`aspect` (`Scene.fs:305-310`,
  `:393`), so the frustum stored in the scene — and `toggleFocal`, which only the
  interactive handler reads — are both ignored: set `focal = 10.25` (and adjust the
  camera distance, since the body then looks ~12x smaller).
  This matters beyond framing: a selected annotation's outline spheres are scaled
  by `dist * size / viewportWidth` (`Utilities.drawSpheresFast`), which omits the
  field of view. At 60° that is 13% off and invisible; at the AFC's 5.53° it is
  **10.35x**, and the annotation renders as a giant red blob. See issue #770.
- **When a measurement disagrees with what the app does in front of you, instrument
  the app — do not keep theorising.** Hours went into that blob from the outside
  (zoom-invariance tests, viewport hypotheses, server-vs-desktop comparisons), and
  every conclusion was wrong, including a confident "it is the harness". Three
  `Log.line` calls on the actual scale computation, plus an env-var kill switch to
  bisect the draw, settled it in two runs. Print the numbers the renderer uses.

### Do not let the metric reward the bug

The sharpest lesson from the projection work. Step 3 of
[`../docs/ProjectionValidation.md`](../docs/ProjectionValidation.md) once
compared *the viewer with the projection* against *the viewer without it* —
"did these two frames stay the same" — over a region restricted to the lit side.
It reported correlation **1.0000**, ΔDN **0.000**, 99.99% bit-identical, and
concluded the projection was exact. It was rotated.

The score was dominated by pixels the projection never touched, so **the more
broken it was, the fewer it repainted and the closer to 1.0000 it scored**. The
same trap sits in any "count the changed pixels" coverage number: it cannot tell
"not covered" from "covered by a value that matches" (8.5% vs a true 41.3% on
one frame).

Before trusting a number, ask: *if the feature were completely broken, what would
this metric say?* If the answer is "it would look fine", the metric is wrong.
Compare against a **source of truth** (the image itself), never against the thing
being modified, and check the geometry (do the silhouettes coincide?) and the
orientation (is the identity really the best of the eight square symmetries?)
before believing any content-level correlation.

Tests are machine-local (GPU + local datasets, `PRO3D_*` env vars); they are
not run in CI, which makes running them locally the only line of defense.
