# Geological map — plan (#843)

Status: 2026-10-13. Branch `features/843_geological-map`, stacked on #841/#842 (fills
against the surface depth), which this builds on.

| step | state |
|---|---|
| topology core (phase 2) | done: `ContactTopology` |
| step 1 - see a map (phase 1 skeleton + phase 3 rendering) | done: model, persistence, panel, shell volumes (`MapShell`), one-draw rendering, assign via active unit / selected points, showcase (`--make-geomap-showcase`, `tests-ui/tests/geological-map.spec.ts`) |
| step 2 - Assign-unit tool (colour table, face ids in the pick target, click/paint/eyedropper, hatching, unit codes) | open |
| step 3 - Draw-contact tool (phase 4, incl. incremental updates) | open |
| step 4 - GeoPackage export (phase 5) | open |

Learned on real data (Dimorphos showcase):

- **Noding must be radial.** Two contacts draped on rough terrain cross on the surface, but
  their chords pass each other at different heights - tens of centimetres, above any sensible
  3D tolerance. Crossings are now found seen from the body centre (`radialCrossing`); the 3D
  tolerance only joins near misses (ends stopping just short).
- **Locating on a sparse map** searched empty grid cells one by one (300 ms for 1700 samples);
  the grid now walks its occupied cells when a query box exceeds them (30 ms).
- **Group keys are renewed on every annotation load**, so maps remap their `sourceGroup` there.
- Faces are triangulated in a stereographic chart with the pole placed outside the face (as
  far from its boundary as candidate directions allow), so bands around the body and faces
  larger than a hemisphere need no special case. Volumes are radial shells between an inner and
  an outer radius from the contacts' radii plus the fill reach.

## Goal

Geological maps in PRo3D the way geologists build them in GIS with the *contacts and points*
method (QGIS plugin Mappy, PLANMAP/GMAP teaching): the mapper draws **contacts** (lines) and
assigns **units**; the units (polygons with holes) are *derived*. Refining a map means drawing
another contact across a unit - every shared boundary is traced once, and a changed contact
updates the units on both its sides.

First target: a **whole-body** map of Dimorphos. The same code serves Mars regions.

**The map is immutable.** Units are never edited directly - not their geometry, not by
splitting or merging them. All changes go through contacts and unit assignments. (Mappy works
the same way: its *Recompute* overwrites the polygon layer. Plain QGIS edits polygons directly
and needs topological editing, snapping and a topology checker to keep shared boundaries
consistent - exactly the work this model avoids.)

Interaction must be **snappy**: every routine action (assign a unit, finish a contact,
hover) answers within a frame or two.

## Workflow

1. **New map** in the *Geological maps* panel: it creates its source group in the annotation
   tree (marked with a map badge) and becomes the **active** map.
2. **Draw contacts** with the *Draw contact* tool. Ends snap onto existing contacts; while
   drawing, the unit about to be split is outlined and the cut previewed; Enter commits and
   the map updates right away.
3. **Assign units** with the *Assign unit* tool: pick the active unit in the palette (keys
   1-9, type to search, or "new unit"), click a unit on the terrain, or drag across several to
   paint them. Alt+click picks the unit under the cursor.
4. Work through **problems**: unlabelled units are hatched, open contact ends marked; the
   panel lists them, `N` flies to the next one.
5. **Export** to GeoPackage.

Several maps per scene (e.g. two interpretations, units vs. morphology); one is active for
the tools, the others can be shown or hidden.

## Data model

| what | where | persisted |
|---|---|---|
| contacts | polyline annotations in the map's source group (ordinary annotations; the contact tool creates them, any polyline in the group counts) | yes, as today |
| unit assignments | point annotations in the source group with a new field `geoUnit : Option<string>` - created and moved by the *Assign unit* tool, hidden in 3D (label points) | yes, new key `geoUnit` (`Json.tryRead`, default `None`) |
| `GeologicalMap` | scene object: `key`, `name`, `sourceGroup : Guid` (group `Node.key`), `units`, `visible`, `opacity` | yes, new scene key; the scene holds a list of maps plus `activeMap` |
| units (the palette) | `IndexList<Unit>`, `Unit = { code; name; color; description }` - created on the fly while mapping, colours suggested from a qualitative palette | yes, inside the map |
| derived units | faces, rings, volumes, unit ids, diagnostics | **no** - recomputed on load and on every change |

Scene compatibility: new data only under new keys; older releases load the scene and ignore
the maps; contacts and label points stay ordinary annotations there.

## Interaction

