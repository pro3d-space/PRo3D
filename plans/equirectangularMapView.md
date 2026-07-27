# Equirectangular projection — small-body map view

## Goal

Add an equirectangular (`x = lon / π`, `y = 2·lat / π`) projection of the
currently-loaded small-body OPC as a **dock panel** in PRo3D, rendered alongside
the main 3D view. Same pattern as the existing Instrument View: another
`RenderControl` driven by its own camera/projection, fed from the same
`m.scene.surfacesModel.sgGrouped` but rendered through a different shader
chain.

Scope is intentionally narrow: **only bodies whose `getConvention` is
`Spherical r` get a non-empty view**. Ellipsoidal and Planetographic produce
`Sg.empty` plus a "not available for this body" overlay. This sidesteps body-
fixed-frame plumbing (radial directions and `meanRadius` are unambiguous on a
sphere) and keeps v1 strictly about getting pixels on screen.

The equirect Sg is **built inside `viewEquirectView`**, not stored on the
model. PRo3D's existing `SurfaceModel.sgGrouped` is itself state-in-model
that the team would rather see derived in the view layer; we are not making
that worse by adding a parallel `sgGroupedEquirect` field. If
`viewEquirectView` can't get at the underlying `SgSurface`s without that
field, the right move is to **first refactor the existing 3D-view SG
construction out of the model into `viewRenderView`** — and only then add the
equirect view, both deriving from the same `sgSurfaces : amap<Guid, SgSurface>`
input. That refactor is a precondition flagged here, not part of v1's scope.

## Why this works in PRo3D's architecture

The existing Instrument View
(`src/PRo3D.Viewer/Viewer/Viewer.fs:2345` — `viewInstrumentView`) already
demonstrates the pattern we need:

- A second `DomNode.RenderControl` is mounted in a dock panel
  (`id = "instrumentview"` in `DockConfigs.fs:13`).
- It reuses `m.scene.surfacesModel.sgGrouped` via
  `ViewerUtils.renderCommands` but with its own camera (`instrumentCam`) and
  frustum (`instrumentFrustum`).
- The render commands are wrapped through `ViewerUtils.mapRenderCommand` and
  handed to `Gui.Pages.pageRouting` so the framework can hot-mount the panel
  when the user toggles the dock entry.

We mirror this 1:1 for the equirect view: new `viewEquirectView`, new dock id
`"equirectview"`, new entry in the dock layouts that should expose it
(`DockConfigs.full`, etc.).

## Architecture

### Math (vertex transform)

Given body **mean radius `R`** (from `Spherical r` in `getConvention`) and a
world-space vertex `p`:

```
r       = p - bodyCenter            // bodyCenter = barycenter (see assumption)
lon     = atan2(r.y, r.x)           // in (-π, π]
lat     = asin(clamp(r.z / |r|, -1, 1))
ndc.xy  = (lon / π, 2 * lat / π)    // both in [-1, 1]
ndc.z   = remap(|r|, R_min, R_max)  // see "Depth" below
```

The vertex shader writes `ndc` directly to `gl_Position`; the standard
`viewProj` chain is **bypassed** for this render task. (The `Sg` still receives
a `viewTrafo` so the OPC LoD decider gets a sensible camera position — see
"LoD decider" — but `projTrafo` is forced to identity in the render command
for the equirect panel.)

### Frame assumption (v1)

- `bodyCenter = V3d.Zero` in the OPC's world coordinates. This is the
  **barycenter** by construction: SPC OBJs and the OPCs derived from them are
  authored in `*_SHM` body-fixed frames whose origin is the body's centre of
  mass (per `plans/sbmtImport.md` "Coordinate frame knowledge"). True for
  Dimorphos. Assumed true for the other Hera-mission bodies.
- The body-fixed axes are taken as-is: `+Z = spin pole`, `lon = 0` along
  `+X`. We do **not** apply a `bodyToCanonical` rotation in v1. For
  `DIMORPHOS_SHM` the spin pole is `−Z` (see `plans/sbmtImport.md` axis
  cheat-sheet) so the resulting map is **flipped** vertically vs. the
  astronomical convention. Document this; resolve via a per-body rotation
  uniform in v2.
- These assumptions live in **one place**: a `body-frame` uniform block in
  the equirect shader module. v2 surfaces it as
  `bodyToCanonical : aval<M44d>` driven by SPICE / `getConvention`.

