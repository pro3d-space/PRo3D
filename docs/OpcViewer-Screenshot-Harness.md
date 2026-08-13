# OpcViewer Screenshot Harness

A headless renderer for OPC datasets that writes PNGs instead of opening a window, built
for reproducing and bisecting surface rendering artefacts.

The interesting bugs in the OPC pipeline — precision loss at planetary scale, driver
differences between Apple Silicon and the Windows/Linux GL stacks — only appear on real
data from a specific camera. Reproducing them by hand through the full viewer is slow and
not repeatable. This turns a bug report into one command.

Source: [`src/OpcViewer/ScreenshotViewer.fs`](../src/OpcViewer/ScreenshotViewer.fs),
CLI in [`src/OpcViewer/Program.fs`](../src/OpcViewer/Program.fs).

## Quick start

```bash
dotnet build src/OpcViewer/OpcViewer.fsproj -c Release
dotnet bin/Release/net9.0/OpcViewer.dll --dataset victoria --data-root /path/to/data --out ./shots
```

That renders the whole effect ladder (see below) and writes one PNG per rung.

## The effect ladder

The point of the tool. `--stack` selects how much of the viewer's real surface effect
(`ViewerUtils.surfaceEffect`) to run; the rungs are cumulative and composed in the
viewer's own order:

| rung | adds | rules out if clean |
|------|------|--------------------|
| `minimal` | `stableTrafo` + diffuse texture | OPC geometry, the LOD tree, texture loading |
| `filter`  | `triangleSizeFilter` (geometry shader) | the view-space triangle filter |
| `normals` | `generateNormal` (a *second* geometry shader composed onto the first) | geometry-shader composition |
| `lit`     | planet-local lighting + `solarLighting` | the lighting path |
| `color`   | `mapColorAdaption` + `mapRadiometry` | the colour chain |

With no `--stack`, every rung is rendered. The rung where an artefact first appears is the
stage that causes it.

These are the viewer's own shaders, not copies. `stableTrafo` and `triangleSizeFilter`
were moved to `PRo3D.Base.OpcSurfaceShader` specifically so both the viewer and this
harness compose the same code — a copied shader would drift and quietly invalidate every
result the tool produces.

## Cameras

`--eye X Y Z --bearing <deg> --pitch <deg>` takes exactly the numbers PRo3D's on-screen
readout prints, so a bad frame in the viewer is reproduced by copying three values off the
screen rather than guessing at a look-at pair. Up is the planetocentric radial direction.

`--look-at X Y Z` gives an explicit target instead. With neither, the preset's camera is
used, falling back to framing from the dataset bounding box.

## Presets

A preset is a dataset path relative to `--data-root` plus the camera an artefact is
visible from. `--data-root` defaults to `$PRO3D_DATA`, then the working directory.

| name | dataset | notes |
|------|---------|-------|
| `victoria` | `HiRISE_VictoriaCrater` | Apple Silicon surface artefact repro |
| `victoria-sr` | `HiRISE_VictoriaCrater_SuperResolution` | same camera |
| `capedesire` | `MER-B_CapeDesire_wbs` | MER-B ground level |

`victoria`'s near/far are PRo3D's own defaults (`ViewConfigModel.initNearPlane` /
`initFarPlane`) — 0.1 m to 500 km, a 5,000,000:1 depth range that is worth varying with
`--near` / `--far` when depth precision is a suspect.

Add a preset in `Presets` in `Program.fs` when a new artefact gets a repro camera.

## Options that reproduce viewer state

The harness deliberately starts from "nothing switched on" and lets you add back the one
thing you suspect:

- `--triangle-filter <m>` — enables the view-space triangle filter with this
  `MaxTriangleSize`. Off by default, matching `SurfaceApp.mk`. Uploaded as a `double`,
  exactly as the viewer does (`surf.triangleSize.value` is an `aval<float>`), so a
  CPU-side conversion mismatch would reproduce here too.
- `--sun X Y Z` — enables solar lighting from a world-space direction. Off by default:
  without SPICE the viewer has no sun, and with lighting off the `lit` rung passes colour
  through unchanged.