### Draw contact (tool strip, active while a map is active)
- Polyline, Sky projection on bodies, clicks on the terrain as with other annotations.
- **Snapping:** a first/last click within ~10 px (screen space) of an existing contact snaps
  exactly onto it - the contact's sampled line is projected to screen and the nearest point
  taken, so the end lies on the contact, not near it. Otherwise the end stays open (and is
  marked as such).
- **Split preview:** while drawing, the stroke so far is noded against the map and the unit(s)
  it would split are outlined, the cut shown - from the same topology code, on the
  uncommitted stroke.
- Enter commits; Backspace removes the last point; Esc discards.
- The contact goes into the active map's group automatically.

### Assign unit (tool strip)
- **Palette** in the map panel: units with colour swatches; keys 1-9 select, typing filters,
  "new unit" creates one (code, name, colour suggested). The active unit is shown at the
  cursor.
- **Click** a unit on the terrain: it takes the active unit. Under the hood the label point of
  that unit is created or moved to the click position (one undo step).
- **Drag-paint:** every unit the cursor passes while the button is held takes the active unit.
- **Alt+click:** eyedropper - the unit under the cursor becomes active.
- **Del / right-click:** remove the assignment (label point deleted; unit becomes unlabelled).
- **Hover:** the unit under the cursor is highlighted, its name shown at the cursor.

### Panel (*Geological maps*, docking tab)
- Map list: name, visibility, active map, opacity slider.
- Palette / legend: units with colour, name, area, visibility per unit; rename/recolour.
- Problems: counts and lists of unlabelled units, conflicting labels, open contact ends;
  clicking an entry flies there; `N` jumps to the next problem.
- Export button.

### Display
- Units: semi-transparent fills (default ~50 %), one opacity per map, terrain shading stays
  readable; no unit outlines - the contacts are the lines.
- Unit codes as text at each unit's interior (toggle); label points themselves hidden
  (a debug toggle shows them).
- Contacts drawn on top as ordinary polylines.

## Why this can be snappy

- **Assigning a unit changes no geometry.** Faces are fixed by the contacts; an assignment
  only changes which unit a face belongs to. Fills read their colour from a **unit-id →
  colour table** (a small buffer/texture); assigning updates one entry. No re-triangulation,
  no new volumes.
- **Click → unit without a KdTree ray.** The fill pass writes the face id into the existing
  annotation pick target; click and hover read it from the pixel under the cursor
  (microseconds). The click's terrain position (for the label point) comes from the same
  target's depth. The first click on a surface today waits for its KdTree to load (seconds) -
  this path never does.
- **Contacts update incrementally.** A committed contact is noded only against the existing
  contacts near it (spatial hash); face tracing over the half-edge structure is linear and
  cheap even for whole-body maps; only the faces whose boundary changed get new volumes.
  Faces keep their units through the label points, so colours stay put.
- **Rendering at scale:** all units in one draw (#842: 0.12 ms for 10k fills).

## Algorithm: faces on the surface from local questions only

No global chart. Every step asks a question that is local to one place on the surface and is
answered in 3D or in the tangent plane at that place. This works for the whole body, has no
pole or date-line cases, and does not need the body to be star-shaped.

Input: the contacts' **sampled** polylines (the terrain-draped points the outline is drawn
with, `getPolylinePointsAt`), not just their control points.

1. **Segments.** All contact samples as 3D segments, each tagged with its contact and sample
   index (provenance, for snapping, diagnostics and incremental updates).
2. **Noding.** Candidate pairs from a spatial hash over segment bounding boxes. For a pair:
   closest points of the two segments; they cross if the distance is below a tolerance (a
   fraction of the local sampling spacing - draped polylines of two contacts never hit each
   other exactly) and both parameters lie inside. Split both at the crossing; the node is the
   midpoint of the closest points. Snapped ends (above) are exact by construction.
3. **Graph.** Nodes and edges into a half-edge structure (DCEL). Iteratively remove
   degree-1 nodes and their edges: **open ends / dangles** (kept for diagnostics).
4. **Angular order at a node.** The outgoing edges' first directions, projected into the
   node's tangent plane, sorted by angle. Tangent plane normal: the surface normal at the node
   (from the samples / KdTree; on star-shaped bodies the radial direction is a fine stand-in).
5. **Face tracing.** For half-edge `h` ending in node `v`, `next(h)` is the half-edge leaving
   `v` right after `twin(h)` in the angular order. Following `next` yields the boundary cycles.