### Convention gate

```fsharp
// in viewEquirectView, before building the Sg:
let planet = m.scene.referenceSystem.planet.value
match CooTransformation.getConvention planet with
| ConventionKind.Spherical r -> buildEquirectSg r m runtime ...
| _                          -> emptySgWithMessage "Equirectangular view requires a spherical body convention."
```

Empty case must still mount a valid `RenderControl` (else the dock panel
flashes errors); a single full-canvas quad with a centered text label is
sufficient.

### Depth strategy — outermost-surface visibility

Equirect is many-to-one wherever the body isn't star-shaped from the
barycenter (overhangs, boulders). To keep the **outermost** surface, write
`ndc.z` so that larger `|r|` wins the z-test:

```
ndc.z = 1.0 - (|r| - R_min) / (R_max - R_min)    // close to 0 = outermost
```

`R_min`/`R_max` come from the OPC's global AABB (cached at SG-build time). On
Dimorphos these are O(60 m) — plenty of headroom in a 24-bit depth buffer.

Document caveat: on 67P-class bilobed bodies this is still wrong (the COM is
between the lobes). Out of scope for v1; flagged for v3.

### Antimeridian seam — geometry shader

A triangle straddling lon ≈ ±π has one vertex at `ndc.x ≈ −1` and others at
`ndc.x ≈ +1` after the per-vertex projection, so it draws a giant horizontal
ribbon across the map. Solution: a geometry shader joining the existing OPC
effect chain.

The pattern is already in use on the OPC path: `Viewer-Utils.fs:847`
defines `surfaceEffect`, which is composed onto every OPC group at line 971
(`Sg.effect [surfaceEffect]`), and which already contains the
`triangleSizeFilter` GS at line 747 — an FShade `triangle { ... }` builder
operating on the OPC's `Vertex` type *after* `PatchNode` produces vertices.
The equirect seam splitter is just another `triangle { ... }` member of the
same effect chain — composed into an **equirect-specific surface effect**
that replaces `surfaceEffect` when the equirect Sg is built in
`viewEquirectView`. No PatchNode-level changes needed.

Sketch:

```fsharp
[<ReflectedDefinition>]
let equirectSeamSplit (tri : Triangle<EquirectVertex>) =
    triangle {
        let p0 = tri.P0
        let p1 = tri.P1
        let p2 = tri.P2

        // detect seam: if any pairwise |Δlon| > π, the triangle wraps
        let dx01 = abs (p1.lon - p0.lon)
        let dx12 = abs (p2.lon - p1.lon)
        let dx20 = abs (p0.lon - p2.lon)
        let wraps = dx01 > Math.PI || dx12 > Math.PI || dx20 > Math.PI

        if not wraps then
            yield p0; yield p1; yield p2
        else
            // shift the vertex(es) whose lon is on the "small" side by +2π,
            // emit one copy; shift back by −2π, emit second copy.
            // Both copies are clipped by the NDC viewport, so only the
            // visible pieces survive.
            ... // emit two shifted copies
    }
```

Two-pass-via-emit is the cheapest correct technique: the GPU's NDC clip stage
discards the off-canvas portion of each copy for free. Discarding wrapping
triangles outright is *not* acceptable — it leaves visible gaps near ±180°.

Alternative considered: per-fragment discard on large `dFdx(lon)` derivative.
Rejected — leaves jagged seam pixels and doesn't handle the case where the
seam runs through the triangle's interior.

### LoD decider

`PatchNode` (`src/PRo3D.Core/Surface/Surface.Sg.fs:464`) takes the decider as
constructor arg. Existing implementations (`LodDecider.lodDeciderMars`,
`lodDeciderFixed`, the commented `marsArea` / `reworkedLoD`) all use 3D
camera distance as the screen-size proxy — wrong for equirect, where there
is no camera distance.

The equirect decider computes the **patch footprint in equirect-NDC** and
compares to a target pixel size:

