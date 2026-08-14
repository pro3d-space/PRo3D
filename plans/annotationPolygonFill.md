# Annotation polygon fill — Option B (cap on a surface chart)

Fill closed annotations (`Polygon`, `Ellipse`, `AxisEllipse`, `Axis4PEllipse`) with a
translucent cap built in a 2D **surface chart** — either a fitted plane or a geographic
(lat/lon) parameterisation — generalising the two modes the ellipse code already implements.

**Explicitly out of scope** (see discussion for the full option set):
- terrain-following fill meshes (Option A — subdivide in chart space + raycast every vertex)
- stencil prism / shadow-volume fills (Option C — `GeologicSurfaceApp.StencilAreaMasking`)
- shader-analytic or decal fills (Option D)

Option B is the cheap first ship: it establishes the chart abstraction, the model fields, the
UI, the serialization and the packed draw call. Options A and C are drop-in replacements for
*step 5 only* (geometry generation) and reuse everything else.

## Decisions taken

- **Occluded, not overlay.** `DepthTest.LessOrEqual` + depth offset: a ridge in front hides the
  fill and it reads as lying on the surface. The original request said "overlay the scene", but
  a fill that ignores occlusion reads as a floating decal and destroys depth perception. The
  "doesn't shine through far geometry" bonus comes free with this choice.
- **Per-annotation fill properties** — `showFill` / `fillColor` / `fillAlpha` on `Annotation`
  (section 3), not a global toggle. Buys selective filling and a fill colour independent of the
  outline, at the cost of the serialization and UI plumbing.
- **Geographic chart is opt-in**; the annotation's own chart wins where it has one (ellipses),
  else the fitted plane (section 2).
- **Fills are clickable** (section 8), with a config toggle to switch it off.
- **Merge/split region model deferred** to that PR (section 10); this PR only keeps the
  renderer region-capable so the decision stays cheap.

## Known limitations

> **Accepted, not a bug: large polygons on rugged terrain.** The cap is flat within each
> triangle, so across a large or rough polygon its middle sits above or below the surface while
> only the rim is anchored. No depth offset fixes this - it is inherent to Option B. Verified in
> the viewer and accepted as the shipped behaviour.
>
> The fix, if it ever becomes worth it, is Option A: subdivide in chart space and raycast every
> interior vertex onto the surface. That replaces the geometry step in section 5 and reuses the
> model, UI, serialization, renderer and picking unchanged.

- **The cap is flat within each triangle.** With a plane chart the whole cap is planar; with a
  geographic chart the vertices sit on the datum but triangle interiors still chord across it.
  Full surface conformance needs subdivision (Option A).
- **Overlapping translucent fills blend in arbitrary order.** One packed draw with depth writes
  disabled means no back-to-front sorting between annotations. Accepted; the alternative is
  per-annotation passes, which defeats the packed design.
- **The cap rim is straight between control points** (step 5), so in plan view it does not
  exactly follow a terrain-projected outline that wiggles between corners.

---

## 1. Fix `getPolylinePoints` duplication (independent, do first)

`Drawing.Sg.fs:161-182` emits `startPoint, samples…, endPoint` **per segment**. Segment *i*'s
`endPoint` and segment *i+1*'s `startPoint` are the same corner by construction
(`Drawing-App.fs:161`), so every interior corner is duplicated; the last interior sample can
also coincide with `endPoint` (`Drawing-App.fs:171-179`, sample count is
`floor(len / samplingDistance)`), giving triples.

This is not a fill-specific problem. Duplicates become **zero-length segments** in
`linesNoIndirect` (`PackedRendering.fs:474-486`), which the thick-line geometry shader expands
via a direction normalize — `normalize(p1 - p0)` on a zero vector. A latent source of artifacts
today, independent of this feature.

Fix inside `getPolylinePoints` with an epsilon consecutive-duplicate filter. All 8 call sites
(`Drawing.Sg.fs:259,355,468`, `PackedRendering.fs:373,455,581,591,665`,
`OpcViewer/AnnotationViewer.fs:227`) are geometry consumers; none rely on duplicates.

**Commit separately** from the fill feature — standalone bug fix, wants its own before/after
check on line rendering.

## 2. Surface charts — new `src\PRo3D.Base\SurfaceChart.fs`

The ellipse code already has two ways to get from world space into a 2D domain where planar
geometry is valid, and back:

- **fitted plane** — `constructAndSampleFromPlane` (`EllipseAnnotation.fs:62-82`) via
  `GetWorldToPlane` / `GetPlaneToWorld`
