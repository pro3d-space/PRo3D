# tests-ui — Playwright end-to-end tests for the PRo3D UI

Drives the **real PRo3D viewer** (real GPU, real OPC data, real SPICE) through
its browser UI and verifies behavior by DOM state and by **screenshots of the
rendered 3D view**. This is how UI- and rendering-level changes get verified
without a human clicking through the app — if you changed viewer behavior,
run (or write) a spec here rather than declaring victory from a green build.

Machine-local by design: the tests need a GPU and local datasets, so they are
not part of CI. They are cheap to run during development (~20 s per spec once
the shader cache is warm).

## Running

From the repository root, `runUiTests.cmd` / `runUiTests.sh` builds the viewer and
pro3d-tool in Release, installs the npm dependencies if needed and runs every spec
(extra args go to Playwright, e.g. `runUiTests.cmd projection-e2e`);
`runFullTests` runs the Expecto suite first. By hand:

```
cd tests-ui
npm install                       # once
npx playwright install chromium   # once

# data comes from a PRo3D.Resources.TestData checkout
$env:PRO3D_TEST_DATA = "C:\path\to\PRo3D.Resources.TestData"
$env:PRO3D_SPICE_KERNELS = "C:\path\to\spice"   # matching the frames' epoch, see below
npx playwright test                       # all specs
npx playwright test tests/stack-ui.spec.ts

npm run test:projection                   # projection end to end, see below
```

| Env var | Meaning | Default |
|---|---|---|
| `PRO3D_EXE` | viewer binary | `../bin/Release/net9.0/PRo3D.Viewer.exe` |
| `PRO3D_TEST_DATA` | PRo3D.Resources.TestData checkout; the specs use `HERA/Dimorphos_opc` | — (required unless the two below are set) |
| `PRO3D_SCENE` | `.pro3d` scene to load (`""` = empty PRo3D) | the test-data scene template, with its paths rewritten to the checkout and `PRO3D_SPICE_KERNELS` |
| `PRO3D_IMAGE_DIR` | image folder for import-driven specs | `HERA/Dimorphos_opc/AFC_2027-03-21` in the test data |
| `PRO3D_PORT` | HTTP port for the viewer | 54321 |
| `PRO3D_SELECT_IMAGE` / `PRO3D_STACK_IMAGES` | specific images for the projection specs | first library row |
| `PRO3D_AFC_DIR` | AFC frames with sidecars for `looking-at-dimorphos` | `PRO3D_IMAGE_DIR` |
| `PRO3D_SPICE_KERNELS` | SPICE kernel tree, written into the scene and handed to `pro3d-tool` | — |
| `PRO3D_E2E_OPCS` | OPC directories for `projection-e2e`, `;`-separated | the test-data Dimorphos |
| `PRO3D_E2E_SCENE_TEMPLATE` | scene `projection-e2e` derives its own from | the test-data scene template |
| `PRO3D_E2E_DATE` / `PRO3D_E2E_EPOCH` | observation for `projection-e2e` | 2027-03-21 / 20:00:00 |
| `PRO3D_E2E_SCENE_EPOCH` | scene time for its cross-epoch fly-to case | 14:00:00 |
| `PRO3D_PYTHON` | interpreter with numpy, for the data generator | `python` |

Current specs:

| spec | what it proves |
|---|---|
| `projection-e2e` | **the projection is correct**: generates a frame of the OPC with its sidecar, projects it through the UI, and requires the render to reproduce it (details below) |
| `projection-smoke` | import → stack → the projection visibly lands on the surface |
| `stack-ui` | add/toggle/reorder/remove through the GIS tab |
| `hover-flyto` | hover preview + footprint, exact reversion, fly-to camera move |
| `looking-at-dimorphos` | fly-to lands looking at the body, at the size the sidecar's range predicts |

### `projection-e2e` — generate, project, compare

The regression test for image projection. Per OPC it:

1. runs `scripts/make-projection-test-data.py`, which renders one HERA/AFC-1
   frame with `pro3d-tool simulate-image --write-mbi` (the sidecar describes the
   camera the render actually used) and writes a scene set up for projection;
2. opens that scene, imports the frame, sets *Orientation Source* MBI and
   *Transfer Function* off, flies to the frame and adds it to the stack;
3. requires the render to reproduce the frame (`registration` in `src/image.ts`):
   correlation > 0.9 at zero shift, the peak within 2 px of zero shift, the
   identity beating every mirror/rotation, gray staying gray — and > 90 %
   coverage from the shader's own coverage view with < 2 % spill.

It runs twice: once with the scene already at the frame's epoch, and once with
the scene 6 h earlier (`PRO3D_E2E_SCENE_EPOCH`, default 14:00), where fly-to has to
move the scene time to the frame's epoch *before* computing the camera — the other
order leaves the body rotated half a turn out from under it. About two minutes on a
warm shader cache; generated data, screenshots and the measured numbers land in
`artifacts/e2e/<label>/` and the test output.

