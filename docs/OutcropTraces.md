# Feature Outcrop Traces

Synopsis: where a modelled bedding sequence would crop out on the terrain
Status: New in 6.2.0
Interacts with: [Contour Lines](./Contour-Lines.md), [Cross Sections](./CrossSections.md)

An **outcrop trace** is the line where a geological plane meets the ground surface — what a
geological map draws. This feature takes **one attitude** (dip + dip direction), measured from
the current annotation selection, repeats it at a constant **bed thickness**, and marks every
terrain fragment lying within a given width of any plane of that sequence. What appears on the
terrain is the **outcrop pattern** that sequence would make.

The use case is stratigraphic correlation across sites: measure bedding at two outcrops, select
both, and see whether the traces run continuously between them.

> Sketched as *coast lines*, which is a good intuition — a coastline is exactly the trace of a
> horizontal plane on topography, the dip = 0° case of this. The general term is *outcrop trace*.

## Using it

The controls live in **Annotations → Outcrop Traces**:

| Row | Meaning |
| --- | --- |
| `Outcrop traces on/off` | activates the feature |
| `Polyline` / `DnS` | which annotation types contribute their fitted plane |
| `Bed Thickness (m)` | true (stratigraphic) thickness between successive beds; **0 = a single plane** |
| `Fit to selection` | re-seeds the thickness so about eight traces span the projection radius |
| `Colour` | trace colour |

Appearance settings are on the **config page → Outcrop Traces**, next to *Frustum* and
*Coordinate System*, because they are scene settings rather than per-user ones:

| Row | Meaning |
| --- | --- |
| `Trace Width (m)` | full width of the drawn band, measured perpendicular to the plane |
| `Trace Smoothing (m)` | smoothstep falloff either side of the band |
| `Projection Factor` | multiplier on the selection's own footprint, giving the projection radius |
| `Projection Floor (m)` | minimum projection radius; what sizes a single-annotation selection |

Workflow:

1. Draw dip-and-strike annotations on the bedding you care about, at one or more sites.
2. Select them — individually via the cube icons, or a whole group with *Select All*. A single
   selected annotation works too.
3. Enable outcrop traces. The panel reports the attitude actually being drawn, e.g.
   `Mean attitude of 7 annotations — 34.2° / 118.7° (dip / dip direction), S₁ = 0.94`.
4. Set the bed thickness, or press *Fit to selection* to get a legible starting point.
5. Raise *Projection Factor* to extrapolate further — carefully, see the warning below.

The same mean attitude appears as a **Selection average** row in the *Dip&Strike* panel, so the
number is available without switching outcrop traces on.

## What the panel refuses to draw, and why

Each plane contributes its **pole** (unit normal). Those poles are combined with the
**orientation tensor** — the principal eigenvector of `Σ nᵢnᵢᵀ`, the standard treatment of axial
orientation data — and its normalised eigenvalues `S₁ ≥ S₂ ≥ S₃` describe the result:

| message | spectrum | meaning |
| --- | --- | --- |
| the attitude is drawn | `S₁ > 0.65`, `S₂/S₁ < 0.3` | one dominant attitude; a mean plane is meaningful |
| *"no dominant attitude"* | `S₁ ≤ 0.65` | the poles are scattered; any mean would be arbitrary |
| *"the poles form a girdle"* | `S₃/S₂ < 0.3` | the poles lie on a great circle: **the selection is folded**, so no single attitude represents it. The message reports the fold axis (π-axis) as trend/plunge |

The girdle message is the one that earns its keep. Selecting both limbs of a fold and averaging
them yields a plane perpendicular to both — geologically meaningless — and no single confidence
number can flag it; only the eigenvalue spectrum can.

`S₁` is **not** the rose diagram's `R`. The rose measures agreement of dip *directions* only;
`S₁` measures agreement of full 3D orientations. Two beds dipping 5° in opposite directions have
`R = 0` and `S₁ = 0.99`, and both numbers are correct about different questions.

## Limitations

> **A measured attitude is a local statement.** The projection radius is a mitigation, not a
> fix. Raising *Projection Factor* far past the selection's own footprint produces a confident
> line with no evidence behind it. This is the most likely way to mislead yourself with this
> feature.

