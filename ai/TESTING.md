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

### Never derive the background colour from the frame

The viewer clears to `#222222` (34). An unobserved region of a texture mosaic
renders **pure black (0)**, so "body" means *not the clear colour*, not *bright* —
and the clear colour must be the fixed constant 34. Every attempt to infer it
from the picture fails on exactly the frames that matter:

- **Sampling a corner** breaks as soon as the black body covers that corner.
  `image.ts:bodyCoverage` still does this.
- **Taking the most common value** breaks too: when most of the visible body is
  unobserved, black *is* the most common value.

Both then classify the black body as background and report the body as missing.
That produced a "`bodyPixels=9501`, essentially empty frame" reading on a scene
that was rendering perfectly, and cost a session chasing a rendering bug that did
not exist — while the real fault was a camera 136 m from a body that needs ~199 m
to fit the fov. Assert the share of pixels actually at 34 and fail loudly if it is
~0, rather than trusting a plausible-looking coverage number.

### Framing: check the angle before blaming the harness

A body of radius *r* seen from distance *d* subtends `2·asin(r/d)`. Dimorphos is
`r = 77.2 m` and the scene's `focal 10.25` is a 60° fov, so it needs *d* ≳ 190 m
to fit — at 136 m it subtends ~69° and overflows the frame. Compute this before
concluding the render page, the aspect ratio or the viewport is at fault.
`tests-ui/src/probe-bookmarks.ts` renders every bookmark of a scene and prints
distance, subtended angle and body coverage, which answers the question in one run.

### Reproduce a viewpoint from a bookmark, never by re-aiming

A bookmark stores the full `[Sky, Location, Forward, Up, Right]`. Write it
verbatim into the scene's top-level `cameraView` and relaunch. Rebuilding it as
"position + look at the origin" throws the saved orientation away and reframes the
shot. Where a bookmark is radial (`Forward = -Location/|Location|`) you may scale
`Location` to dolly in or out — that leaves all four directions untouched —
but assert the radial property first.

### aardvark.media hover needs a real crossing

`onMouseEnter` handlers do not fire for `new MouseEvent("mouseenter",
{ bubbles: false })`. They also do not fire if the pointer never *leaves* the
element: library rows are the full panel width (~1092 px), so nudging from
`x - 40` to `x` stays inside and nothing happens. Move the mouse away first, then
in. And resolve the row element by requiring it to contain **its own** name and no
other row's — walking up to "the nearest ancestor with an inline border" lands on
the container shared by every row, so all of them hover the same thing and produce
byte-identical frames.

### Hiding the chrome for a screenshot

The HUD, tool strip and colour bar are ordinary absolutely-positioned DOM over
`img.rendercontrol`, and they are **not** all siblings of it — walking up from the
render control's parent misses the HUD. Sweep `document.body` and hide every
`position: absolute|fixed` element that is not an ancestor of the render control.
The scale bar and axis cross are rendered into the image itself and survive this.

Tests are machine-local (GPU + local datasets, `PRO3D_*` env vars); they are
not run in CI, which makes running them locally the only line of defense.