Prerequisites beyond the other specs: a Release build of **`PRo3D.Tool`** as
well as the viewer (`bin/Release/net9.0/PRo3D.Tool.exe`), Python with numpy,
and a kernel tree **matching the default epoch**:
it was chosen against `hera_plan_v182_20260820`, and ESA's plan kernels move
HERA's future trajectory between releases — with an older plan the generator
fails with "the body does not appear in the frame". Set `PRO3D_SPICE_KERNELS`
(or change `PRO3D_E2E_EPOCH`).

## How it works — read this before writing a spec

**Launching** (`src/pro3d.ts`): the viewer runs with `--server` (no Aardium
window) and `--scene`. Server mode blocks on `Console.Read()` — the launcher
keeps stdin an **open pipe**; with stdin at EOF the app exits immediately.
Closing stdin is also how `stop()` shuts it down cleanly.

**Panels are pages.** Every docking panel is its own aardvark.media page:
`http://localhost:<port>/?page=render`, `?page=gis`, … Open them as separate
Playwright pages in one browser context — they share the server-side app
state, so clicking in the GIS page changes what the render page shows. No
golden-layout iframe navigation needed.

**Native dialogs are stubbed.** File/directory pickers go through
`top.aardvark.dialog.showOpenDialog` (an Electron API that does not exist in
plain Chromium). Inject a stub before clicking the button:

```js
await gis.evaluate(`(() => {
  window.aardvark = window.aardvark || {};
  window.aardvark.dialog = { showOpenDialog: () =>
    Promise.resolve({ canceled: false, filePaths: ["C:/data/images"] }) };
})()`);
```

The chosen paths flow back through `aardvark.processEvent(..., 'onchoose', …)`
which works in any browser.

**Click via single-shot DOM `evaluate`, not Playwright locators-with-actions.**
The incremental UI re-renders elements often enough that Playwright's
actionability retry loop (scroll/stability checks) can starve indefinitely.
Find the element inside one `page.evaluate` and call `.click()` /
`dispatchEvent` directly (see `clickRowIcon` in the specs). Prefer matching
the row that *contains* the target icon over sibling-walking from a text
node. Hover handlers are triggered with
`el.dispatchEvent(new MouseEvent("mouseenter"))`.

**Judging the 3D view = screenshots + pixel math** (`src/image.ts`):

- `streamLive(buf)` — the server renders its AARDVARK loading splash INTO the
  stream (bright logo, pure black background); the live viewer clears to dark
  gray `#2A2A2A`. Never trust a frame before `streamLive` is true — the splash
  fools any naive brightness check.
- `litFraction(buf)` — fraction of lit pixels in the *central* region only:
  the false-color legend (left edge) and the HUD text (top left) are DOM
  overlays that count as "content" otherwise.
- Then wait for two consecutive near-identical frames (`diffPng < 0.1 %`) so
  OPC/LoD streaming has settled — an *empty* view is perfectly "stable", which
  is why the lit gate must come first.
- Assertions compare before/after screenshots (`diffPng`), with baselines
  captured **after** any step that adds overlays (importing images shows the
  false-color legend; a baseline from before pollutes every diff).

**Shader-cache cold starts.** Any textual change to a composed surface shader
changes the effect id; the ~300 KB surface program then compiles from scratch
on the next app start — **surfaces are simply absent for up to minutes, with
zero log output**. The gates above absorb this (they wait up to 10 min), but
budget for it: give specs `test.setTimeout(15 * 60_000)` and don't conclude
"my shader broke rendering" from an early empty screenshot. Subsequent runs
are fast.

**Probes** (`src/probe-*.ts`, run with `npx tsx src/<probe>.ts`) are one-shot
diagnostic scripts using the same launcher — for dumping DOM structure, taking
ad-hoc screenshots, or capturing the documentation image set
(`probe-docs-shots.ts` regenerates `docs/images/multiProjection-*.png`).
Two `tsx` quirks: pass `page.evaluate` code as **strings** (tsx's transform
injects an `__name` helper that doesn't exist in the page), and remember
probes bypass Playwright's reporting entirely.

## Writing a new spec — checklist

1. Launch once per file (`beforeAll` / `afterAll` with `launchPro3d`).
2. Open the panels you need as pages; stub dialogs before clicking import.
3. Gate every render-view screenshot on `streamLive` + `litFraction`, then
   frame stability; capture baselines after overlay-adding steps.
4. Interact via single-shot `evaluate` clicks; poll DOM state with
   `expect.poll` (updates arrive asynchronously while the app re-renders).
5. Keep data paths behind `PRO3D_*` env vars with sensible local defaults.
6. When a spec fails, look at the artifacts before theorizing:
   `artifacts/*.png`, `test-results/**/test-failed-*.png`, and the app log
   `pro3d.log` (in this directory).
