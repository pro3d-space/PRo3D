# Window Layouts

PRo3D arranges its panels (Main View, Surfaces, Annotations, GIS View, …) with
[Golden Layout](https://golden-layout.com/): panels can be resized, moved between stacks,
maximised and popped out into their own windows (issue
[#614](https://github.com/pro3d-space/PRo3D/issues/614)).

A window layout belongs to **you, not to the scene**. PRo3D remembers the layout you left
and restores it at the next start, whatever scene you open. To share an arrangement, PRo3D
stores the current layout **beside** a scene when it is saved, and offers it when someone
opens that scene.

## Using it

**Layout menu** (main menu → *Layout*):

| Entry | Does |
|-------|------|
| *M2020*, *PRo3D Core*, *Surface Comparison*, *Render Only*, *Provenance*, *GIS* | apply a built-in layout. *M2020* is the default of a first start. |
| *My Layouts* | your saved layouts; click one to apply it |
| *Save Current Layout As...* | store the current arrangement under a name |
| *Manage My Layouts...* | load, rename or delete saved layouts |
| *Reopen Panel* | bring back a panel you closed; it is added as a tab to the largest stack without the main view |

The top bar shows the active layout, e.g. `Layout: M2020`, and `(modified)` once you have
rearranged, closed or reopened panels. Resizing alone does not count as a modification.

The **Main View** cannot be closed. Every layout contains it.

**Popouts.** The *open in new window* button in a stack's header moves the stack into its own
window. Closing that window docks the panels back. Popouts are part of the layout and are
restored with it.

### Saving and opening scenes

Saving a scene (*Save*, *Save as*, Ctrl+S) also writes `<scene>.pro3d.layout` next to
`<scene>.pro3d`. This file is only a convenience: **if it cannot be written, the scene is
still saved**. PRo3D then tells you so and logs the reason.

Opening a scene that has a `.pro3d.layout` beside it asks what to do, unless its arrangement
is the one you already have:

- **Add it to my layouts**: stores it in *My Layouts* under the scene's name (`name (2)` if
  that name is taken). This option is not offered if your layouts already contain the
  arrangement.
- **Use it now**: applies it.
- **Ignore**: leaves everything as it is.

Popout windows of a shared layout are placed by the window system; screen positions are not
stored beside scenes, because they only make sense on the machine that stored them.

### When something is broken

Nothing about layouts can stop PRo3D from starting, opening or saving a scene:

| Situation | What happens |
|-----------|--------------|
| the last used layout (`current.json`) cannot be read | PRo3D starts with *M2020*, keeps the file as `current.json.corrupt` and shows a notice that stays until you confirm it |
| a layout in *My Layouts* cannot be read | it stays listed; opening it tells you why it fails; it can be deleted |
| the layout beside a scene cannot be read | the scene opens normally; a message says the layout was ignored |
| the layout beside a scene cannot be written | the scene is saved; a message says the layout could not be stored |
| a layout names a panel this PRo3D does not know (newer version, hand edit) | the panel is left out |

## Where layouts live

| File | Content |
|------|---------|
| `%APPDATA%/Pro3D/layouts/current.json` | the layout you left, restored at start |
| `%APPDATA%/Pro3D/layouts/library/<name>.json` | *My Layouts* |
| `<scene>.pro3d.layout` | the layout stored with a scene |

`PRO3D_LAYOUT_DIR` replaces `%APPDATA%/Pro3D/layouts` (used by the tests so they never touch
your layouts).

All three have the same format:

```json
{
  "format": "pro3d-layout",
  "version": 1,
  "name": "M2020",
  "layout": { "root": { "type": "row", "content": [ ... ] }, "openPopouts": [ ... ] }
}
```

`layout` is Golden Layout's resolved configuration as aardvark.media reads it
(`GoldenLayout.Json.deserialize`): `type` `row` / `column` / `stack` / `component`,
`componentType` = panel id, `size` + `sizeUnit` (`fr` or `%`). A file with a higher `version`
than this PRo3D knows is refused rather than misread.

The current layout is written in the background, at most once per burst of changes (500 ms
after the last one), to a temporary file that is then moved into place.

## Scene compatibility

Scenes do **not** carry the window layout, and their format is unchanged. PRo3D up to 6.2
stored its docking layout in the scene's `dockConfig` field and requires that field when
reading a scene. So:

- a scene that has `dockConfig` writes it back exactly as it was read;
- a new scene writes the 6.2 M2020 docking layout.

Scenes saved by this version therefore open in every older PRo3D, and older scenes open
here. Their old docking layout is not used.

## Implementation

| File | Role |
|------|------|
| [`src/PRo3D.Viewer/DockConfigs.fs`](../src/PRo3D.Viewer/DockConfigs.fs), [`DashboardModes.fs`](../src/PRo3D.Viewer/DashboardModes.fs) | built-in layouts |
| [`src/PRo3D.Viewer/Layouts/LayoutFiles.fs`](../src/PRo3D.Viewer/Layouts/LayoutFiles.fs) | `LayoutPanels` (registry of panel ids and titles), `LayoutOps` (sanitize, shape, reopen), `LayoutFile`, `LayoutLibrary`, `SceneLayoutSidecar` |
| [`src/PRo3D.Viewer/Layouts/Layout-Model.fs`](../src/PRo3D.Viewer/Layouts/Layout-Model.fs) | `LayoutModel` on the root `Model`, `LayoutAction` |
| [`src/PRo3D.Viewer/Layouts/LayoutApp.fs`](../src/PRo3D.Viewer/Layouts/LayoutApp.fs) | update, scene-open offer, menu, dialogs |
| [`src/PRo3D.Viewer/Layouts/LegacyDockConfig.fs`](../src/PRo3D.Viewer/Layouts/LegacyDockConfig.fs) | the 6.2 `dockConfig` written into new scenes |
| [`src/PRo3D.Viewer/Viewer/Viewer-IO.fs`](../src/PRo3D.Viewer/Viewer/Viewer-IO.fs) | `saveEverythingWithLayout`: sidecar after the scene |

Every layout that enters the viewer (from the browser, a library file, `current.json` or a
sidecar) goes through `LayoutOps.sanitize`: unknown and duplicate panels are dropped, titles
and closability come from `LayoutPanels`, sizes are clamped, empty containers removed, and
the main view is added if missing. All file reads and writes return `Result` and never throw.

Two properties of aardvark.media's Golden Layout wrapper shape the code:

- **Replay.** Its `SetLayout` channel sends the last pushed layout to every client that
  connects later, e.g. a reloaded page. PRo3D therefore keeps that payload equal to the
  current layout (same version, so connected clients do not receive it again), but only once
  the browser has shown the pushed arrangement. Before that, a late event from the old layout
  could replace the push before it is sent. For the same reason the `GoldenLayout` record is
  never recreated at runtime, e.g. on *New Scene*: a fresh record restarts the version and
  the next push would be dropped as already seen.
- **Serializer asymmetry.** `GoldenLayout.Json.serialize` writes sizes as `"7fr"`, while
  `deserialize` reads `size` + `sizeUnit` (the form the browser reports) and turns anything
  else into weight 1. Layout files are stored in the resolved form.

Other known limitations: the active tab of a stack and a maximised panel are not part of a
stored layout, and closed panels come back through *Reopen Panel* by replacing the whole
layout (which briefly reloads the panels and reopens popouts).

## Tests

- Expecto, `src/Tests/Features/WindowLayoutTests.fs`: sanitize (including a property test),
  reopen, file format round trip, parsing truncated/garbage files, library operations,
  corrupt `current.json`, replay handling, sidecar offer/import/ignore, scene compatibility
  (a new scene's and a re-saved 6.2 scene's `dockConfig` read with the 6.2 reader), saving
  with a sidecar that cannot be written, *New Scene* keeping the layout.
- Playwright, `tests-ui/tests/window-layouts.spec.ts` (real viewer, own `PRO3D_LAYOUT_DIR`):
  built-in layouts, unclosable main view, close/reopen, page reload, restart, library save
  as/load/rename/delete, saving a scene through the menu (sidecar written, `dockConfig`
  unchanged, blocked sidecar still saves and tells the user), opening scenes with a different
  layout at start and later (ignore, import + apply, no question for the same arrangement,
  broken sidecar reported), broken `current.json` and library entries, popout out and back.