```fsharp
let lodDeciderEquirect (meanRadius : float) (preTrafo : Trafo3d)
                       self (viewTrafo : aval<Trafo3d>) (_proj : aval<Trafo3d>)
                       (p : RenderPatch) (lodParams : aval<LodParameters>)
                       (isActive : aval<bool>) =
    let bb        = p.info.GlobalBoundingBox.Transformed(lodParams.trafo * preTrafo)
    // project AABB corners through (lon, lat) → NDC, take 2D extent
    let cornersNdc = projectCornersEquirect bb meanRadius
    let widthPx    = (cornersNdc.maxX - cornersNdc.minX) * 0.5 * float lodParams.size.X
    let heightPx   = (cornersNdc.maxY - cornersNdc.minY) * 0.5 * float lodParams.size.Y
    // patch's average triangle covers p.triangleSize world meters
    // after equirect mapping ≈ p.triangleSize / R radians ≈ that fraction of canvas
    let pxPerTri   = max widthPx heightPx * (p.triangleSize / max 1e-3 bb.Size.Length)
    pxPerTri > 1.0 * exp lodParams.factor
```

Two quirks worth flagging in code comments:

- Patches that **contain a pole** project to the full canvas width — the AABB
  corner trick under-estimates badly there. Detect with
  `bb.Contains(barycenter + R * V3d.OOI)` (or `−V3d.OOI`) and force max LoD
  for those patches in v1.
- Patches that **cross the antimeridian** project to a (false) full-width
  footprint via the AABB. Detect with `lon-range > π` after wrapping; either
  force max LoD or split the AABB into two longitude bands for the metric
  (v2).

Sites that construct OPC SG today (`Surface.Sg.fs:464`,
`OpcViewer/Solarsystem.fs:220`, `PRo3D.GIS/TestViewer.fs:305`) all take their
decider from `scene.lodDecider`. We don't touch those — instead, the
equirect view constructs **its own** OPC sub-graph at view-build time,
keyed on the same `patchHierarchies` but with `lodDeciderEquirect
meanRadius` passed in. Same KdTrees, same vertex/texture loading pipeline;
just a different `PatchNode`.

**This SG is built inside `viewEquirectView` itself, not stored on the
model.** A small helper — call it `Surface.Sg.buildEquirectSg meanRadius
sgSurfaces : ISg` — lives in `src/PRo3D.Core/Surface/Surface.Sg.fs`
alongside the existing `createSg`, but is called from the view function,
not from any model update. Inputs: the existing
`m.scene.surfacesModel.sgSurfaces : amap<Guid, AdaptiveSgSurface>` plus
`meanRadius`. Output: a single `ISg` ready to wear an equirect surface
effect.

**Open question / precondition.** The existing `sgGrouped` on
`SurfaceModel` (`Groups-Model.fs:458`, `Groups-Model.g.fs:263`) is itself
a pre-built `IndexList<HashMap<Guid, SgSurface>>` — i.e. scene-graph state
sitting in the model. The current `viewRenderView` consumes it directly
via `ViewerUtils.renderCommands`. If it turns out that
`buildEquirectSg` *also* needs the grouping logic that today only lives
on the path that populates `sgGrouped`, we should **first move that
grouping/SG construction out of the model into `viewRenderView`**, then
share the inputs between both view functions. v1 should attempt the
direct construction from `sgSurfaces` first; only escalate to the
refactor if it actually blocks.

### Effect chain

The equirect view defines its own surface effect that replaces
`Viewer-Utils.surfaceEffect` for the equirect Sg:

```fsharp
let equirectSurfaceEffect =
    Effect.compose [
        toEffect equirectVertex                  // (world p) → (lon, lat) → NDC
        toEffect equirectSeamSplit               // geometry shader, see above
        // optional: a triangleSizeFilter analogue if we want to suppress
        // patch-edge degenerates on the map. Re-evaluate after v1.
        toEffect equirectFragment                // flat shading + optional color ramp
    ]
```

Applied via `Sg.effect [equirectSurfaceEffect]` inside `viewEquirectView`,
exactly the way `surfaceEffect` is applied in `Viewer-Utils.fs:971`.

`equirectFragment` v1: solid color modulated by `cos(lat)` for visual
relief, or sample the OPC's per-vertex color if present. No lighting (the
"sun direction" doesn't map naturally onto a 2D world map; users want to see
the surface, not its shading).

## Sites to modify

