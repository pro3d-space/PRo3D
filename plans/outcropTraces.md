# Outcrop Traces — one attitude, a modelled bedding sequence, traced across the surface

**Goal.** Derive **one orientation** from an annotation selection, then mark every surface
fragment lying within a given thickness of **any** plane in an equidistant family with that
attitude. The result is the **outcrop pattern** the sequence would make: the traces of a
regularly spaced stack of parallel planes across the topography, the way a series of water
levels would each draw a coastline.

- **one annotation selected** → its fitted plane's attitude;
- **several annotations, or a whole group, selected** → the **mean attitude** of the
  selection (section 4);
- **bed thickness** → the perpendicular (true, stratigraphic) distance between successive
  planes. Zero collapses the sequence to a single plane through the reference point.

Selecting a set of bedding measurements, combining them into one attitude and watching where
that whole sequence would crop out across the scene is the stratigraphic-correlation use case
that motivates this: if two outcrops belong to the same sequence, the traces run continuously
between them.

## Terminology

This was sketched as *coast lines*, which is a good intuition — a coastline is exactly the
trace of a horizontal plane on topography, the dip = 0° case of what this draws. The
established term for the general case is **outcrop trace**: the line where a geological
plane meets the ground surface, which is what a geological map draws. The plan and the UI
use the standard vocabulary throughout; keep the "coast line" origin in the docs page so the
term stays findable for whoever remembers the sketch.

| sketch term | standard term | meaning |
| --- | --- | --- |
| coast line | **outcrop trace** | where a plane meets the topographic surface |
| the whole set of them | **outcrop pattern** | the map-view shape those traces make |
| the plane's orientation | **attitude** | dip angle + dip direction; PRo3D stores both in `DipAndStrikeResults` |
| average plane / direction | **mean attitude** | from the orientation tensor (section 4) |
| spacing between planes | **bed thickness** | perpendicular = *true* / *stratigraphic* thickness, as opposed to apparent thickness in an oblique section |
| the sequence of planes | **bedding sequence** | modelled: constant attitude, constant thickness |
| line thickness | **trace width** | a rendering parameter, deliberately *not* a geological term |
| extent radius | **projection radius** | how far the measured attitude is extrapolated |
| poles bunched / smeared | **cluster / girdle** | standard fabric-shape vocabulary |
| fold axis | **fold axis (π-axis)** | from π-analysis: poles to bedding lie on a great circle whose pole is the axis |

One inconsistency this inherits rather than introduces: the existing UI labels dip direction
as *"Dipping Orientation"* ([AnnotationHelpers.fs:466](../src/PRo3D.Base/Annotation/AnnotationHelpers.fs:466)).
The standard term is **dip direction**. Not this feature's to change — new UI here says
"dip direction", and the mismatch is worth a separate cleanup.

**Many planes, one uniform.** The sequence is *not* N planes uploaded to the GPU. It is one
attitude plus a modulo in the fragment shader — `d mod bedThickness` instead of `d`. So there
are no uniform arrays, no per-fragment loop, no cap on the selection size, no per-plane
colours and no fill-rate question to measure. The shader is a single dot product and a
remainder. Everything expensive or uncertain lives on the CPU, in `double`, once per frame.
`contourLines` ([Utilities.fs:749](../src/PRo3D.Base/Utilities.fs:749)) already uses the same
trick against a texture value.

**The reference point is only the sequence's phase.** With an infinite equidistant stack
there is no "the" plane to position; the reference point just decides which offsets the
planes land on. It is fixed at the selection's centroid, so one bed of the sequence passes
exactly through the middle of the measurements that defined it. Nothing else about it needs
deciding.

Every DnS annotation already carries a fitted `Plane3d`
(`DipAndStrikeResults.plane`, [Annotation-Model.fs:160](../src/PRo3D.Base/Annotation/Annotation-Model.fs:160)),
so nothing needs fitting either — the feature is *combine, transform to view space, upload,
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
- **Average with the orientation tensor** (principal eigenvector of `Σ nᵢnᵢᵀ`), which is the
  standard method for *axial* orientation data and the one stereonet software uses for
  poles. Not the mean unit normal, and emphatically not the rose's circular-mean-of-azimuth
  plus mean-of-dip. Section 4 works through where the three disagree, with numbers; the
  short version is that the tensor method is the only one that is correct at shallow dips,
  at near-vertical dips, and under the sign ambiguity of a fitted plane.
- **All state transient, and the settings are scene properties.** Trace width, trace
  smoothing and the projection radius describe how a particular *scene* should be read, not
  how a particular machine likes things, so `UserPreferences` is the wrong destination for
  them; `Scene` is the right one, and they are held transiently until persistence is wanted
  (section 5). Transient now, `Scene` later, `userPreferences.json` never.
- **Tensor only — no pooled-point refit mode.** Fitting one plane through the union of all
  selected annotations' points answers a different question ("do these sites lie on *one*
  plane", with an RMS residual) and was considered. It is out: with the equidistant family
  as the primary mode the visual answer is sufficient — traces that run continuously between
  two outcrops *are* the correlation — and a second orientation source would double the UI
  and the explanation for a number most users would not read. Recorded in 13 in case it is
  wanted later; the machinery (`LinearRegression3d`) is already there.