- **geographic** — `constructAndSampleGeographical` (`:84-119`) via
  `CooTransformation.tryGetLatLonAlt` / `tryGetXYZFromLatLonAlt`, with `Conv` (`:9-16`)
  carrying the base altitude

These are the same abstraction twice. Make it explicit and use it everywhere planar geometry is
done on a curved surface:

```fsharp
/// A 2D chart of a neighbourhood of the surface: the domain in which planar
/// geometry (ellipse construction, triangulation, area) is valid.
/// Both directions are partial — geographic conversion can fail.
type SurfaceChart = {
    name    : string
    toChart : V3d -> V2d option
    toWorld : V2d -> V3d option
}

module SurfaceChart =
    val ofPlane      : Plane3d -> SurfaceChart                              // total
    val ofUpVector   : up: V3d -> origin: V3d -> SurfaceChart               // CrossSectionClipping.fs:10-24
    val geographic   : Planet -> SpiceReferenceSystem option -> basePoint: V3d -> SurfaceChart
```

`ofPlane` caches the two matrices rather than rebuilding them per point.

Place at `PRo3D.Base.fsproj` ~line 58, after `GisModels.fs` (54) and `CooTransformation.fs`
(43), before `Annotation\RegressionInfo.fs` (59).

### Why this matters for fills, not just tidiness

With a plane chart, `toWorld` lifts every vertex onto a **secant plane** — over a km-scale
polygon on Mars that plane cuts through the body and the cap sinks below the datum in the
middle. With a geographic chart, `toWorld` puts vertices back at the base *altitude*, so the
cap follows the body's curvature. For large annotations that is a materially better fill, and
it comes free once the abstraction exists.

### Chart-specific hazards to handle in `geographic`

- **Antimeridian seam.** A polygon crossing ±180° longitude wraps and triangulates wrongly.
  Unwrap longitudes relative to the first point (add ±360 to keep the ring contiguous).
- **Poles.** Longitude degenerates; reject a point set spanning a pole rather than emitting
  nonsense.
- Equirectangular lat/lon is neither conformal nor equal-area. Triangulation *topology* is
  unaffected (ear clipping only needs a consistent simple polygon) but ear quality degrades at
  high latitude — acceptable, worth a comment.

### Chart selection

v1 rule, explicit rather than heuristic: use the annotation's own construction chart when it
has one (ellipses do), else the fitted plane. Geographic stays **opt-in**. Note that
`Drawing-App.fs:98` currently hard-disables the geographic ellipse path (`let geo = false`);
this abstraction is what makes re-enabling it tractable, but that is a separate change.

### Follow-up callers (not this PR)

Four more places implement an ad-hoc chart and should migrate once this lands:
`AnnotationHelpers.calculatePolygonArea:109`, `AnnotationQuery.queryFunctionsFromPointsOnPlane:61`,
`CrossSection.buildPolygon:27`, and both `EllipticAnnotations.constructAndSample*`.

## 3. Model — `src\PRo3D.Base\Annotation\Annotation-Model.fs`

Add three fields to `Annotation` (record at `:474`):

```fsharp
    showFill  : bool
    fillColor : ColorInput
    fillAlpha : NumericInput      // 0.0 .. 1.0
```

Add an initial next to `textSize`/`thickness`:

```fsharp
    let fillAlpha = { value = 0.35; min = 0.0; max = 1.0; step = 0.05; format = "{0:0.00}" }
```

In `Annotation.make` (`:1034`) default to `showFill = false`, `fillColor = color`,
`fillAlpha = Initial.fillAlpha`.

The compiler will flag every other record-construction site; all take the same defaults:
`Annotation-Model.fs` `readV0`–`readV5` (`:534`, `:597`, `:661`, `:725`, `:792`, `:860`),
`SbmtImporter.fs:127,244`, `MeasurementsImporter.fs:160`.

### Serialization

**No version bump.** Follow the `crossSectionClipping` precedent (`:899-902`, `:989`): optional
read in `readV5` only, hard default in `readV0`–`readV4`. Serialize primitives rather than
`ColorInput`/`NumericInput` to avoid `Ext` plumbing:

```fsharp
// in readV5
let! showFill  = Json.tryRead "showFill"
let! fillColor = Json.tryRead "fillColor"      // C4b string
let! fillAlpha = Json.tryRead "fillAlpha"      // float
...
showFill  = showFill |> Option.defaultValue false
fillColor = fillColor |> Option.map (fun s -> { c = C4b.Parse s }) |> Option.defaultValue color
fillAlpha = { Initial.fillAlpha with value = fillAlpha |> Option.defaultValue Initial.fillAlpha.value }
```

