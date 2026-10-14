# Handover: annotation fills (#841/#842) and geological maps (#843/#844)

State as of 2026-10-14. Two stacked PRs from one working session.

## Where things are

| what | branch / worktree | PR | state |
|---|---|---|---|
| fills drawn onto the terrain (#841) | `bugs/841_stencil-fills`, `.claude/worktrees/841-stencil-fills` | [#842](https://github.com/pro3d-space/PRo3D/pull/842) → `develop` | ready for review, CI green, mergeable, no review yet |
| geological maps (#843) | `features/843_geological-map`, `.claude/worktrees/843-geological-map` | [#844](https://github.com/pro3d-space/PRo3D/pull/844) → `bugs/841_stencil-fills` | **draft**; stacked on #842 - rebase onto `develop` once #842 is merged |
| aardvark.media ai docs: offscreen passes and resource lifetime | `ai-docs-offscreen-pass-lifetime` in `Desktop/aardvark/aardvark.media-docs` | [aardvark.media#87](https://github.com/aardvark-platform/aardvark.media/pull/87) | open |
| RenderPass cost experiment | `experiments/renderpass-cost`, `.claude/worktrees/renderpass-cost` | none (local only, parked) | `src/Tests/RenderPassCostBenchmark.fs`, `docs/dev/renderpass-cost.md` |

Docs to read first: `docs/AnnotationFill.md`, `ai/RENDERING.md` (offscreen surfaces, deferred
fills) for #842; `docs/GeologicalMap.md`, `docs/GeologicalMap-story.md`,
`docs/dev/GeologicalMap-architecture.md`, `docs/dev/GeologicalMap-plan.md`, `ai/DOMAIN.md`
(Geological Maps) for #844.

## #842 - fills onto the terrain

Surfaces render offscreen (colour + depth), are blitted per sample, and all fills are accumulated
in one draw against the surface depth (`PackedRendering.DeferredFills`, `OffscreenSurfaces`).
10k fills ≈ 0.12 ms. Known limitations, documented in the PR and `docs/AnnotationFill.md`:
screenshots at a different aspect/sample count than the view, fixed offscreen background colour,
single-sampled accumulation, the Snapshots path still uses the stencil fallback (rebuilds batches).

Checked during #843: annotation fills do draw in the map projection view (headless test and
`map-projection.spec.ts` pass). Low-alpha white fills are nearly invisible on grey terrain - not
a bug.

## #844 - geological maps

Done: topology core (`ContactTopology`), shell volumes (`MapShell`), model + persistence (new keys),
Geological Maps panel, Assign unit tool (click / drag / Alt+click, face-id target, no KdTree),
unit codes, snapping (click time and under the cursor), split preview, problem list + `N`,
open-end markers, ellipses as contacts, showcase generator, story with screenshots, two review
rounds (must-fixes done). Tests: ContactTopology 15, MapShell 8, persistence 6 (unit);
`geological-map.spec.ts` 5 + `geological-map-story.spec.ts` (UI). Full unit suite: 773 pass; the
11 failing are SPICE tests needing the pinned kernel set (`PRO3D_SPICE_KERNELS`), unrelated.

### Open, in priority order

1. **Review "should fix" items** (second review, not done):
   - every annotation edit anywhere re-runs each map's `sources` and re-uploads its face table
     (`sources` reads the whole annotation map; split per group or compare before re-evaluating);
   - `N` (`problemsOf`, `Annotation.retrievePoints`) and the panel (`Sg.getPolylinePointsAt`)
     read contacts slightly differently (duplicates at segment joins) - tolerances can differ;
   - snap radius uses an arbitrary viewport width (`m.viewPortSizes |> Seq.tryHead`) and the main
     frustum even in the instrument view (`Viewer.fs`, `matchPickingInteraction`);
   - importing an annotation file (`DrawingAction.AddAnnotations`) drops its maps;
   - a label point placed by the Assign tool near a contact can land in the neighbouring unit
     (position from MSAA sample 0 depth, not re-checked against the face);
   - "New unit" on an inactive map / `AssignToSelected` labels points outside the map's group;
   - `attached` (preview) scans all contact segments per stroke vertex - use the network's grid.
2. **Video of the story** - planned, not started in code (see below).
3. **Maps in the map projection view** (`PRo3D.MapProjection`) - not drawn there yet.
4. Deferred by decision: maps in Snapshots, GeoPackage export.
5. Known geometric limits (documented in the architecture doc): star-shaped bodies only; many
   separate contact networks are O(C²)/O(C³) in containment; one float32 pivot per map (~0.5 m for
   a body-wide Mars map); pinched cycles; two contacts joining the same two nodes directly.
6. Before un-drafting: rebase onto `develop` after #842, mark ready, request review.

### Video of the story (plan agreed, not implemented)

- Reuse `tests-ui/tests/geological-map-story.spec.ts`: factor its steps into one function over a
  small `Ui` interface (input page + offset, render/panel/annotations/toolbar frames, `caption`,
  `shot`), run it twice: pictures (as now) and video (`PRO3D_VIDEO=1`).
- Video run: the **main page** (full UI) recorded by Playwright (`recordVideo`, 1920×1080);
  layout via `PRO3D_LAYOUT_DIR` with a `current.json` docking render left, Geological Maps
  top-right, Annotations bottom-right (format: `docs/WindowLayouts.md`). Panels are iframes
  (`iframe[src*="page=geomaps"]`): drive them through `page.frames()`; mouse via the main page
  with the render iframe's bounding-box offset.
- **Cursor**: mouse events inside iframes never reach the top document, so the helpers that move
  the real mouse also position an injected overlay (pointer-events: none) and show a click ripple.
- **Captions**: a DOM banner in the main page (the story sentences).
- **ffmpeg** (`C:\Users\haral\Desktop\development\ffmpeg-8.1-full_build\bin\ffmpeg.exe`): cut the
  loading time at the start (record the start time when the story begins), encode MP4
  (`-c:v libx264 -pix_fmt yuv420p`). Checked in: the helper and a short `docs/dev` note only.
- Pitfalls learnt with the story: `settled()` rejects yellow-green in the frame centre as the
  splash (a selected annotation is that colour) - use a two-equal-frames wait; colour pickers in
  legend rows contain hidden inputs (`input:visible`); wait until the toolbar shows the chosen
  geometry before clicking; snap offsets ~4 px, not at the 10 px limit; preview picks need the
  tool armed (Ctrl held) and the first pick loads a KdTree (seconds).

## Environment notes

- Builds: strip `__DOTNET_PREFERRED_BITNESS` / `VSCMD_*`; run `dotnet fsi utilities/Adapt.fsx`
  after model changes. A running viewer locks `bin/Release` - build elsewhere
  (`-p:OutputPath=...`) or close it; never kill a viewer the user started.
- Test data: `PRO3D_TEST_DATA=C:\Users\haral\Desktop\pro3d\PRo3D.Resources.TestData`. Untracked
  there (not committed to the test-data repo): `cases/geologicalMap/` (written by
  `dotnet run --project src/Tests -- --make-geomap-showcase`) and `cases/polyfillStencil/` (the
  user's fill problem scene). The UI specs need the showcase `.ann`.
- Delete `src/Tests/data/TestScene_*.pro3d.layout` and the root `recent` before committing.
- Commits without `Co-Authored-By`; PR texts end with the Claude Code line.

## Pre-existing issues seen, not filed

- the closing edge of Sky-projected polygons is not drawn;
- memory grows with camera motion (OPC LoD caching);
- an old develop build crashed when the render page was reopened;
- `ProfileAttributeExtraction` tests race under parallel KdTree loading (pass sequentially);
- `ColorPicker`/`Numeric` slider in the panel showed the opacity knob at the end once (value 0.50).