| File | Change |
|---|---|
| `src/PRo3D.Base/CooTransformation.fs` | Already exposes `getConvention`. No change — just consumed. |
| `src/PRo3D.Core/Surface/Surface.Sg.fs` (~`LodDecider` module) | Add `lodDeciderEquirect meanRadius`. |
| `src/PRo3D.Core/Surface/Surface.Sg.fs` (helper, not SG-on-model) | Add `buildEquirectSg : meanRadius → sgSurfaces → ISg`. **Called from the view function, never assigned into the model.** |
| `src/PRo3D.Viewer/Viewer/EquirectShaders.fs` *(new)* | `equirectVertex`, `equirectSeamSplit`, `equirectFragment`, `equirectSurfaceEffect`, the `EquirectVertex` record. |
| `src/PRo3D.Viewer/Viewer/Viewer.fs` (~`viewInstrumentView`) | Add `viewEquirectView : IRuntime → string → AdaptiveModel → DomNode`. Convention gate, equirect-Sg construction, and effect application all live here. Threads through `pageRouting` like `viewInstrumentView`. |
| `src/PRo3D.Viewer/Viewer/ViewerGUI.fs` (~`pageRouting`) | Add `"equirectview"` page-routing case. |
| `src/PRo3D.Viewer/DockConfigs.fs` | Add `{id = "equirectview"; title = Some " Map View "; ...}` to **`DockConfigs.gis` only**. Other dashboard modes do not get the panel in v1. |
| `src/PRo3D.Viewer/Viewer-Model.fs` | **No new model state in v1.** Pan/zoom and toggle state get added in v2 only if needed; v1 is fixed-canvas. |

## Phasing

- **v1 — pixels on screen.** Convention gate, identity body-frame, fixed
  canvas (no pan/zoom), equirect vertex shader, seam-splitting GS, equirect
  LoD decider with pole/seam fallback to max LoD, depth = outermost. Goal:
  load Dimorphos, open Map View panel, see a recognizable map.
- **v2 — usability.** Pan/zoom in equirect space (it's just a camera in NDC).
  Graticule overlay (lon/lat grid). Cursor lon/lat readout. Annotation
  overlay (points & ellipses from the SBMT importer reproject for free; only
  their long edges might need GS seam treatment).
- **v3 — correctness.** Body-frame uniform driven from SPICE so the map is
  oriented astronomically (north up, lon = 0 = prime meridian). Pole-patch
  LoD instead of force-max. Bilobed-body depth strategy (probably skip for
  Hera targets entirely).

## Assumptions to surface in code comments

1. `bodyCenter = origin` of the active OPC's world frame. Holds for Hera
   bodies (SHM frames are barycentric). Wrong for any body whose OPC is
   loaded with a non-zero translation.
2. Body-fixed axes already match the canonical (spin pole = +Z) frame at the
   shader's input. For `DIMORPHOS_SHM` this is false (spin = −Z); the v1 map
   will be vertically flipped. Acceptable for v1.
3. Convention is `Spherical r`. Tri-axial bodies under `Ellipsoidal` get
   an empty view, even though their `(lat, lon)` is identical to `Spherical`
   under `CooTrafoUpdatePlan.md`. Reason: we don't want to litter v1 with
   "does this work on Ellipsoidal too?" branches; flip the gate in v2 after
   verifying.
4. Body is approximately star-shaped from its barycenter (no major overhangs
   or bilobing). True enough for Phobos/Deimos/Didymos/Dimorphos that the
   `depth = -|r|` policy hides the worst artifacts.

## Verification

1. Build clean.
2. Load Mars OPC → no "Map View" content (convention is `Planetographic`);
   panel shows the "not available" message.
3. Load Dimorphos OPC → Map View panel populates; map shows the body
   silhouette filling lon ∈ [−180°, +180°], lat ∈ [−90°, +90°]; antimeridian
   has no gap or ribbon-streak; pole regions are filled (no missing
   triangles).
4. Rotate body-frame mentally: features known to be at `+X` direction should
   appear near `lon = 0`. Document the flip if v1's identity frame produces
   an upside-down map for SHM bodies.
5. Performance: panel renders without stuttering when the main 3D view is
   active. The parallel `PatchNode` should reuse OPC tile data already cached
   by the main view's loader runner.

## Out of scope (explicitly)

- Multiple bodies at once (the panel renders whichever body satisfies the
  convention gate; ambiguous when more than one is loaded — pick the first or
  add a selector in v2).
- Non-OPC geometry (Assimp-loaded OBJs from the SBMT pipeline are *not*
  routed through this view in v1 — they don't have the LoD machinery and the
  shader chain assumes `PatchNode`).
- Interaction: clicking the map to fly the 3D view to that point. Easy
  follow-up via the existing picking + `flyTo` plumbing.
- Saving Map View state in the scene file.
