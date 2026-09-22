# Projection Testbed — CLI validation tool for image→OPC projection

## Goal

A standalone, CLI-evaluable project that loads an OPC, projects an instrument image onto
it using **only** parameters derived from the mbi sidecar and SPICE, renders offscreen, and
writes a screenshot. Success criterion: rendering *from the instrument's own camera* at the
observation time must reproduce the image the instrument actually took.

Primary suspicion to resolve: image flips / coordinate-system handedness.

Default scenario:
- OPC: `C:\pro3ddata\HERA\Didymos_SK_OPC_KT\Didymos_ASPECT\Didymos_ASPECT`
- Reference frame: `DIDYMOS_FIXED`, body `DIDYMOS`
- Images: `C:\pro3ddata\HERA\ASPECT-Data\2B\MBI`
- First image: `ASP_000000_270323T060000_2B_NIR1_0.tif`, obs 2027-03-23T06:00:00Z, instrument `ASPECT`

## The core idea

The validation is self-evident if the **render camera == the projector camera**.

Set view/proj to the instrument's own `projectOntoQuat` trafo. Then the projected texture
must land exactly 1:1 on the framebuffer, and the rendered silhouette of Didymos must match
the silhouette in the real `.tif`. Any flip, transpose, or axis swap shows up as an obvious
mismatch rather than a subtle misregistration. This is why the tool is worth more than the
existing tests, which stop at trafo comparison and never rasterize.

## Feasibility gate (already checked — it passes, with one caveat)

`src/Tests/InstrumentProjectionComparisonTest.fs:149,185` asserts ASPECT/Didymos projection
returns `None` ("Milani kernel coverage may have been extended past cruise"). That assertion
reflects an older kernel set. Checked against what is on this machine:

- Sidecar asks for `hera_plan_v180_20250616_001`; not present, but
  `C:\Users\haral\Desktop\pro3d\spice\kernels\mk\hera_plan.tm` (→ v182_20260527) is.
- `hera_plan.tm` includes `spk/hera_milani_mlp_270130_270417_v01.bsp` → covers 2027-03-23. ✅
- `ck/didymos_flp_000006_260630_271231_v01.bc` → Didymos attitude covers the epoch. ✅
- Milani **spacecraft** CK is only `cruise_v02` + `default_v01` — likely no real attitude at
  this epoch. **This does not matter**: `projectOntoQuat` takes attitude from the mbi
  `sc_quat`, not from a CK. Hence `ProjectionMethod.MbiBased` is the viable path, and it is
  also exactly what "all parameters from the mbi or from spice" asks for.

**Blocker to fix first** — `src/PRo3D.Base/InstrumentProjection.fs:123-124`:

```fsharp
match getLookAtQuat ... , getLookAt ... with
| Some view, Some refold ->      // `refold` is bound and never used
```

`projectOntoQuat` needlessly also requires the SPICE-attitude `getLookAt` to resolve. Since
`getLookAt` needs the Milani CK that probably has no coverage, this is the most likely reason
ASPECT returns `None` today. Dropping the dead `refold` conjunct is step 1 and may be the
entire fix. Re-check the two `isFalse` assertions in the comparison test afterwards — if
projection starts resolving, those tests are asserting the bug and must be inverted.

## Decisions taken

- **Base the testbed on `src/PRo3D.GIS/TestViewer.fs`, not `Solarsystem.fs`.** TestViewer is
  a fork of Solarsystem (66 verbatim lines plus a long list of copied bindings: `hierarchies`,
  `marsSg`, `marsTrafo`, `planets`, `bodies`, `info`, `sunLight`, `inNdcBox`, `toUtcFormat`,
  `farPlaneMars`, `spiceFileName`, `initialView`) that then grew the entire projection
  pipeline Solarsystem lacks. Cloning Solarsystem would have meant hand-porting that back,
  creating a fourth fork.
- **`Solarsystem.fs` is to be deleted** once the testbed reaches feature parity. Parity means
  the planet-point overlay, orbit-history trails, and text/info overlays it has and TestViewer
  does not. Until then it stays, but nothing new should be built on it.
- **Bounded extraction** (option a): extract only what the testbed needs, convert
  `GIS/TestViewer.fs` to prove it, leave the other 11 `PatchHierarchy.load` sites alone.
- Shared instrument frusta live in the base lib; near/far come from a heuristic with a CLI
  override; the testbed references PRo3D.GIS.

## Progress

Done and verified — full solution builds clean (`dotnet build src/PRo3D.sln`):

1. **`projectOntoQuat` unblocked** (`InstrumentProjection.fs:123`). Removed the dead
   `getLookAt`/`refold` conjunct that made the whole projection fail whenever the spacecraft
   CK had no coverage — the exact ASPECT/Didymos case. Attitude comes from the mbi `sc_quat`.
2. **Instrument frusta de-duplicated** 3 copies → 1. `InstrumentProjection.fovs` (private) +
   `InstrumentProjection.instruments near far`. The three call sites (`Visualization.fs` ×2,
   `TestViewer.fs`) now share it; `TestViewer`'s divergent HSH aspect and missing ASPECT entry
   are gone.
