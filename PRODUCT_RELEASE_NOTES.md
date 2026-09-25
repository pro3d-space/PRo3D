## 6.3.5
The GIS camera follows its camera source body through mission time.

- GIS: **the Mission Time slider and row click now move the camera** from the camera source body (e.g. HERA) to the observed body, like the Time field already did; before, only the time, the sun and the bodies moved. A new setting, *GIS → Settings → Camera follows camera source body* (on by default, saved with the scene), switches this off to scrub time under your own camera. Choosing bodies or frame and *Re-use settings above* always re-aim; *Fly to* an image and *Load Spice and Time* keep their own camera (#826, docs/GisView.md)
- GIS: nothing moves when the kernel has no data for the camera source at that time (the log says `[SPICE] failed to transform body`), e.g. HERA at the default 2025 time, or a camera source that is fixed in the observed body's frame, such as Didymos seen in `DIMORPHOS_FIXED`
- Snapshots: bookmark replay re-aims at each bookmark's time whatever that setting says
- Scenes saved with 6.3.5 still open in earlier versions: the setting is stored under a new key they skip

## 6.3.4
Boulders and fractures as tables: ellipse axes and orientation, the surface inside each ellipse, and one row per fracture segment.

- Annotations: **the *Boulders (ellipses)* export preset writes one CSV row per ellipse** with its semi-major and semi-minor axis in metres and the azimuth of its long axis (clockwise from local north, 0–180°), at the ellipse centre. Ellipses now keep their shape from the moment they are drawn or imported from an SBMT catalog; a new *Annotation types: ellipses only* filter keeps lines and points out. The `majorDiameter` / `minorDiameter` columns are gone: they were empty for every drawn ellipse and in degrees otherwise (#644, docs/AnnotationExport-CSV.md)
- Annotations: **statistics of the surface inside each ellipse.** With *Ellipse statistics* ticked (the Boulders preset ticks it), every ellipse row carries its true surface area, footprint area and vertex count and, per per-vertex OPC layer, the covered area and the area-weighted mean, std, min and max, integrated over the mesh of the surface the ellipse was drawn on, visible or not. `statisticsNote` says why a row has none (imported ellipse, surface not loaded, a mesh). A 4,800-boulder catalog on Dimorphos takes about 3 s (docs/EllipseStatistics.md)
- Annotations: **the *Fractures (segments)* preset writes one CSV row per segment**, clicked point to clicked point, with its length along the surface, its straight length, its azimuth and both endpoints; `wayLength` repeats the line's total. New granularity *one record per segment*, CSV only
- Color by Category: **semi-major axis, semi-minor axis and long-axis azimuth** of ellipses, the azimuth on the 0–180° hue wheel
- Color by Category: **resampling a surface layer no longer decodes textures per point.** On an imported SBMT catalog (~4,800 ellipses on Dimorphos) it ran for minutes at ~10 GB, decoding every attribute texture at every control point although the chosen layer is per-vertex; it now reads only the chosen per-vertex layer (100 s, +1.3 GB). Point samplers read per-vertex `.aara` layers only (#817, docs/VertexAttributes.md)
- Scenes saved with 6.3.4 still open in earlier versions: the ellipse shape is stored under a new key they skip

## 6.3.3
Feedback while PRo3D is busy, a map that follows the 3D cursor, and a per-point export that no longer takes hours.

- UI: **PRo3D now says what it is doing while it is too busy to draw.** Long operations run on the update thread, which shares one lock with the UI and the render service, so the whole window freezes for their duration — one click on *ArcBall* costs seconds on a cold scene, because it intersects the centre ray against every active surface. A pill at the bottom of the window now names the operation and counts the seconds: picking, camera changes, importing surfaces, loading and saving scenes, exporting annotations and Color by Category's surface resampling. It appears only past 400 ms, so ordinary interaction looks unchanged. `-nobusy` switches it off and `-busyms <n>` moves the threshold (docs/BusyIndicator.md)
- UI: **the hover preview pick is reported too.** It runs in the background, so the window stays responsive — but until a cold patch's KdTree has loaded, the 3D cursor and the *Under Cursor* read-out are still showing the **previous** hit, which looks like a current answer in the wrong place rather than an obvious wait. Several operations at once are reported together, each held to the threshold on its own
- Small bodies and planets: **the map projection view can follow the 3D cursor.** With *Follow cursor* on, the map centres on the preview pick's hit point — the same one the *Cursor* panel reads — and draws it as a green marker beside the orange camera marker. On a planet the data is a speck, so the map is only useful next to the 3D view if it goes where you are looking (#772, docs/MapProjectionView.md)
- Annotations: **a per-point export with surface properties no longer takes hours.** On the HERA Dimorphos OPC it ran for hours at ~10 GB: every layer but Earth is served by the per-vertex `.aara` data, while Earth exists only as an 8652x4324 colour texture that the fallback decoded once per exported point. The fallback now samples only layers the `.opcx` declares as a *Map* with a physical range; colour textures carry no attribute value and are skipped (#809, docs/AnnotationExport.md)
- Annotations: **the texture fallback no longer drops a first texture that is a declared attribute.** It always treated the first non-weights texture as the patch's base colour — right for HERA/Dimorphos, whose first texture is Earth8K, wrong for the `Dimorphos_DRACO1_DRACO2_Earth` export, which puts `DRACO_1` first and gives it its own `.aara`. It is dropped only when the patch does not declare it in `<Attributes>`
- pro3d-tool: **`sample-layers` assembles instrument observations onto a body.** For every vertex of the finest OPC level and every observation it decides whether the image sees the vertex (in frame, facing, unoccluded along a double-precision kd-tree ray cast), where it lands in image coordinates, the incidence, emission and phase there, and every band's value, and writes vertices, per-observation samples and per-vertex layers as CSVs with a manifest (docs/Pro3DTool-SampleLayers.md)
- pro3d-tool: **`simulate-image --product` writes HyperScout and ASPECT cubes** — one 25-plane float `_Stacked.tif` for `HERA_HSH`, 37 single-band float TIFFs for `MILANI_ASPECT_NIR1`, each with statistics and one `.mbi.json` declaring every band file, so a simulated observation is read like a delivered one
- pro3d-tool: **`simulate-image --aim <body>`** turns the planned boresight onto that body's centre by the smallest rotation of the whole instrument basis, keeping the planned roll — Milani's plan leaves Dimorphos 2–3 degrees off ASPECT's boresight, at or over the frame edge. An aimed frame is not the planned observation and says so, per render and as `PRO3DAIM` in the `.mbi.json` (docs/Pro3DTool-SimulateImage.md)
- pro3d-tool: **verbs no longer hang when their GL runtime was created on another thread.** `sun-angles` deadlocked in the full test suite: creating an OPC load runner from any thread other than the one that created the GL application posts to that thread's queue and waits for a drain that never comes. The runner the host already published is reused, and a new one is created only where none exists — the command line tools, whose main thread owns GLFW
- Development: the Expecto suite creates its GL runtime on the main thread before Expecto starts, and a missing pinned SPICE metakernel now fails with setup instructions instead of an obscure error

## 6.3.2
The map projection view works for planets.

- Small bodies and planets: **the Map Projection panel is no longer restricted to small bodies.** It draws for any body the scene is referenced to; without one (a scene observed in `J2000`) it still shows a hint and creates no render control. What made a planet unusable was scale, not correctness: a Jezero OPC tile is 2.7 km across, which on Mars is 0.045 degrees, an eighth of a pixel on a whole-planet map. Three things now make it usable (#772, docs/MapProjectionView.md):
  - **data footprints**: a rectangle per surface where its data is, never smaller than 9 pixels, so sub-pixel data can be seen and aimed at. Data that fills the map, as on a small body, gets none
  - **a camera marker**: the 3D view's position as a crosshair at a constant size on screen, so the map tells you where you are looking from
  - ***Zoom to data*** and a maximum zoom of 32768 (0.6 m/pixel on Mars), where the map's float32 body positions are still worth well under a pixel
- Map projection: opening the panel reads each surface's root bounding box once instead of three times, which is noticeable in a scene with a hundred surfaces

## 6.3.1
Fixes surface transformations in Earth scenes.

- Surfaces: **on Earth, yaw now turns a surface around the local vertical.** Earth scenes are geocentric, but the transformation used the scene's x/y/z axes for Earth, so yaw rotated around Earth's spin axis (53 degrees off the vertical at 37 degrees latitude) and translation ran along the ECEF axes. Earth now uses local north/east/up like Mars and the other bodies, for surface transformations and scale bars. Earth scenes saved with a non-zero transformation load with the surface in a different place and need to be adjusted once; data that is not in Earth-centred coordinates should use the planet *None* (#791, docs/Transformations.md)

## 6.3.0
The first non-prerelease of the 6.3 line: a new window layout system, popouts, and the map projection view for small bodies. Everything from `6.3.0-prerelease001` and `-prerelease002` is included.

- UI: **the window uses Golden Layout.** Panels can be resized, moved between stacks, maximised and popped out into their own windows; closing a popout docks its panels back. The main view cannot be closed, and closed panels come back via *Layout -> Reopen Panel* (#614, based on work by Thomas Ortner in #618)
- UI: **window layouts belong to the user, not the scene.** The layout you leave is restored at the next start (`%APPDATA%/Pro3D/layouts`). Built-in layouts (M2020 is the default) and *My Layouts* (save as, load, rename, delete) are in the *Layout* menu
- Scenes: **saving a scene also stores its layout beside it** (`<scene>.pro3d.layout`), and opening a scene with a different layout asks whether to add it to *My Layouts* and whether to use it. The sidecar can never fail a save; unreadable layout files are reported and ignored
- UI: **popouts work in the installed app.** Popping out a stack opened the system browser instead of a window, and its panels vanished from the main window (pro3d-space/aardium#1). Popouts now open as windows, dock back when closed, and reopen where they were, also on a second display
- Small bodies: **a new Map Projection panel shows the body as a 2D map**, equirectangular or polar stereographic, with the surfaces, a lat/lon graticule and the scene's annotations. Drag pans, the wheel zooms, and the projection is chosen in the panel. It is part of the M2020, PRo3D Core and GIS layouts and available for Phobos, Deimos, Didymos and Dimorphos in a body-fixed scene; other bodies show a hint instead. The same view also runs standalone as `PRo3D.MapProjection.Standalone.exe` (#772, docs/MapProjectionView.md)
- Cross sections: **the curtain is visible without an image**, drawn in its Base Color; a missing image file falls back to that colour instead of hiding the curtain. The image dialog reports back correctly, cancelling keeps the current image, and a path can be typed as a fallback. Moving the camera no longer rebuilds the curtain and reloads its image (#784, docs/CrossSections.md)
- UI: the title bar credits everyone behind PRo3D: *powered by Aardvark and the PRo3D community, led by VRVis*
- Development: the Adaptify output (`*.g.fs`) is no longer checked in; `adapt.cmd` / `adapt.sh`, the build and the test scripts generate it (#774)

## 6.3.0-prerelease002
Collects the work merged since `6.3.0-prerelease001`.

- UI: **popouts work in the installed app.** Popping out a stack opened the system browser instead of a window, and its panels vanished from the main window; running from a build worked. The desktop shell treated the viewer's own address as external (pro3d-space/aardium#1). Popouts now open as windows, dock back when closed, and reopen where they were, also on a second display
- UI: the title bar credits everyone behind PRo3D: *powered by Aardvark and the PRo3D community, led by VRVis*

## 6.3.0-prerelease001
First prerelease of the 6.3.0 line, based on `6.2.0-prerelease003`.

- UI: **the window uses Golden Layout.** Panels can be resized, moved between stacks, maximised and popped out into their own windows; closing a popout docks its panels back. The main view cannot be closed, and closed panels come back via *Layout → Reopen Panel*. The top bar follows the dark panel theme (#614, based on work by Thomas Ortner in #618)
- UI: **window layouts belong to the user, not the scene.** The layout you leave is restored at the next start (`%APPDATA%/Pro3D/layouts`). Built-in layouts (M2020, now with the GIS view, is the default) and *My Layouts* (save as, load, rename, delete) are in the *Layout* menu
- Scenes: **saving a scene also stores its layout beside it** (`<scene>.pro3d.layout`), and opening a scene with a different layout asks whether to add it to *My Layouts* and whether to use it. The sidecar can never fail a save; unreadable layout files are reported and ignored
- Scenes: **the scene format is unchanged.** Scenes keep their `dockConfig`, so PRo3D 6.2 and older still open scenes saved by this version

## 6.2.0-prerelease003
Collects the work merged since `6.2.0-prerelease002`.

- Surfaces: **OPC rendering pays only for the features a surface actually uses.** The two parts that cost even while switched off — the geometry shader stage (triangle-size filtering and face normals) and the cross-section clip, the only `discard` in the stack — are now composed only for surfaces that need them, and switched in at runtime when one is enabled. The geometry stage alone dominated OPC frame time on Apple Silicon
- Surfaces: **the terrain shader is ~27× smaller and starts far faster.** Every `if … then return … else return …` in a shader stage made FShade duplicate the whole rest of the stack, so the generated code grew as the *product* of those branches: 21971 lines, with the last stage inlined 861 times. Written with a single return each, the same shader is ~800 lines with no duplication left. Start-up shader work went from minutes to ~6 s on the first start after an update, and nothing at all on every start after that (reported upstream as FShade#39)
- GIS: **the global planet and the GIS observation are one setting, the scene body.** Picking *Dimorphos* in either place, loading a kernel and adding an image is enough for projection, sun lighting, MapView, lat/lon and measurements to work — no per-surface setup. Scenes observed in a body's own fixed frame get the identity placement, so nothing is transformed twice; surfaces with no body of their own inherit the scene body. Bookmarks contribute time and camera only. *Multi-body scenes and scenes observed in another frame (e.g. `J2000`) are not covered yet (#761)*

## 6.2.0-prerelease002
Collects the work merged since `6.2.0-prerelease001`.

- GIS: **multi-image projection** — an ordered stack of up to 32 same-instrument images projected onto OPC surfaces (top wins), with a stack panel, hover footprints, fly-to and a coverage view. Each image is projected at its own observation time, and the same shader now runs on macOS
- GIS: **pre-transformed surfaces (pre-transformation, Flip Z, SketchFab) get no image projection** instead of a misplaced one; winding correction is opt-in; observation times are UTC
- Annotations: **Outcrop Traces** — the mean attitude of the annotation selection (orientation tensor), repeated at a constant bed thickness, traced where that bedding would crop out on the terrain. Dip&Strike gains a *Selection average* row
- Surfaces: **LatLon graticule overlay** — 1° / 5° / 15° parallels and meridians plus equator and prime meridian, per surface, on reference bodies. Costs nothing while switched off; meridians can break up at rover-scale zoom (#748)
- Annotations: **merge annotations moved to the main view**, and a **reverse flag** for annotation editing and picking
- View planner: the **footprint boundary is drawn again** (as in `6.1.0-prerelease005`)
- Region operations: **merging two regions no longer returns an area larger than their union**
- Development builds **report "development build"** instead of a stale version number

## 6.2.0-prerelease001
First prerelease of the 6.2 line. Collects the work merged since `6.1.0-prerelease004`.

- Tool strip: **navigation mode and the active tool moved out of the top dropdowns into a vertical icon rail** on the right edge of the render view. Icons are grouped and colour-coded (Navigation, Annotation, Selection, Placement, Reference & camera); exactly one navigation icon and one tool icon is lit at all times. The second toolbar row is labelled with the selected tool's name and carries that tool's own controls plus its CTRL-click hint. *Map view* is greyed out when no reference body is loaded, the same rule the old dropdown applied
- Navigation gizmo: the **bottom-left axis gizmo shows the three reference-system axes and its labels follow the selected reference system** — `N` / `E` / `U` on real bodies and ENU, `X` / `Y` / `Z` for `None` / `JPL` — matching the in-scene coordinate cross. Clicking a **circle** snaps the camera (no animation) to an axis-aligned view framing the multi-selected surfaces; with nothing multi-selected the circles are disabled. In MapView the vertical circles are disabled (looking along the polar axis is its gimbal-lock singularity)
- Navigation: **lock a navigation axis** by clicking a gizmo edge — navigation can then only rotate the camera about that axis. ArcBall collapses each step to a twist about the locked world axis around the orbit centre; MapView supports the vertical edge only, as a constant-latitude orbit about the body spin axis. Switching camera mode clears the lock
- GIS: **sun illumination and cast shadows for OPC surfaces** — a *Sun / Lighting Mode* dropdown (Off / `SunDirect` / `SunShadow`) shades surfaces with the real sun position for the current observation time. `SunDirect` uses Lommel-Seeliger photometry over the per-face terrain normal (physically appropriate for dark regolith; plain Lambert over-darkens the limb); `SunShadow` adds a sun-aligned shadow map. Scrubbing the observation time moves the terminator and the shadows. The mode is saved with the scene, so `PRo3D.Snapshots.exe` batch rendering uses it and a sequenced-bookmark run sweeps the sun across bookmarks. One global 4096² shadow map, OPC surfaces only — aimed at small-body scenes; `SunDirect` changes the look compared to older PRo3D versions
- Annotations: **Color by Category** — a global panel that recolours every annotation by one shared attribute instead of its own colour. The override is display-only, nothing is written, so switching it off restores the stored colours exactly. Categorical attributes (annotation type, semantic, surface) get a colour picker per category; cyclic attributes (bearing, dip / strike azimuth) a banded hue wheel; numeric attributes (slope, length, area, height, dip angle, …) the standard false-colour ramp. It can also colour by a **surface scalar (AARA) layer** sampled at the annotation's points, with a separate colour for annotations that have no value
- Annotations: **rose diagram in the Bulk Edit tab** — a 16-sector equal-area polar histogram of the selected polyline / Dip&Strike azimuths, with a circular-mean direction line, a sample count and an uncertainty indication. It is a pure function of the selection, rebuilt live as the selection or the type toggles change; degenerate (NaN) dip directions are excluded so they cannot poison the mean
- MapView: **`Planet.None` gets its own east / north / up frame** (JPL north, ENU up) instead of sharing the ENU branch, which put its north 90° out. Missing cases were added to `getReferenceSystemBasis_local`, small bodies use the body-fixed frame, and the camera up is only refreshed when the sky vector actually moved
- Reference system: the **navigation-gizmo labels, the in-scene coordinate cross and the tilt axis were brought into agreement** — a spurious northing update was removed, the west coordinate line now looks east, the tilt axis colour changed, surface reference-system placement was improved and given an on-screen hint, and the animation was removed from the coordinate-cross gizmo
- Annotations: **the Draw Annotation row hides controls that do not apply** — *Sampling* only shows for viewpoint / sky projection, *Fill / Alpha* only for fillable geometries, and the projection dropdown offers only the projections allowed for the chosen geometry (ellipses → sky only). Annotations that need a reference body are blocked when no body is selected, the Bookmark projection option was removed, "Ref. System" was renamed to "reference system", and a 4-point ellipse whose points do not project onto the body is discarded instead of crashing
- Groups: updated **active / selected group styling** in the group trees, and the fill-colour label text
- Tools: **`pro3d-tool`, a command-line companion published as a dotnet tool**, supersedes `opc-tool`. Verbs: `kdtree` (validate OPC directories and generate KdTrees), `sun-angles` (per-pixel illumination geometry as single-band float32 GeoTIFF), `unproject` (image pixel coordinates → body-fixed surface coordinates on a shape model), `simulate-image` (a simulated instrument image of a body at a SPICE time — Lommel-Seeliger sun lighting, procedural micro-structure, cast shadows, optional de-shaded texture albedo). Nothing in the tool requires the viewer to be installed
- MapView: WASD **invert toggle moved to the toolbar**, plus a MapView controller fix

## 6.1.0-prerelease005
- View planner: the **footprint boundary is drawn again** — creating a view plan placed the rover, let you pick an instrument and showed the instrument view, but no outline of the instrument image ever appeared on the surface. The matrix that projects each OPC patch into the instrument image stopped being computed when the per-patch OPC uniforms were generalised, so every patch was projected with the identity matrix and fell outside the image entirely. Reported as #733, also fixed in 6.0.1
- Builds that did not come from the release pipeline **no longer report a stale version number** — a locally built viewer's title bar read `5.4.0`, several minor lines behind the source it was built from, which is how #733 arrived quoting the wrong version. Such builds now say `development build`; published builds carry the real version, as before

## 6.1.0-prerelease004
- Annotations: **one *Export…* window replaces the eleven fixed export commands** — file type (CSV table, GeoJSON, attitude planes, continuous GeoJSON), scope, one record per annotation vs. per point, coordinate kind and the exported attributes are composed per export instead of being baked into a menu entry. Presets pre-fill the window for the usual cases (GIS/QGIS, annotation table, profile, attitude planes) and stay editable afterwards; per-point exports can additionally sample the OPC surface layers. Saving as native `.pro3d.ann` is unchanged and stays on the menu
- Annotations: geographic export now goes through the **convention-aware coordinate transform** — planetographic, spherical or ellipsoidal per body. The QGIS exporter bypassed it and wrote the string `"Error: No / invalid reference frame set"` inside coordinate arrays for bodies such as Dimorphos; points that cannot be converted now come out as empty cells (CSV) or a `null` geometry (GeoJSON)
- Annotations: each record records **which routine produced its lat/lon/alt** — SPICE, naming the routine, or the surface's per-vertex `.aara` data. The sources disagree about what the third value is (a height above the spheroid, or a distance from the body centre), so `alt` could not be interpreted from the file alone before
- Annotations: **longitude convention and range are explicit settings** — mirroring, a 180° prime-meridian shift, and `-180…180` vs. `0…360` notation, applied to every file type. The old exporters each picked something different without saying so
- Annotations: the window **no longer writes silently wrong or empty files** — a scope matching nothing, a failed write, or geographic coordinates in a scene with no reference frame keep it open with a warning naming what to change
- Annotations: records are written in **group-tree order**, the order shown in the annotation list; the old exports iterated a hash map, so row order varied between runs. *Selected only* now covers the multi-selection as well, which the old profile exports dropped

## 6.1.0-prerelease003
- Surfaces: **numeric controls work again** — Blend Factor, Min, Max and Color Map never appeared in surface properties, and the controls that did render were inert. `NoSemUi` kept its own semantic-ui dependency list pointing at the pre-5.7 resource paths, so all four URLs 404'd; one of them, `essentialstuff.js`, defines the jQuery `numeric` plugin the controls boot with, and its absence aborted the rest of the panel's DOM setup. The GIS entity numerics were broken the same way. Present in shipped 6.0.0

## 6.1.0-prerelease002
- Annotations: the **4-point ellipse is drawn as an ellipse again** — the four clicks were kept as raw points and rendered as an open polyline, because the ellipse fit on the dip/strike plane only handled the 3-point case. A four-point ellipse is now built as two half-ellipses that share the major axis and differ only in the semi-minor axis
- Annotations: the **4-point ellipse is hidden from the geometry selector** for now. The geometry and its update/rendering path are unchanged, so existing annotations still load and draw
- Under Cursor: the read-out adds the picked point's **latitude, longitude and altitude** next to its cartesian position, in the same convention as the Coordinate System panel. The rows are omitted when the conversion is unavailable — a non-planetary reference frame, or a SPICE call that fails

## 6.1.0-prerelease001
- Annotations: **boolean operations** — union two or more selected annotations into one, or cut the selected annotation along a polyline stroke drawn on the terrain. The stroke colours green or red as a live answer to "would this cut". A union of disjoint operands explodes into one annotation per component, a union that would enclose a hole is refused rather than silently dropping it, and both operations undo in a single step
- Annotations: **move control points on the surface** — drag an annotation's vertices to new terrain positions in the new edit mode; Escape puts a grabbed point back, and moving a vertex onto a different surface is reported on screen

## 6.0.0
First stable release of the 6.0 line. The individual changes since 5.x are listed in the `6.0.0-prerelease*` and `6.0.0-rc*` entries below.

- Sequenced bookmarks: **playback keeps the camera pointing where the bookmarks point** — moving between two bookmarks that look the same way lost the view direction and swung the camera up into the sky for the whole segment. Stepping to a bookmark, and batch-rendered snapshots, were unaffected

## 6.0.0-rc2
- Snapshots: **batch-rendered images are no longer black** — sequenced-bookmark animations, panorama collections and command-line snapshot rendering all wrote empty frames, because the offscreen framebuffer the images were rendered into did not match the one the scene was prepared for

## 6.0.0-rc1
- Screenshots: **screenshots are written again** — this completes the fix started in prerelease9, which got the rendering statistics parsing right but left the image download failing. Taking a screenshot blocked the update thread while the render service was waiting for that very thread to release the scene, so the request timed out after 100 seconds and PRo3D froze meanwhile

## 6.0.0-prerelease9
- Surfaces: fixed **holes across OPC surfaces on Apple Silicon Macs** — cross-section clipping ran on every scene even with no cross-section defined, discarding fragments based on a per-vertex attribute that was never filled; Windows and Linux were unaffected
- Reference system: the **coordinate cross no longer vanishes** on click or scroll — refreshing up/north at the camera position also moved the cross's origin onto the camera
- Screenshots: fixed **screenshots failing** with a deserialization error on the `/rendering/stats.json` body
- Annotations: **lat/lon/alt columns** in the CSV export, and the annotation list shows the annotation text instead of its geometry type
- Groups: fixed group activation

## 6.0.0-prerelease8
- Groups: the **active group is visible again** in the surface, annotation, bookmark and GIS trees — the filled/outline circle indicator broke with the Aardvark.Media 5.7 icon set, and the active group name is now highlighted; folder icons are no longer black on the dark panels
- Surfaces: the **DistanceFilter** now explains itself — it only takes effect once a home position is set, so the (previously unlabeled) *Home Position* button moved next to the filter, shows `set`/`not set`, and both rows have tooltips
- Traverses: **sol label size** uses the same screen-size convention as annotation and scale-bar labels, in both the fast and the stable text mode; sizes stored in existing scenes are converted on load, so labels keep their size

## 6.0.0-prerelease7
- Snapshots: fixed a regression that prevented **bookmark / panorama snapshot animations from loading** in `PRo3D.Snapshots.exe` ("Could not read json File") — a required-vs-optional JSON read introduced by an earlier untested merge
- Snapshots: `PRo3D.Snapshots.exe` (the sequenced-bookmark / batch renderer) is now **bundled into the installers** alongside `PRo3D.Viewer`, not only in the standalone zip

## 6.0.0-prerelease6
- Navigation: the camera-mode dropdown now shows **MapView** even when no reference body is loaded — greyed out with a "needs a planet / reference body" note — instead of hiding the option entirely
- Release: restored the non-electron Windows standalone build (`PRo3D.Viewer-<version>-win-x64-standalone.zip`) attached to the draft release alongside the installers

## 6.0.0-prerelease5
- macOS: fixed PRo3D failing to start on Apple Silicon (and Intel) Macs — a stale x86_64 `libCooTransformation` bundled with the instrument-platform wrappers shadowed the correct-architecture SPICE native; removed it so SPICE/CooTransformation initializes on all platforms
- Profiles: multi-attribute profile data extraction and export
- MapView overview camera controller; small-body camera fixes (sky vector, pole guard)
- SBMT annotation import (points + ellipses, chunked UI groups)
- Instrument image projection testbed and fixes: SPICE kernel loading no longer depends on the process working directory; per-hierarchy OPC face-normal winding; `getRotationTrafo` returns honest `None` on failure; mbi-quaternion attitude without a spacecraft CK
- Per-computer user preferences (MapView WASD invert)

## 6.0.0-prerelease4-media57
- Updated to Aardvark.Media 5.7
- Switched from Suave to Giraffe
- Implemented report dialogs

## 6.0.0-prerelease4
- Drawing: added undo/redo support for drawing/annotation edits, including a fix for scenes created before this feature
- Groups: annotations now default to their group's color; removed the redundant top-menu annotation color picker
- Cross sections: added a file dialog for choosing a cross-section image
- RIMFAX: reworked surface selection visualization and added priority rendering so RIMFAX and rover traverse render correctly relative to each other; white-band discard for RIMFAX imagery moved to the traverse section
- Fixed the DnS (Drift and Stare) plane, which had stopped rendering

## 6.0.0-prerelease3
- Snapshots: fixed "could not find PRo3D.Snapshots.exe" — the snapshot/panorama renderer is now resolved beside the running executable and launched with that working directory (previously a relative path failed because SPICE init leaves the process working directory in the kernel config folder)

## 6.0.0-prerelease2
- Dashboards: default to the M2020 docking layout (includes the Traverse panel) and added "M2020" to the Change Mode menu so it can be selected

## 6.0.0-prerelease1
- Apple Silicon: native macOS arm64 builds — PRo3D now runs natively on M-series Macs (no Rosetta), alongside Intel macOS, Windows and Linux
- SPICE: updated to PRo3D.SPICE 1.0.9 with native CooTransformation/cspice libraries for macOS arm64
- Build/release: unified the GitHub draft release so the standalone zip and the installers share one draft, with consistent v-prefixed tags and commit provenance

## 5.9.0-prerelease1
- **Cross sections (experimental preview).** Place a cross section from a line annotation and render an extruded "curtain" along it. This is a preview feature; its model and UI may still change.
  - curtain: surface-relative texture mapping, smoother cross-section clip edge, adjustable base colour, and a button to remove a placed cross section
- Traverse: added a "Fast Text" toggle for sol-number labels — fast batched billboards (default) or numerically stable per-label rendering that does not jitter at distance
- Surface distance filter: fixed — it previously clipped the entire surface and ignored the distance value; now works correctly in stable view space
- Sequenced bookmarks: fixed a crash (NullReferenceException) when playing a sequence containing bookmarks with identical / coincident camera positions
- Camera & picking: fixed surface picks and placements firing while navigating — e.g. placing a coordinate system and then orbiting no longer re-places it
- OBJ surfaces: added a white-pixel discard render stage (with GUI checkbox) and a threshold modifier in surface properties
- removed debug logging that spammed the console during animation

## 5.6.0-prerelease2
- added cross sections
## 5.7.0-prerelease1
- MapView camera controller (overview mode)
- Profile data extraction / multi-attribute export
- MapView rotate fix under Ctrl modifier


## 5.5.0
- triangle Filter for Scene Objects (and Checkbox for de-activation added)
- Preview Cursor
- Recalculation of obj-KDTrees fixed
- Automatic recalculation of Orbit Center when change to ArcBall (to surface in camera center with fallback)
- Ellipse Tool with second kind of AxisEllipses (assymetric with 4Point Selection)


## 5.4.0
- planets updates for local resSys in transforamtions

## 5.3.0-prerelease1
- axis-ellipses added 
- area calculation for closed polygons and ellipses included
- surface intersection preview added

## 5.2.0-prerelease-hera-3
- Linux support   

## 5.2.0-prerelease-hera-2
- ellipse drawing

## 5.2.0-prerelease1            
- reworked trafo system

## 5.1.2           
- fixed legacy traverse scene file / m20 waypoint loading

## 5.1.1            
- added phobos/deimos/moon options in planet selection combo box.

## 5.1.1-prerelease-hera-8  
- ellipse drawing

## 5.1.1-prerelease-hera-7  
- ellipse drawing

## 5.1.0
- native dependency fix for mac

## 5.1.0-prerelease3
- native dependency fix for mac

## 5.1.0-prerelease2
- native dependency fix for mac

## 5.1.0-prerelease1
- native dependency fix for mac

## 5.0.7-prerelease1
- #516
- #525

## 5.0.7
- ViewPlan: lookAt function for distance points, changes pan and tilt
- Trafos: export/import trafos for specific surface

## 5.0.6
- ViewPlan: lookAt function for distance points, changes pan and tilt
- Trafos: export/import trafos for specific surface

## 5.0.5
- first official 5.0 release

## 5.0.4
- updated PRo3d.SPICE/fixed osx cootrafo build (again)

## 5.0.3
- updated PRo3d.SPICE/fixed osx cootrafo build (again)

## 5.0.2 
- updated PRo3d.SPICE/fixed osx cootrafo build

## 5.0.0 
- next major release preparation

## 4.27.0-prerelease2
- tweaked opc parameters
- 
## 4.27.0-prerelease1
- tweaked opc parameters
- 

## 5.0.0-prerelease-hera-7
- product style release for hera deliverable

## 4.26.0-prerelease1
- fixed surface priority
- improved handling of large trajectories
 
## 4.25.0-prerelease7
- distance- and trianglefilter fix
- fixed problem with sequenced bookmarks performance problem when a traverse is loaded
- fixed obj not being rendered with batch rendering

## 4.24.0
- fixed obj not being rendered with batch rendering

## 4.21.0-prerelease3
- bugfix contour lines

## 4.21.0-prerelease2
- Bugfix Entity creation
- Added new default Entities
- Spice Kernel is now loaded when a scene is loaded and a spice kernel is defined in GisApp

## 4.21.0-prerelease1
- added readme to opc-tool

## 4.24.0
- streamlined up kdtree loading

## 4.23.2   
- fixed sequenced bookmark loading in cross-platform scenarios https://github.com/pro3d-space/PRo3D/pull/391

## 4.23.1
- tweaked kdtree split limit epsilon for smaller kdtrees

## 4.23.0
- tweaked kdtree split limit epsilon for smaller kdtrees


## 4.22.0
- further improved kdtree loading on NTFS/macbook

## 4.21.0-prerelease3
- further improved kdtree loading on NTFS/macbook

## 4.21.0-prerelease2
- fixed kdtree loading on NTFS/mac
 
## 4.21.0-prerelease1
- added support to re-create kdtrees 
 
## 4.20.2
- opc tool now supports "ignoreMasterKdTree" option which can be used to force leaf kdtree construction

## 4.20.1
- added readme to opc-tool

## 4.20.0
- added opc tool

## 4.2.0-prerelease1
- added rake to annotations

## 4.20.0-prerelease1
- provex and multitexturing

## 4.12.0-prerelease10
- new trafo version
- distance filter for surface
- increased minimum value for depth image colors 

## 4.12.0-prerelease9
- bugfix sequenced bookmarks paths

## 4.12.0-prerelease8
- various fixes for focal length, batch rendering
- radiometry calculation changed

## 4.12.0-prerelease7
- #261 Zooming enhancements
- #167 radiometry

## 4.12.0-prerelease6
- show depth (+ gui and legend) in instrument view
- #234, snapshots: --renderDepth writes a depth image as tiff
- bugfix: #329 obj without textures import
- bugfix: #157 planet reset stopped
- bugfix: #324 traverse updates
- #314: surface transformation with pivot revised
- bugfix for "save footprint"

## 4.12.0-prerelease5
- opc rendering now works on linux

## 4.12.0-prerelease4
- opc rendering now works on linux

## 4.12.0-prerelease3
- workaround for case-sensitivity problem in isOpc: https://github.com/pro3d-space/PRo3D/issues/280

## 4.12.0-prerelease2
- testing linux deployment

## 4.12.0-prerelease1
- testing linux deployment

## 4.11.1
- #110, #126, #138, #144, #145, #166, #212, #231, #259,
- Transformations, hide exploration center, visibility of annotations- and scalebar text

## 4.11.0-prerelease4
- NewScene crash fixed: https://github.com/pro3d-space/PRo3D/issues/277

## 4.11.0-prerelease3
- #274: objs with multiple geometries fixed

## 4.10.3
- #179 and #246: serialization of viewplans
- #256: load scenes with wrong obj path and possibility to reload the obj

## 4.10.2
- build kdtrees for objs from faces of triangulated mesh copy for #264
- bugfix for large coordinates 
- load obj without textures; show vertex colors instead

## 4.10.1
- making projection measurments fit for profile extractions #247 containing to following features
  - exported projection measurements contain all sampling points
  - when creating a projection measurement users can control the sampling rate #203
  - the selected annotation can be exported as csv in the for of absolute elevation over distance #221
- also supports MSL traverse ingestion

## 4.10.0
- added configurable sampling scheme to measurement projections (viewpoint, sky) as requested in #203

## 4.9.7
- bugfix: near/farplane not set correctly in batch rendering [#241](https://github.com/pro3d-space/PRo3D/issues/241)

## 4.9.5
- fixed kdtree paths on osx
- switched to dotnet6 

## 4.9.4-prerelease3
- testing autodeploy

## 4.9.2-prerelease5
- bugfix: frustum now set correctly when batch rendering


## 4.9.1-prerelease3
- bugfix: frustum for batch rendering
- removed recording of animation, replaced with saving batch file directly (no looping, easing, splines, global animation for batch rendering)
- now allowing saving and restoring scene state for sequenced bookmark animations
- scene states for animation are now identified with data and time and listed under properties of sequenced bookmarks
- added traverses to scene state
- new import for objs with large coordinates

## 4.9.1-prerelease2
 
- bugfix: reading scene with sequenced bookmarks could lead to an error
- bugfix: scale bars not updated correctly when updating scene state for sequenced bookmarks

## 4.9.1-prerelease1

- sequenced bookmarks can now store scene state
- new animation features (easing, smooth path, looping, scene state is applied according to bookmarks)
- batch rendering can now use sequenced bookmarks and scene state
- anti-alisaing for batch rendering
- bugfix: wrong path opening when clicking on batch rendering output path

## 4.9.0-prerelease1

- geoJSON exports now contain sampled points of visible annotations [#217](https://github.com/pro3d-space/PRo3D/issues/217)

## 4.8.2-prerelease1
 
- electron build test   
   
## 4.7.0-prerelease1  

- added Continuous Export of Dip & Strike [#185](https://github.com/pro3d-space/PRo3D/issues/185)
- added Custom Background Color for Screenshots [#183](https://github.com/pro3d-space/PRo3D/issues/183)
- Integration of Mars2020 rover traverse [#127](https://github.com/pro3d-space/PRo3D/issues/127) also including custom sized waypoint labels [#154](https://github.com/pro3d-space/PRo3D/issues/154)

## 4.6.2-prerelease1

- adapted GeoJSON parser to read numeric values
- added file dialog to import traverses in the form of the specified GeoJSON M20_waypoints.json
- added datamodel and GUI to maintain multiple traverses
- added adjustable textsize for waypoints

## 4.6.1-prerelease2

- base 5.2 upgrade

## 4.6.1-prerelease1

- added viewplanner placement for traverse waypoints
- changed text positions for all annotations to center of the object
- fixed instrument view text scaling bug
- added waypoints file to resources

## 4.6.0-prerelease1

- revived viewplanner and footprint projection
- fixed triangle filter (was in projective space)
- added sequenced bookmarks (from other branch)

## 4.5.0-prerelease1

- added traverse loading and visualization (dots, text, lines)
- added local reference frames according to rover poses
- added flyto animation according to rover poses
- added list gui for sols and traverse visibility flags

## 4.4.4-prerelease1

- added csv export for vertical thickness computation
- added csv export of angular error values for dns computation

## 4.4.3-prerelease1

- added vertical thickness computation for TT (True Thickness) annotation tool

## 4.4.2-prerelease1

- added recalculation of all angular values dependent on north and up (dip and strike angle and azimuth, bearing, slope)

## 4.4.1-prerelease1

- fixed "box sequence must not be empty" exception when loading a scene with surfaces that have faulty paths

## 4.4.0-prerelease1

- added dip azimuth to true thickness tool [#17](https://github.com/pro3d-space/PRo3D/issues/17)
  - improved true thickness computation via point over plane height
- merged xzy coordinate system and renamed it to ENU (East North Up) [#117](https://github.com/pro3d-space/PRo3D/issues/117)
- added missing calculation numbers of measurements to csv export (slope, bearing, vertical distance, horizontal distance) [#100](https://github.com/pro3d-space/PRo3D/issues/100)
- added `showText`flag to annotations to show or hide text [#114](https://github.com/pro3d-space/PRo3D/issues/114)

## 4.3.0-prerelease1

- added xzy coordinate system and `sketchfab` transformation to support models created out of agisoft

## 4.2.0-prerelease1

- static screen-shot service that can be found in the `config` tab
- coordinate systems are inferred automatically mostly to distinguish between elipsoid (Mars, Earth) and Euclidean (None, JPL) / Rover Frame Systems

## 4.1.0-prerelease2

- fixed broken priority rendering

## 4.1.0-prerelease1

- using methods as described in `Quinn, D. P., & Ehlmann, B. L. (2019). A PCA‐based framework for determining remotely sensed geological surface orientations and their statistical quality. Earth and Space Science, 6(8), 1378-1408.`
  - using new method for plane fitting
  - added angular error measures
  - added versioned serialization and deserialzation
  - added export of dip and strike annotations as json for attitude integration (annotions>export>attitude planes (*.json))

## 4.0.3

* fixed arcball crash
* macbook pro amd graphics support
* removed automatic recent loading

## 4.0.1

- fixed "flyto animation does not reach destination" issue
- fixed issue with obj annotation when using flipZ https://github.com/pro3d-space/PRo3D/issues/98

## 4.0.0

contains features and fixes from all prereleases since 3.3.1

- features
  - Scalebar integration https://github.com/vrvis/PRo3D/issues/10
  - Import and Visualization of "SceneObjects" https://github.com/vrvis/PRo3D/issues/13
  - Geologic Surface Creation https://github.com/vrvis/PRo3D/issues/19
  - Mastcam-Z Improvements https://github.com/vrvis/PRo3D/issues/53
  - Adjust Focal Length https://github.com/vrvis/PRo3D/issues/54
  - Flip Z direction for surfaces and scene objects
  - Improved annotations performance https://github.com/vrvis/PRo3D/issues/60
  - new color correction UI + ordering of operations https://github.com/vrvis/PRo3D/issues/52
  - annotation picking
    - added picking tolerance in meters for annotation picking to viewconfig to address picking problems at orbital scale.
    - this tolerance does not affect the accuracy of the line picking itself
    - it rather affects performance: having a large tolerance for small scale scenes may result in unnecessary intersections tests
    - picking tolerance is serialized with view config (now version 2), older versions will be intialized with 0.01m
    - numeric control for picking tolerance directly next to interaction dropdown with text "eps.:"
    - removed unnecessary surface intersection computation when trying to pick annotations
    - added pixel-based real-time highlighting when `PickAnnotation` is active
  - annotation properties
    - added properties tab to docking GUI
    - currently only shows annotation properties when an annotation is selected
    - this will not show in already existing scenes. to reset the docking GUI and make properties visible press <kbd>F8</kbd> and save your scene afterwards.
  - geoJson export
    - added geoJSON export via menu > annotations > export xyz (*.json)
    - added geoJSON export via menu > annotations > export (*.json)
    - data is exported as `geometryCollection` of `geometry` objects with 3D coordinates `(lon, lat, alt)` computed via SPICE
  - coordinate prints
    - added print location / coordinate for point annotation
    - added print location / coordinate for bookmark location
    - added long lat alt prints via cootrafo
  - mac support (preliminary, unofficial, testing only)
- fixes
  - fixed screen space scaling of annotation spheres
  - changed lod metric to omit aggressive culling
  - added missing c libs on windows
  - updated CooTrafo build to release - should fix missing ucrtbase
  - fixed crash with goto animation and arc ball controller
  - fixed UNC aardium problem
  - current directory now set to main entry point location https://github.com/vrvis/PRo3D/issues/63, https://github.com/vrvis/PRo3D/issues/62
  - aardium path fix
  - Update navigationMode and exploreCenter in SaveScene and LoadScene
  - Import obj
  - LatLonAlt output sequence changed
  - isSurfaceFolder is always false for objs (in Surfaces) so the red exclamation icon in the little surface menu is shown  
  - fixed picking issues
  - tried to fix color picker history https://github.com/vrvis/PRo3D/issues/56
  - fixed dns colors
  - performance improvements for dns annotations
  - fixed multiselect
  - fixed duplication bug when moving multiple annotations via "select all" in a group mechanic
  - "select all" only selects all leaves in a group not including the leaves of sub groups
  - fixed problem with picking points on surface

## 4.0.0-prerelease6

- updated CooTrafo build to release - should fix missing ucrtbase

## 4.0.0-prerelease5

## 4.0.0-prerelease4

- fixed crash with goto animation and arc ball controller
- fixed screen space scaling of annotation spheres
- changed lod metric to omit aggressive culling
- added missing c libs on windows
- mac support (unofficial, testing only)

## 4.0.0-prerelease3

## 4.0.0-prerelease2

## 4.0.0-prerelease1

## 3.8.0-prerelease3

- fixed UNC aardium problem

## 3.8.0-prerelease3

- current directory now set to main entry point location https://github.com/vrvis/PRo3D/issues/63, https://github.com/vrvis/PRo3D/issues/62

## 3.8.0-prerelease2

- aardium path fix

## 3.8.0-prerelease1

- features:
  - Scalebar integration https://github.com/vrvis/PRo3D/issues/10
  - Import and Visualization of "SceneObjects" https://github.com/vrvis/PRo3D/issues/13
  - Geologic Surface Creation https://github.com/vrvis/PRo3D/issues/19
  - Mastcam-Z Improvements https://github.com/vrvis/PRo3D/issues/53
  - Adjust Focal Length https://github.com/vrvis/PRo3D/issues/54
  - Flip Z direction for surfaces and scene objects
  - Super slow annotations https://github.com/vrvis/PRo3D/issues/60
- bugfixes:
  - Update navigationMode and exploreCenter in SaveScene and LoadScene
  - Import obj
  - LatLonAlt output sequence changed
  - isSurfaceFolder is always false for objs (in Surfaces) so the red exclamation icon in the little surface menu is shown

## 3.7.0-prerelease6

- fixed picking issues
- tried to fix color picker history https://github.com/vrvis/PRo3D/issues/56

## 3.7.0-prerelease5

- new color correction UI + ordering of operations https://github.com/vrvis/PRo3D/issues/52
- fixed dns colors

## 3.7.0-prerelease4

- new color correction UI + ordering of operations https://github.com/vrvis/PRo3D/issues/52
- fixed dns colors

## 3.7.0-prerelease3

- added geoJSON export via menu > annotations > export xyz (*.json)

## 3.7.0-prerelease2

- performance improvements for dns annotations
- fixed multiselect

## 3.7.0-prerelease1

- added geoJSON export via menu > annotations > export (*.json)
- data is exported as `geometryCollection` of `geometry` objects with 3D coordinates `(lon, lat, alt)` computed via SPICE

## 3.6.1-prerelease1

- performance improvements take I

## 3.6.0-prerelease1

- fixed duplication bug when moving multiple annotations via "select all" in a group mechanic
- "select all" only selects all leaves in a group not including the leaves of sub groups

## 3.5.1-prerelease1

- added print location / coordinate for point annotation
- added print location / coordinate for bookmark location
- added long lat alt prints via cootrafo
## 3.4.1-prerelease2

- fixed problem with picking points on surface

## 3.4.1-prerelease1

- added properties tab to docking GUI
- currently only shows annotation properties when an annotation is selected
- this will not show in already existing scenes. to reset the docking GUI and make properties visible press F8 and save your scene afterwards.

## 3.4.0-prerelease2

* added picking tolerance in meters for annotation picking to viewconfig to address picking problems at orbital scale.
  * this tolerance does not affect the accuracy of the line picking itself
  * it rather affects performance: having a large tolerance for small scale scenes may result in unnecessary intersections tests
* picking tolerance is serialized with view config (now version 2), older versions will be intialized with 0.01m
* numeric control for picking tolerance directly next to interaction dropdown with text "eps.:"
* removed unnecessary surface intersection computation when trying to pick annotations

## 3.3.1

* csv export contains visible annotations only
* also added manualDipAngle and trueThickness result to export (NaN of not applicable to annotation)
* fixed error in AnnotationResultsSerialization
* single select annotations / multi select with ctrl + shift
* reactivated tooltips by default, -notooltip in command line supresses all tooltip displays
* fixed annotation flyto (could not reproduce anymore)
* added true thickness measurement tool TT to DrawAnnotation Geometry
* added units to measurement and dip and strike results
* removed positions from measurement properties
* changed direction of vertical distance computation

(contains changes from 3.1.4 prereleases)

## 3.2.1-prerelease

* test prerelease


## 3.2.0
* new deploy system 

