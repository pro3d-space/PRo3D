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

## A testing ladder for rendering features

For a rendering feature, climb from cheap to expensive, each rung green before the next.
`docs/MapProjectionView.md` (#772) is a worked example:

1. **CPU math** (Expecto): the double-precision model the shader mirrors. It becomes the
   source of truth for the rungs above.
2. **Shader codegen, no GPU** (Expecto, `OutcropTraceShaderTest.compile`): FShade decompiles
   at runtime, so an effect that type-checks can still fail to generate. Assert the stages
   you expect and the vertex inputs the geometry really has.
3. **Headless GPU render** (Expecto, `Render.context` + `OpcSg.build` into a
   `PRo3D.Tool.SunAnglesVerb.FloatTarget`): real data, pixels checked against rung 1.
   - Write a quantity the shader did **not** derive the position from. Writing the same
     lon/lat the position came from agrees by construction and proves nothing.
   - Add a control that must fail, e.g. flipped rows.
   - Measure the readback row order instead of assuming it.
4. **Benchmark** (`Tests.dll --bench-...`, reusing `SurfaceEffectBenchmark.Bench`): a tool,
   not a test.
5. **Playwright**: the panel in the real app. A feature that can run standalone
   (`PRo3D.MapProjection.exe --server`) gets a spec against the standalone app, plus one
   check that PRo3D's page shows the same thing.

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

### Screenshot probes: four rules, each learned the hard way

The documentation probes share `tests-ui/src/probe-lib.ts`. Everything below is
enforced there rather than restated per probe, because every one of these bugs
lived in a hand-tuned copy of a helper that had drifted from its siblings.

**1. The clear colour is a constant, never inferred.** `#222222` (34). "Body"
means *not the clear colour*, never *bright*: terrain a mosaic never observed
renders **pure black (0)**, and that is a body pixel. Sampling the background
from a corner breaks the moment the black region reaches that corner; taking the
most common value breaks when most of the body is unobserved, because black then
*is* the mode. Both then report the body as absent. That is how a correctly
rendering scene once read as "`bodyPixels=9501`, essentially an empty frame" and
sent a session hunting a rendering bug that never existed — the real fault was a
camera 136 m from a body that needs ~199 m to fit the field of view.
`assertBackground` fails loudly when a frame is not against the assumed colour.

**2. A readiness gate must not be brightness-based.** `litFraction` can never be
satisfied by a body that is mostly unobserved, so the probe waits out its whole
timeout on a frame that was correct all along. Ask instead whether anything
differs from the clear colour (`drawingSurface`). `streamLive` samples corners
and has the same blind spot.

**3. `settled()` throws on timeout.** It used to fall through to
`page.screenshot()`, so a probe that never saw the body published a loading
splash into `docs/images/` under a confident caption. A figure that cannot be
produced is an error, not a default.

**4. Two scene fields silently decide whether you can aim the camera at all.**

- **`gisApp.defaultObservationInfo.target`** — the GIS *Camera source Body*.
  While it is set, the camera is **placed at that entity** and aimed at the
  observed body, and the scene's own `cameraView` is ignored entirely.
  `PRo3D.Resources.TestData`'s `ProjectionTest.pro3d` ships with it set to HERA,
  ~8 km out, which leaves the body a few hundred pixels across and makes every
  attempt to place the camera look like it was ignored — because it was. This is
  the likeliest reason an earlier session concluded the camera could not be set
  and fell back to scraping coordinates out of the HUD. Clear it with
  `withFreeCamera`.
- **`gisApp.defaultObservationInfo.referenceFrame`** — in an inertial frame such
  as `J2000` the body does not turn with the scene, so nothing that depends on
  its rotation is visible. Measured: the lit area creeps from 32.5% to 33.6%
  across a four-day mission-time row, i.e. visually nothing. Use
  `withBodyFixedFrame`, which is what picking the body under *Reference System*
  does in the GUI.

Framing is arithmetic, so check it before blaming the harness: a body of radius
*r* seen from *d* subtends `2·asin(r/d)`, against the fov the scene's `focal`
implies (`focal 10.25` → 60°). `probe-bookmarks.ts` prints distance, subtended
angle and coverage for every bookmark of a scene in one run.

Reproduce a viewpoint from a saved `[Sky, Location, Forward, Up, Right]` written
verbatim into `cameraView`; never rebuild one as "position, looking at the
origin", which discards the orientation and reframes the shot. Scaling `Location`
to dolly in or out is safe only for a radial view, so `pullBack` asserts that
rather than assuming it.