3. **Near/far heuristic** `InstrumentProjection.nearFarForDistance` (distance/100, distance*100),
   fed from `mbi.targetPos`. `Visualization.projectDirectWithNearFar` takes an explicit
   override; `projectDirect` keeps its old signature and uses the heuristic. Also dropped the
   dead `t` binding in `Visualization.project` that computed a projection and discarded it.
4. **`src/PRo3D.GIS/OpcProjectionSg.fs`** — new. `SpiceBoot` (kernel resolution + init routed
   through the existing `initCooTrafo`/`switchKernel`, replacing hardcoded absolute kernel
   paths) and `OpcSg` (PatchNode construction with `asyncLoading` as a real config field).
5. **`GIS/TestViewer.fs` converted** to both — its hardcoded `C:\Users\haral\...hera_ops.tm`
   and its inline PatchNode block are gone.

6. **`src/PRo3D.ProjectionTestbed`** — new project, in the solution, runs end to end:
   `Config.fs` (scenario + CLI), `Setup.fs` (image/kernel/projector-camera resolution),
   `Offscreen.fs` (headless framebuffer render), `Compare.fs` (reference load, NCC,
   orientation sweep), `Program.fs`. Plus `scripts/run-projection-testbed.ps1`.

### First real run — results

`dotnet run --project src/PRo3D.ProjectionTestbed -- --flip-sweep` completes. Confirmed:

- **The `projectOntoQuat` fix was the blocker.** ASPECT/Didymos projection now resolves at
  2027-03-23T06:00. The `isFalse` assertions in `InstrumentProjectionComparisonTest.fs:151-152,188`
  are now asserting the bug and must be inverted.
- **The sidecar's exact kernel exists** — `spice/kernels/mk/former_versions/hera_plan_v180_20250616_001.tm`.
  No substitution, so the geometry is against the kernel the image was generated with.
- Target distance 10209.6 m → heuristic near/far 102 / 1020963. The old hardcoded
  `near = 1000.0` would have sat *inside* the body at this range.
- Pointing looks broadly right: the rendered body is centred and of comparable size to the
  reference.

## FINAL STATUS (2026-07-22) — the alignment ledger

The pipeline is verified correct end to end; what remains is a data-level offset.

Verified correct, each by direct measurement:
- Camera attitude: sc_quat boresight = TRG_POS direction to 1e-4 rad; body centre
  projects to the principal point to 0.03 px (image-free check, in the testbed).
- Camera position: SPICE Milani = sidecar TRG_POS to 0.0 m (same-kernel caveat).
- Frames: sidecar vectors are J2000 equatorial (Sun-vector latitude -24 deg proves
  equatorial; quaternion consistent). TRG_POS is spacecraft->target; the negation in
  Visualization.fs is the frame-origin conversion.
- Time: our DIDYMOS_FIXED->J2000 rotation matches spiceypy's proper UTC->ET
  computation to 6 decimals (str2et accepts the 'Z' suffix; the +69.186 s
  leap-second correction is applied). NOT a UTC/TDB error.
- Shape: OBJ + both OPCs are the same model (area-weighted CoF agree to 0.3 m);
  poles fully populated (no missing caps -- Didymos is genuinely oblate); the 57 m
  origin-to-CoF offset is real body geometry, present in the real image too.
- Not relativistic: stellar aberration ~0.4 px, light-time ~2 um. Orders too small.
- Barycenter: kernel's DIDYMOS_BARYCENTER is ~100 km wrong (ESA/ESAC confirmed,
  Piluca, May-2026 kernels). We use DIDYMOS everywhere and dodge it entirely.

The remaining problem:
- A rigid ~11 m / ~5 px offset of the WHOLE binary relative to the reference image.
  Common to both bodies (Didymos dx-4 dy-3, Dimorphos dx-5 dy-4 silhouette;
  topography agrees), so it is a system location offset -- not shape, not rotation,
  not per-body. Correction that nulls it: image (-5, +2.5) px = world
  (-2.91, -8.89, 4.67) m in DIDYMOS_FIXED (testbed --model-offset-px).
- Undetermined: body-frame-fixed (shape-model/frame registration; the same vector
  would null every epoch) vs J2000-fixed (ephemeris-level; varies). ONE SECOND
  EPOCH AT A DIFFERENT GEOMETRY DISCRIMINATES. That is the single missing
  measurement.

Known upstream issues to report:
- PRo3D-Extensions DeInit leaves stale DAF handles: after repeated metakernel
  swaps, binary-kernel (SPK/CK) reads fail with SPICE(DAFNOSUCHHANDLE) while
  text-kernel frames keep working. Needs kclear in native DeInit. Tests work
  around it by ordering plan-kernel tests before the swap-heavy ones.
- getRotationTrafo used to return Some Identity on failure (now honest None);
  the dimorphos test was green against that garbage for some time.

## HANDOVER — read this before continuing

### 1. Solar shading: `planetLocalLightingViewSpace` gives a SPHERE normal, not terrain

The goal of solar shading here is a signal that is *independent of the projected texture*:
shading generated by the model's own geometry, comparable against the real image's shading.
It is the only way to escape the circularity described below.

**The existing `Shaders.solarLighting` cannot provide it.** Its normal comes from
`planetLocalLightingViewSpace` (`src/PRo3D.GIS/Shaders.fs:45-55`):

