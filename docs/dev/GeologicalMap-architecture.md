# Geological maps - how it works

Developer notes for #843 (handover and open work: [GeologicalMap-handover.md](GeologicalMap-handover.md)). User docs: [../GeologicalMap.md](../GeologicalMap.md). Plan and status:
[GeologicalMap-plan.md](GeologicalMap-plan.md). Fills against the surface depth, which this builds
on: [../AnnotationFill.md](../AnnotationFill.md), `ai/RENDERING.md` (#842).

## Pipeline

```
annotation group ─► sources ─► build ──────────► label ─► geometry ─► per-map render pass
 (contacts,          (points,    (Network:          (faces    (shell       (face id + depth
  label points)       labels,     faces, faceAt)     with      volumes,     target, composite)
                      tolerance)                     units)    anchors)
                                                       └────► face table (colour per face)
```

| stage | code | input | output | recomputed when |
|---|---|---|---|---|
| sources | `GeologicalMapApp.sources` | the group's annotations | contact polylines (sampled), label points, tolerance | any annotation changes (cheap: reads, no geometry) |
| build | `ContactTopology.build` | contacts, tolerance | `Network`: faces (boundary cycles), open ends, Euler check, `faceAt` | the contacts change (memo: same contacts return the same `Network` object) |
| label | `ContactTopology.label` | network, label points | each face's unit (unlabelled / unit / conflict) | labels change (microseconds) |
| geometry | `MapShell.tryFace` per face | network (by reference), reach | one closed volume per face, anchors for the code text | the network object or the reach changes |
| face table | `GeologicalMapApp.faceTable` | units, legend, opacity, preview | RGBA32F texture, one texel per face | assignments, legend, opacity, the stroke being drawn |
| render | `GeologicalMapApp.mapSg` | volumes, surface depth | the units over the terrain | every frame (one draw + one full-screen pass per map) |

Assigning a unit therefore re-runs only `label` and the face table: no triangulation, no new
vertex buffer. `derived` is cached per map (`ConditionalWeakTable` keyed by the adaptive map),
so the main view, the instrument view and the panel share one computation.

## Central concepts

### Contacts and points; the map is derived

Contacts are polylines, label points are points with `geoUnit`; both are ordinary annotations of
one group. Units are never stored or edited - they are recomputed from the group (immutable map,
the Mappy model). Persisted: only `GeologicalMapsModel` (legend, opacity, source group) and
`Annotation.geoUnit`, under new JSON keys.

### Local questions on the radial projection - no chart for the whole map

A whole-body map has no global chart without singularities (poles, date line). Every step instead
asks a question local to one place, answered in 3D or in the tangent plane there:

- *do these two segments cross?* - seen from the body centre (`radialCrossing`);
- *in which order do the edges leave this node?* - angles in the node's tangent plane;
- *which face is this point in?* - nearest contact edge, then which side of it (great-circle side).

All of this uses the **radial projection** about the body centre (`GeologicalMapApp.center`,
body-fixed origin). It is exact on a sphere and gives the right topology on any body where each
ray from the centre meets the surface once (*star-shaped*: Dimorphos, Didymos, Mars). Bodies with
overhangs (67P) break it; not supported.

### Draped contacts never meet exactly - noding

Contacts are sampled polylines draped on the terrain. Two contacts that cross on the surface have
chords (straight pieces between samples) that pass each other at different heights - up to the
relief between samples, tens of centimetres on rough terrain. Three rules turn that into nodes:

1. **radial crossing**: two segments cross if their projections from the centre cross
   (each segment meets the other's plane through the centre inside both) - height plays no role;
2. **near miss**: segments closer than the pair's tolerance touch (an end stopping just short);
3. **clustering**: points closer than the tolerance become one node (union-find).

The tolerance is per pair of contacts: at most `0.3 x` the finer contact's sample spacing (and the
caller's bound). Samples of the same contact are never merged, except exact duplicates (the closing
point of a closed contact). Without these two rules, one tolerance for all contacts collapsed a
finely sampled contact into a single node.

**Snapping** (`ContactSnap`) removes the need for the tolerance where it matters most: a point
placed within 10 px of a contact while drawing lands exactly on its chord, and the radial/near-miss
test then nodes it at distance 0. The radius is an angle (pixels × field of view per pixel), and
the snap point must also lie near the picked terrain point in 3D, so a contact on the far side
never takes the click. The same snap runs on the viewer's preview pick (the surface point under
the cursor, `Model.surfaceIntersection`) for the green marker before clicking and for the split
preview. Its markers are not depth-tested: snap points lie on contact chords, which often run
under the terrain.

