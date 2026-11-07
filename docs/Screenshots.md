# Screenshots

*Config → Screenshots* saves an image of the **main view**: press the camera button. Width and
height (pixels), background colour and format (PNG or JPEG) are set in the same panel; the
folder button opens the folder the images go to.

- Images are named `img000.png`, `img001.png`, … - the next free number in the folder.
- The default folder is PRo3D's AppData folder (`%APPDATA%\PRo3D` on Windows). Start PRo3D with
  `--screenshotDirectory <folder>` to save elsewhere.
- Any size and aspect ratio works: the image is rendered again at the requested size, not
  scaled from the screen. Very large images need correspondingly more GPU memory while they are
  taken.
- The background colour shows wherever no surface is.
- Failures (no main view open, the render server not answering) are written to the log; no file
  is saved then.

## Which view is captured

Always the main view - also when the instrument view, the map views or several windows are open.
With the main view open in more than one window, the one opened last is captured; they all show
the same camera.

Screenshots of the instrument view and other views are not available from this panel yet.

## How it works (developers)

The panel asks PRo3D's render server for the image
(`/rendering/screenshot/<scene>?w=&h=&samples=&background=&fmt=`). `<scene>` is the render
control's scene name on the server, which aardvark.media makes from the control's DOM id when it
mounts it - the view code never sees it. The server lists all live controls
(`/rendering/stats.json`), but in no particular order and without saying which view each is,
so the panel used to take an arbitrary one.

So the main view's render control reports its own id from its boot script
(`aardvark.processEvent('__ID__', 'pro3dSceneBooted', '__ID__')`, `ViewerApp.viewRenderView`).
That arrives as `ViewerAction.MainViewBooted` and is kept in `Model.mainViewScenes`, newest first
(session-only; a new scene keeps it, since the controls on screen do not boot again). On a
screenshot, `ScreenshotUtilities.tryChooseScene` takes the first of those the server still
renders; a page that was closed or reloaded is skipped that way.

`samples` is always the viewer's sample count: with it the server renders the live scene it
already has, OPC patches loaded. Another sample count makes it compile a new scene, whose single
frame comes out without surfaces.

The remote-control screenshot API (`--remoteControl`, `POST /shots`) is separate and unchanged.

Tests: `src/Tests/Features/Section09_ViewerConfiguration.fs` (TC-9.2: which scene is chosen) and
`tests-ui/tests/screenshot.spec.ts` (clicks the panel's button with several views open and
compares the file with the main view).