```fsharp
n = (vp.XYZ - planetCenter) |> Vec.normalize
```

That is a smooth analytic **sphere** normal -- position minus body centre -- with no input
from the terrain whatsoever. `solarLighting` then does `dot(c, n)`, so the result is a clean
limb-darkening gradient with **zero topographic relief**: no craters, no boulders, nothing
to align on. It looks entirely plausible and tells you nothing. Do not build the shading
comparison on it.

**Use `generateNormal`'s face normal instead.** It is the true per-face geometric normal
(the one whose cross-product operand order was fixed this session), already computed and
sitting in the `localNormal` semantic, currently consumed only by the projection's
front-facing test.

The only missing piece is frames: `localNormal` is patch-local, `SunDirectionWorld` is in
the render frame. Add a per-patch `SunDirectionLocal` uniform right next to the existing
`ApproximateBodyNormalLocalSpace` in `src/PRo3D.Core/Surface/ImageProjectionOpcRendering.fs:59`,
which already does exactly this shape of computation:

```fsharp
patch.info.Local2Global.Backward.TransformDir(sunWorld).Normalized
```

then a fragment shader doing `Vec.dot n uniform.SunDirectionLocal |> max 0.0f`. The cosine
(not just its sign) survives because `Local2Global` is rigid for OPC patches.

Sun direction itself is already plumbed: `ProjectedImages.sunDirection` feeds a
`SunDirectionWorld` uniform through the same file. The testbed currently passes `None`.

### 2a. The ASPECT `specialTrafo` fix is REAL, but the det = -1 EXPLANATION IS NOT ESTABLISHED

Fix applied and retained: `MILANI_ASPECT_NIR1` changed from `Trafo3d.Identity` to
`Trafo3d.FromOrthoNormalBasis(V3d.IOO, -V3d.OIO, V3d.OOI)`.

A controlled A/B run (everything else pinned, only this entry toggled, render fixed at
640x512, no `loadReference` flip in either arm) measured:

```
                     Identity      (X,-Y,Z)
  silhouette IoU       0.8128        0.9479
  lit pixels           95629        106603      (reference 112285)
  centroid offset  +9.9,-10.2     +5.8,+1.5
  best orientation  flip-v 0.8603  as-is 0.9598
```

So the change is large, real, and in the right direction. **Keep it.**

