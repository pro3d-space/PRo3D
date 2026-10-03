# PRo3D Lite

PRo3D Lite is the minimal PRo3D: load and view OPC surfaces, navigate them, measure them
with annotations. It is a separate, small application (`PRo3D.Lite.exe`) built from the same
code as the full viewer: the PRo3D.Core sub-apps (surfaces, drawing, reference system) and
the host glue in PRo3D.Composition that PRo3D.Viewer uses as well. Nothing in Lite is a copy
of viewer code.

## What it does

The complete, panel-by-panel list - including which full-viewer controls have no effect in
Lite - is [PRo3DLite-Features.md](PRo3DLite-Features.md).


| Feature | How |
|---|---|
| Load OPCs | *Import OPC* (menu or Surfaces panel) takes OPC directories or folders of them; `--opc <dir>` on the command line. The camera frames the first import. |
| View | The surfaces render with the plain textured OPC effect (triangle-size filter, level of detail). No SPICE lighting, image projection or shadows. |
| Navigate | Free fly and orbit (ArcBall), plus map view on a planet — the full viewer's controllers. The blue icons of the tool strip switch the mode; **Page Up** / **Page Down** change the speed; *Home* frames all visible surfaces. |
| View settings | *Scene → View*: navigation sensitivity, near/far plane, picking tolerance, import triangle size, arrow and dip-and-strike glyph sizes, orbit-centre marker; *Camera* shows position, bearing and pitch. |
| Annotate | Pick a tool in the tool strip, hold **Ctrl** and click the surface. Point, line, polyline, polygon, dip-and-strike, ... from the geometry selector on the second toolbar row. **Enter** finishes, **Backspace** removes the last point, **Esc** discards. *Select* picks annotations, *Edit vertices* drags their control points. |
| Measure | The *Properties* panel shows and edits the selected annotation and its measurements (length, height, bearing, slope, dip/strike, ...). Choose the planet under *Reference System* in the top bar so up vector and altitudes are right; changing it recomputes every measurement. |
| Undo | **Ctrl+Z** / **Ctrl+Y** (or the buttons) undo and redo annotation changes. |
| Surfaces | The *Surfaces* panel lists them with visibility, fly-to and their properties. |
| Layout | *Reset layout* restores the default panel layout, bringing closed panels back. |
| Save | *Save scene as* writes a `.pro3d` scene plus the `<scene>.pro3d.ann` annotation sidecar; *Load/Save annotations* reads and writes `.pro3d.ann` on their own. |

The window is the full viewer's, reduced: the top bar (hamburger menu, reference system, scene
name), the second toolbar row (the selected tool, its settings, the Ctrl+click hint) and the
icon tool strip on the 3D view ([ToolStrip.md](ToolStrip.md)) are the same components,
shared through PRo3D.Composition, with Lite's tools only - as is the camera readout (frame,
bearing, pitch, position, lat/lon/altitude) on the top left of the 3D view. The 3D view, *Annotations*,
*Surfaces*, *Scene* and *Properties* are Golden Layout panels: drag their tabs to rearrange them.

## Files are full PRo3D files

- **Annotations** are the full viewer's `.pro3d.ann` format: an annotation file from Lite
  opens in PRo3D and vice versa.
- **Scenes** are `.pro3d` scenes. Lite reads and writes the *core* of a scene - camera,
  navigation mode, surfaces, view configuration, reference system - with the full viewer's
  codecs. Every other part of a scene it opened (bookmarks, GIS, view plans, scale bars, ...)
  is kept as it was read and written back unchanged, so opening and saving a full PRo3D scene
  in Lite loses nothing. A scene created in Lite holds only the core; PRo3D opens it because
  `Scene.read3` treats everything else as optional (PRo3D releases from before this change
  cannot open such a scene).

## Limits

- Surfaces placed through the GIS observation (SPICE observer/observed systems) are shown at
  their stored transformation only; Lite has no GIS.
- No image projection, traverses, scale bars, bookmarks or the other mission tools: Lite is
  deliberately the minimum. They stay in the full viewer.

## Command line

```
PRo3D.Lite.exe [--opc <dir> ...] [--scene <file.pro3d>] [--port 4340] [--server]
```

`--server` serves on the port without opening a window and runs until stdin closes; it prints
`LITE_URL:<url>` once it serves. This is the mode the Playwright spec
(`tests-ui/tests/lite.spec.ts`) drives.

## How it is built

`src/PRo3D.Lite` holds only the wiring:

- `Lite-Model.fs` — `LiteModel` composes `SurfaceModel`, `DrawingModel`, `NavigationModel`,
  `ReferenceSystem` and `ViewConfigModel` with a Golden Layout and a little UI state.
- `LiteApp.fs` — the update: each case delegates to a Core sub-app or to PRo3D.Composition.
- `LiteGui.fs` — the pages; the panels are the Core sub-app views.
- `Program.fs` — the Giraffe/Aardium host.

PRo3D.Composition (see `ai/ARCHITECTURE.md`) is what makes this possible: surface loading,
KdTree picking, the click→interaction routing, navigation, the surface scene graph and the
scene core codec live there, and the full viewer calls the same functions.
