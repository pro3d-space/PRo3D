# Geological map — plan (#843)

Status: plan, 2026-10-13. Branch `features/843_geological-map`, stacked on #841/#842 (fills
against the surface depth), which this builds on.

## Goal

Geological maps in PRo3D the way geologists build them in GIS with the *contacts and points*
method (QGIS plugin Mappy, PLANMAP/GMAP teaching): the mapper draws **contacts** (lines) and
places one **label point** per unit; the **units** (polygons with holes) are *derived*. Refining
a map means drawing another contact across a unit - no polygon editing, every shared boundary
is traced once, edits to a contact update both units on its sides.

First target: a **whole-body** map of Dimorphos. Later: Mars regions (same code), contact
types, live regeneration.

## Workflow

1. Create a *Geological map* (new scene object) and pick its **source group** - an ordinary
   annotation group. Everything outside it (boulders, measurements) does not take part.
2. In that group, draw **contacts**: polylines (Sky projection on bodies). They only need to
   cross each other; overshoots are fine, dangling ends are dropped.
3. Place **label points**: point annotations with a **geological unit** (`geoUnit`) set.
4. **Regenerate**: units appear as fills on the terrain, coloured from the map's unit table;
   problems show in 3D (below).
5. Refine: draw a contact across a unit, put a label in the new part, regenerate.
6. **Export** units, contacts and labels for GIS.

## Data model

| what | where | persisted |
|---|---|---|
| contacts | polyline annotations in the source group (unchanged type) | yes, as today |
| label points | point annotations in the source group, new field `geoUnit : Option<string>` | yes, new key `geoUnit` (`Json.tryRead`, default `None`) |
| `GeologicalMap` | scene object: `key`, `name`, `sourceGroup : Guid` (group `Node.key`), `unitTable`, `visible`, `fillAlpha` | yes, new scene key |
| unit table | `HashMap<string, Unit>`; `Unit = { code; name; color; description }` - entries created on first use of a code | yes, inside the map |
| derived units | faces, rings, volumes, diagnostics | **no** - regenerated on load and on demand; `sourceFingerprint` marks the map out of date |

Scene compatibility: new data only under new keys; older releases load the scene and ignore
the map, contacts and labels stay ordinary annotations.

`geoUnit` is a dedicated field (not `text`) - edited in the annotation properties, shown for
point annotations; the unit dropdown offers the codes of the map's unit table plus "new".

## Algorithm: faces on the surface from local questions only

No global chart. Every step asks a question that is local to one place on the surface and is
answered in 3D or in the tangent plane at that place. This works for the whole body, has no
pole or date-line cases, and does not even need the body to be star-shaped.

Input: the contacts' **sampled** polylines (the terrain-draped points the outline is drawn
with, `getPolylinePointsAt`), not just their control points.

1. **Segments.** All contact samples as 3D segments, each tagged with its contact.
2. **Noding.** Candidate pairs from a spatial hash over segment bounding boxes (O(n) for
   well-spread data). For a pair: closest points of the two segments; they cross if the
   distance is below a tolerance (a fraction of the local sampling spacing - draped polylines
   of two contacts never hit each other exactly) and both parameters lie inside. Split both
   at the crossing; the node is the midpoint of the closest points. Endpoints within tolerance
   of another contact snap to it (undershoot), so "touching" works like "crossing".
3. **Graph.** Nodes and edges into a half-edge structure (DCEL). Iteratively remove
   degree-1 nodes and their edges: **dangles** (kept for diagnostics).
4. **Angular order at a node.** For each node, the outgoing edges' first directions,
   projected into the node's tangent plane, sorted by angle. Tangent plane normal: the
   surface normal at the node (from the KdTree hit or the adjacent samples; on star-shaped
   bodies the radial direction is a fine stand-in).
5. **Face tracing.** Standard: for half-edge `h` ending in node `v`, `next(h)` is the
   half-edge leaving `v` right after `twin(h)` in the angular order. Following `next` yields
   the boundary cycles.
6. **Cycles to faces.** On a closed surface a face can have several boundary cycles when the
   contact graph is disconnected (a closed contact loop inside a unit = a hole). Group cycles
   per connected component; place each component into the face of the others that contains
   it with the label-point test below (a point of the component against the other
   components). Sanity check per connected graph: V − E + F = 2 (Euler, sphere-like body).
