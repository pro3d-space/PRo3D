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

```
cd tests-ui
npm install                       # once
npx playwright install chromium   # once

# a scene + data must exist locally; defaults target the HERA workshop set
$env:PRO3D_IMAGE_DIR = "C:\pro3ddata\HERA\workshop3\COP\COP\2027-03-01"
npx playwright test                       # all specs
npx playwright test tests/stack-ui.spec.ts
```

| Env var | Meaning | Default |
|---|---|---|
| `PRO3D_EXE` | viewer binary | `../bin/Release/net9.0/PRo3D.Viewer.exe` |
| `PRO3D_SCENE` | `.pro3d` scene to load | `C:\pro3ddata\HERA\workshop3\projectionScene.pro3d` |
| `PRO3D_IMAGE_DIR` | image folder for import-driven specs | a COP date folder |
| `PRO3D_PORT` | HTTP port for the viewer | 54321 |
| `PRO3D_SELECT_IMAGE` / `PRO3D_STACK_IMAGES` | specific images for the projection specs | first library row |

Current specs: `projection-smoke` (import → stack → the projection visibly
lands on the surface), `stack-ui` (add/toggle/reorder/remove through the GIS
tab), `hover-flyto` (hover preview + footprint, exact reversion, fly-to camera
move).

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