**Write all three unconditionally.** Gating the write on `x.showFill` silently discards a
configured colour and alpha when the user turns the fill off and saves.

`Annotation-Model.g.fs` is regenerated by Adaptify on build (`PRo3D.Base.fsproj:8`).

## 4. UI — `src\PRo3D.Core\Drawing\Drawing-Properties.fs`

Add to `AnnotationProperties.Action` (`:17`) and `update` (`:33`):

```fsharp
| SetShowFill      of bool
| ChangeFillColor  of ColorPicker.Action
| SetFillAlpha     of Numeric.Action
```

Three rows in `view` (`:84`) and `viewBulk` (`:110`). Distinct ColorPicker ids so the pickers do
not collide: `"pro3dFill"` / `"pro3dBulkFill"` (the panel already uses `"pro3d"` / `"pro3dBulk"`
for this reason, see the comment at `:105-109`).

Gate the rows on `geometry` being a closed kind. `DnS` is excluded — it already has its own
plane visualisation via `showDns`.

## 5. Geometry — new `src\PRo3D.Base\Annotation\PolygonFill.fs`

```fsharp
module PolygonFill =
    type FillMesh = { positions : V3d[]; chart : SurfaceChart }   // TriangleList, world space

    /// Exposed for testing — the duplicate/degenerate handling is the fiddly part.
    val normalize      : eps: float -> V3d[] -> V3d[]
    val tryComputeFill : SurfaceChart -> V3d[] -> FillMesh option
```

Place after `SurfaceChart.fs`, before `Annotation-Model.fs`.

### Input: control points, not the sampled rim

Feed `a.points` (picked corners / the analytic ellipse ring), **not** `Sg.getPolylinePoints`:

- **Cost.** Default sampling distance is 1 metre (`Drawing-Model.fs:167` ←
  `Annotation.Initial.samplingAmount = 1.0`). A 4-corner polygon 200 m across gives ~800 rim
  points; a km-scale one gives thousands. Ear clipping is O(n²) and the enclosing `AVal.custom`
  re-runs on any annotation change. Control points are 4–20.
- **Robustness.** A terrain-following rim flattened into the chart is far more likely to
  self-intersect than a clean corner polygon.
- **The rim buys nothing.** A flat-per-triangle cap and a terrain-following outline disagree in
  3D by construction.
- The closing segment from `closePolyline` is built backwards
  (`startPoint = firstP; endPoint = lastP`, `Drawing-App.fs:55`), so the rim ring's last edge
  runs in reverse — harmless for a `LineList`, not for a boundary walk.

Ellipses are unaffected: segments are disabled for them (`Drawing-App.fs:150`), so `a.points`
already *is* the 200-sample analytic ring.

### Steps

1. **`normalize`** — drop consecutive duplicates within `eps`, then drop the trailing point if
   it is within `eps` of the first. `closePolyline` appends a duplicate first point to
   `a.points` **only** in the `Linear` branch (`Drawing-App.fs:68`); Viewpoint/Sky/Bookmark add
   a closing *segment* and leave `points` open (`:55-66`). `computeEllipsePoints` emits
   `samples+1` points, so ellipse rings always carry the duplicate
   (`EllipseConstruction.fs:69-78`). Require ≥ 3 remaining.

   **`normalize` is the fallback for legacy data, and it is not optional.** Section 1 fixes
   duplicates that `getPolylinePoints` *computes* from `segments`, so it applies to old and new
   projects alike — but it does not touch anything stored. Stored `a.points`, which is what the
   fill reads, can legitimately contain duplicates: `closePolyline`'s `Linear` branch appends
   `firstP` (`Drawing-App.fs:68`), `computeEllipsePoints` emits `samples+1`
   (`EllipseConstruction.fs:69-78`), a user can pick the same spot twice, and importers can
   carry them in. `normalize` therefore runs on every fill computation regardless of data age.

   Behind it, libtess is itself built to tolerate duplicate and degenerate contour vertices, so
   a duplicate that slips through yields `[]` or a valid tessellation rather than an exception.
   Worst case `tryComputeFill` returns `None`: no fill is drawn and the outline still renders.
   The failure mode is a missing fill, never a crash.

   **Do not normalize on load.** Rewriting stored points would silently change
   `calculatePolygonArea` results and the rendered outline for existing projects. Normalize at
   fill time only, and leave user data alone.

   **Do not filter `IsNaN` here.** No producer of `annotation.points` can emit NaN: interactive
   picking skips failed samples rather than substituting (`Drawing-App.fs:177-179`), ellipse
   reprojection uses `Array.choose` (`EllipseAnnotation.fs:69-79`), both importers build points
   from finite arithmetic (`SbmtImporter.fs:224-227`, `MeasurementsImporter.fs:76,138`). The
   `IsNaN` filters in `AnnotationHelpers.fs` (`:113`, `:228`, `:305`, `:419`) are consumer-side
   guards in front of plane fits, added in `cbbb5bd1`, not evidence of a producer — do not
   propagate them into new code.

