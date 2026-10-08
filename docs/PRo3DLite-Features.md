# PRo3D Lite — feature list

Every feature PRo3D Lite offers, panel by panel, with its status. The overview and the design
are in [PRo3DLite.md](PRo3DLite.md).

Many of Lite's panels are the full viewer's own panels, so some controls appear in Lite that
only have an effect in the full viewer. This list says so for each of them, instead of hiding it.

| Status | Meaning |
|---|---|
| ✅ | works in Lite |
| ◐ | works partly — see the note |
| ⛔ | the control is shown, but has no effect in Lite (it needs a part of the full viewer Lite does not have) |

"Tested" names the test that covers the feature: **E** = Expecto (`src/Tests/LiteTests.fs`,
`src/Tests/CompositionTests.fs`), **P** = Playwright (`tests-ui/tests/lite.spec.ts`).
Untested features were checked by reading the code only.

---

## 1. Application

| Feature | Status | How / notes | Tested |
|---|---|---|---|
| Desktop window | ✅ | `PRo3D.Lite.exe` opens an Aardium window titled *PRo3D Lite*. | |
| Import OPCs at start | ✅ | `--opc <dir>`, repeatable; a directory holding OPCs or one OPC. | P |
| Open a scene at start | ✅ | `--scene <file.pro3d>`. | P |
| Port | ✅ | `--port <n>`, default 4340. | P |
| Server mode | ✅ | `--server`: serves without a window, prints `LITE_URL:<url>`, runs until stdin closes. | P |
| SPICE frames | ✅ | The default SPICE kernels are initialised at start; planetary up vectors, latitude/longitude and altitudes work for Mars, Earth, Moon, Phobos, Deimos, Didymos and Dimorphos. | E |
| User preferences | ◐ | `%APPDATA%/Pro3D/userPreferences.json` is read (e.g. MapView WASD inversion). Lite has no panel to edit it. | |

## 2. Window and menu

The window is the full viewer's, reduced to Lite's functions: the same top bar, second
toolbar row and icon tool strip ([ToolStrip.md](ToolStrip.md)), shared code.

| Feature | Status | How / notes | Tested |
|---|---|---|---|
| Hamburger menu | ✅ | Top left; opens on hover or click. *Surfaces → Import OPCs*; *Scene → Open scene / Save scene / Save scene as*; *Annotations → Load annotations / Save annotations*; *View → Home / Reset layout*. | P |
| Reference system | ✅ | Planet dropdown in the top bar (§8). | |
| Second toolbar row | ✅ | Chip with the selected tool's name in its group colour, the tool's settings (§7: *Draw* → geometry, projection, thickness, sampling, fill; *Place coordinate cross* → cross size and visibility), and the Ctrl+click hint — which also tells whether a grabbed vertex is in hand. | |
| Camera readout | ✅ | Top left of the 3D view, as in the full viewer: reference frame (e.g. *Mars (IAU ellipsoid)*), bearing, pitch, camera position, latitude, longitude, altitude and coordinate convention. Bearing and pitch read *n/a* on small bodies; lat/lon/altitude need a planet. | |
| Tool strip | ✅ | Icon rail on the right edge of the 3D view: navigation modes (blue) and tools (green annotation, amber selection, teal reference); the active ones are filled chips, tooltips on hover. | |
| Panels | ✅ | *3D View*, *Annotations*, *Surfaces*, *Scene*, *Properties* are Golden Layout panels: drag a tab to move, dock, stack or float it; drag the splitters to resize; maximise. | |
| Default layout | ✅ | 3D view on the left; *Annotations* / *Surfaces* / *Scene* stacked on the right above *Properties*. | |
| Reset layout | ✅ | *View → Reset layout* restores the default layout, bringing closed panels back. | |
| Status line | ✅ | One line at the bottom: what the active tool expects, results of imports, saves and loads, errors. | E |
| Scene name | ✅ | Right end of the top bar: the open scene's file name, or *\*new scene*. | |
| Open scene | ✅ | *Scene → Open scene* — see §9. | P, E |
| Save scene / Save scene as | ✅ | *Scene* menu — see §9. | P, E |
| Import OPC | ✅ | *Surfaces → Import OPCs* or the *Surfaces* panel — see §3. | P, E |
| Load / Save annotations | ✅ | *Annotations* menu — see §9. | P, E |
| Home | ✅ | *View → Home* or the *Surfaces* panel: flies the camera to frame all visible surfaces. | E |