**But the det = -1 story does not survive derivation.** `specialTrafos` sits in camera
space (between `viewTrafo` and `projTrafo`), and `FromOrthoNormalBasis(X,-Y,Z)` is
`diag(1,-1,1)`. For a symmetric perspective frustum that negates `y_ndc` and leaves
`x_ndc` and depth untouched — i.e. it is *exactly* a vertical image flip. It provably
cannot change lit-pixel count, and it does not alter the `normal.Z < 0` front-facing test
(which depends on the normal's camera-space **z**, untouched by `diag(1,-1,1)`).

The measurement above contradicts that: the pixel count changed, and arm B's `flip-v`
score (0.8603) does not equal arm A's `as-is` score (0.9598), as it would if one were the
flip of the other.

Resolution: the derivation tracked the vertex **position** but not the **texture
coordinate**. Both are driven by the same `ProjectedImageModelViewProj`, so both flip:

- Identity: point Q lands at pixel `(x,y)`, samples texel `(u,v)`
- Mirrored: point Q lands at pixel `(x,H-y)`, samples texel `(u,1-v)`

A pure image flip would require pixel `(x,H-y)` to hold texel `(u,v)`. It holds `(u,1-v)`.
The two flips compose into a genuinely different texture-to-surface mapping. **So what
this entry actually fixes is the UV/texture orientation, not a broken camera basis.**

**What is therefore NOT established:** that the ASPECT camera basis was mirrored, or that
this cancels the improper `FromBasis(-C0,-C1,-C2)` at `getLookAtQuat:131`. That line is
still det = -1 and still a real smell, and the HERA entries do still all happen to be
det = -1 — but this experiment says nothing about whether those facts are connected.
Do not cite 2a as evidence for it.

**Also note:** `silhouetteIoU` thresholds *brightness*, so it responds to texel placement,
not only to geometry. It is not a pure silhouette metric. Read it accordingly.

**Caveat retained:** one observation cannot distinguish `(X,-Y,Z)` from other bases that
agree at this geometry. Confirm across several epochs/phase angles before treating the
axis choice as calibrated.

#### Method note — three over-inferences made while getting here, all the same shape

Recorded because the pattern recurred:

1. "det = -1 confirmed by a 0.83 -> 0.95 IoU jump" — attributed a jump to the last thing
   touched, without pinning the other variables. The A/B run above is what a controlled
   test looks like.
2. "the improvement was really the 1024x1024 -> 640x512 viewport aspect" — inferred from
   two leftover output dirs that turned out to be from different code states entirely.
3. "`context.modelTrafo` is identity for Dimorphos" — inferred from the lit-pixel count
   moving by 1, using a metric that is ~97% Didymos and measures brightness rather than
   correctness. Direct logging showed it is `[-1137.64, -250.72, 31.70]`, |t| = 1165.3 m,
   exactly the SPICE placement. It was never identity.
4. "Dimorphos is misregistered by half a diameter" — read off `overlay.png`, then reasoned
   from for several rounds (suspecting SPICE sign, rotation, composition order, OPC
   centring) without ever isolating it. `Compare.overlay` thresholds **brightness**, so a
   partially *unprojected* body is indistinguishable from a *displaced* one. Cropping and
   zooming the region showed the silhouette was in exactly the right place all along, with
   its right third simply black. The bug was the winding-dependent front-facing test.
5. "Dimorphos is a hemisphere" — from a local-coordinate bbox reading. The global bbox is
   176.9 x 173.9 x 115.5 m against a true ~177 x 174 x 116 m: complete, and centred on its
   origin to 1.1 m. Corrected by the user.

In each case an insensitive or wrong-quantity metric was used to support a structural
claim. Prefer a controlled toggle, a direct measurement of the quantity in question, or --
cheapest of all -- **crop and zoom the actual pixels** before theorising about them.

Corollary worth keeping: `silhouetteIoU` and `overlay` both threshold brightness, so
neither is a pure geometry metric. The shaded (untextured) pass is the one to use when the
question is about position or shape.

#### Original analysis (retained — this is how it was found)

`getLookAtQuat` (`src/PRo3D.Base/InstrumentProjection.fs:131`) builds the camera basis as:

```fsharp
let t = Trafo3d.FromBasis(-frame.C0, -frame.C1, -frame.C2, V3d.Zero)
```

Negating **all three** columns of a rotation matrix gives `det(-R) = (-1)^3 * det(R) = -1`.
That is an **improper transform — a mirror**, not a rotation.

Now the determinants of `specialTrafos` (`InstrumentProjection.fs:138`):

| instrument | basis | det |
|---|---|---|
| `HERA_AFC-2` | `(Y, X, Z)` — X/Y swapped | **-1** |
| `HERA_AFC-1` / `HERA_HSH` | `(-Y, -X, Z)` — swap + two negations | **-1** |
| `MILANI_ASPECT_NIR1` | `Identity` | **+1** |

Composed with the -1 from the basis construction:

- **HERA instruments:** (-1) x (-1) = **+1** -> proper rotation, correct
- **ASPECT:** (-1) x (+1) = **-1** -> **mirrored**

So the HERA `specialTrafos` are doing double duty: an axis remap *and* silently cancelling
the improper `-C0,-C1,-C2`. ASPECT's `Identity` does not cancel it, so its camera basis is
left-handed and the render comes out mirrored. **A det = -1 basis produces exactly a
single-axis flip — which is what was measured: Y mirrored, X exact.**

The existing code comment already says ASPECT is `Identity` *"until this is calibrated
against a real rendered ASPECT image"*. That image now exists, and this is that calibration.

**Test:** set `MILANI_ASPECT_NIR1` to a det = -1 basis (e.g.
`Trafo3d.FromOrthoNormalBasis(V3d.IOO, -V3d.OIO, V3d.OOI)`), **remove the vertical flip in
`Compare.loadReference`**, and re-run. If Dimorphos aligns *and* the ~8 px residual shrinks
together, that is the answer. The determinant argument establishes that a mirror exists, not
which axis — sweep the candidate bases to find it.

**If this is right, the `loadReference` flip is masking a real bug and must be reverted.**

Two things that cannot be settled from the code alone:
- whether `Rot3d(sc_quat.Conjugated)` has the right sense — needs the sidecar's attitude
  convention documented; the sidecar declares bare `SC_QUATW/X/Y/Z` with no frame field.
- whether the improper `-C0,-C1,-C2` should itself be corrected rather than cancelled
  downstream. Fixing it at source would require re-deriving all three HERA `specialTrafos`,
  which are presumably tuned to their current values — so cancelling per-instrument is the
  lower-risk change, but the root cause is that line.

### 2b. UNRESOLVED (see 2a first): which side does the vertical flip belong on?

`Compare.loadReference` now flips the reference vertically. **This is a normalisation, not a
diagnosis, and it may be on the wrong side.**

What is established:
- There is a genuine vertical convention mismatch between render and reference. Dimorphos
  proves it: its rendered position comes from SPICE geometry, wholly independent of texture
  sampling, and only aligned after the flip.
- It is **not** a `getRelState` sign error. The native source
  (`PRo3D-Extensions/CooTransformation/src/CooTransformation.cpp`, `GetRelState`) passes
  `spkezr_c`'s target-relative-to-observer state through **unnegated**, and empirically
  Dimorphos's screen X matched exactly while only Y mirrored -- a position negation would
  displace both axes.

What is **not** established: whether the reference or the render is the inverted one.
Flipping either produces identical metrics. The delivered TIFF has Dimorphos upper-left;
our render puts it lower-left. If Aardvark's `runtime.Download` returns bottom-up rows then
the *render* is inverted and the flip is currently in the wrong place -- metrics unaffected,
but both output PNGs would be upside down relative to the instrument's true orientation.

Note the disk interior cannot settle this: with render camera == projector camera, a V-flip
in texture sampling and a V-flip in image loading produce identical interior agreement. Only
the silhouette discriminates, and it cannot give the absolute sense.

**Settle this before interpreting the 8 px residual below**, since that residual's meaning
depends on getting the flip on the correct side.

### 3. The comparison is partly circular -- know what the numbers mean

With `cameraMode = FromInstrument` the render camera and the projector camera are the *same
matrix*. Projecting with M and viewing with M places every texel at the screen position its
texture coordinate already implies, **for any geometry whatsoever**. So the projected texture
is a pixel-perfect copy of the source image wherever the model has surface, and a crude
potato would reproduce the boulders just as convincingly. The model is low-detail; those
features are painted on, not resolved.

Consequences:
- **NCC is near-worthless here** -- it is self-fulfilling over the disk interior.
- **Silhouette IoU is the honest metric**; it is the only place shape, scale and pointing can
  disagree.
- Solar shading (item 1) is the way out, because shading is generated by geometry.

### Measurements so far

```
                              Didymos only     + Dimorphos, flip normalised
  silhouette IoU              0.8302           0.8275
  raw centroid offset         21.3 px          15.7 px
  best-fit align              dx0 dy0 s1.00    dx-8 dy-8 s1.01
  best-fit IoU gain           -0.0011          +0.0281
```

Didymos alone showed **no** removable error: no translation or scale improved the overlap,
so pointing/ephemeris/FOV/scale validated to within ~4 px and ~1% (search granularity), and
the raw centroid offset was purely an artifact of the incomplete shape model.

Adding Dimorphos revealed a small **genuine** residual (~8 px, ~1%) that the symmetric
single-body test could not see. Interpretation pending item 2.

### Both shape models are partial

```
Didymos OPC   bbox X -392..431 (823)  Y -361..441 (802)  Z -340..266 (606)   <- Z cap missing
Dimorphos OPC bbox X -86.2..87.7(174) Y -0.085..86.7(87) Z -56.8..56.8(114)  <- hemisphere
```

Didymos X/Y match its ~780 m diameter but Z is short and asymmetric -- a cap is absent, which
is the clean smooth arc cutting across the overlay. Dimorphos's Y spans exactly half its
extent: DRACO only imaged one side before impact. Neither is a clean geometry reference, and
the IoU deficit is dominated by this rather than by any error.

Dimorphos is **body-centred, not `DIDYMOS_FIXED`** (its bbox maxes at ~88 m; in Didymos-fixed
coordinates it would sit ~1.2 km off origin), so it needs the SPICE transform --
`Setup.secondaryBodyTrafo`. Its OPC carries real `DRACO`/`Elevation`/`Gravity` layers,
whereas the Didymos OPC has only `Checkerboard` (which is what the first broken render was
showing).

`getRelState` returns **metres**, not km -- verified against Dimorphos's ~1.19 km orbit
(`spicePositionScale = 1.0`).

### Blocking issue (RESOLVED): the projected image was not visible

The render shows the OPC in a checkerboard texture — i.e. the patch's own default. No
"could not load texture" / "channel out of bounds" warning is logged, so
`createProjectedTiffTexture` *succeeded*; the projection is simply not being drawn, meaning
`stableImageProjection` discards every fragment (`inRange`, or the `normal.Z < 0` facing
test).

**Root cause: `generateNormal` had its cross-product operands reversed.**
`Vec.cross edge2 edge1` pointed the face normal *into* the body for OPC winding, so the
`normal.Z < 0` front-facing test in `stableImageProjection` rejected every fragment and
nothing was ever projected onto an OPC. The sphere/planet path masked this by following
`generateNormal` with `flipNormals`; the OPC paths did not, so they were silently dead.

Fixed at source (`ImageProjection.fs:154`) with the now-redundant `flipNormals` removed from
TestViewer's sphere path — net effect: sphere unchanged, OPC fixed. Confirmed by the score
going 0.4887 → 0.8656 with `as-is` becoming the best orientation.

**This very likely fixes image projection in the main viewer too** —
`Viewer-Utils.fs:861` uses `generateNormal` alone on OPC surfaces.

### Other fixes made while getting here

- **`tryLoadKernel` chdir removed.** It used to `Directory.SetCurrentDirectory` into the
  kernel folder and never restore it (the restore disposable was bound with `let`, not
  `use`), so every later relative path in the process resolved against the kernel tree.
  Restoring it is *not* the fix either — kernels load concurrently and a process-global
  directory cannot be saved/restored around a racing call. Instead `materializeMetaKernel`
  rewrites the meta-kernel's `PATH_VALUES` to absolute and furnshes a copy from `%TEMP%`.
  Kernel trees are routinely read-only, so the copy must not go next to the original.
  `PATH_VALUES` uses SPICE's `+` continuation because a kernel-pool string caps at 80 chars
  and temp paths are typically *longer* than the kernel tree. Continuation was verified
  empirically by forcing `chunk = 12` and confirming SPICE still resolved every CK/SPK with
  the path split mid-word.
- **Render defaults to the source image's native resolution** (640x512 here). A square
  viewport with a 1.25-aspect frustum stretches the result and makes the from-instrument
  comparison meaningless.
- **`visualizationRange` from the sidecar's statistics.** This ASPECT band peaks at 0.196,
  so the default 0..1 remap rendered everything at a fifth brightness.
- **`patchHierarchiesOf` filters to directories containing `Patches`.** Data folders sit
  next to saved scenes (`testdimo.pro3d`), and feeding one to `PatchHierarchy.load` throws.

### How the extrinsics are actually computed (reference)

`projectOntoQuat` (`InstrumentProjection.fs:117`):

```fsharp
toSpaceCraft * CameraView.viewTrafo view * specialTrafos[p.instrumentName] * (Frustum.projTrafo frustum)
```

with `toSpaceCraft = getRotationTrafo referenceFrame "J2000"`, i.e. `DIDYMOS_FIXED -> J2000`,
applied **first**. So points reach J2000 *before* the quaternion-derived view trafo — meaning
**`sc_quat` is treated as J2000-referenced**. The name `toSpaceCraft` is misleading; it is
`bodyToJ2000`. The sidecar carries no frame annotation on SC_QUAT, so J2000 is an assumption,
albeit the conventional one.

`position` is `-mbi.targetPos * 1000.0` (sidecar gives target-relative-to-spacecraft in km;
negated and converted to give spacecraft-relative-to-target), and must therefore also be in
J2000 for `CameraView.withLocation` to be consistent.

Also noted: `getLookAtQuat:125-128` calls `getRelState` and **discards the result** —
`let pos = targetState.pos` is never used. It is a pure failure-gate, the same vestigial
pattern as the `refold` conjunct removed from `projectOntoQuat` this session. Removing it
would let the MBI path work without any ephemeris for the viewer body.

### DONE since this plan was written

- **Solar shading (was item 1).** `Setup.sunDirection` (SPICE body->sun, returned as a
  `Result` so a failure disables the pass rather than inventing a direction), `Shading.fs`
  with a Lambertian term off `generateNormal`'s **face** normal, and `Compare.nccMasked`.
  First non-circular number: **masked NCC +0.65 over ~104k px** against the real image.
  Two traps found, both worth remembering:
  - `SunDirectionWorld` **must** be set via the `ProjectedImages` record.
    `projectionUniformMap` installs it as a *per-patch* uniform defaulting to `V3d.Zero`,
    and being per-patch it silently shadows any outer `Sg.uniform'` of the same name.
  - Ranking normal-sign variants by NCC picks the **wrong** sign (a tiny wrongly-lit
    region can score higher than the whole body). Rank by lit coverage.
- **Opposite winding between datasets.** `cross edge1 edge2` yields outward normals on
  Didymos and inward on Dimorphos, so a global sign blackens one body. Fixed by
  `Shading.generateOutwardNormal`: per **triangle**, compare the face normal against that
  triangle's own centroid in the body frame (both taken through a new per-patch
  `Local2Global` uniform, since `Local2Global` may rotate and the test is invalid in
  patch-local space). View-independent, LOD-independent, no per-dataset config.
  - Rejected: `ApproximateBodyNormalLocalSpace`. It is one direction *per patch*, and at
    coarse LOD a single patch covers a whole body, so it degenerates to a constant and
    splits the body in half. See `debug_outward_sign.png`.
  - Rejected for the angle products: orienting the normal towards the camera. It works,
    but forces emission <= 90 deg by construction, erasing the "this facet should not be
    visible" signal that flags an unreliable model at grazing angles. Still used in
    `sunDiffuse`, where it is harmless.