6. **Cycles to faces.** A face can have several boundary cycles when the contact graph is
   disconnected (a closed contact loop inside a unit = a hole). Place each connected component
   into the face of the others that contains it with the label test below. Sanity check per
   connected graph: V − E + F = 2 (Euler, sphere-like body).
7. **Labels.** For each label point: nearest contact edge (spatial hash), side of that edge in
   the tangent plane at the nearest point → the half-edge on that side → its face.
   (Interactive assignment does not need this - it gets the face id from the pick - but
   loading a scene and moving contacts do.)

Pure code (PRo3D.Base: no rendering, no model), unit-tested on synthetic spheres and on
recorded Dimorphos contacts.

## Diagnostics

| problem | in 3D | in the panel |
|---|---|---|
| unlabelled unit (face without label) | hatched fill | count + list, fly-to |
| conflicting labels (one face, labels of different units - usually a missing contact) | flashing outline, both label positions marked | list, fly-to |
| open contact end | red marker at the end (the dangling piece is trimmed for the units) | list, fly-to |
| topology check failed (Euler) | map not drawn, contacts highlighted | error with the offending component |

## Rendering

Units are **B2 fills** (#842): closed volumes, one draw against the surface depth. Additions:

- **Rings with holes** in `PolygonFill`/`FillVolume`: a face is triangulated from all its
  boundary cycles; walls come from the triangulation's boundary edges, so holes close by
  construction.
- **Radial shells** for map units: cap vertices offset along their own direction from the body
  centre (inner/outer radius from the face's relief plus reach), walls radial. Works for any
  face size up to nearly the whole body; ordinary filled annotations keep plane prisms.
- **Triangulating a face** per face: small faces in the tangent plane at their centre; a face
  too large for that (more than a hemisphere) in a projection centred on itself, with the
  projection's singular point placed in another face.
- **Unit-id colour table** instead of per-vertex colours, so assignment is a table update.
- **Face ids into the pick target** (hover, click, paint, eyedropper).
- **Hatching** for unlabelled units in the fill composite (screen-space stripes by face flag).
- **Unit code text** at a pole of inaccessibility of each face (computed in its triangulation's
  chart), as billboards.

## Export (GeoPackage)

- One `.gpkg` per map with layers **units** (polygons with holes: code, name, colour, area),
  **contacts** (lines) and **labels** (points).
- 2D lon/lat (SPICE body-fixed, as the existing exporters); faces crossing the date line or
  containing a pole are **split there** - the only place a global chart appears.
- Library decided in that phase (NetTopologySuite + its GeoPackage IO via Paket, or writing the
  few SQLite tables directly).

## Not planned

- Editing derived units (geometry, split, merge) - the map is immutable; all changes go through
  contacts and assignments.
- Converting a map into ordinary polygon annotations ("bake").
- Importing a predefined legend - units are created while mapping.

## Phases

1. **Model + panel skeleton** - `geoUnit` field, `GeologicalMap` (list + active map), panel
   (map list, palette, opacity), persistence and compatibility tests.
2. **Topology core** (pure, PRo3D.Base) - noding, DCEL, dangles, angular order, face tracing,
   cycles → faces, label assignment, Euler check, incremental update for one added/removed
   contact. Unit + FsCheck tests on synthetic spheres (random great/small-circle contacts,
   nested loops, whole-body coverage) and on recorded Dimorphos contacts.
3. **Rendering + assignment** - rings with holes, radial shells, unit-id colour table, face ids
   in the pick target, Assign-unit tool (click, paint, eyedropper, hover), hatching, unit
   codes. GPU tests; Playwright on Dimorphos.
4. **Contact tool** - snapping, split preview, automatic incremental update, problem list with
   fly-to and `N`.
5. **Export** - GeoPackage with date-line/pole splitting.
6. **Later** - contact type/certainty (solid/dashed, exported as an attribute), unit areas and
   statistics in the legend.

Every phase ends with `docs/GeologicalMap.md` (user docs) updated and `ai/` notes where the
architecture changed.

## Open questions

- **Contact type/certainty** (certain/approximate/inferred): later; contacts stay ordinary
  polylines, a type field can be added without breaking anything.
- **Noding tolerance**: relative to sampling spacing or absolute metres per body? Decide with
  real Dimorphos contacts.
- **Surface normal** at nodes on non-star-shaped bodies (67P-like): KdTree normal vs. averaged
  sample directions.
- **Unit areas**: on the terrain (KdTree) or on the radial shell's cap? Needed for the legend
  and export.
- **Moving / deleting a contact** that units depend on: the incremental update handles it, but
  label points may end up in a different face - re-check conflicts after every change.