## 3. Surfaces — loading

| Feature | Status | How / notes | Tested |
|---|---|---|---|
| Import OPC directories | ✅ | Folder dialog with multi-select. Each chosen folder may be one OPC or a folder of OPCs (and zipped OPC folders); every OPC found is imported. | P, E |
| Camera frames the first import | ✅ | Importing into an empty scene puts the camera in front of the data. Later imports leave the camera alone. | E |
| `.opcx` attribute and texture layers | ✅ | Read on import, same as the full viewer. | |
| Legacy `.trafo` pre-transform | ✅ | A `<surface>.trafo` next to the import path is applied. | |
| `.opc.json` metadata | ✅ | Logged (DEM reference model, DSK summary). | |
| KdTrees for picking | ✅ | Loaded with the surface (lazy KdTree cache). | E |
| Broken data | ✅ | A failed import is reported in the status line, the app keeps running. | |
| Mesh surfaces (OBJ etc.) | ◐ | No import for meshes. Meshes in an opened full-PRo3D scene are loaded, but are drawn with the OPC surface effect (not checked). | |

## 4. Surfaces — display

Lite draws surfaces with a plain textured effect: the full viewer's view-space triangle filter
and CPU-composed stable transformation, with the OPC texture. It has none of the viewer's
SPICE lighting, shadows, image projection or false-colour layers.

| Feature | Status | How / notes | Tested |
|---|---|---|---|
| Textured OPC | ✅ | Diffuse texture of the OPC. | P |
| Level of detail | ✅ | Patches refine with distance; *Quality* steers it. | P |
| Priority groups | ✅ | Each *Priority* value is drawn as its own pass with fresh depth (higher on top), as in the full viewer. | |
| Placement | ✅ | Surface transformation, pre-transform, flip Z and SketchFab conventions. | P |
| Selected surface box | ◐ | A green box around the selected surface. Always on for the selection — *Highlight Selected* / *Highlight Always* are ignored. | |
| Primary texture choice | ✅ | The texture layer chosen under *Properties → Primary Texture* is shown. | |
| Scalar false colour | ⛔ | Needs the full viewer's surface effect. | |
| Secondary texture, texture combiner, transfer function | ⛔ | Same. | |
| Colour adaptation, radiometry | ⛔ | Same. | |
| Contour lines, Lat/Lon grid | ⛔ | Same. | |
| Distance-to-home filter | ⛔ | Always off in Lite. | |
| Pivot point and local reference system markers | ⛔ | Not drawn. The values themselves work (§5). | |
| Home position marker | ⛔ | Not drawn. Fly-to uses it (§5). | |

## 5. Surfaces panel

### Surface list

| Feature | Status | How / notes | Tested |
|---|---|---|---|
| Group tree | ✅ | Surfaces in groups; expand/collapse; *Set active* group (new imports go there); *Add Group*. | |
| Select a surface | ✅ | Click its name (green when selected). The cube icon adds it to a multi-selection. | |
| Priority \| name | ✅ | Shown per surface. | |
| Fly to surface | ✅ | House icon: flies to the surface's home position, or frames its box. | E |
| Open folder | ✅ | Folder icon opens the surface's directory (desktop window only). | |
| Visible | ✅ | Eye icon. | |
| Active | ✅ | Checkbox: inactive surfaces are not picked by the drawing tools. | E |
| Rebuild KdTrees | ✅ | Wrench icon; asks for confirmation. Restart and reload the surface afterwards. | |
| Missing data marker | ✅ | A red exclamation mark when the surface's directory is gone. | |
| Toggle group | ✅ | Eye icon of a group. | |

### Properties (selected surface)

