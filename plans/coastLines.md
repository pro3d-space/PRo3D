# Coast Lines — one plane, traced across the surface in the shader

**Goal.** Take **one** plane and mark every surface fragment that lies within a given
thickness of it. The result is a "shoreline": the trace of that plane across the
topography, the way a water level draws a coast.

The plane comes from the annotation selection:

- **one annotation selected** → its fitted plane;
- **several annotations, or a whole group, selected** → the **average** plane of the
  selection (section 4).

Selecting a group of bedding measurements, averaging them into one plane and watching where
that plane outcrops across the scene is the cross-site correlation use case that motivates
this.

The name is the sketch name. In geology this is a *plane trace* / structure contour; the
docs page should say both so the term is findable.

**One plane is the whole design constraint.** No uniform arrays, no per-fragment loop, no
cap on the selection size, no per-annotation colours, no fill-rate question to measure. The
shader is a single dot product. Everything expensive or uncertain about this feature lives
on the CPU, in `double`, once per frame.

Every DnS annotation already carries a fitted `Plane3d`
(`DipAndStrikeResults.plane`, [Annotation-Model.fs:160](../src/PRo3D.Base/Annotation/Annotation-Model.fs:160)),
so nothing needs fitting either — the feature is *average, transform to view space, upload,
test*.

---

## 1. Decisions taken

- **Shader distance test, not CPU mesh slicing.** Slicing the OPC triangles against the
  plane on the CPU would give crisp, exportable, pickable polylines, but needs a pass over
  the LOD hierarchy on every plane edit and every LOD change. For interactive "nudge the
  selection and watch the trace move" exploration the shader wins outright, and it is
  resolution- and LOD-independent for free. CPU slicing is the right implementation *later*,
  if the traces ever need to become annotations or be exported; it replaces section 6 only.
- **View space, `float32` in the shader, plane composed on the CPU in `double`.**
  Non-negotiable at planetary scale — section 3.
- **Average by mean unit normal** (Fisher/vector mean), not by separately averaging dip
  azimuth and dip angle — section 4, including the sign trap that makes a naive
  implementation cancel planes out.
- **Report the resultant length `R` next to the average**, and refuse to draw below a
  threshold. A selection with no preferred orientation averages to a confident, meaningless
  plane. The rose diagram already established this exact guard (`minResultant = 0.05`,
  [RoseDiagram.fs](../src/PRo3D.Viewer/Viewer/RoseDiagram.fs)) — reuse the concept and the
  threshold so the two features agree about when a mean is real.
- **Transient viewer state, not scene state** (section 5). The rose set this precedent, and
  the reason is concrete: the feature is driven by the *selection*, and `selectedLeaves` is
  explicitly not persisted ([Groups-Model.fs:363](../src/PRo3D.Core/Groups-Model.fs:363)).
  Persisting `enabled` while the selection restores empty would give a scene that says "on"
  and shows nothing.
- **The plane is clipped to a radius around the selection.** A plane fitted to a 2 m
  annotation is meaningless 4 km away; extended globally it paints a confident, wrong line
  across the whole scene. The trace fades out beyond `extentRadius` of the mean centre of
  mass, and the default radius is *derived from the selection's own spread* (section 4), not
  a fixed number.
- **Optional repeat spacing.** `distance mod spacing` instead of `distance`, giving a whole
  parallel family of traces from one plane — a bedding sequence rather than a single bed.
  Two lines in the shader, and the geologically interesting mode; `contourLines`
  ([Utilities.fs:749](../src/PRo3D.Base/Utilities.fs:749)) already does the same modulo trick
  against a texture value.
- **Drawn last in the effect stack**, so the trace colour is not modulated by lighting or
  shadows. `contourLines` sits *before* `solarShadingLS` and so gets shaded; that is right
  for contours as a terrain property and wrong for this, which is an interpretive overlay.
  Note the difference in the docs page.

---

## 2. Scope

In:

