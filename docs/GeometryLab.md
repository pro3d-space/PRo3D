# Geometry Lab

`src/PRo3D.GeometryLab` is a small 2D SVG application for exercising the annotation boolean
operations (`PRo3D.Base.Geometry.RegionOps`) before they reach the viewer. The geometry is the
real thing — the same cut and merge code PRo3D will ship — only terrain, picking and the 3D
renderer are absent. Design and rationale: `plans/booleanOperations.md` and
`plans/testingStrategy.md`.

## Running

```
dotnet run --project src/PRo3D.GeometryLab
```

Opens an Aardium window served at `http://localhost:4322/`.

## Tools

- **Draw** — click to add points, **Close** to finish the polygon. A ring that encloses nothing
  (collinear, fewer than three distinct points) is rejected with a message.
- **Cut** — drag a stroke across a shape. A stroke cuts when both ends are outside the shape and
  it leaves area on both sides; every shape it cuts is replaced by its pieces.
- **Select** — click shapes to toggle selection; **Merge** unions exactly two selected shapes,
  **Delete** removes the selection.
- **Undo / Redo** — one step of history per operation.

Holes are drawn dashed red on white, so a merge that produces one is visible rather than
silently filled. The status line reports component and hole counts after a merge — the data that
decides whether the viewer integration refuses or supports holes.

## Fixtures

**Export fixture** writes all current shapes to `src/Tests/data/regions/lab-<id>.region` as
plain text (one contour per line, `x,y x,y …`, `#` for comments). The format lives in
`PRo3D.Base.Geometry.RegionFixture`, shared between the lab (writer) and the test suite
(reader).

Every `.region` file in that directory is replayed by `RegionFixtureTests` — each region is
checked against the cut/merge/round-trip invariants in `src/Tests/RegionInvariants.fs`, with
strokes derived from its bounds. Adding a regression case is therefore just: reproduce the case
in the lab, export, commit the file.

## Model types

`Model.fs` carries the `[<ModelType>]` record; `Model.g.fs` is generated. Regenerate with

```
dotnet fsi ./src/PRo3D.GeometryLab/RunAdaptify.fsx
```

or `adapt.cmd` / `adapt.sh` at the repository root, which cover all projects. Note that adaptify
must be able to typecheck the project — run it after a successful restore/build, otherwise it
reports "no models" and generates nothing.