| Field | Status | Notes |
|---|---|---|
| Name | ✅ | |
| Visible, Active | ✅ | as in the list |
| Highlight Selected, Highlight Always | ⛔ | see §4 |
| Priority | ✅ | render order (§4) |
| Quality | ✅ | level of detail |
| TriangleFilter, TriangleSize | ✅ | hides triangles with an edge longer than *TriangleSize* (in view space) |
| DistanceFilter, FilterDistance | ⛔ | |
| Home Position | ✅ | set from the current camera; used by fly-to |
| Fillmode | ✅ | fill / line / point |
| Cull Faces | ✅ | |
| Scalars | ⛔ | selection is stored, not shown |
| OPCx Info path | ✅ | read-only |
| Primary Texture | ✅ | |
| Transfer Function, Secondary Texture, Texture Combiner, Blend Factor, Min/Max | ⛔ | |

With a *group* selected instead, *Properties* offers renaming the group.

### Transformation (selected surface)

| Field | Status | Notes |
|---|---|---|
| Reference system mode | ✅ | placement relative to the global or the surface's local reference system |
| Translation, Scale, Yaw, Pitch, Roll, Euler order | ✅ | the surface moves accordingly |
| Flip Z, SketchFab | ✅ | |
| Pivot mode, Pivot Point | ✅ | rotation pivot; picking the pivot on the surface is not available (no tool for it) |
| Show PivotPoint, Pivot visualization size, Show local RefSys, RefSys Size | ⛔ | not drawn |
| Local Reference System | ✅ | read-only values |
| Import / Export Trafodata | ✅ | transformation from / to a file |

### Other sections

| Section | Status | Notes |
|---|---|---|
| Color Adaptation (colour correction, radiometry) | ⛔ | values are kept and saved with the scene |
| Contours | ⛔ | same |
| LatLon Shader | ⛔ | same |
| Scalars ColorLegend | ⛔ | same |
| Actions → Remove | ✅ | removes the selected surface |
| Actions → Selection: Move / Clear | ✅ | moves the multi-selection into the active group / clears it |
| Actions (group) → Remove / Clear | ✅ | |

## 6. Navigation

| Feature | Status | How / notes | Tested |
|---|---|---|---|
| Free fly | ✅ | Left-drag looks around, middle-drag pans, right-drag dollies, wheel zooms, **W A S D** move. | |
| Orbit (ArcBall) | ✅ | Left-drag orbits the orbit centre, middle-drag pans, right-drag zooms, wheel, **W A S D**. Switching to it picks the surface under the screen centre as orbit centre; without a surface there it stays in free fly and says why. | |
| Map view | ✅ | Needs a planet (greyed out for *None*). Left/middle-drag move over the ground, right-drag and wheel zoom, **W A S D** move north/west/south/east. Up is north, speed scales with height. | |
| Switch mode | ✅ | Blue icons of the tool strip: rocket (free fly), circle (orbit), map (map view; greyed out without a planet). | E |
| Set orbit centre | ✅ | Tool *Set orbit centre*: Ctrl+click on the surface; switches to orbit. | E |
| Orbit centre marker | ✅ | Magenta dot while orbiting; switch it off in *Scene → View*. | |
| Camera keeps upright | ✅ | Up follows the planet's local up (body-aware sky). | |
| Fly-to animations | ✅ | *Home*, fly to surface, fly to annotation; mouse input waits until a fly-to has finished. | E |
| Camera is free while a tool is armed | ✅ | Holding **Ctrl** gives the left button to the tool; releasing it gives it back to the camera. | P |
| Navigation sensitivity | ✅ | **Page Up** / **Page Down** in the 3D view step it by 0.5 (the new value shows in the status line); slider and input in *Scene → View*. Saved with the scene. | E |
| Navigation gizmo, axis lock | ⛔ | Not in Lite. | |

## 7. Annotations

### Tools (tool strip; hold **Ctrl** in the 3D view)

| Tool | Status | How / notes | Tested |
|---|---|---|---|
| Draw | ✅ | Ctrl+click on a surface adds a point. | P, E |
| Select | ✅ | Ctrl+click an annotation selects it; with **Shift** it is added to the multi-selection. | |
| Edit vertices | ✅ | Select an annotation; its control points get handles. Ctrl+click a handle to grab it, move, Ctrl+click on the surface to drop it there. **Esc** puts it back. Moving a vertex onto another surface is reported in the status line. | E |
| Select surface | ✅ | Ctrl+click selects the surface under the cursor (visible and active surfaces only). | E |
| Set orbit centre | ✅ | see §6 | E |
| Place coordinate cross | ✅ | Ctrl+click moves the reference system's origin there and re-ups the camera. | E |

