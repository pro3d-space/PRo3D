# Boolean operations on annotations

Cut an annotation along a drawn line; merge two annotations. Built against properties first, with
a 2D lab as the interactive front end rather than the place the logic lives.

Testing approach and its rationale: `plans/testingStrategy.md`.

## Status

| Part | State |
|---|---|
| A. Vendored `PolyRegion<'a>` in `PRo3D.Base` | **done** |
| B. `RegionOps` — cut, merge, and their properties | **done** |
| C. `PRo3D.GeometryLab` — SVG lab + fixture export | **done** — builds, in the sln, `docs/GeometryLab.md` |
| D. Fixture replay | **done** — `RegionFixtureTests`, one test per `.region` file |
| Viewer integration | **planned** — `plans/viewerIntegration.md` |

293 tests passing, FsCheck properties confirmed running 100 cases each.

Two defects found and fixed after the lab went interactive, both worth remembering:

- **Vertex grazing** (`fix(base) 118f870b`): a cut stroke passing exactly through region
  vertices made LibTess drop tangent slivers — a lab-exported fixture caught 14% of a U-shape's
  area vanishing. The stroke is now nudged off every vertex before cutting; the shift is far
  below the invariant tolerances. Found by the export→replay loop on its first real use.
- **Cut is now a polyline stroke** (`feat(base) 9e3bf29d`), not only a straight line: one side
  of the stroke is built by extending its ends far past the region and closing through a far
  arc, so within the region even-odd membership is decided by the stroke alone. Only the
  stroke's *ends* must be outside the region — interior points may dip inside, letting a cut
  follow a feature instead of a chord.

## A. Vendoring