- **Equal weighting per annotation**, for now. Weighting by fit quality (`error.stdev`) or by
  point count is one line in 4.3 and can be added without disturbing anything else; it is
  left out until there is evidence a knob is needed.
- **Guard on the eigenvalue spectrum, not on a resultant length.** `S₁ > 0.65` says a
  dominant orientation exists at all; `S₂/S₁ < 0.3` says it is a cluster rather than a
  girdle, i.e. that the selection is one bed family and not a fold with two limbs. Both
  thresholds are calibrated in 4.4 rather than guessed. The rose's `minResultant = 0.05`
  ([RoseDiagram.fs](../src/PRo3D.Viewer/Viewer/RoseDiagram.fs)) is the right guard for the
  rose and the wrong one here -- 4.2 case 1 is a selection the rose refuses and this feature
  must accept.
- **Transient viewer state, not scene state** (section 5). The rose set this precedent, and
  the reason is concrete: the feature is driven by the *selection*, and `selectedLeaves` is
  explicitly not persisted ([Groups-Model.fs:363](../src/PRo3D.Core/Groups-Model.fs:363)).
  Persisting `enabled` while the selection restores empty would give a scene that says "on"
  and shows nothing.
- **Anchored at the selection's centroid**, which for a family means only its phase. Decided,
  not open. Anchoring on a chosen reference annotation, and reporting each annotation's
  signed offset along the shared normal, were both considered and dropped -- see 13.
- **The plane is clipped to a radius around the selection.** A plane fitted to a 2 m
  annotation is meaningless 4 km away; extended globally it paints a confident, wrong line
  across the whole scene. The trace fades out beyond the **projection radius** of the mean centre of
  mass, and the default radius is *derived from the selection's own spread* (section 4), not
  a fixed number.
- **The equidistant family is the primary mode, not an add-on.** Spacing defaults to on,
  and the single plane is the degenerate case (`bedThickness = 0`). This is a reversal from
  the first draft, and it changes the priorities below: the bed-thickness control has to be
  discoverable and sensibly initialised (section 8), and screen-space aliasing moves from a
  theoretical footnote to the first thing that will go wrong (section 12).
- **Initial bed thickness is derived, not a magic constant.** A sequence whose beds are far
  too thin paints the terrain a solid colour; far too thick shows one line. Both read as "the
  feature is broken". The first value is therefore set from the selection's own extent so
  that roughly eight traces are visible immediately, and the user dials from there.
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
- Per-annotation persistence of an outcrop-trace flag.
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

## 4. The average plane — which mean is correct

New module `OutcropTrace` in `OutcropTraceApp.fs`, **pure**, so the tests reach it without a GL
context — the same split `RoseDiagram.includes` made for the rose, for the same reason.

The numbers in 4.2 are reproduced by
[tools/analysis/compare_attitude_averaging.py](../tools/analysis/compare_attitude_averaging.py);
the thresholds in 4.4 by
[calibrate_attitude_cluster_guard.py](../tools/analysis/calibrate_attitude_cluster_guard.py).

### 4.1 The three candidates

Each measurement is a plane, represented by its unit normal (its **pole**). Write dip angle
δ and dip azimuth α; the pole is tilted δ from vertical, trending α + 180°.

- **(A) Mean unit normal** — sum the poles, normalise. Fisher's mean for *directed* vectors,
  and what the previous draft of this plan proposed.
- **(B) Circular mean of α + arithmetic mean of δ**, reassembled into a normal the way
  `calculateManualDipAndStrikeResults`
  ([AnnotationHelpers.fs:254](../src/PRo3D.Base/Annotation/AnnotationHelpers.fs:254)) already
  does. This is the arithmetic the rose diagram uses, extended with the dip angle — the
  "established method" in the sense of *established in this codebase*.

  **This is not a criticism of the rose.** For its own question — how are dip *directions*
  distributed — the rose is textbook-correct: bins centred on the cardinals, equal-area
  wedges (`r ∝ √count`, so area ∝ count), and a circular mean, which is the right statistic
  because dip azimuth is genuinely *directional* data. What (B) does is promote that
  arithmetic to a mean *plane*, which needs the dip angle the rose deliberately discards.
  The failure below is in the promotion, not in the rose.
- **(C) Orientation tensor** — form `T = (1/N) Σ nᵢnᵢᵀ`, take the eigenvector of its largest
  eigenvalue. Scheidegger/Watson; the standard treatment of *axial* orientation data in
  structural geology, and what stereonet packages use for poles. The eigenvalues
  `S₁ ≥ S₂ ≥ S₃` (summing to 1) come out as a free by-product and describe the *shape* of the
  distribution.

### 4.2 Where they disagree

| case | truth | (A) vector mean | (B) az + dip mean | (C) tensor |
| --- | --- | --- | --- | --- |
| 1. two beds, 5°/000 and 5°/180 | horizontal | **0.0°**, R = 0.996 | 5.0°, **Rz = 0.000** | **0.0°**, S₁ = 0.992 |
| 2. tight cluster, 5 beds ≈30°/120 | ≈30°/120 | 30.38°/120.5 | 30.40°/120.4 | 30.38°/120.5 |
| 3. 40°/060, 40°/120, 35°/090 | — | 35.66°/090 | **38.33°**/090 | 35.65°/090 |
| 4. 8 measurements of one **near-vertical** bed | ≈90° | **0.11°, R = 0.009** | **89.5°, Rz = 0.000** | **89.9°, S₁ = 0.9999** |
| 5. fold, two limbs 40°/090 and 40°/270 | *no single plane* | 0.02°, **R = 0.766** | 40°, **az 022 (arbitrary)** | flagged: S₁ = 0.587, S₂ = 0.413 |