- **`ImageProjectionOpcRendering.fs`: `context.modelTrafo` was bound and discarded** in
  both `ProjectedImageModelViewProj` and `ProjectedImagesLocalTrafos` (the commented-out
  code beneath each shows the intent). Restored. This broke projection onto **any** surface
  with a non-identity model trafo; only harmless while everything sits at identity.
- **Photometric angle backplanes (visualisation).** `angle_incidence/emission/phase.png`:
  angle colour-mapped, brightness modulated by the projected band-0 image, `e > 90 deg`
  painted magenta as a quality flag. Phase measures ~20-25 deg — a **low-phase**
  observation, which is exactly the geometry where a smooth ellipsoid and a real rubble
  pile look most alike. Do not over-read the +0.65 until a high-phase epoch is tested.

### Remaining

1. **The rigid ~11 m offset** (see FINAL STATUS): needs a second epoch to attribute
   body-frame vs J2000. Highest-value open item.
2. Run the other ~13 NIR1 bands and a second instrument. One geometry that happens to work
   is not a validated pipeline.
3. A **high-phase** epoch — the only way to tell topographic agreement from gross shape.
4. Feature parity, then delete `Solarsystem.fs`.
5. **Float32 angle backplanes (data product, not the current colour-mapped PNGs).**
   The angle passes (`Shading.angleIncidence/Emission/Phase`) currently emit only 8-bit
   colour-mapped PNGs -- fine for discussion, useless for calibration (256 levels).
   TODO:
   - Emit raw radians (or degrees) as **float32 TIFF**. Use the LibTiff already in the
     repo (`BitMiracle.LibTiff.Classic`, `src/PRo3D.GIS/Tiff.fs`): mirror
     `readPlaneFloat32` in reverse -- `Tiff.Open(path,"w")`, SAMPLEFORMAT=IEEEFP,
     BITSPERSAMPLE=32, PHOTOMETRIC=MINISBLACK, WriteScanline per row. ~30 lines, no new
     dependency, and symmetric with how the mbi images are read (the calibration team
     already ingests float multi-band TIFF). NOTE: there is NO tinyexr writer in this
     repo -- the only EXR code is a read-only texture loader; EXR would mean adding a
     writer, so TIFF is the honest/easy path unless the team asks for EXR.
   - Reserve **NaN** for no-data (off-body, and shadowed once the mask exists); document it.
   - Add a `saveFloat32Tiff` next to `Offscreen.save`; render the angle passes to R32f
     (or download `PixImage<float32>`) instead of only the colour PNG.