`src/PRo3D.Base/Geometry/PolyRegion2d.fs` is a verbatim copy of aardvark.base
`0420fd19` (fixes [aardvark.base#100](https://github.com/aardvark-platform/aardvark.base/issues/100)),
changed only in its namespace. Carried because the commit is on master but in no tag (latest
5.3.27) while PRo3D resolves 5.3.26.

**Delete it and switch back to `Aardvark.Geometry` once a release carries `0420fd19`.** Keeping it
byte-identical apart from the namespace is what makes that a clean revert.

`PolygonFill` opens the vendored namespace too, so `PolygonTessellator`, `Polygon2d<'a>` and
`Triangle2d<'a>` come from one place. `paket.lock` is untouched — `Unofficial.LibTessDotNet 2.0.2`
was already resolved transitively, so one line in `src/PRo3D.Base/paket.references` sufficed.

## B. `RegionOps`

`src/PRo3D.Base/Geometry/RegionOps.fs`, UI-free. `Region = PolyRegion<V3d>`, the attribute carrying
the source world position — in the plane that is the same point with z = 0, but keeping the
attributed type means the lab exercises the exact path PRo3D uses.

### `cutsThrough` — the formulation that survived

A stroke cuts a region iff **both ends are outside it, the segment actually reaches it, and the
line leaves area on both sides**.

It was first written as *both ends outside plus at least two boundary crossings*, which is what the
plan specified. The round-trip property falsified that on the second generated case, shrinking a
4-point ring to a triangle: **re-cutting a piece with the same line still reported a cut**.

The reason is structural, not numerical. After a cut, the piece's boundary *runs along* the cut
line, and the two edges adjacent to that collinear stretch each register a transition — exactly as
they would if the line passed through the interior. Crossing counts cannot separate the two cases,
so vertex and collinear special-casing does not help. Testing that both sides hold area separates
them exactly, and is simpler than what it replaced.

Worth remembering: hand-written tests would have missed this. "Cut a square, get two halves" passes
either way.

### Deviations from the original design, and why

- **`contains` is hand-written even-odd across all contours**, not `PolyRegion.Contains`. The
  latter is `Seq.exists` over contours, so it answers true for a point inside a hole. Pinned by a
  test.
- **`cut` returns the two side-regions, not connected components.** A side may legitimately hold
  several contours; splitting into components needs holes paired with their outer contour, which
  is a separate concern and only matters at viewer-integration time.
- **`segmentReaches` samples the segment at 64 points** rather than solving exactly. A false
  negative is possible for a sliver thinner than the sample spacing — preferable to the vertex
  fragility that the crossing-count version already demonstrated.

### Properties

`src/Tests/RegionInvariants.fs` states them as violations so the corpus, the generators and (later)
the fixtures check one definition:

- **cut** — a non-cutting stroke changes nothing; a cutting one yields ≥2 pieces; areas sum to the
  original; pieces do not overlap; each piece is contained in the original; re-cutting a piece with
  the same line is a no-op
- **merge** — inclusion–exclusion on area, commutative, idempotent, both operands contained
- **round trip** — cut then merge the pieces back must reproduce the original by area *and* sampled
  containment. The most valuable property here: it checks both operations against each other rather
  than against a hand-computed expectation, and it is what found the `cutsThrough` defect.

Region equivalence is **sampled over a grid**, because contour order, vertex count and winding all
differ between equal regions — geometric comparison is not available after re-tessellation.

Generators produce angle-ordered simple rings (so shrinking keeps them simple), plus
guaranteed-cutting and guaranteed-missing strokes so both branches are actually exercised.
Shrinkers go through `Arb.fromGenShrink`; `Arb.fromGen` has an empty shrinker.

## C. `PRo3D.GeometryLab` — done

SVG lab modelled on `aardvark.media/src/Examples (dotnetcore)/07 - Simple2DDrawing`. Tools: draw,
cut, select; merge selected; undo/redo. Holes rendered dashed red so a merge that produces one is
obvious rather than silently filled. Usage: `docs/GeometryLab.md`.

**The update function only sequences and calls `RegionOps`** — no geometry in the lab. That is what
keeps the logic testable without a GUI. Keep it that way.

### The adaptify blocker, resolved

The handover left the lab not building: adaptify reported *"no models in Model.fs"* and two
hypotheses were open. **Hypothesis 2 was the cause — the project had never been restored.** After
`dotnet tool restore` + a build of the project, `dotnet adaptify --lenses --local --force`
generates `Model.g.fs` without complaint. No design change was needed; adaptify decomposes the
model fine.

Worth remembering: **adaptify must typecheck the project, so it only works after a successful
restore.** On an unrestored project it does not error — it reports "no models" and generates
nothing, which reads like a modelling problem and cost a whole handover. The lab now has
`RunAdaptify.fsx` like every other project, and `adapt.cmd` / `adapt.sh` include it.

The `[<TreatAsValue>]` on `shapes` was removed — it was a workaround for the misdiagnosis, never
needed. `Shape` is a plain record (not a `[<ModelType>]`), so adaptify treats elements as values
and `shapes` becomes an `alist<Shape>`: per-shape change tracking with real `Region` values in the
model, which is what the view's `AList.map` expects anyway. `past`/`future` keep their
`[<TreatAsValue>]` (the standard undo/redo pattern; the recursion must stay opaque).

Other changes on the way to a clean build: the server code moved from the Suave-era API to
`Aardvark.UI.Giraffe` (`Server.startLocalhost`, the pattern `PRo3D.Lite` uses), and the project
was added to `src/PRo3D.sln`.

## D. Fixture replay — done

The fixture format moved to `PRo3D.Base.Geometry.RegionFixture` so the lab (writer) and the test
suite (reader) share one definition. `ExportFixture` in the lab writes
`src/Tests/data/regions/lab-<id>.region`; `src/Tests/RegionFixtureTests.fs` replays **every**
`.region` file there through the invariants — cut/round-trip with bounds-derived strokes at four
angles, merge idempotence, and pairwise merge across the file's regions. One test per file, so
adding a regression case is dropping in a file. A hand-written `seed-basic.region` is committed so
the replay mechanism itself runs in CI from day one.

### Then

- run the lab and report **how often merge produces holes** — that decides refuse-versus-extend for
  the viewer integration below

## Viewer integration — planned in `plans/viewerIntegration.md`

The decisions taken here carry over unchanged and are folded into that plan:

- a cut deletes the original annotation and creates **N new ones with metadata copied to each**
- merge results explode into one annotation per component; **holes are refused with a message**

This avoids growing `Annotation` to multiple rings: cut never produces a hole, and merge only does
when two rings enclose a gap. The lab measured the hole case: it takes deliberate construction
(a U capped by a bar) but is natural once users merge shapes grown around something — refusal
with a clear message stands for v1.

The integration plan stages the work as **union first** (operating on the existing annotation
multi-selection — no new interaction mode needed) **then cut** (a drawing-like mode over the
selected annotation, reusing the working-annotation pipeline). The annotation-key dangling
audit this section demanded is done and recorded there.