- `--samples <n>` — MSAA. The viewer's main control runs at 4; the offscreen default here
  is 1. Multisampled targets are resolved into a single-sample texture before download.
- `--near` / `--far`, `--cull`, `--wireframe`, `--compressed`.

## Diagnostics

- `--dump-glsl` logs the generated GLSL for every rung. The cheapest way to check what a
  shader's types actually became — in particular whether anything reached the GPU as
  `double`, which Apple Silicon has no hardware for. (As of this writing nothing does:
  every uniform and varying in the OPC path compiles to `float`/`vec*`.)
- `--wireframe` separates "missing triangles" from "triangles shaded wrong".
- `--interactive` opens a window on the selected rung and lets you fly around
  (PageUp/PageDown change speed).

## How screenshots are taken

Patches load synchronously (`asyncLoading = false`) so a screenshot cannot race the
loader. Frames are then rendered until two consecutive frames are byte-identical, because
the LOD decider needs a rendered frame to decide against — a fixed warm-up count either
captures a coarser LOD than the viewer would show, or makes every run pay for the worst
case. `--max-frames` caps it; the log says whether it converged or hit the cap.

## Bisecting the viewer's surface effect

The harness renders the OPC path in isolation. When an artefact appears in the viewer but
not here, the cause is a stage of `ViewerUtils.surfaceEffect` the harness does not
compose. That effect is a **named, filterable stage list**
([`Viewer-Utils.fs`](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs)), driven by environment
variables so stages can be A/B'd without a rebuild:

```bash
PRO3D_SURFACE_EFFECT=minimal            # only the stages this harness proves clean
PRO3D_SURFACE_EFFECT_ADD=a,b            # minimal plus these  (bisect upwards)
PRO3D_SURFACE_EFFECT_DROP=a,b           # everything except these (bisect downwards)
```

Unset changes nothing. Unknown stage names throw rather than silently doing nothing — a
typo that quietly changes the effect produces a "clean" run that means nothing.

Stages are not independent: a fragment stage that reads a varying needs the vertex stage
that writes it, or the GL backend throws `Could not get attribute '<name>'` at draw time.
`stageDependencies` declares those links and dropping a producer drops its consumers, so
any subset is safe to ask for. The log prints what actually ran:

```
surfaceEffect: 16/22 stages active
surfaceEffect: dropped footprintV, fixAlpha, markPatchBorders, ...
```

One unattended round — launch on the most recent scene, screenshot, shut down:

```bash
scripts/capture-surface-effect.sh half-a ADD=contourLines,crossSectionClip
```

It uses a **system screenshot** rather than PRo3D's own snapshot feature: that feature is
broken on this branch, and capturing the window tests the image actually on screen rather
than a second render of it. Needs Screen Recording permission (System Settings → Privacy &
Security), else `screencapture` fails with "could not create image from display". Start
PRo3D with `-loadRecent` (single dash) to reload the last saved scene, which is what makes
the loop unattended.

### Worked example: the Apple Silicon dark-quads artefact

1. Every harness rung rendered clean on an M1 → not geometry, LOD, textures, `stableTrafo`,
   `triangleSizeFilter` or `generateNormal`.
2. `PRO3D_SURFACE_EFFECT=minimal` in the viewer was clean too → the cause was one of the
   13 stages outside the minimal set.
3. Two halvings, then single stages: `crossSectionClip` alone reproduced it; `contourLines`
   alone was clean.
4. `PRO3D_SURFACE_EFFECT_ADD=crossSectionDebug` painted the attribute the shader tests
   instead of discarding on it, showing garbage where zeros were bound.

Cause and fix: [CrossSections.md](CrossSections.md#why-crosssectiondefined-exists).

## Legacy viewers

The older hard-coded viewers still exist behind `--legacy scene|annotations|solarsystem|multitexturing`.
They carry Windows-only paths and are kept only for the code they exercise; new work
should use `--opc` / `--dataset`.