Reading the table:

- **Case 1 kills (B).** Two shallow beds dipping gently away from each other average to a
  horizontal plane — an answer that is well defined and obviously right. (B) reports a 5°
  plane, and its azimuth resultant is exactly zero, so under the rose's own guard it would
  *refuse to draw at all*. Averaging two spherical coordinates independently is not an
  average on the sphere; near-horizontal beds have wildly unstable azimuths and (B) weights
  that instability equally with a well-constrained steep measurement.
- **Case 3 shows (B)'s bias is systematic, not just noisy.** Scattered azimuths at a common
  dip must average to a *shallower* plane; (A) and (C) give 35.7°, (B) gives 38.3° because
  the arithmetic mean of the dip angles cannot know that the azimuths disagree. The error
  grows with azimuthal spread.
- **Case 4 kills (A).** A pole is only *directed* data if you can reliably orient it, and
  PRo3D orients it by `signedOrientation up`
  ([AnnotationHelpers.fs:250](../src/PRo3D.Base/Annotation/AnnotationHelpers.fs:250)) —
  the sign of the normal's up-component. For a near-vertical bed that component is ≈0, so
  the orientation is decided by whichever side of vertical each measurement happens to land
  on: the eight poles in this case are all correctly up-oriented and still point in opposing
  *horizontal* directions, because half the beds lean a fraction east and half a fraction
  west. (A) cancels — eight consistent measurements of one bed report R = 0.009, "no
  preferred orientation" — and adding fit noise to a truly vertical bed does the same thing
  for the same reason. (C) is sign-invariant by construction (`nnᵀ = (-n)(-n)ᵀ`) and returns
  89.9° with S₁ = 0.9999.
- **Case 5 is why a scalar confidence is not enough.** A fold has no mean plane. (A) returns
  a horizontal plane — perpendicular to both limbs, geologically meaningless — with
  R = 0.766, comfortably above any reasonable "is this real" threshold, so it would draw a
  confident wrong trace. Only the *spectrum* distinguishes this: S₁ ≈ S₂ ≫ S₃ is a girdle,
  and no single number can express that.

**Conclusion: (C).** It is right in every case above, it needs no `up`/`north` frame for the
computation itself, and — the part that matters for the work — it makes the sign trap the
previous draft of this plan devoted a whole subsection to **disappear entirely**. Delete the
`orient` helper; it is not needed.

### 4.3 The computation

```fsharp
type AveragePlane = {
    plane   : Plane3d   // world space
    anchor  : V3d       // mean centre of mass, world space
    s       : V3d       // S1 >= S2 >= S3, normalised, sum to 1
    spread  : float     // max distance from anchor to a contributing centre of mass
    count   : int
}
```

```fsharp
// normals as fitted - NO sign correction, that is the point
let T =
    normals |> Array.fold (fun (m : M33d) (n : V3d) ->
        m + M33d(n.X*n.X, n.X*n.Y, n.X*n.Z,
                 n.Y*n.X, n.Y*n.Y, n.Y*n.Z,
                 n.Z*n.X, n.Z*n.Y, n.Z*n.Z)) M33d.Zero
    * (1.0 / float normals.Length)

match SVD.Decompose T with
| Some (u, s, _) ->
    let normal = u.C0            // eigenvector of the largest eigenvalue
    let ev     = s.Diagonal      // descending
    …
| None -> None
```

`T` is symmetric positive semi-definite, so its SVD *is* its eigendecomposition: `u.C0` is
the principal eigenvector and `s.Diagonal` holds the eigenvalues in descending order.
`SVD.Decompose` on a 3×3 is already the in-repo idiom — `LinearRegression3d.TryGetRegressionInfo`
does exactly this on a covariance matrix and likewise relies on the descending order
([RegressionInfo.fs:1602](../src/PRo3D.Base/Annotation/RegressionInfo.fs:1602)), so there is
neither new machinery nor a new dependency here.

`n = 1` is not a special case: `S₁ = 1`, the eigenvector is that annotation's normal, and the
average plane *is* its plane.

Weighting stays equal per annotation (decided, section 1): a physically larger or more
densely sampled annotation is not necessarily a better measurement. If evidence later says
otherwise it is a one-line change — `T = Σ wᵢ nᵢnᵢᵀ`, with `wᵢ` from
`DipAndStrikeResults.error.stdev` or the point count — and nothing else in the plan moves.

### 4.4 Guards, calibrated

`S₁` ranges from 1/3 (no structure) to 1 (all poles identical). Measured against synthetic
populations, n = 30:

| population | S₁ | S₂/S₁ |
| --- | --- | --- |
| pole scatter σ = 10° | 0.971 | 0.019 |
| σ = 20° | 0.896 | 0.074 |
| σ = 30° | 0.792 | 0.158 |
| σ = 40° | 0.697 | 0.270 |
| uniformly random poles | 0.432 ± 0.035 (max 0.549 over 200 trials) | — |
| fold, limbs 60° apart | 0.892 | 0.119 |
| fold, limbs 90° apart | 0.785 | 0.272 |
| fold, limbs 120° apart | 0.692 | 0.443 |