- OPC surfaces (`surfaceEffect`, [Viewer-Utils.fs:958](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:958))
  and OBJ surfaces (`objEffect`, [Viewer-Utils.fs:944](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:944)).
  Both already run `Shader.stableTrafo`, which writes the `ViewPos` varying the fragment
  stage needs, so OBJ costs one extra line.
- Multi-selection (`selectedLeaves`, which a group *Select All* fills) **and** the single
  selected annotation (`singleSelectLeaf`) when the multi-selection is empty.
- Both `Geometry.DnS` and `Geometry.Polyline` sources, behind the same two toggles the rose
  diagram uses, so the two panels agree on what "the selection's planes" means.

Out:

- More than one plane at a time, in any form.
- Exporting traces as annotations or geometry.
- Per-annotation persistence of a coast-line flag.
- Any change to how individual planes are fitted.

---

## 3. Precision — why this is a view-space shader

Read [ai/CONVENTIONS.md](../ai/CONVENTIONS.md#numerical-precision--read-this-before-touching-geometry-or-shaders)
first. The rule the feature lives or dies by:

> Geometry lives in local space, the MVP is composed on the CPU in `double`, and the
> fragment stage works in view space.

A world-space plane test in the shader would be a `float32` dot product against coordinates
of ~3.4e6 m on Mars — about 0.25 m of representable resolution, and worse on larger bodies
or body-fixed frames with big offsets. A line thickness of 0.1 m is then pure noise. In view
space the fragment's `vp` is camera-relative: at 10 km the resolution is ~1 mm, three orders
of magnitude better than the smallest line width anyone will ask for.

So, once per frame, **on the CPU in `double`**:

```fsharp
// plane : Plane3d (world, already averaged), com : V3d (world anchor), view : CameraView
let mv    = (view |> CameraView.viewTrafo).Forward          // double
let nView = mv.TransformDir plane.Normal |> Vec.normalize   // rigid: stays unit
let p0    = com - plane.Normal * plane.Height com           // com projected onto the plane
let dView = Vec.dot nView (mv.TransformPos p0)
// upload V4f(nView, dView) -> signed distance of a view-space point x is dot(n, x) - d
```

`p0` is any point on the plane mathematically; projecting the anchor onto it
(`Plane3d.Height` is the signed distance, as used throughout
[AnnotationHelpers.fs](../src/PRo3D.Base/Annotation/AnnotationHelpers.fs)) keeps the
intermediate near the data instead of near the body centre. The result `dView` is
camera-relative and therefore small, which is the whole point.

The surface's own `Sg.trafo` (`TransformationApp.fullTrafo`, flipZ, sketchfab, …) needs no
special handling: both the fragment and the plane arrive in view space, where the surface
placement has already been applied. Same reason `HomePositionViewSpace` and
`CursorViewSpace` work ([Viewer-Utils.fs:420-436](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:420)).

**Use the `view` that is actually rendering the pass.** `createGroupedSgs` takes `view` as a
parameter and `renderCommands` calls it once for the main camera and once for the instrument
cam ([Viewer.fs:2677](../src/PRo3D.Viewer/Viewer/Viewer.fs:2677)). Reaching for
`m.navigation.camera.view` instead would silently draw the main camera's plane into the
instrument view.

---

## 4. The average plane (the part that can actually be wrong)

New module `CoastLines` in `CoastLinesApp.fs`, **pure**, so the tests reach it without a GL
context — the same split `RoseDiagram.includes` made for the rose, for the same reason.

### 4.1 The sign trap — read this before writing the average

`DipAndStrikeResults.plane` is stored **as the regression returned it**. The up-orientation
correction in `calculateDipAndStrikeResults`
([AnnotationHelpers.fs:345](../src/PRo3D.Base/Annotation/AnnotationHelpers.fs:345)) is applied
to a *local* `planeNormal` used to derive dip and strike — it is never written back to
`plane`. So two annotations on the same bed can perfectly well carry normals pointing in
**opposite** directions.

Average those raw normals and they cancel: the mean is near zero, `R` collapses, and the
feature reports "no preferred orientation" for a selection that is in fact perfectly
consistent. This is the one bug in this feature that produces a plausible-looking wrong
answer rather than a visible failure.

Every normal must therefore be sign-corrected with the existing helper before it is used:

```fsharp
let orient (up : V3d) (p : Plane3d) =
    match DipAndStrike.signedOrientation up p with
    | -1 -> -p.Normal
    | _  ->  p.Normal
```

`up` comes from `m.scene.referenceSystem.up.value`, the same source
`AnnotationProperties.viewResults` already uses.

### 4.2 The average itself

```fsharp
type AveragePlane = {
    plane      : Plane3d   // world space
    anchor     : V3d       // mean centre of mass, world space
    resultant  : float     // R in [0,1] - 1 = all normals identical
    spread     : float     // max distance from anchor to a contributing centre of mass
    count      : int
}
```

Mean unit normal (vector / Fisher mean), which is the standard structural-geology answer and
is consistent with the rose's mean resultant vector:

```fsharp
let sum       = normals |> Array.fold (+) V3d.Zero      // all already sign-corrected
let resultant = sum.Length / float normals.Length       // R
let normal    = sum |> Vec.normalize
let anchor    = (centres |> Array.fold (+) V3d.Zero) / float centres.Length
let plane     = Plane3d(normal, anchor)
```

Chosen over separately averaging dip azimuth (circularly) and dip angle (arithmetically)
because it needs no `up`/`north` frame beyond the sign correction, degrades gracefully as
dips approach vertical, and gives `R` for free. The two methods agree closely for a tight
cluster and diverge for a scattered one — which is exactly the case where `R` is already
telling the user not to trust the answer.

`n = 1` is not a special case: `R = 1`, `anchor` is that annotation's centre of mass, and the
average plane *is* its plane.

### 4.3 Guards

- `R < 0.05` (`RoseDiagram.minResultant`) → do not draw; the panel says the selection has no
  preferred orientation. Without this, a scattered selection still yields a unit normal
  (the sum's direction is arbitrary but non-zero) and would trace a confident wrong plane.
- No contributing annotation → do not draw; the panel says so explicitly (mirror the rose's
  empty-state wording), so an empty result is never mistaken for a broken shader.
- Invalid / NaN normals filtered before the sum, via the same gate the rose uses.

### 4.4 Default extent radius

`spread` (max distance from `anchor` to any contributing centre of mass) is the selection's
own footprint, so the default extent radius is `max(spread, minimum) * multiplier` with the
multiplier under user control and defaulting to something modest (1.5). One annotation gives
`spread = 0`, so the minimum floor is what sizes that case — a few tens of metres.

This is better than a fixed number: it scales with the outcrop the user actually selected,
and it makes the extrapolation the user asks for explicit (raise the multiplier) rather than
accidental.

### 4.5 Collecting the selection

Use the aggregate shape the rose panel already worked out and documented at
[ViewerGUI.fs:972](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs:972) — read it before writing this,
it is the non-obvious part:

- one `AMap.filter` over `annotations.flat` against the selected ids, **not** N
  `AMap.tryFind` calls (`tryFind` re-evaluates on every change of the map, so N lookups turn
  one annotation edit into N invalidations);
- `AMap.chooseA` to cache `(geometry, plane, centerOfMass)` per annotation, so editing one
  annotation re-reads one entry;
- the source toggles applied at the *leaf*, filtering the already-collected map, so clicking
  a checkbox does not tear the per-annotation subtree down.

See [ai/CONVENTIONS.md §6](../ai/CONVENTIONS.md#6-deriving-an-aggregate-from-an-adaptive-collection).

The selection source:

```fsharp
let ids =
    adaptive {
        let! multi = annotations.selectedLeaves.Content
        if HashSet.isEmpty multi then
            let! single = annotations.singleSelectLeaf
            return single |> Option.toList |> HashSet.ofList
        else
            return multi |> HashSet.map (fun ts -> ts.id)
    }
```

That single expression covers "either a selection or a group selection": a group's *Select
All* fills `selectedLeaves`, and clicking one annotation leaves it empty and sets
`singleSelectLeaf`.

---

## 5. Model — `src/PRo3D.Core/CoastLines-Model.fs`

Follows the `CrossSectionModel` shape ([CrossSection-Model.fs](../src/PRo3D.Core/CrossSection-Model.fs)):
a `[<ModelType>]` record of `NumericInput` / `ColorInput` fields, an `initial`, and a sibling
`CoastLinesApp` with actions and `update`.

```fsharp
[<ModelType>]
type CoastLinesModel = {
    enabled       : bool
    usePolyline   : bool          // mirrors the rose diagram's two source toggles
    useDnS        : bool
    thickness     : NumericInput  // metres, full width of the band
    smoothing     : NumericInput  // metres, smoothstep falloff either side
    extentFactor  : NumericInput  // multiplier on the selection's own spread (4.4)
    extentMinimum : NumericInput  // metres, floor for a single annotation
    repeatEnabled : bool
    repeatSpacing : NumericInput  // metres between parallel traces
    color         : ColorInput
}
```

Defaults: `enabled = false`, `useDnS = true`, `usePolyline = false`, `thickness = 0.25`,
`smoothing = 0.1`, `extentFactor = 1.5`, `extentMinimum = 25.0`, `repeatEnabled = false`,
`repeatSpacing = 1.0`, `color = C4b.Red`.

Wiring, all mechanical:

- `PRo3D.Core.fsproj` — `CoastLines-Model.fs` then `CoastLinesApp.fs`, next to the
  CrossSection pair ([lines 106/108](../src/PRo3D.Core/PRo3D.Core.fsproj:106)).
- `Viewer-Model.fs` — `coastLines : CoastLinesModel` on `Model`, and
  `ViewerAction.CoastLinesMessage of CoastLinesAction`.
- `Viewer.fs` — one `| CoastLinesMessage msg, _ -> { m with coastLines = CoastLinesApp.update m.coastLines msg }` arm.
- `InitialViewerModel.fs` — `coastLines = CoastLinesModel.initial`.
- Run `adapt.sh` for the `.g.fs`. **Never hand-edit a `.g.fs`.**

No `Scene` field, so no serialization and no `Scene.current` bump.

---

## 6. Shader — `CoastLineShader` in `Viewer-Utils.fs`

Put it directly after `CrossSectionShader`
([Viewer-Utils.fs:832](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:832)), so the surface effect
stack and everything it depends on stay in one file.

```fsharp
module CoastLineShader =
    open FShade

    type UniformScope with
        member x.CoastLineEnabled : bool = x?CoastLineEnabled
        /// xyz = view-space unit normal, w = view-space plane offset d
        member x.CoastLinePlane   : V4f  = x?CoastLinePlane
        /// xyz = view-space anchor, w = extent radius (metres)
        member x.CoastLineExtent  : V4f  = x?CoastLineExtent
        /// x = thickness, y = smoothing, z = repeat spacing (<= 0 disables repeat)
        member x.CoastLineParams  : V4f  = x?CoastLineParams
        member x.CoastLineColor   : V4f  = x?CoastLineColor

    let coastLine (v : Effects.Vertex) =
        fragment {
            if not uniform.CoastLineEnabled then
                return v.c
            else
                let p         = v.vp.XYZ
                let pl        = uniform.CoastLinePlane
                let ext       = uniform.CoastLineExtent
                let par       = uniform.CoastLineParams
                let halfWidth = par.X * 0.5f
                let smooth    = par.Y
                let spacing   = par.Z

                // signed distance to the plane, view space, metres
                let signed = Vec.dot pl.XYZ p - pl.W

                // repeat mode folds the distance into one spacing interval
                let d =
                    if spacing > 0.0f then
                        let m = signed - spacing * floor (signed / spacing)
                        min m (spacing - m)              // distance to nearest repeat
                    else
                        abs signed

                // band: 1 inside halfWidth, smoothstep out over `smooth`
                let band = 1.0f - Fun.Smoothstep(d, halfWidth, halfWidth + smooth)

                // fade out away from the selection the plane was averaged from
                let r    = Vec.distance ext.XYZ p
                let fade = 1.0f - Fun.Smoothstep(r, ext.W, ext.W * 1.15f)

                let a = band * fade
                return V4f(v.c.XYZ * (1.0f - a) + uniform.CoastLineColor.XYZ * a, v.c.W)
        }
```

Notes:

- `v.vp` is the `ViewPos` varying written by `Shader.stableTrafo`
  ([Viewer-Utils.fs:790](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:790)) and already consumed
  by `textureOrLightingIfPossible` — nothing new to plumb.
- No loop, no array uniform, so no fill-rate question to measure and nothing to gate on
  `Config.limitedShaderCapabilities`. This is the payoff of the one-plane constraint.
- **`CoastLineEnabled` must gate the whole thing, and the uniforms must always be
  uploaded** — zero-filled when the feature is off, never left unbound. The reason is
  written up at [Viewer-Utils.fs:847](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:847) and in
  [docs/CrossSections.md](../docs/CrossSections.md): an unguarded per-fragment test against a
  binding whose value did not actually arrive produced a lattice of discarded fragments
  across the terrain on Apple Silicon, on that platform only.

**Placement in the effect stack** ([Viewer-Utils.fs:958](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:958)):
last, after `PRo3D.SPICE.Shaders.terrainSunShadow`, so the trace colour survives lighting and
shadowing. Add the same single line to `objEffect`.

---

## 7. Sg wiring

Bind the uniforms where the cross-section uniforms are bound, inside `createGroupedSgs`
([Viewer-Utils.fs:1268](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:1268)) — one binding for the
whole surface set, since the coast line is global rather than per-surface:

```fsharp
// aval<Option<view-space plane * extent>>; recomputed when the camera or the selection moves
let coastLine = CoastLines.viewSpacePlane view m.coastLines m.drawing.annotations m.scene.referenceSystem

|> Sg.uniform "CoastLineEnabled" (coastLine |> AVal.map Option.isSome)
|> Sg.uniform "CoastLinePlane"   (coastLine |> AVal.map (function Some (p,_) -> p | None -> V4f.Zero))
|> Sg.uniform "CoastLineExtent"  (coastLine |> AVal.map (function Some (_,e) -> e | None -> V4f.Zero))
|> Sg.uniform "CoastLineParams"  (…thickness, smoothing, spacing as V4f…)
|> Sg.uniform "CoastLineColor"   (m.coastLines.color.c |> AVal.map (fun c -> c.ToV4f()))
```

`coastLine` returning `None` folds together *disabled*, *nothing selected*, *no valid
planes* and *`R` below threshold* — one gate, one uniform, and the shader cannot be reached
with a half-valid plane.

Three things to get right:

1. The uniforms are always bound, zero-filled when `None`. See the Apple Silicon note in
   section 6.
2. `view` is the parameter of `createGroupedSgs`, not `m.navigation.camera.view` (section 3).
3. `getSurfacesScenegraphs` ([Viewer-Utils.fs:1038](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:1038),
   marked *"TODO TO refactor screenshot specific"*) is a **second, older** surface path that
   does not bind the cross-section uniforms either. Check which path snapshots actually take
   ([Viewer.fs:2751](../src/PRo3D.Viewer/Viewer/Viewer.fs:2751)) before claiming coast lines
   appear in snapshots; if it is the legacy path, either bind there too or say plainly in the
   docs page that snapshots do not show them yet.

---

## 8. UI

One new accordion in the Annotations panel, next to *Bulk Edit* / *Dip&Strike*
([ViewerGUI.fs:1516](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs:1516)):

```fsharp
GuiEx.accordion "Coast Lines" "map outline" false [
    Incremental.div AttributeMap.empty (AList.ofAValSingle (Annotations.viewCoastLines m))
]
```

Its own accordion rather than a section inside *Bulk Edit*, because the bulk panel refuses to
render below two selected annotations and coast lines must work for one.

Contents: an on/off button styled like the rose activation button, the Polyline / DnS
toggles, numeric rows for thickness / smoothing / extent factor / extent minimum, a repeat
checkbox with its spacing, and the colour picker.

Above the controls, a readout of **what plane is actually being drawn** — this is the part
that makes the averaging trustworthy, and it is cheap because the numbers already exist:

```
Average of 7 annotations — dip 34.2°, azimuth 118.7°, R = 0.94
```

derived from the averaged normal via the same `up`/`north` construction
`calculateDipAndStrikeResults` uses, so the numbers are directly comparable to the ones in
the *Dip&Strike* panel and to the rose's mean direction. The three failure states get
explicit text instead of a silent blank:

- nothing selected → *"Select an annotation, or a group, to trace its plane."*
- nothing contributes → *"No planes in the selection (enable a type, or select DnS /
  Polyline annotations)."* (the rose's wording)
- `R` below threshold → *"The selection has no preferred orientation (R = 0.03); the average
  plane would be meaningless."*

---

## 9. Tests

Pure, no GL, in the style of [Section13_ContourMultitexturing.fs](../src/Tests/Features/Section13_ContourMultitexturing.fs)
and [BulkAnnotationRoseTest.fs](../src/Tests/BulkAnnotationRoseTest.fs).

New `src/Tests/Features/Section21_CoastLines.fs`:

- defaults: disabled, DnS on, polyline off;
- `ToggleEnabled`, `SetThickness`, `SetExtentFactor`, `SetRepeatSpacing`, colour change each
  land on the model.

New `src/Tests/CoastLinePlaneTest.fs` — the parts that can be silently wrong:

- **the sign trap (4.1)**: two planes on the same bed, one constructed with a flipped normal.
  Averaging must give `R ≈ 1` and the correct normal. Without the `signedOrientation`
  correction this test yields `R ≈ 0` — it is the whole reason the test file exists;
- **average of one** is that annotation's plane, `R = 1`, anchor = its centre of mass;
- **scattered selection** → `R` below `minResultant`, and the result is `None`;
- **view-space round trip**: build a plane and a `CameraView` far from the origin at
  planetary scale, transform, and assert that a point known to lie on the plane maps to a
  signed distance below a tight epsilon while a point 1 m off maps to 1.0 ± epsilon. This is
  what fails if someone reintroduces a world-space transform or drops to `float32` too
  early — the other bug that looks like "the line is a bit off" rather than like a crash;
- **spread / extent** is the max centre-of-mass distance from the anchor, and `0` for a
  single annotation (so the floor is what applies);
- **selection aggregation** over the checked-in 250-annotation fixture
  `src/Tests/data/bulk-rose-annotations.pro3d.ann` (already in the repo for the rose): the
  DnS-only / polyline-only / both-toggles contributor counts must agree with the rose's
  numbers, since both features gate on the same annotation types. Reuse those constants
  rather than recomputing them.

Register both in `src/Tests/Tests.fsproj` and `src/Tests/Program.fs`.

**Manual check in the viewer** (there is no image-diff harness for this yet; see
[plans/sceneRenderTestHarness.md](sceneRenderTestHarness.md)): load an outcrop scene, draw
two DnS annotations on the same bed a few tens of metres apart, select both, enable coast
lines, and confirm the trace passes through both and follows the bed between them. Then raise
the extent factor and watch it extrapolate. Screenshot both for the docs page.

---

## 10. Docs

- **`docs/CoastLines.md`** — required by the house rule (every feature gets a docs page, in
  the same change). Synopsis, the UI table, the workflow above, a teaser image, and an
  *Implementation* section covering: view space and why; the averaging method, the sign
  correction and what `R` means; the derived extent radius and why the fade is on by default;
  and the `CoastLineEnabled` guard with a pointer to the Apple Silicon note in
  [docs/CrossSections.md](../docs/CrossSections.md).
- Cross-link from `docs/Contour-Lines.md` (the other procedural line shader) and from the
  rose-diagram documentation — the rose shows the selection's orientations as a histogram,
  coast lines show the *mean* of the same selection in 3D, and `R` is the number both share.
- `ai/DOMAIN.md` — one row for `CoastLinesModel` / `CoastLinesApp`; `ai/README.md` type table
  likewise.
- `PRODUCT_RELEASE_NOTES.md`.

---

## 11. Phasing

Each phase is a commit; the branch is `features/coast-lines` off `develop`.

1. **Model + averaging + tests, no rendering.** `CoastLines-Model.fs`, `CoastLinesApp.fs`,
   the average-plane function with the sign correction, adaptify, both test files. Fully
   testable with nothing on screen, and it is where the real risk is.
2. **Shader + Sg wiring.** `CoastLineShader`, both effect stacks, uniforms in
   `createGroupedSgs`. Drive it from a hard-coded plane first if that is faster to debug.
3. **Selection plumbing + UI.** The adaptive aggregate from 4.5, the accordion, the dip /
   azimuth / `R` readout and the three empty states.
4. **Repeat spacing.** Small once 1–3 are in; separate so the bisect surface stays honest.
5. **Docs + release notes**, with the screenshots from the manual check.

---

## 12. Known limitations, to be written into the docs page as such

- **An averaged plane is a local statement.** The derived extent radius is a mitigation, not
  a fix; raising the factor far past the selection's own footprint produces a confident line
  with no evidence behind it. This is the most likely way to mislead someone with this
  feature, and the docs must say so next to the control.
- **A mean plane is not a fitted plane.** Averaging normals answers "what orientation does
  this selection share"; it does not fit a plane through all the selected points. For
  scattered-but-coplanar annotations spread over a large area the two differ, and this
  feature deliberately answers the first question. Refitting across the union of the points
  is a possible later mode, not this one.
- **The plane does not follow surface transformations.** It is built from world-space picked
  points; changing a surface's transformation afterwards moves the terrain and leaves the
  plane where it was. Same behaviour as cross sections. Re-picking is the workaround.
- **Band width is measured perpendicular to the plane, not on screen.** Where the plane meets
  the terrain at a shallow angle the trace is wide; where it cuts steeply it is thin. That is
  geometrically honest and it is what makes the line read as an intersection rather than a
  decal, but it surprises people. A screen-constant variant is possible with `ddx`/`ddy` of
  the signed distance (FShade exposes `ddxFine`/`ddyFine`; nothing in PRo3D uses them yet) —
  out of scope here, noted as the follow-up.
- **Traces are not exportable.** See section 1 on CPU slicing.

## 13. Follow-ups, deliberately not in this plan

- Persisting an explicit annotation-id list on `Scene`, so a saved scene restores its coast
  line. Adding one `Scene` field read with `Json.tryRead` + a default needs no
  `Scene.current` bump, so this is cheap when it is wanted.
- Screen-constant line width via screen-space derivatives.
- A "refit across all selected points" mode alongside "average the orientations".
- Exporting a trace as a polyline annotation (the CPU-slicing path).
- An image-based regression once the render harness in
  [plans/sceneRenderTestHarness.md](sceneRenderTestHarness.md) lands — a fixed camera and one
  known plane is an ideal case for it.

## 14. Open question

**Averaging method.** The plan takes the mean unit normal (4.2). The alternative a
structural geologist might expect is the rose's own arithmetic: circular mean of dip azimuth
plus arithmetic mean of dip angle, reassembled into a normal the way
`calculateManualDipAndStrikeResults` does. They agree for a tight cluster and diverge for a
scattered one. If the second is what you want the panel to report, it is a swap of one
function in `CoastLines`, and nothing else in the plan changes.