**Contacts** are polylines, lines, polygons and ellipses of the map's group
(`GeologicalMapApp.isContact`); polygons and ellipses are closed by repeating their first point
(`isClosedContact`). Only line tools snap (`isLineTool`): an ellipse's clicks are its axes, not
points of its outline.

### Faces from a half-edge graph

Edges between consecutive nodes along each contact form a planar graph on the sphere. Dangling
pieces (an end reaching nothing) are trimmed iteratively and reported as open ends. Each edge is
two half-edges; at each node they are sorted by angle in the tangent plane; following
`next(h)` = the edge just clockwise of the way back traces each face's boundary with the face on
its left (seen from outside). Separate networks (a closed loop inside a unit) give faces with
several boundary cycles: each network is located in the faces of the others. Check: V - E + F = 2
per network (`eulerOk`).

### One closed volume per face: radial shells

Fills colour the terrain inside a closed volume by counting its faces behind the terrain (#842).
A unit's volume is the part of a spherical shell over its face: everything between an inner and
an outer radius in the face's directions.

- **Radii**: from the contacts' radii about the centre, widened by the fill reach (Dimorphos: 15 m).
- **Walls are radial**: two neighbouring faces' walls coincide exactly, so terrain below a contact
  belongs to one unit - whatever the relief, for any face size.
- **Caps are chords**: a flat triangle between three points on the outer sphere dips below it by the
  sagitta `r (1 - cos(θ/2))`. Cap edges are therefore limited to `maxAngle`, chosen so the dip stays
  under half the reach (`MapShell.maxAngleFor`).

### Triangulating a face on the sphere

A face is triangulated (libtess) in a **stereographic chart whose pole lies outside the face**: the
face then maps to a bounded region, whatever its size or shape (bands around the body, faces larger
than a hemisphere). The pole is the candidate direction (means, Fibonacci directions, points just
across the face's own edges) farthest from the boundary that is outside the face.

Conformity rules, because neighbouring faces are triangulated in different charts:

- long boundary edges are densified along the great circle (slerp), computed from the
  lexicographically smaller end, so both faces get bit-identical points;
- boundary edges are then never split again;
- interior edges are split at their chart midpoint, which depends only on the edge's ends, so the
  two triangles sharing an edge split it identically.

**Nearly-whole faces.** When a face is everything but a small region, the pole has to lie in that
small region and the rest of the body is squeezed around the chart origin, inside triangles whose
corners all lie on the small region - no edge test can see that, and the cap comes out inside-out
(negative signed volume). Such a face is built as the **whole shell plus its complement's volume
turned inside out**: counted +1/-1, the two cancel over the complement, so the result is exactly
the face. The complement is small, so its own chart is well-conditioned.

### Rendering: counting, face ids and the face table

Per map, one draw renders all its volumes into an RGBA32F target with additive blending, against
the surfaces' depth. For each volume face behind the terrain point it adds `w = +1` (back) or `-1`
(front):

| channel | sum | why it works |
|---|---|---|
| R | `w * (faceId + 1)` | a volume not containing the terrain point has as many front as back faces behind it: its terms cancel; the containing volume leaves `±(id + 1)` |
| G | `w * terrainDepth` | same cancellation: `±depth` of the pixel's terrain |
| B | `w` | coverage: `±1` inside a unit, 0 outside |

The composite divides by the coverage and looks the face id up in the face table (colour, opacity;
negative opacity = hatched). The Assign unit tool downloads the one pixel under the cursor: face id
directly, terrain position by unprojecting the depth with the view's own view/projection - no
KdTree, no ray cast, no wait for a surface's KdTree to load.

### The adaptive pipeline

All stages are `aval`s built once per map (outside any `AVal.custom`), read with the token
(ai/CONVENTIONS.md). `sources` reads the whole annotation map and so re-evaluates on any annotation
change; `build` compares the contacts structurally and returns its previous `Network` object when
they are equal; `geometry` compares the network by reference. That memoisation is what keeps label
edits and assignments from rebuilding geometry.

## Precision

| where | the issue | what is done |
|---|---|---|
| GPU vertex buffers | float32 has a 24-bit mantissa: ~0.25 m steps at Mars radius (3.39e6 m) | positions relative to a pivot (`Trafo3d.Translation origin`), MV composed in double on the CPU |
| one pivot per map | a body-wide Mars shell spans ~7e6 m: vertices quantised to ~0.5 m | **known limitation**; local Mars maps (km) and small bodies are exact to mm |
| geometry tests | absolute epsilons break between 80 m (Dimorphos) and 3.4e6 m (Mars) | decisions in unit-direction space (`locate`, chart, anchors) or relative thresholds (`radialCrossing`: `1e-6 · |seg| · |n|`) |
| far points | a tangent-plane side test degenerates for points far from the edge (antipode) | side of the edge's great circle: `dot(a × b, q)`, valid at any distance |
| small angles | `acos` of a dot product loses precision near 0 | `atan2(|a × b|, a · b)` |
| draped contacts | crossing chords pass at different heights (rough terrain) | radial crossing instead of a 3D distance |
| unprojection | depth is the surfaces' window depth (`2·d − 1` = NDC, GL convention), pixel centre + y flip | same convention as the annotation pick target |

## Acceleration structures

| structure | where | built from | solves | cost |
|---|---|---|---|---|
| uniform grid over segment boxes | `ContactTopology.build`, noding | contact segments; cell = 2 × mean segment length; boxes enlarged by tolerance + own length | candidate pairs for crossings: O(n) instead of all pairs O(n²) | boxes over 512 cells (long undraped chords) are kept in a side list tested by every query - writing them into every cell is cubic in their length and exhausted memory |
| point grid | node clustering | all samples and split points, cell = tolerance | points within the tolerance | O(n) |
| union-find | clustering, joins at samples, network components, face cycles of one face | - | transitive merging in near-constant time | - |
| half-edge graph + angular order | face tracing | alive edges, angles per node | faces in O(E log degree) | - |
| direction grid | `locate` (labels, `faceAt`, containment probes) | edges in unit-direction space | nearest contact edge to a direction; expanding search | a query box larger than the occupied space walks the occupied cells instead of the empty box (sparse maps: 315 → 31 ms) |
| `Network.faceAt` | labels, split preview | the built network | point → face without rebuilding | per point: a grid query |
| face table texture | composite | units, legend, opacity | assigning a unit = one small texture upload | - |
| face-id / depth target | composite, Assign tool, hover | one draw per map | picking without KdTree; one pixel download per event | RGBA32F at view size per map |
| derived cache (`ConditionalWeakTable`) | `GeologicalMapApp.derived` | adaptive map object | one topology per map for all views and the panel | - |

## Numbers (this machine, Release)

| case | result |
|---|---|
| 40 great circles, 28 800 segments, 413 labels | 1562 faces in ~0.2 s |
| showcase: 5–9 Dimorphos contacts | build 30–90 ms; assignment: face table only |
| GPU, 10k annotation fills (#842) | 0.12 ms |

## Known limitations

- Star-shaped bodies only (radial projection).
- Many separate networks (hundreds of isolated loops): containment is O(C²) queries plus O(C³)
  separation checks.
- Body-wide Mars maps: one float32 pivot per map (~0.5 m).
- A contact passing a node twice (pinched cycle), and two contacts joining the same two nodes
  directly (a lens dropped by edge de-duplication), are not handled specially.
- Maps are not drawn in Snapshots or the map projection view; GeoPackage export is deferred.