So:

- **`S₁ > 0.65`** — "there is a dominant orientation". Random poles top out at 0.55 across
  200 trials, and a genuinely noisy but usable field set (σ = 30–40°) sits at 0.70–0.79.
  The gap is real and this threshold sits in it.
- **`S₂/S₁ < 0.3`** — "it is a cluster, not a girdle". Passes every unimodal case including
  σ = 40°, and rejects a two-limb fold once the limbs are more than ~90° apart.
- **`S₃/S₂ < 0.3`** — separates a girdle from plain scatter, and **the girdle test has to run
  before the `S₁` floor.** Found while implementing: a girdle necessarily has a *low* `S₁`,
  because its poles are spread around a great circle, so the 180°-apart fold sits at
  `S₁ = 0.59` and an `S₁`-first ordering reports it as "no dominant attitude" — true, but far
  less useful than naming the fold. `S₃/S₂` is what tells the two apart: 0.001 for that fold,
  0.64 for uniformly random poles. Classification is therefore three-way and ordered:
  cluster, else girdle, else no dominant attitude. Verified across σ = 20…50° unimodal
  (all cluster), folds at 60/90/180° limb separation, and 300 uniformly random trials
  (all "no dominant attitude"). Below that
  separation a mean plane is defensible and the guard lets it through.

Failing the first is *"no preferred orientation"*; failing the second is *"the selection
looks folded"*, which is a different and much more useful message. In the girdle case the
eigenvector of the **smallest** eigenvalue is the fold axis (verified: for the 40°/090 +
40°/270 pair it comes out as trend 000, plunge 0, exactly right) — worth offering in the
message even though drawing it is out of scope.

`S₁` is also the number to show in the UI. It is **not** the rose's `R` and must not be
labelled as if it were: the rose's `R` measures agreement of *azimuths only*, `S₁` measures
agreement of full 3D orientations, and case 1 above is precisely a selection where one is 0
and the other 0.99.

### 4.5 Default extent radius

`spread` (max distance from `anchor` to any contributing centre of mass) is the selection's
own footprint, so the default extent radius is `max(spread, projectionFloor) * projectionFactor`.
One annotation gives `spread = 0`, so the floor is what sizes that case.

This is better than a fixed number: it scales with the outcrop the user actually selected,
and it makes extrapolation explicit (raise the factor) rather than accidental.

### 4.6 Collecting the selection

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

## 5. Model — `src/PRo3D.Core/OutcropTrace-Model.fs`

Follows the `CrossSectionModel` shape ([CrossSection-Model.fs](../src/PRo3D.Core/CrossSection-Model.fs)):
a `[<ModelType>]` record of `NumericInput` / `ColorInput` fields, an `initial`, and a sibling
`OutcropTraceApp` with actions and `update`.

One record, all of it transient:

```fsharp
[<ModelType>]
type OutcropTraceModel = {
    enabled          : bool
    usePolyline      : bool           // mirrors the rose diagram's two source toggles
    useDnS           : bool
    bedThickness     : NumericInput   // metres, true thickness between beds; 0 = single plane
    traceWidth       : NumericInput   // metres, full width of the drawn band
    traceSmoothing   : NumericInput   // metres, smoothstep falloff either side
    projectionFactor : NumericInput   // multiplier on the selection's own spread (4.4)
    projectionFloor  : NumericInput   // metres, minimum projection radius
    color            : ColorInput
}
```

Defaults: `enabled = false`, `useDnS = true`, `usePolyline = false`, `traceWidth = 0.25`,
`traceSmoothing = 0.1`, `projectionFactor = 1.5`, `projectionFloor = 25.0`,
`color = C4b.Red`.

**These are scene properties, not machine preferences.** Trace width and the projection
radius describe how a *particular scene* should be read — an outcrop with decimetre bedding
wants different numbers from one with ten-metre units, and those numbers belong to the
outcrop, not to whoever opens it. `UserPreferences` (`userPreferences.json`, per computer)
would have been the wrong home for exactly that reason, and the config tab agrees: nearly
everything on it is already `m.scene.*` — `scene.referenceSystem`, `scene.config.frustumModel`,
`scene.screenshotModel` ([ViewerGUI.fs:1177](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs:1177)).

They are nonetheless **transient for this change** — the whole model is, for the reason in
section 1 (the feature is selection-driven and the selection is not persisted). When
persistence is wanted, the destination is `Scene`, and adding one field read with
`Json.tryRead` plus a default needs no `Scene.current` bump. Recorded in 13. Writing them to
`userPreferences.json` in the meantime would be the harder thing to undo, because it puts the
values in a place they would later have to be migrated out of.

`bedThickness` has no useful fixed default — see the "derived, not a magic constant" decision
in section 1. It is initialised to `projectionRadius / 8` the first time a selection produces
an attitude, giving roughly eight visible traces straight away, and is left alone after that
so the user's own value survives a change of selection. `bedThickness = 0` is the
single-plane degenerate case, reachable by dragging the control to its minimum; there is no
separate enable toggle for the sequence.

Wiring, all mechanical:

- `PRo3D.Core.fsproj` — `OutcropTrace-Model.fs` then `OutcropTraceApp.fs`, next to the
  CrossSection pair ([lines 106/108](../src/PRo3D.Core/PRo3D.Core.fsproj:106)).
- `Viewer-Model.fs` — `outcropTraces : OutcropTraceModel` on `Model`, and
  `ViewerAction.OutcropTraceMessage of OutcropTraceAction`.
- `Viewer.fs` — one `| OutcropTraceMessage msg, _ -> { m with outcropTraces = OutcropTraceApp.update m.outcropTraces msg }` arm.
- `InitialViewerModel.fs` — `outcropTraces = OutcropTraceModel.initial`.
- Run `adapt.sh` for the `.g.fs`. **Never hand-edit a `.g.fs`.**

No `Scene` field, so no serialization and no `Scene.current` bump.

---

## 6. Shader — `OutcropTraceShader` in `Viewer-Utils.fs`

Put it directly after `CrossSectionShader`
([Viewer-Utils.fs:832](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:832)), so the surface effect
stack and everything it depends on stay in one file.

```fsharp
module OutcropTraceShader =
    open FShade

    type UniformScope with
        member x.OutcropTraceEnabled : bool = x?OutcropTraceEnabled
        /// xyz = view-space unit normal, w = view-space plane offset d
        member x.OutcropTracePlane   : V4f  = x?OutcropTracePlane
        /// xyz = view-space anchor, w = extent radius (metres)
        member x.OutcropTraceExtent  : V4f  = x?OutcropTraceExtent
        /// x = trace width, y = trace smoothing, z = bed thickness (<= 0 = single plane)
        member x.OutcropTraceParams  : V4f  = x?OutcropTraceParams
        member x.OutcropTraceColor   : V4f  = x?OutcropTraceColor

    let outcropTrace (v : Effects.Vertex) =
        fragment {
            if not uniform.OutcropTraceEnabled then
                return v.c
            else
                let p         = v.vp.XYZ
                let pl        = uniform.OutcropTracePlane
                let ext       = uniform.OutcropTraceExtent
                let par       = uniform.OutcropTraceParams
                let halfWidth = par.X * 0.5f
                let smooth    = par.Y
                let bedThk    = par.Z

                // signed distance to the plane, view space, metres
                let signed = Vec.dot pl.XYZ p - pl.W

                // a sequence folds the distance into one bed-thickness interval
                let d =
                    if bedThk > 0.0f then
                        let m = signed - bedThk * floor (signed / bedThk)
                        min m (bedThk - m)               // distance to the nearest bed
                    else
                        abs signed

                // band: 1 inside halfWidth, smoothstep out over `smooth`
                let band = 1.0f - Fun.Smoothstep(d, halfWidth, halfWidth + smooth)

                // fade out away from the selection the plane was averaged from
                let r    = Vec.distance ext.XYZ p
                let fade = 1.0f - Fun.Smoothstep(r, ext.W, ext.W * 1.15f)

                let a = band * fade
                return V4f(v.c.XYZ * (1.0f - a) + uniform.OutcropTraceColor.XYZ * a, v.c.W)
        }
```

Notes:

- `v.vp` is the `ViewPos` varying written by `Shader.stableTrafo`
  ([Viewer-Utils.fs:790](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:790)) and already consumed
  by `textureOrLightingIfPossible` — nothing new to plumb.
- No loop, no array uniform, so no fill-rate question to measure and nothing to gate on
  `Config.limitedShaderCapabilities`. This is the payoff of the one-plane constraint.
- **`OutcropTraceEnabled` must gate the whole thing, and the uniforms must always be
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
whole surface set, since the outcrop trace is global rather than per-surface:

```fsharp
// aval<Option<view-space plane * extent>>; recomputed when the camera or the selection moves
let outcropTrace = OutcropTrace.viewSpaceAttitude view m.outcropTraces m.drawing.annotations m.scene.referenceSystem

|> Sg.uniform "OutcropTraceEnabled" (outcropTrace |> AVal.map Option.isSome)
|> Sg.uniform "OutcropTracePlane"   (outcropTrace |> AVal.map (function Some (p,_) -> p | None -> V4f.Zero))
|> Sg.uniform "OutcropTraceExtent"  (outcropTrace |> AVal.map (function Some (_,e) -> e | None -> V4f.Zero))
|> Sg.uniform "OutcropTraceParams"  (…traceWidth, traceSmoothing, bedThickness as V4f…)
|> Sg.uniform "OutcropTraceColor"   (m.outcropTraces.color.c |> AVal.map (fun c -> c.ToV4f()))
```

`outcropTrace` returning `None` folds together *disabled*, *nothing selected*, *no valid
planes*, *`S₁` below threshold* and *girdle* — one gate, one uniform, and the shader cannot
be reached with a half-valid plane.

Three things to get right:

1. The uniforms are always bound, zero-filled when `None`. See the Apple Silicon note in
   section 6.