2. **To chart**: `chart.toChart` over the points; any `None` → `None` overall (a partial
   projection means the chart does not cover this annotation).

   Where the chart comes from: `dnsResults.plane` for DnS/ellipse annotations makes the fill
   coincide exactly with the ring (`EllipseAnnotation.fs:62-64` built it on that plane); a
   `PlaneFitting.planeFit` chart for plain polygons makes the fill agree with the area reported
   by `AnnotationHelpers.calculatePolygonArea` (`:109`).

   **This is where NaN can genuinely reach the fill code.** `DipAndStrikeResults` initialises
   every field to `NaN`/`V3d.NaN` (`Annotation-Model.fs:237-242`) and that sentinel round-trips
   through the file format (`annotation_1.ann:112-114`), so a stored `dnsResults` may carry a
   degenerate plane. Validate the plane at chart construction — reject non-finite normal or
   distance and `Plane3d.Invalid` — and fall back to the fitted plane, then to `None`. Guard
   the chart, not the points.

3. **Tessellate carrying the original 3D points as an attribute.**

   ```fsharp
   PolygonTessellator.Triangulate(
       regions     = [ chartPts, worldPts ],          // V2d[] * V3d[]
       rule        = TessellationRule.EvenOdd,
       interpolate = fun ws vs -> Array.map2 (*) ws vs |> Array.sum)
   //  : list<Triangle2d<V3d>>
   ```

   `Aardvark.Geometry.PolygonTessellator.Triangulate` (`PolyRegion2d.fs:249-284` in
   aardvark.base) is libtess-backed and carries a per-vertex attribute `'a` through
   tessellation; invented vertices are resolved through the `interpolate` callback
   (`:259-260`).

   **Why this matters: the chart is used for topology only.** Passing the original world
   points as the attribute means every output vertex that coincides with an input point comes
   back as *exactly that point* — so the fill rim coincides with the drawn outline, terrain
   projection and all. Only triangle interiors chord across the surface.

   Without this, the fill would have to lift vertices back through `chart.toWorld`, which
   **re-flattens them onto the datum**. That is visibly wrong for ellipses in particular: their
   `a.points` are already surface-projected (`constructAndSampleFromPlane` raycasts them and
   `Drawing-App.fs:98-105` writes the result back), so a `toWorld` rim would float above or
   below the ring the user can see.

   > This is only necessary because the tessellator has no *indexed* output — it returns
   > triangles, not indices into the input. Indices would not actually suffice anyway: boolean
   > ops and self-intersection resolution create vertices corresponding to no input index. The
   > attribute channel is the more general answer and it already exists.

   `chart.toWorld` is therefore **not needed on the fill path at all**. Keep it on
   `SurfaceChart` — the ellipse sampler and any future Option A subdivision do need it.

4. **Emit** the `Triangle2d<V3d>` list as a flat world-space triangle soup, taking the `V3d`
   attribute of each vertex and discarding the 2D position. No index buffer — the packed draw
   concatenates many annotations.

5. **Reject empty results.** The tessellator returns `[]` for collinear and degenerate input,
   so that is the only rejection needed — no special-casing, no `try/catch`, and no manual
   winding fix (libtess normalises orientation via the `TessellationRule`). The signed-area
   loop from `CrossSectionClipping.fs:36-45` is *not* needed here.

   Self-intersecting input does **not** fail — it resolves by winding rule into a non-empty
   region. Pin the actual behaviour with a test (section 9) rather than assuming.

> **Where `PolyRegion` fits.** The attributed path above covers the simple fill and needs no
> region type. `Aardvark.Geometry.PolyRegion` (`PolyRegion2d.fs:485-616`) is the *region*
> abstraction — boolean ops plus `containsPoint` — and it is what section 10 needs. Note it is
> attribute-free (`private(polygons : list<Polygon2d>)`, `:485`) and its `Triangulate()` calls
> the non-attributed `LibTess.triangulate` (`:493`), so the world-point channel does **not**
> survive a union. That gap is section 10's problem, not this PR's.