### Drawing

Geometry, projection, thickness, sampling and fill are on the second toolbar row while *Draw*
is active; sampling shows only for viewpoint/sky projection, fill only for closed geometries.

| Feature | Status | How / notes | Tested |
|---|---|---|---|
| Point | ✅ | One click. | |
| Line | ✅ | Two clicks; finishes by itself. | P, E |
| Polyline | ✅ | Any number of clicks, **Enter** finishes. | E |
| Polygon | ✅ | Like polyline, closed. Filled when *Fill/Alpha* is on. | |
| Dip & Strike | ✅ | Polyline with a least-squares plane: strike (red) and dip (green) vectors. Needs a planet. | |
| True Thickness | ✅ | Needs a planet; dip angle / azimuth editable in *Properties*. | |
| Axis ellipse | ✅ | Two points for the axis, a third for the radius. Needs a planet. | |
| Ellipse, 4-point ellipse | ⛔ | Hidden in the geometry list (the full viewer draws them with its own ellipse tool). | |
| Projection | ✅ | *Linear* (straight segments), *Viewpoint* (segments re-projected onto the surface from the camera), *Sky* (re-projected along the planet's up). Offered per geometry. | E |
| Sampling | ✅ | Number or spacing of samples along re-projected segments. | |
| Thickness | ✅ | Line thickness of new annotations. | |
| Fill / alpha for new annotations | ✅ | | |
| Group default colour | ✅ | Colour picker on a group: colour of new annotations drawn into it. | |
| Mesh surfaces | ✅ | Drawing on a mesh switches the projection to linear. | E |
| **Enter** | ✅ | Finishes the annotation being drawn. | E |
| **Backspace** | ✅ | Removes its last point. | E |
| **Esc** | ✅ | Discards it (in *Edit vertices*: puts a grabbed point back). | E |
| Semantic keys **0–3** | ⛔ | Full viewer only. | |
| Cut / union of annotations | ⛔ | Full viewer only. | |

### Annotation list

| Feature | Status | How / notes | Tested |
|---|---|---|---|
| Group tree | ✅ | Expand/collapse, *Set active* (new annotations go there), *Add Group*. | |
| Per annotation | ✅ | Select (click), add to selection, visible (eye), fly to (house icon: the camera it was drawn from). | E |
| Per group | ✅ | Show all / hide all, select all / deselect all, *Recalculate selected polygon measurements*, default colour. | |
| Actions → Remove | ✅ | Removes the selected annotation (undoable). | E |
| Actions → Selection: Move / Clear | ✅ | Moves the multi-selection into the active group / clears it. | |
| Actions (group) → Remove / Clear | ✅ | | |
| Undo / Redo | ✅ | Buttons, or **Ctrl+Z** / **Ctrl+Y** in the 3D view. Covers drawing, removing, moving vertices and property edits. | E |

### Properties and measurements (*Properties* panel, selected annotation)

| Field | Status | Notes |
|---|---|---|
| Geometry, Projection | ✅ | read-only |
| Semantic | ✅ | |
| Thickness, Color | ✅ | |
| Text, TextSize, Show Text | ✅ | label drawn next to the annotation |
| Visible | ✅ | |
| Show DnS | ✅ | dip-and-strike vectors on/off |
| Fill, Fill Color, Fill Alpha | ✅ | polygons |
| Dip Angle, Dip Azimuth | ✅ | true-thickness annotations; recomputes the thickness |
| Cross Section | ⛔ | full viewer only (curtain view) |
| Every edit is undoable | ✅ | |

Measurements (read-only): Position, Height, HeightDelta, Avg Altitude, Length, WayLength,
Bearing, Slope, Vertical / Horizontal Distance, True / Vertical Thickness, Area — and the
dip-and-strike results for DnS annotations. ✅ They follow the reference system: changing the
planet, up or north recomputes all of them (tested, E). *PrintPosition* writes the position to
the log.

## 8. Reference system (top bar, toolbar row, *Scene* panel)

| Feature | Status | How / notes | Tested |
|---|---|---|---|
| Planet | ✅ | *Reference System* dropdown in the top bar: Earth, Mars, Moon, Phobos, Deimos, Didymos, Dimorphos, or the non-planetary frames None, JPL, ENU. Changing it re-bases every surface's local frame, recomputes every annotation measurement, and drops a drawing tool that needs a planet back to *Line* for *None*. | E |
| Coordinate cross | ✅ | Drawn at the reference system's origin, with north label and scale. Place it with the *Place coordinate cross* tool; while that tool is active the second toolbar row sets the cross's size (unit) and visibility. | P |
| Up, North, N-Offset | ✅ | *Scene* panel, editable; measurements and camera follow. | |
| Origin readout | ✅ | Position, longitude, latitude, altitude, convention (planetographic / spherical / ellipsoidal). | |
| Visible, Textsize, Textcolor | ✅ | of the coordinate cross. | |

## 8a. View settings (*Scene* panel → *View*, *Camera*)

The full viewer's view configuration, reduced to the settings that do something in Lite. All of
them are saved with the scene.

| Setting | Status | Effect | Tested |
|---|---|---|---|
| Navigation Sensitivity | ✅ | camera speed of every navigation mode; also **Page Up** / **Page Down** | E |
| Near Plane, Far Plane | ✅ | clipping distances of the 3D view (field of view and aspect kept) | E |
| Picking Tolerance | ✅ | how close a click must be to an annotation to select it | |
| Import Triangle Size (m) | ✅ | default triangle-size filter of newly imported surfaces | |
| Arrow Length, Arrow Thickness | ✅ | size of the coordinate cross arrows and the dip-and-strike vectors | |
| D+S Plane Size | ✅ | size of the dip-and-strike plane glyph | |
| Orbit Centre Marker | ✅ | the magenta orbit-centre dot on/off | |
| Camera | ✅ | read-only: location, forward, sky, bearing, pitch | |

Left out on purpose (the full viewer's config has them, Lite draws none of them): the 3D preview
cursor and its size, LoD colours, the orientation cube, surface leaf labels, and the focal length
(disabled in the full viewer too).

## 9. Files

| Feature | Status | How / notes | Tested |
|---|---|---|---|
| Open scene | ✅ | `.pro3d` from Lite or from full PRo3D (scene format v3, the current one; older versions are not checked). Loads surfaces (relative surface paths are resolved), camera, navigation mode, orbit centre, view configuration, reference system, and the `<scene>.pro3d.ann` annotations next to it. | P, E |
| Unreadable or missing scene | ✅ | Reported in the status line; the current scene stays. | E |
| Save scene | ✅ | Writes the open scene again. Unsaved scenes are told to use *Save scene as*. | |
| Save scene as | ✅ | Writes `<name>.pro3d` and `<name>.pro3d.ann`. | P, E |
| Keeps full-PRo3D content | ✅ | Everything Lite does not understand in an opened scene (bookmarks, GIS, view plans, scale bars, …) is written back unchanged. | E |
| Full PRo3D opens Lite's scenes | ✅ | Versions from this change on (older ones need the parts Lite does not write). | P, E |
| Load annotations | ✅ | Any `.pro3d.ann`; replaces the annotations. | E |
| Save annotations | ✅ | `.pro3d.ann`, the full viewer's format. | P, E |
| Other annotation formats | ⛔ | No PRo3D v1 XML or SBMT import, no GeoJSON/CSV/attitude export — full viewer only. | |

## 10. Not in Lite

These full-viewer features have no place in Lite's UI at all: the 3D preview cursor and the
values under it, LoD colouring, the orientation cube, surface leaf labels, bookmarks and sequenced
bookmarks, scale bars, traverses, view planning and the instrument view, GIS view and SPICE
observation, image import and projection, surface comparison, cross sections, outcrop traces,
color by category, height validation, scene objects, geologic surfaces, map projection panel,
snapshots and screenshots, provenance, the remote API, direct tool mode, window layout files.