2. `view` is the parameter of `createGroupedSgs`, not `m.navigation.camera.view` (section 3).
3. **Snapshots: wanted, deferred.** `getSurfacesScenegraphs`
   ([Viewer-Utils.fs:1038](../src/PRo3D.Viewer/Viewer/Viewer-Utils.fs:1038), marked *"TODO TO
   refactor screenshot specific"*) is a **second, older** surface path that does not bind the
   cross-section uniforms either. Traces *should* appear in snapshots — batch-rendered
   outcrop patterns are a real use — but that is explicitly **not in this change**. So:
   establish which path snapshots take ([Viewer.fs:2751](../src/PRo3D.Viewer/Viewer/Viewer.fs:2751)),
   and if it is the legacy one, **state in the docs page that snapshots do not show traces
   yet** rather than leaving it to be discovered. Binding the uniforms on that path, or
   folding the two paths together, is the follow-up in 13. Do not silently ship a feature
   that vanishes in batch rendering.

---

## 8. UI

One new accordion in the Annotations panel, next to *Bulk Edit* / *Dip&Strike*
([ViewerGUI.fs:1516](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs:1516)):

```fsharp
GuiEx.accordion "Outcrop Traces" "map outline" false [
    Incremental.div AttributeMap.empty (AList.ofAValSingle (Annotations.viewOutcropTraces m))
]
```

Its own accordion rather than a section inside *Bulk Edit*, because the bulk panel refuses to
render below two selected annotations and outcrop traces must work for one.

Contents — only what changes with the selection: an on/off button styled like the rose
activation button, the Polyline / DnS source toggles, then — first, because it is the control
the user actually works with — the **Bed thickness** row with a *Fit to selection* button
that re-derives it as `projectionRadius / 8`, and the colour picker.

**The appearance settings live on the config tab instead.** *Trace width*, *Trace smoothing*,
*Projection factor* and *Projection floor* are set once per scene and then left alone, so they
go in a new `GuiEx.accordion "Outcrop Traces" "Settings" false [...]` on the config page
([ViewerGUI.fs:1177](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs:1177)), alongside *Frustum*,
*Screenshots* and *Coordinate System* — all of which are scene settings, which is what these
are too (section 5). Keeping them out of the Annotations panel is what lets that panel stay a
three-control surface — on, bed thickness, colour — which is all the feature needs in normal
use.

**Bed thickness is the user's value throughout.** The derived `projectionRadius / 8` is only the seed
for the first selection that produces an orientation, and *Fit to selection* re-seeds it on
demand; nothing recomputes it behind the user's back, and changing the selection leaves the
value they set alone. The row shows the resulting trace count across the projection radius
(`≈ 2·projectionRadius / bedThickness`) next to the value, so that "1" versus "100" is legible before
the user looks at the terrain rather than after.

**The *Dip&Strike* panel gets the same numbers.** `Annotations.viewDipAndStrike`
([ViewerGUI.fs:1523](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs:1523)) reports per-annotation dip
and strike only; add a *selection average* row there — dip, azimuth, `S₁`, contributor count
— sharing `OutcropTrace.meanAttitude` so there is exactly one implementation of the combination and
the two panels can never disagree. It is where a geologist looks for the number first, and it
makes the average useful even with outcrop traces switched off.

Above the controls, a readout of **what plane is actually being drawn** — this is what makes
the averaging trustworthy, and it is cheap because the numbers already exist:

```
Mean attitude of 7 annotations — 34.2° / 118.7° (dip / dip direction), S₁ = 0.94
```

Dip and azimuth are derived from the principal eigenvector via the same `up`/`north`
construction `calculateDipAndStrikeResults` uses, so they are directly comparable to the
*Dip&Strike* panel. `S₁` is labelled `S₁`, never `R` — see the warning at the end of 4.4;
showing the rose's symbol for a different quantity is how someone ends up comparing two
numbers that measure different things.

The four failure states get explicit text instead of a silent blank:

- nothing selected → *"Select an annotation, or a group, to trace its plane."*
- nothing contributes → *"No planes in the selection (enable a type, or select DnS /
  Polyline annotations)."* (the rose's wording)
- `S₁ ≤ 0.65` → *"The selection has no dominant attitude (S₁ = 0.48) — a mean attitude would
  be meaningless."*
- `S₂/S₁ ≥ 0.3` → *"The poles form a girdle, not a cluster (S₁ = 0.59, S₂ = 0.41): the
  selection is folded, so no single attitude represents it. Fold axis (π-axis) ≈ 000/00."*

The last one is the message that earns its keep. Selecting both limbs of a fold and getting
a confident horizontal trace is the specific way this feature could quietly mislead someone
doing cross-site correlation, and it is the case (A) cannot detect at all.

---

## 9. Tests

Pure, no GL, in the style of [Section13_ContourMultitexturing.fs](../src/Tests/Features/Section13_ContourMultitexturing.fs)
and [BulkAnnotationRoseTest.fs](../src/Tests/BulkAnnotationRoseTest.fs).

New `src/Tests/Features/Section21_OutcropTraces.fs`:

- defaults: disabled, DnS on, polyline off;
- `ToggleEnabled`, `SetTraceWidth`, `SetBedThickness`, `SetProjectionFactor`, colour change each
  land on the model.

New `src/Tests/OutcropTracePlaneTest.fs` — the parts that can be silently wrong. The first four
are the table in 4.2 turned into assertions, so the method choice is pinned by tests rather
than by a paragraph:

- **shallow opposing dips** (5°/000 + 5°/180) → dip ≈ 0°, `S₁ ≈ 0.992`. Fails if anyone
  reintroduces azimuth+dip averaging, which returns 5° here;
- **near-vertical bed, poles as fitted** (8 measurements about 90°/090, normals *not*
  sign-corrected) → dip ≈ 89.9°, `S₁ ≈ 0.9999`. Fails if anyone reintroduces the mean unit
  normal, which returns 0.11° with R ≈ 0.009. This is the axial-invariance test and it is
  the reason the module takes normals exactly as `DipAndStrikeResults` stores them;
- **girdle rejection**: two limbs at 40°/090 and 40°/270 → `S₂/S₁ ≈ 0.70`, result is `None`
  with the folded message, *not* a horizontal plane;
- **tight cluster** (5 beds ≈30°/120) → 30.4°/120.5, `S₁ > 0.999` — the benign case, pinning
  that the guards do not fire on ordinary data;
- **average of one** is that annotation's plane, `S₁ = 1`, anchor = its centre of mass;
- **view-space round trip**: build a plane and a `CameraView` far from the origin at
  planetary scale, transform, and assert that a point known to lie on the plane maps to a
  signed distance below a tight epsilon while a point 1 m off maps to 1.0 ± epsilon. This is
  what fails if someone reintroduces a world-space transform or drops to `float32` too
  early — the bug that looks like "the line is a bit off" rather than like a crash;
- **spread / extent** is the max centre-of-mass distance from the anchor, and `0` for a
  single annotation (so the floor applies);
- **selection aggregation** over the checked-in 250-annotation fixture
  `src/Tests/data/bulk-rose-annotations.pro3d.ann` (already in the repo for the rose): the
  DnS-only / polyline-only / both-toggles contributor counts must agree with the rose's
  numbers, since both features gate on the same annotation types. Reuse those constants
  rather than recomputing them.

The synthetic orientations for the first four come straight from
[compare_attitude_averaging.py](../tools/analysis/compare_attitude_averaging.py), so the script and
the tests cannot drift apart silently.

Register both in `src/Tests/Tests.fsproj` and `src/Tests/Program.fs`.

**Manual check in the viewer** (there is no image-diff harness for this yet; see
[plans/sceneRenderTestHarness.md](sceneRenderTestHarness.md)): load an outcrop scene, draw
two DnS annotations on the same bed a few tens of metres apart, select both, enable outcrop
lines, and confirm the trace passes through both and follows the bed between them. Then raise
the extent factor and watch it extrapolate. Screenshot both for the docs page.

---

## 10. Docs

- **`docs/OutcropTraces.md`** — required by the house rule (every feature gets a docs page, in
  the same change). Synopsis, the UI table, the workflow above, a teaser image, and an
  *Implementation* section covering: view space and why; the averaging method, the sign
  correction and what `R` means; the derived extent radius and why the fade is on by default;
  and the `OutcropTraceEnabled` guard with a pointer to the Apple Silicon note in
  [docs/CrossSections.md](../docs/CrossSections.md).
- Cross-link from `docs/Contour-Lines.md` (the other procedural line shader) and from the
  rose-diagram documentation — the rose shows the selection's orientations as a histogram,
  outcrop traces show the *mean* of the same selection in 3D, and `R` is the number both share.
- `ai/DOMAIN.md` — one row for `OutcropTraceModel` / `OutcropTraceApp`; `ai/README.md` type table
  likewise.
- `PRODUCT_RELEASE_NOTES.md`.

---

## 11. Phasing

Each phase is a commit; the branch is `features/outcrop-traces` off `develop`.

1. **Model + averaging + tests, no rendering.** `OutcropTrace-Model.fs`, `OutcropTraceApp.fs`,
   the orientation-tensor average and its two guards, adaptify, both test files. Fully
   testable with nothing on screen, and it is where the real risk is.
2. **Shader + Sg wiring, including the sequence.** `OutcropTraceShader` with the modulo, both
   effect stacks, uniforms in `createGroupedSgs`. Drive it from a hard-coded orientation and
   bed thickness first if that is faster to debug. The sequence is in from the start — it is
   two lines and it is the mode the feature exists for; deferring it would mean tuning trace
   width and projection radius against a picture nobody wants.
3. **Selection plumbing + UI.** The adaptive aggregate from 4.6, the Annotations accordion,
   the new config-tab accordion, the mean-attitude readout and the four empty states.
4. **Aliasing mitigation** (section 12) once there is something on screen to judge it
   against. Separate commit so the before/after is visible.
5. **Docs + release notes**, with the screenshots from the manual check.

---

## 12. Known limitations, to be written into the docs page as such

- **An averaged plane is a local statement.** The derived extent radius is a mitigation, not
  a fix; raising the factor far past the selection's own footprint produces a confident line
  with no evidence behind it. This is the most likely way to mislead someone with this
  feature, and the docs must say so next to the control.
- **A mean plane is not a fitted plane.** The orientation tensor answers "what orientation
  does this selection share"; it does not fit a plane through all the selected points. For
  scattered-but-coplanar annotations spread over a large area the two differ, and this
  feature deliberately answers the first question. Refitting across the union of the points
  is a possible later mode, not this one.