> **Keep `FillMesh` region-capable from the start.** A triangle soup is a triangle soup whether
> or not it came from a region with holes or several components, so the renderer needs no
> changes when merge/split starts producing them. The *model* stays a single ring for this PR;
> that is where the cost lands later.

## 6. Renderer — `src\PRo3D.Core\Drawing\PackedRendering.fs`

New `fills` function modelled on `linesNoIndirect` (`:435-518`):

```fsharp
let fills (depthOffset : aval<float>) (annoSet : aset<Guid * AdaptiveAnnotation>) (view : aval<M44d>) =
```

Inside a single `AVal.custom`, for each annotation where `visible && showFill` and `geometry` is
a closed kind: read `anno.points`, build the chart, call `PolygonFill.tryComputeFill`, append
positions and one `C4f` per vertex from `fillColor.c` with `A = fillAlpha`.

Transform vertices into model space with the shared `modelTrafo.Backward`, reusing the
first-annotation-wins `mutable modelTrafo` from `:462-468`.

> **That trick is correct, do not "fix" it.** `getPolylinePoints` and `points` are world-space,
> so the trafo serves only as a float32 precision pivot; any common pivot works. It is not a
> per-annotation model transform.

```fsharp
Sg.draw IndexedGeometryMode.TriangleList
|> Sg.vertexAttribute DefaultSemantic.Positions ...
|> Sg.vertexAttribute DefaultSemantic.Colors ...
|> Sg.uniform "MV" mv
|> Sg.uniform "DepthOffset" (depthOffset |> AVal.map (fun d -> (d * fillOffsetFactor) / (100.0 - 0.1)))
```

Shader: `StableLight.stableTrafo'` (`:30-77`) + `DefaultSurfaces.vertexColor` +
`PRo3D.Base.Shader.DepthOffset.depthOffsetFS`.

> **Shader detail to verify at implementation time.** `depthOffsetFS` computes
> `(v.pos.Z - offset) / v.pos.W` (`Utilities.fs:261-293`) and needs clip-space position with
> `W` intact. The line path only works because the geometry shader explicitly restores `W`
> (`PackedRendering.fs:298-301`, comment: *"restore W component for depthOffset"*). There is no
> geometry shader here, so a vertex shader emitting `viewProj * p` is already correct — but
> confirm `stableTrafo'` passes clip position through rather than perspective-dividing.

Render state:

- `Sg.blendMode BlendMode.Blend`
- `Sg.writeBuffers' [WriteBuffer.Color DefaultSemantic.Colors]` — **no depth write**
- depth test per open decision (i)
- a dedicated `RenderPass` ordered **before** the packed lines pass, so outlines land on top.
  Do not rely on `Sg.ofList` ordering.
- `fillOffsetFactor`: a cap needs a much larger bias than a surface-hugging line. Start at
  ~5× `config.offset`; promote to its own `ViewConfigModel` numeric input
  (`ViewConfigModel.fs:156,186`) if 5× is not a good universal default.

`CullMode.None` and `FillMode.Fill` are already applied by `Viewer.fs:2365-2378` — which
matters, because the winding fix in 5.3 guarantees consistent orientation, not front-facing.

### Caching

The enclosing `AVal.custom` re-runs on any annotation change and re-triangulates everything.
Add a `Dictionary<Guid, V3d[] * FillMesh>` keyed on annotation id + point-array reference from
the start — the triangulation is a pure function of the points and the chart, and it is ~10
lines.

## 7. Wiring — `src\PRo3D.Core\Drawing\Drawing-App.fs`

Packed branch (`:866-946`): build `packedFills` next to `packedPoints` (`:926`), prepend to the
overlay list (`:930-936`). Legacy branch (`:948-979`): per-annotation construction wrapped in
`Sg.trafo t`.