- **A mean attitude is not a plane fitted through all the points.** It answers "what orientation
  do these share", not "do these all lie on one plane". Two parallel beds 50 m apart look
  identical to it.
- **The attitude does not follow surface transformations.** It is built from world-space picked
  points; transforming a surface afterwards moves the terrain and leaves the planes behind. Same
  behaviour as cross sections. Re-pick the annotations to fix.
- **Traces are wider where the sequence meets the terrain at a shallow angle** and thinner where
  it cuts steeply. That is geometrically honest — it is what makes them read as intersections
  rather than decals — but it surprises people.
- **Traces are not exportable** as geometry or annotations.
- **Headless batch rendering (`PRo3D.Snapshots`) will not show them.** Not a render-path
  problem: the scene graph is shared (`SnapshotSg.createSceneGraph` → `ViewerUtils.createGroupedSgs`),
  so interactive screenshots *do* include traces. The state is simply transient and not part of
  the scene, so a batch job that loads a scene has the feature switched off. Persisting the
  settings on `Scene` is the fix.

## Implementation

- Model: `OutcropTraceModel` / `OutcropTraceApp` (`src/PRo3D.Core/OutcropTrace-Model.fs`,
  `OutcropTraceApp.fs`). All transient. The appearance fields are conceptually *scene*
  properties — an outcrop with decimetre bedding wants different numbers from one with
  ten-metre units, and those numbers belong to the outcrop, not to whoever opens it — so they
  belong on `Scene` when persistence is wanted, never in `userPreferences.json`.
- Combination: `OutcropTrace.meanAttitude`. The outer product is what makes the tensor correct
  here: a pole and its antipode describe the same plane, and `n nᵀ = (-n)(-n)ᵀ`, so the sign of
  `DipAndStrikeResults.plane` — which is stored exactly as the regression produced it,
  uncorrected — does not matter. Summing the normals instead (a Fisher mean) cancels
  measurements of one near-vertical bed against each other.
- Dip and dip direction come from `DipAndStrike.attitudeFromNormal`, shared with the Dip&Strike
  panel so the two cannot report different numbers.
- Rendering: `ViewerUtils.OutcropTraceShader.outcropTrace`, added **last** to both the OPC and
  OBJ effect stacks so the trace colour survives lighting and shadowing. `contourLines` sits
  earlier and *is* shaded, which is right for a terrain property and wrong for an interpretive
  overlay.
- **One plane uniform, however many beds are on screen.** The sequence comes from folding the
  signed distance into one bed-thickness interval (`d mod bedThickness`), the same trick
  `contourLines` uses against a texture value. No uniform arrays, no per-fragment loop.
- **Everything is view space.** The plane is composed on the CPU in `double`
  (`OutcropTrace.viewSpaceAttitude`) and uploaded camera-relative. A world-space test would be a
  `float32` dot product against ~3.4e6 m on Mars — about 0.25 m of resolution, noise next to a
  0.25 m trace width. See [ai/CONVENTIONS.md](../ai/CONVENTIONS.md).
- **Antialiasing is not optional here.** Trace width and bed thickness are both in metres, so a
  bed thinner than a couple of pixels of terrain shimmers and crawls. At 500 m on a 1080-tall
  viewport with a 60° vertical FOV one pixel covers ~0.5 m face-on, so a 1 m bed thickness is at
  the Nyquist limit before the terrain tilts. The shader takes `ddxFine`/`ddyFine` of the signed
  distance, clamps the band to at least ~1.5 px, and fades the sequence out below ~3 px per bed.
- **`OutcropTraceEnabled` gates the whole shader and the uniforms are always bound, zero-filled
  when off.** Never read an unbound per-fragment value — see the Apple Silicon note in
  [CrossSections.md](./CrossSections.md).

Tests: `src/Tests/OutcropTraceAttitudeTest.fs` (the combination maths, including the cases that
distinguish it from the alternatives), `src/Tests/OutcropTraceShaderTest.fs` (FShade GLSL code
generation for the shader and both effect stacks, no GL context needed) and
`src/Tests/Features/Section21_OutcropTraces.fs` (the app's update logic).