- **For a near-vertical mean plane the reported dip *direction* is arbitrary.** A bed dipping
  89.9° toward 090 and one dipping 89.9° toward 270 are the same plane, and the tensor
  method — correctly — does not distinguish them, so the readout may show either. This is
  harmless for the trace itself, which only needs the plane, but it looks like a bug if it
  is not documented.
- **The plane does not follow surface transformations.** It is built from world-space picked
  points; changing a surface's transformation afterwards moves the terrain and leaves the
  plane where it was. Same behaviour as cross sections. Re-picking is the workaround.
- **Band width is measured perpendicular to the plane, not on screen.** Where the sequence
  meets the terrain at a shallow angle the traces are wide and far apart; where it cuts
  steeply they are thin and close. That is geometrically honest and it is what makes them
  read as intersections rather than decals, but it surprises people.

> **Aliasing is the first thing that will go wrong, and the sequence makes it certain.**
> With a 60° vertical FOV and a 1080-pixel-tall viewport, one pixel covers ≈0.53 m of
> terrain at 500 m *face-on*, and several times that at grazing incidence. So a 1 m bed thickness
> is already at the Nyquist limit before the terrain tilts: the traces shimmer, moiré with
> the LOD, and crawl as the camera moves. A single plane never showed this because there was
> only ever one line on screen.
>
> The mitigation is the standard analytic-antialiasing move for a procedural line pattern,
> and it is what phase 4 is for: take the screen-space derivative of the signed distance
> (`ddxFine`/`ddyFine` — FShade exposes both; nothing in PRo3D uses them yet), and use
> `w = length(V2f(ddx d, ddy d))` as the per-pixel scale. Then (a) clamp the band's
> half-width to at least ~1.5·w so a trace never falls below a pixel and disappear-flickers,
> and (b) fade the whole sequence out as `bedThickness / w` drops below ~3, so an over-dense stack
> dissolves into a flat tint instead of a shimmering mess.
>
> Judge it on a moving camera, not a screenshot — a still frame hides exactly the artifact
> this fixes.
- **Traces are not exportable.** See section 1 on CPU slicing.

## 13. Follow-ups, deliberately not in this plan

- Persisting an explicit annotation-id list on `Scene`, so a saved scene restores its traces. Adding one `Scene` field read with `Json.tryRead` + a default needs no
  `Scene.current` bump, so this is cheap when it is wanted.
- A "refit across all selected points" mode alongside "average the orientations".
- Exporting a trace as a polyline annotation (the CPU-slicing path).
- **Outcrop traces in snapshots** (wanted, deliberately deferred — see 7.3): bind the
  uniforms on the legacy screenshot scene-graph path, or merge it with `createGroupedSgs` so
  there is only one path to keep in step.
- **Profile sections through a fold**: in the girdle case, trace the sequence *perpendicular*
  to the π-axis instead of refusing. Same shader, different normal, so the cost is a mode
  switch — but it shows fold cross-sections, not bedding, and overloading one visual with two
  meanings is why it is out.
- **Pooled-point refit as a second orientation mode** (decided out, section 1): one plane
  through the union of all selected annotations' points, via `LinearRegression3d`, answering
  "do these sites lie on one plane" with an RMS residual in metres rather than "do they dip
  the same way".
- **Per-site offsets.** The signed distance of each contributing annotation along the shared
  normal, `dᵢ = n·(cᵢ - anchor)`: tightly clustered `dᵢ` mean the sites are on one bed, and
  the spread is the stratigraphic separation in metres. Would make the correlation a number
  rather than a visual judgement, and needs no new rendering -- but it is a readout the
  centroid-only decision above deliberately leaves out of the first version.
- An image-based regression once the render harness in
  [plans/sceneRenderTestHarness.md](sceneRenderTestHarness.md) lands — a fixed camera and one
  known plane is an ideal case for it.

## 14. Open questions

Decided since the first draft and no longer open: the averaging method (4.2), tensor-only
with no pooled refit, equal weighting, the *Dip&Strike* selection-average row, the centroid
anchor, and the modelled sequence as the primary mode with a user-defined bed thickness.

**Nothing is open.** The girdle case is settled too: refuse to draw, and name the fold —
report the π-axis as trend/plunge in the message (4.4). Tracing planes perpendicular to the
fold axis (serial profile sections) was considered and rejected: it is nearly free, being the
same shader with a different normal, but it answers a question nobody asked and would put a
second meaning on the same visual. Recorded in 13.

**Verified during implementation, not decisions:**

- Which render path snapshots take ([Viewer.fs:2751](../src/PRo3D.Viewer/Viewer/Viewer.fs:2751)).
  Traces in snapshots are wanted but deferred (7.3); what this verifies is only whether the
  docs page has to say so.
- Whether `Config.limitedShaderCapabilities` targets need the effect gated (section 6).

**Noted, belongs to another file:**

- The rose's `R > 0.05` guard is sample-size independent (`RoseDiagram.minResultant`). For
  uniformly random azimuths E[R] is 0.40 at n = 5, 0.28 at n = 10 and 0.056 at n = 250, so it
  fires on ~2% of random 10-annotation selections. It does its actual job — suppressing the
  due-north line `atan2 0 0` would produce — but it is not the "no meaningful preferred
  direction" test its comment claims; the calibrated version is Rayleigh, `R > sqrt(3/n)`.
  Recorded because this plan's 4.4 deliberately did not copy the constant.