> The two branches **do not** handle the SPICE reference-system trafo the same way: the packed
> path drops it (`:886`, `:943`), the legacy path applies it (`:961`, `:977`). Tracked as
> [pro3d-space/PRo3D#672](https://github.com/pro3d-space/PRo3D/issues/672). Until fixed, the
> fill must mirror whatever the *outline* does in each branch, or fill and outline visibly
> separate for annotations carrying a reference system. This is why a single shared fill sg
> cannot serve both branches.

`PRo3D.Snapshots\Program.fs:89` sets `usePackedAnnotationRendering <- false`, so skipping the
legacy branch means snapshots silently render without fills.

## 7b. Default fill in the drawing toolbar — implemented

Two controls beside thickness and sampling in `viewAnnotationToolsHorizontal`
(`Drawing.UI.fs:83-93`): whether the next annotation is filled, and at what alpha.

- `DrawingModel.fillNewAnnotations : bool` and `defaultFillAlpha : NumericInput`
- `SetFillNewAnnotations` / `ChangeDefaultFillAlpha`, handled beside `ChangeThickness`
- applied in `addPoint`'s existing `with` clause (`Drawing-App.fs:198`) — `Annotation.make`'s
  signature is untouched

**No fill colour control, deliberately.** `Drawing.UI.fs:87` records that the tool-level colour
picker was removed because annotation colour comes from the active group's default. A toolbar
fill colour would reintroduce exactly that and give one annotation two colour sources.
`Annotation.make` already sets `fillColor = color`, so the fill follows the group colour for
free; the properties panel still allows a per-annotation override.

Session state, like the controls next to it: `drawing : DrawingModel` lives on `Model`, not
`Scene` (`Viewer-Model.fs:598`), so geometry, thickness, sampling and now the fill defaults all
reset each launch.

## 8. Picking — build it, clickable by default

`Sg.pickable'` uses the line bounding box and the pick target only rasterizes lines
(`pickRenderTarget:522-550`), so clicking inside a filled polygon does not select it today.
Fix: feed the fill triangles into the same pick render target with a matching `ObjId` vertex
attribute and the existing `Picking.pickId` shader (`:307-331`) — same geometry as the visible
fill, one extra draw in an offscreen pass.

Add a config flag to disable it (fills clickable **on** by default).

> An earlier draft argued for defaulting this off, on the grounds that a large filled polygon
> would swallow clicks meant for drawing a new annotation on the terrain inside it. **That
> hazard does not exist**: `allowAnnotationPicking` (`Viewer.fs:2248-2256`) is true only in
> `Interactions.PickAnnotation` and `Interactions.DrawLog`, so annotation picking is off
> entirely while drawing. The modes are mutually exclusive.

The residual case the toggle exists for is selecting an annotation that lies *behind* a large
fill while in `PickAnnotation` mode.

Note the depth interaction: the visible fill does not write depth (section 6), but the pick
target is a separate pass with its own depth buffer, so fill and line pick geometry compete
there normally. Give the fill the same `DepthOffset` treatment as the visible draw so a fill
does not win the pick against its own outline.

Once merge/split lands, this can be reimplemented as `PolyRegion.containsPoint` in chart space
and the pick-target geometry dropped entirely (section 10).

### The object-id space — implemented

Line and fill draws both write object ids indexing the same array. They originally agreed only
because each enumerated `annoSet.Content` independently and happened to see the same order —
two separate `AVal.custom` nodes with nothing enforcing agreement. A drift would have shown up
as clicking one annotation and selecting another.

`PackedRendering.orderedAnnotations` is now the single cached ordering both consume; object id N
*is* index N of that array, so `fills` uses the loop index directly and its counter is gone. Any
future packed draw that writes `ObjId` must take its ids from the same ordering.

> Unrelated: a report of hover highlighting while ctrl+click failed to select is **not** this.
> Ctrl is a stateful toggle that flips `model.pick` on key-up (`Viewer.fs:1571-1576`), and
> selection is gated on `match (act, model.draw, model.pick)` needing `(_, false, true)`
> (`Drawing-App.fs:615`) while hover is gated only by `allowAnnotationPicking`. A parity mismatch
> produces exactly that symptom. Pre-existing; worth its own look if it recurs.

## 9. Unit tests — `src\Tests\PolygonFillTests.fs`

Expecto, following `TriangleSetTests.fs`; register in `Program.fs` and add to `Tests.fsproj`.
The whole filling logic is pure and therefore fully testable without a renderer — the only part
that is not is the sg construction in step 6.

### Point normalisation — the duplicate machinery

This is where the real defects live (`getPolylinePoints` produces doubles *and* triples), so
test `normalize` directly rather than only through `tryComputeFill`:

- open ring passes through unchanged
- trailing point exactly equal to first is dropped
- trailing point within `eps` of first is dropped; just outside `eps` is kept
- the `getPolylinePoints` shape `[a; b; b; c; c; a]` → `[a; b; c]`
- the triple shape `[a; b; b; b; c; c; c; a]` → `[a; b; c]`
- all-identical points → `None` from `tryComputeFill`
- exactly 2 distinct points padded with duplicates → `None` (must not be fooled into counting 3)
- fewer than 3 points → `None`
- a legitimate ring that revisits a *non-adjacent* point is **not** silently collapsed
- `normalize` is idempotent: `normalize (normalize xs) = normalize xs`

### Charts

- `ofPlane` round-trip: `toWorld (toChart p) ≈ p` for points on the plane; for off-plane points
  the result lands *on* the plane (`plane.Height v ≈ 0`)
- `geographic` round-trip within tolerance for several lat/lons; returns `None` where
  `CooTransformation` fails rather than emitting NaN
- antimeridian: a ring spanning ±180° longitude produces a contiguous chart polygon (unwrapped),
  and its triangulation has the same triangle count as the same ring away from the seam
- pole spanning → rejected
- **chart independence**: the same polygon under a plane chart and a geographic chart yields the
  same triangle *count* and the same index topology, differing only in vertex positions

### Triangulation and invariants

`PolyRegion` owns triangulation and winding, so these test *our* use of it, not its internals:

- convex square → summed triangle area equals the square's
- concave L → summed area equals the polygon's; no triangle centroid falls outside the polygon
  (catches ears cut across a concavity)
- 200-sample ellipse ring → area ≈ `πab` within tolerance
- collinear points → `None` (via `isEmpty`)
- self-intersecting bowtie → a **non-empty** region whose area is the winding-rule resolution,
  not a crash and not garbage. Pin the actual behaviour with a test rather than assuming it;
  this is the case an earlier draft wrongly expected to fail.
- winding: CW and CCW inputs produce the same summed area (regions are orientation-normalised)
- degenerate `dnsResults` plane (all-NaN, per `Annotation-Model.fs:237-242`) → falls back to the
  fitted plane and still produces a fill, rather than `None`
- **every output vertex lies on the chart surface** (plane chart: `plane.Height v ≈ 0`)
- **summed triangle area == `AnnotationHelpers.calculatePolygonArea`** for the same points —
  ties the rendered fill to the number shown in the properties panel
- a region with a hole (constructed via `PolyRegion.difference`) triangulates to a mesh whose
  summed area equals outer − inner — proves the renderer path is region-ready for section 10

## 10. Next feature: merge / split annotations (plan ahead, do not build yet)

Boolean ops on annotations are the natural follow-up and they fall out of this design almost
entirely: both prerequisites — a shared 2D chart and a region type — are introduced here.

```
merge  a b = PolyRegion.union        (region a) (region b)
split  a b = PolyRegion.difference   (region a) (region b)
common a b = PolyRegion.intersection (region a) (region b)
```

### What this PR should get right so the follow-up is cheap

- **`SurfaceChart` is load-bearing, not tidiness.** Two annotations can only be combined in a
  *common* chart. Two polygons with different fitted planes have no meaningful union until one
  chart is chosen for both — so `SurfaceChart` needs to be a value that can be constructed
  independently of any single annotation and shared. The signature in section 2 already allows
  that; do not let it collapse into "the annotation's plane".
- **Keep the renderer region-capable** (section 5) so merged results render without changes.

### The one hard problem, worth deciding early

**A region is not a ring, but `Annotation.points : IndexList<V3d>` is.** A union can produce a
polygon with holes, or several disjoint components. `toPolygons` will hand back multiple rings;
the model cannot store them. Three ways out:

1. **Restrict** — only commit a merge when the result is a single hole-free ring; refuse with a
   message otherwise. Zero model change, but refuses many legitimate merges.
2. **Explode** — emit one annotation per component and discard holes. No model change, but
   silently wrong for anything with a hole.
3. **Extend the model** to a list of rings with orientation. Correct, but it is an
   `Annotation` format change: version bump to 6, a `readV6`, and every consumer that assumes a
   single ring (`calculatePolygonArea`, the exporters, `getPolylinePoints`, the line packer)
   has to cope.

(3) is the honest answer and (1) is the cheap one. Worth deciding before the merge/split PR
starts, because it determines whether that PR is a week or a day.

### An aardvark.base contribution worth making

The attribute channel that makes the fill rim exact (section 5, step 3) **does not survive a
boolean op**. `PolygonTessellator.Triangulate` and `Combine` both carry a per-vertex `'a`
through libtess via the `interpolate : float[] -> 'a[] -> 'a` callback
(`PolyRegion2d.fs:238-284`), but `PolyRegion` itself is `list<Polygon2d>` (`:485`) — plain,
attribute-free — and its operators (`+`, `-`, `*`, `^^^`) drop any payload.

So a merged annotation cannot carry its original terrain-projected world points through the
union, and its fill would fall back to `chart.toWorld` — re-flattening the rim onto the datum,
exactly the artifact section 5 avoids.

The fix belongs upstream, not in PRo3D: a `PolyRegion<'a>` whose boolean operators thread the
attribute through, taking the same `interpolate` callback the tessellator already accepts. The
underlying machinery is already wired — libtess's `CombineCallback` is invoked for precisely
the vertices a boolean op invents (`:259-260`). It is a matter of threading `'a` through the
type and its operators.

Note that an *indexed* variant would not be sufficient: boolean ops create vertices that
correspond to no input index, so "60% of A + 40% of B" has to be expressible. The attribute +
`interpolate` design already handles that; indices cannot.

Filed upstream as [aardvark-platform/aardvark.base#100](https://github.com/aardvark-platform/aardvark.base/issues/100).
Source is checked out at `C:\Users\haral\Desktop\aardvark\aardvark.base`.

### Vendoring — the answer for *this* feature, and it does not block on upstream

**Nothing needs vendoring for the fill.** `Aardvark.Geometry.PolygonTessellator.Triangulate`
with its `'a` attribute channel ships in the 5.3.26 package PRo3D already resolves (verified by
reflection against the packaged assembly), so section 5 works against the released library.

Only boolean ops need the attributed `boundary`, and #100 must not gate them. So when merge/split
starts, vendor rather than wait: copy `src/Aardvark.Geometry/PolyRegion2d.fs` into `PRo3D.Base`,
implement the attributed `boundary` + `PolyRegion<'a>` there, use it, and upstream the same diff.
Feasibility is confirmed:

- **Self-contained** — one 716-line file whose only external dependency is
  `Unofficial.LibTessDotNet` 2.0.2, a public NuGet package (`Aardvark.Geometry.fsproj:36`).
  `PRo3D.Base` would take a direct reference on it.
- **Licence** — aardvark.base is Apache 2.0; vendor with attribution and keep the header.
- **`module private LibTess`** is file-private, so the file cannot be extended from outside —
  copying it wholesale is the only option, which is what vendoring means anyway.

Two rules that keep the upstream PR clean:

1. **Put it in a different namespace** (`PRo3D.Base.Geometry`, not `Aardvark.Geometry`).
   `PRo3D.Base` already references the real `Aardvark.Geometry`, so an identical
   namespace + type name would be ambiguous at every use site.
2. **Keep the file byte-identical to upstream** except the namespace line and the new
   attributed additions. No reformatting, no drive-by improvements. Then the upstream PR is
   the diff minus the namespace change.

Record the upstream commit the copy was taken from in a header comment, and mark the file
`TODO: delete when #100 lands`. The alternative — doing the work upstream first and waiting for
a release — blocks PRo3D on aardvark's cadence for no benefit.

### Free wins to fold in at the same time

- `AnnotationQuery.queryFunctionsFromPointsOnPlane` (`:61-97`) currently reduces the annotation
  to its **convex hull** (`:81`, `ComputeConvexHullIndexPolygon`) before doing point-in-polygon.
  Concave annotations therefore over-select today. `PolyRegion.containsPoint` fixes that
  outright.
- Section 8's optional fill-picking becomes `containsPoint` in chart space — no GPU pick
  geometry needed at all.
- `AnnotationHelpers.calculatePolygonArea` (`:109`) and `CrossSection.buildPolygon` (`:27`) are
  the other two ad-hoc charts; migrating them onto `SurfaceChart` + `PolyRegion` retires the
  last of the hand-rolled polygon code.

## 11. Verification beyond unit tests

- Round-trip: load `src\Tests\data\mola-annotations.pro3d.ann` (no fill fields) → defaults
  applied, no read error → toggle fill on → save → reload → survives → toggle off, save, reload
  → colour and alpha survive.
- Old-reader compat: a file saved with fills still declares `version 5`; older builds ignore
  unknown keys. Confirm against a 6.0.0-prerelease build.
- Visual: polygon + `AxisEllipse` on an OPC scene — fill sits on the terrain, outline on top,
  occlusion per decision (i).
- Step 1 regression: line rendering unchanged (or improved) after the `getPolylinePoints`
  dedupe — check a dense polygon and a DnS annotation.
- Snapshot path: render a snapshot containing a filled annotation (the legacy-branch check).

## Order of work

1 (standalone, own commit) → 2 → 5 + 9 (chart and fill logic land with their tests) → 3 → 6 →
7 → 8 (picking) → 4 (UI) → 11 (verification).

Section 10 is the next PR, not this one.