7. **Labels.** For each label point: nearest contact edge (spatial hash), side of that edge in
   the tangent plane at the nearest point → the half-edge on that side → its face. A face
   gets its unit from its label(s).

Everything here is pure (PRo3D.Base, no rendering, no model), unit-tested on synthetic
spheres and on recorded Dimorphos contacts.

## Diagnostics (shown in 3D, listed in the map panel)

- **Unlabelled unit** - a face without label point: hatched/flagged fill, count in the panel.
- **Conflicting labels** - a face with label points of different units: usually a missing
  contact between them; both points highlighted.
- **Dangling contact** - an end that does not reach another contact: marker at the end
  (trimmed away for the units).
- **Euler check failed** - topology bug or degenerate input; reported, never silently drawn.

## Rendering

Units are **B2 fills** (#842): closed volumes, all in one draw against the surface depth.
Two additions:

- **Rings with holes** in `PolygonFill`/`FillVolume`: a face is triangulated from all its
  boundary cycles; walls already come from the triangulation's boundary edges, so holes close
  by construction.
- **Radial shells instead of plane prisms** for map units: cap vertices offset along their
  own direction from the body centre (inner/outer radius from the face's relief plus reach),
  walls radial. Works for any face size up to nearly the whole body; normal filled
  annotations keep their plane prisms.
- **Triangulating a face** is a per-face question too: small faces in the tangent plane at
  their centre; a face too large for that (more than a hemisphere) in a projection centred on
  itself, with the projection's singular point placed in another face.

Contacts keep drawing as ordinary polylines (on top of the fills); units draw no outlines.
Legend: unit table with colours, area per unit, visibility per unit.

Scale: a whole-body map can have thousands of units; the one-draw fills (0.12 ms at 10k)
are why #842 came first.

## Regeneration

Explicit **Regenerate** button first. The map stores a fingerprint of its source group
(contacts' samples, labels' positions and units); when it changes the panel shows *out of
date* and keeps the last result. Noding and face tracing are cheap; re-draping crossing points
(raycast) is the expensive part - "live" regeneration comes once that is measured.

## Export

GeoPackage (GIS standard, what QGIS/Mappy users use), GeoJSON as the cheap second:

- layers: **units** (polygons with holes, unit code/name/colour), **contacts**, **labels**;
- 2D lon/lat (SPICE body-fixed, as the existing exporters); faces crossing the date line or
  containing a pole are **split there** - the only place a global chart appears;
- library: decide in that phase (NetTopologySuite + its GeoPackage IO via Paket, or writing
  the few tables directly).

## Phases

1. **Model + UI skeleton** - `geoUnit` field (properties UI), `GeologicalMap` object, map
   panel (source group, unit table, regenerate), persistence + compatibility tests.
2. **Topology core** (pure, PRo3D.Base) - noding, DCEL, dangles, angular order, face tracing,
   cycles → faces, label assignment, Euler check. Unit + FsCheck tests on synthetic spheres
   (random great-circle and small-circle contacts, nested loops, whole-body coverage).
3. **Rendering** - rings with holes, radial shells, unit fills via DeferredFills, diagnostics
   in 3D, legend. GPU tests; Playwright on Dimorphos (draw contacts, label, regenerate).
4. **Export** - GeoPackage/GeoJSON with date-line/pole splitting.
5. **Later** - contact type/certainty (solid/dashed, exported as attribute), live
   regeneration, area/statistics per unit, contacts snapping while drawing.

Every phase ends with `docs/GeologicalMap.md` updated (user docs) and `ai/` notes where the
architecture changed.

## Open questions

- **Contact type/certainty** (certain/approximate/inferred): left open; the contact stays an
  ordinary polyline, a type field can be added later without breaking anything.
- **Tolerances** for noding and snapping: relative to sampling spacing or absolute metres per
  body? Decide with real Dimorphos contacts.
- **Surface normal** at nodes on non-star-shaped bodies (67P-like): KdTree normal vs. averaged
  sample directions.
- **Several maps per scene**, maps sharing contacts (e.g. a structural and a lithological map)?
  The model allows several maps; contacts belong to one source group each.
- **Unit areas**: on the terrain (KdTree) or on the radial shell's cap? Needed for the legend
  and export.