6. **Illumination / shadow mask.** `cos(i) > 0` is currently assumed lit, so every
   self-shadowed pixel in the angle images is WRONG. Needs ray casting against the shape
   model, not a dot product. This is where the NaN sentinel above goes.
7. Useful additional angles/backplanes if the team wants them: local vs
   ellipsoid-referenced incidence/emission (we only do local), azimuth between the
   incidence/emission planes, and per-image scalars (heliocentric distance, sub-solar /
   sub-spacecraft lat-lon, slant range, ground sample distance).

## Project layout

New `src/PRo3D.ProjectionTestbed/` — modelled on `src/OpcViewer/OpcViewer.fsproj`
(net9.0 exe, `..\..\bin\$(Configuration)\` output, `<Import Project="..\..\.paket\Paket.Restore.targets" />`,
no Adaptify). Registered in `src/PRo3D.sln` with a fresh GUID.

Unlike OpcViewer it must also reference **PRo3D.GIS** (for `ImageProjection.Shaders`,
`Visualization`, `Tiff`/`MultiBandReader`, `ColorMapping`), alongside PRo3D.Base and PRo3D.Core.

```
src/PRo3D.ProjectionTestbed/
  Config.fs        -- scenario record + JSON load + argv override
  Scene.fs         -- OPC PatchNode + projection Sg assembly
  Offscreen.fs     -- framebuffer render + PixImage save
  Compare.fs       -- reference-tif load, flip sweep, diff metric
  Program.fs       -- [<EntryPoint>], window mode vs screenshot mode
  paket.references
```

`paket.references`: same list as `src/OpcViewer/paket.references`. Everything else arrives
transitively through the three project references. Add nothing to `paket.dependencies` unless
a build error proves otherwise (note `Aardvark.PixImage.Pfim` is lock-only, not declared —
it resolves today, leave it).

## What gets reused (do not reimplement)

| Need | Call |
|---|---|
| mbi sidecar → record | `InstrumentMetadata.tryParseMetadataForImagePath` (`src/PRo3D.Core/InstrumentMetadata.fs:312`) |
| enumerate a folder | `InstrumentMetadata.discoverInstrumentFolder` (`:317`) |
| image → `ITexture` | `Visualization.createProjectedTexture` (`src/PRo3D.GIS/Visualization.fs:53`) |
| raw tif pixels for comparison | `MultiBandReader.tryReadMultiBandTiff` (`src/PRo3D.GIS/Tiff.fs:171`) |
| mbi instrument → SPICE name | `InstrumentProjection.instrument2SpiceName` (`src/PRo3D.Base/InstrumentProjection.fs:141`) |
| projector trafo | `Visualization.projectDirect` (`src/PRo3D.GIS/Visualization.fs:65`) |
| SPICE init + kernel swap | `CooTransformation.initCooTrafo` / `switchKernel` (`src/PRo3D.Base/CooTransformation.fs:142,211`) |
| kernel lookup from sidecar | `CooTransformation.tryFindSpiceKernelFile` (`:117`) |
| OPC Sg + per-patch uniforms | `TestViewer.fs:273-341` pattern + `ImageProjectionOpcExtensions.projectionUniformMap` (`src/PRo3D.Core/Surface/ImageProjectionOpcRendering.fs:13`) |
| projection shaders | `ImageProjection.Shaders.*` (`src/PRo3D.GIS/ImageProjection.fs`) |
| remap/false-colour uniforms | `InstrumentImageVisualization.applyProperties` (`src/PRo3D.GIS/ColorMapping.fs:50`) |

Deliberately **not** reused: `Surface.Sg.createSgSurfaces` and the whole Surface/Scene/GUI
model stack. Constructing `PatchNode` directly (as `TestViewer.fs:302` does) lets async
loading be passed as a constructor argument and avoids dragging in the viewer's model.

Note `src/PRo3D.GIS/SpiceInterfacing.fs` is dead (compiled by no fsproj) — ignore it.

## Steps

### 1. Unblock `projectOntoQuat`
Remove the unused `getLookAt`/`refold` conjunct at `InstrumentProjection.fs:123`. Add a test
asserting ASPECT@2027-03-23 now yields `Some` with `hera_plan.tm` loaded, and reconcile the
two now-stale `isFalse` assertions in `InstrumentProjectionComparisonTest.fs:151-152,188`.

### 2. `Config.fs`
```fsharp
type Scenario = {
    opcPath          : string
    body             : string          // "DIDYMOS"
    referenceFrame   : string          // "DIDYMOS_FIXED"
    observer         : string          // "MILANI"
    imageFolder      : string
    imageFile        : string option   // None -> first from discoverInstrumentFolder
    spiceKernel      : string option   // None -> resolve via sidecar SPICE_MK
    projectionMethod : ProjectionMethod
    channel          : int
    width : int; height : int
    outputDir        : string
    mode             : Interactive | Screenshot
    cameraMode       : FromInstrument | ThirdPerson
    flipSweep        : bool
}
```
JSON-loadable (Chiron or `System.Text.Json`; Chiron is already in OpcViewer's references),
with argv overrides. Defaults hardcoded to the Didymos/ASPECT scenario above so a bare run
does the right thing.

### 3. SPICE bootstrap
`initCooTrafo None appData`, then resolve the kernel: explicit `spiceKernel` if given, else
sidecar `SPICE_MK` via `tryFindSpiceKernelFile` against `C:\Users\haral\Desktop\pro3d\spice\kernels`,
falling back to `hera_plan.tm` with a loud warning that the exact requested version was
substituted. Use `switchKernel` (DeInit+Init+Add), never layered `AddSpiceKernel` — see the
rationale comment at `src/Tests/HeraSpiceTests.fs:37-50`.

### 4. `Scene.fs` — OPC + projection
Follow `TestViewer.fs:273-341`:
- `runtime.CreateLoadRunner 1`, `FsPickler.CreateBinarySerializer()`
- `PatchHierarchy.load`, `PatchLod.toRoseTree`
- `PatchNode(..., useAsyncLoading = false, OpcRenderingExtensions.captureContext,
   ImageProjectionOpcExtensions.projectionUniformMap, ...)` — **`false` is the whole
   deterministic-screenshot mechanism**; the LOD decider is otherwise a pure function of
   camera+viewport, so sync loading makes a frame fully reproducible.
- `Sg.applyBody (AVal.constant (Some body))`
- `Sg.applyProjectedImages'` supplying `{ imageProjection; localImageProjectionTrafos;
  sunDirection; sunLightEnabled }` (`src/PRo3D.Core/Surface/OpcRenderingProperties.fs:33`)
- shader chain per `TestViewer.fs:445-456`: `stableImageProjectionTrafo` → `generateNormal`
  → `stableTrafo` → `constantColor` → `diffuseTexture` → `stableImageProjection`
- `applyProperties` + `Sg.texture "ProjectedTexture"`

### 5. `Offscreen.fs`
Per `SnapshotApp.executeCameraAnimation` (`src/PRo3D.SimulatedViews/Snapshots/SnapshotApp.fs:133-190`)
but standalone — no Suave, no mutableApp: create Rgba8 + DepthComponent32f textures, a
framebuffer signature, `CompileClear` + `CompileRender`, `task.Run(outputDescription)`,
`runtime.Download`, `.Save(path)`. In screenshot mode render a few frames before downloading
so the LOD tree settles even with sync loading.

### 6. `Compare.fs` — the actual validation
With `cameraMode = FromInstrument`, view/proj = the instrument trafo, so:
- Render → `render.png`
- Load the reference `.tif` via `tryReadMultiBandTiff`, normalise → `reference.png`
- Emit `sidebyside.png` and a normalised-cross-correlation / abs-diff score
- With `flipSweep = true`, re-render the four (flipU × flipV) combinations plus a transpose
  and print a ranked table of scores

That table is the direct answer to "are the images flipped, or is it worse". A clean win for
one flip combination means a UV convention bug; all four scoring badly means the trafo chain
(`specialTrafos["MILANI_ASPECT_NIR1"]` is `Identity` and explicitly **uncalibrated**, per
`InstrumentProjection.fs:100-103`) is wrong and the next step is solving for the axis remap.

### 7. Test script
`scripts/run-projection-testbed.ps1` — parameters for OPC path, image folder, image name,
frame, kernel, resolution, output dir; defaults to the Didymos/ASPECT scenario; forwards to
`dotnet run --project src/PRo3D.ProjectionTestbed -- --screenshot ...`. Plus a
`scenarios/didymos-aspect.json` and a `scenarios/mars-hsh.json` so a second instrument is
exercised from day one.

## Open questions

1. **Instrument frusta are duplicated three times** and disagree — `Visualization.fs:68` and
   `:110` (identical), and `TestViewer.fs:153` (divergent: no ASPECT, different HSH aspect).
   The testbed should consume one shared source. Suggest hoisting the map into
   `InstrumentProjection` as `defaultInstruments` and having all three sites use it. Worth
   doing as part of this, or keep it a separate cleanup?
2. **Near/far planes** are hardcoded to Mars scale (`near = 1000.0`, `far ≈ 3.01e10` m). At
   Didymos ranges (~10 km) a 1 km near plane will clip the body. This almost certainly needs
   to become scenario-derived — likely from `mbi.targetPos` magnitude.
3. **Window mode**: reuse `Solarsystem.fs:169-187`'s `OpenGlApplication` + `CreateGameWindow(8)`
   with `DefaultCameraController` for interactive inspection, sharing the identical Sg with
   screenshot mode so what you debug is what gets captured.

## Note on unrelated in-flight change

`src/PRo3D.GIS/ImageProjection.fs:120` was edited earlier this session (multi-image clip test
now checks `tc.Z`, matching the single-image path). Unbuilt and unverified. The larger
projector-eye/facing-test refactor discussed alongside it was **not** started.
