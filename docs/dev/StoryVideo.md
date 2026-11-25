# Story videos

A Playwright story spec can record a captioned screen video of the full UI. The first one is the
geological map story (`tests-ui/tests/geological-map-story.spec.ts`, pictures in
[../GeologicalMap-story.md](../GeologicalMap-story.md)).

```
cd tests-ui
set PRO3D_TEST_DATA=...\PRo3D.Resources.TestData
set PRO3D_VIDEO=1
set FFMPEG=...\ffmpeg-8.1-full_build\bin\ffmpeg.exe
npx playwright test geological-map-story
```

Result: `tests-ui/artifacts/geological-map-story/geological-map-story.mp4` (1920×1080, H.264).
Without `PRO3D_VIDEO` the spec takes the pictures, as before.

The projection stack story (`tests-ui/tests/projection-stack-story.spec.ts`) works the same
way: drag & drop reordering with the fade and the live 3D preview. It runs on the test data by
default; the published video uses the Mars swing-by:

```
set PRO3D_STACK_STORY_SCENE=C:\pro3ddata\HERA\workshop4\marsCorrectHSH.pro3d
set PRO3D_STACK_STORY_IMAGES=...\Mars-Swing-By\HSH-1B\1B
set PRO3D_STACK_STORY_FROM=T121545
npx playwright test projection-stack-story
```

(`..._FROM`: the first of four consecutive frames - the 12:15-12:36 ones overlap well.)

## Pacing: machine time is cut, viewer time is explicit

- waits on PRo3D (`until`, `idle`, `panelHas`, the settling in `shot`) keep 0.35 s, and nothing
  when they directly follow another wait;
- only captions (`say(text, hold)`: reading time before the action - the caption stays up during
  it) and results (`shot`: 1.5 s) hold the picture;
- pointer glides (0.2-0.6 s), pauses before a Ctrl+click, typing and key badges are as short as
  can still be followed.

A wait not wrapped in `until` is dead time in the video.

## How it works (`tests-ui/src/video.ts`)

| part | how |
|---|---|
| layout | `PRO3D_LAYOUT_DIR` with a `current.json` written by the spec: render view left, panels right ([../WindowLayouts.md](../WindowLayouts.md)) |
| driving | the same steps as the pictures, over a small `Ui` (render, panel, annotations, toolbar, input page). Video: the panels are iframes of one page; mouse and keyboard go to that page |
| positions | the story's points are fractions of a 1600×900 view; another view size scales them about the centre by width (the field of view is horizontal) |
| frames | Chromium screencast over CDP, JPEG, each with its time - not Playwright's `recordVideo` (fixed low bitrate, blurry at 1080p) |
| pointer | drawn into the page and moved with the mouse: headless Chromium draws none, and mouse events in iframes never reach the top document. Clicks show a ripple, held keys a badge |
| captions | a banner over the render view (`say`) |
| waits | `until(...)` waits (picks, KdTree loads, panel updates) are cut from the video except for their first 0.35 s (nothing of a wait that directly follows another); the loading before the story is dropped |
| encode | the frame list with per-frame durations (ffmpeg concat demuxer), 30 fps, x264 CRF 20 |

Writing a step for both modes: use the `Ui` helpers (`pick`, `gesture`, `hover`, `useTool`,
`clickButton`, `press`), wrap waits in `until`/`idle`/`panelHas`, and add a `say` caption.
