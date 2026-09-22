# Handover: simulated AFC image series

Written to be read cold. Every number below was measured in this repo; the command that
produced it is named nearby. Where something is unverified it says so.

## 1. Where things stand

**Branch** `features/801_simulate-series`, 21 commits off `origin/releases/6.2.0`,
**not pushed**. Built from `releases/6.2.0` by cherry-picking only the #801 commits, so it
does *not* carry the two annotation-profile-export commits that sat on the older
`features/801_instrument-geometry-and-deshading` branch (those belong to
`origin/features/annotation-profile-export-test`).

**`stash@{0}` holds three unrelated strands** that were in the working tree: the
cross-section curtain work, the ellipse annotations, and the installation docs — plus the
previous version of this file. `git stash pop` on the right branch to get them back.

| | |
|---|---|
| `src/PRo3D.Tool/SimulateSeries.fs` | **`simulate-series`**: a whole series in one process |
| `src/PRo3D.Tool/SimulateImage.fs` | shared shading params, de-shading fit, `ShapeSource` (OPC or mesh), eclipse occluder, pointing pre-flight |
| `src/PRo3D.Tool/ObjShape.fs` | the Wavefront reader, the winding vote and the mesh scene graph |
| `scripts/make-image-time-series.py` | drives it once: epochs, SPICE reference, validation, stack, scene, manifest |
| `scripts/check-series.py` | re-checks a series against **our own** ray-cast |
| `scripts/check-lighting.py` | checks the SHADING -- shadows, acne, photometry -- against a ray-cast of the same mesh |
| `scripts/make-shape-crosscheck-figures.py` | the OPC/OBJ/DSK comparison matrix in `ShapeModelCrosscheck.md` |
| `scripts/make-eclipse-figure.py` | the eclipse light curve and ingress strip |
| `scripts/check-renderers.py` | checks against **other people's** renderers — the independent test |
| `scripts/pro3d_sim.py` | shared: tool invocation, sidecar check, scene writer, `dsk_render`, validation, comparison helpers |
| `docs/dev/AFC-image-orientation.md` | the orientation investigation, with figures — **read this before touching `specialTrafos`** |

Data root `C:\Users\haral\Desktop\pro3d\image-series\`: `close-2027-02-25` (244 epochs) and
`rotation-2027-03-21` (48), each with `delit/ baked/ micro/ smooth/ spice/ stack/`,
`README.md`, `series.json`, `ImageSeries.pro3d`. Both validate.

Reference data (outside the repo):

| | |
|---|---|
| `C:\pro3ddata\HERA\workshop3\ref\` | comet-toolbox screenshots, `HHMM.png` |
| `…\image-series\eclipse-2027-02-25\` | eclipse series, OPC + delit, 750 epochs at 8 s — its `delit` is 97 % filler texture, see §2 |
| `…\image-series\eclipse-2027-04-08-fullframe\` | eclipse series from the **OBJ** (no invented texture), 750 epochs at 7 s, `--distance 2600`, real Didymos casting |
| `C:\Users\haral\Desktop\pro3d\spice2\misc\cosmo\` | a full Cosmographia config on the same planning kernels — the unused third renderer |
| `C:\pro3ddata\HERA\workshop3\COP\COP\<date>\` | Pilucas' COP set, 85 days, 8065 frames |
| `C:\pro3ddata\HERA\Workshop2\OPC\Didymos\` | Didymos OPC, 341 MB |
| `C:\Users\haral\Desktop\pro3d\spice2\kernels\` | our kernels, plus `mk/former_versions/hera_plan_v182_20260805_001.tm` fetched from ESA |

## 2. What changed

**`simulate-series`: 50 minutes → 50 seconds.** `simulate-image` is a complete program per
frame — GL context, OPC load, de-shading fit, scene graph, LOD warm-up — and none of it
depends on the epoch. The new verb hoists all of it and drives the sun, camera and shadow
frustum as `cval`s. 429 frames at 0.12 s each, **bit-identical** to the per-frame path
(0 DN over nine frames spanning a rotation). Both verbs share one shading-uniform block,
one shader stack and one pointing pre-flight.

**A fourth lit variant, `baked`**: the DRACO mosaic lit *without* de-lighting, so users can
measure whether de-lighting matters for their reconstruction instead of taking it on
trust. It is `delit` with the division switched off and nothing else changed — including
brightness, via its own `rawScale`. The first attempt reused `delit`'s internal fallback
normalisation, came out 1.8× too bright and clipped 55–75 % of the body: an exposure
artefact that would have read as the effect the variant exists to demonstrate.

**`--obj`: rendering from a mesh.** Both verbs take `--obj <file>` (`.obj.gz` read
directly) instead of `--opc`, behind a `ShapeSource` record that is the only thing the two
shape models disagree about — how the scene graph is built, how many warm-up frames a pass
needs, and whether there is a texture to fit. Camera, photometry, shadow pass, shader
stack, sidecar and pointing pre-flight are shared unchanged. The reader is our own: the
delivered file is `v`/`f` lines and nothing else, and `ObjParser` → `GetFaceSetMeshes` →
`PolyMesh` would build double-precision meshes and per-vertex crease normals we throw away.
175 MB parses in **1.0 s**; `simulate-series` pays it once.

**And it is exact.** The silhouette of a PRo3D render of the kernels' Dimorphos OBJ against
a `spiceypy` ray-cast of the DSK built from that same OBJ: **IoU 1.000** over four epochs —
79 836 covered pixels against 79 837, raw and uncentred. The OPC manages 0.972. That closes
"the remaining difference is the shape model": our rasteriser, camera, instrument frame,
FOV and units agree with SPICE's own ray-cast to under a pixel of 1020.

**The OBJ has no texture, and the `.png` beside it is a preview render**, not a map — the
kernel set's own `aareadme.txt` says so ("an image example in png format for convenience").
So `delit`/`baked` (and `--deshade`/`--texture-albedo`/`--texture-only`) are **refused** on
an untextured mesh instead of falling back to the constant albedo: an untextured `delit/`
folder is pixel for pixel `micro/`, and the README would have claimed otherwise. With
`--obj-texture` they work again — the fit then samples the texture per vertex and goes
through the same `fitFromSamples` the OPC path uses.

**Units.** `--obj-scale` is metres per file unit, default **1000**: the DSK models are in
km (`INPUT_DATA_UNITS = DISTANCES = KM` in their own MKDSK setup). The extent in metres is
logged on load and an implausible body is warned about.

**The model is in the test repo.** `PRo3D.Resources.TestData/HERA/Dimorphos_dsk/` —
`g_00243mm_spc_obj_dimo_0000n00000_v004.obj.gz` (175 MB → 39 MB; GitHub refuses >100 MB),
the preview PNG, and a `CREDITS.md` with the provenance out of the `.bds` comment area
(DART SOC, 2023-05-30; ESS DSK 2023-07-31; SPC from DRACO). **It is CC BY-NC 3.0 IGO**, the
ESA SPICE dataset licence, which is not the licence of the rest of that repo — the
CREDITS.md and the repo README both say so.

**The eclipse is a real shadow pass now, and it is verified.** `181aff62` tested a triaxial
ellipsoid analytically in the fragment shader. It is now a second sun-side depth map fitted
to the OCCLUDER -- 0.3 m a texel over Didymos, against the 5 cm the target's own map keeps
-- and the occluder's geometry is a `ShapeSource` like any other: `--occluder-obj` for the
primary's real shape, otherwise a tessellation of its reference radii. One path, so the
coarse case and the real case cannot disagree for any reason but the shape.

Measured on 2027-02-25 at 5 min: first contact just after 10:35, totality 10:55-11:45 at
the ambient floor (DN 3 of 140), last contact just after 12:05. The real shape and the
radii agree exactly during totality and differ by up to 30 percentage points of the disk
through ingress and egress -- the radii say WHEN, only the shape says HOW MUCH.
`scripts/make-eclipse-figure.py` renders the window three ways and writes the light curve.

**The shading is checked against SPICE now, and that found acne.** Rendering from the
kernels' own mesh makes a per-pixel comparison meaningful -- it measures the SHADING, not
the shape -- and `scripts/check-lighting.py` asks it over sun-facing facets only (a facet
turned away from the sun is the terminator, which every renderer gets right).

`--shadow-bias 0.002`, the old default, wrongly darkened **2.6-6.1 %** of the sun-facing
body, 1.0-3.2 % of it isolated pixels: acne, visible in the figure as a stipple across every
sun-grazing slope and invisible in the greyscale render. The default is **0.006** now,
swept: 0.0005 gives 18-20 % darkened, 0.02 leaves 19-24 % of the real shadow lit. At 0.006,
cast shadows agree at IoU **0.891-0.984**, leak 0-2.9 %, brightness r **0.9934-0.9937** with
RMS 3.3-4.8 % -- and that residual is our 5 % Lambert admixture, which the ray-cast has not.

Slope-scaling the bias, as the viewer's `terrainSunShadow` does, was tried and **measured no
better**: this map is FINER than the geometry it renders (6.6 cm a texel against 0.24 m
facets), so the depth error is not the sampling footprint slope-scaling models. Not in the
shader; the reason is written where the constant bias is subtracted.

`pro3d_sim.dsk_illumination` exposes mu0, mu and the lit flag, which is what makes any of
this askable -- `illumf`'s image is zero exactly where the interesting pixels are.
`dsk_render` is a two-line wrapper on it, so there is still one ray loop.

**No eclipse shows the DRACO mosaic lit, and no epoch list fixes that.** Over all 188 umbra
events between 2027-02-01 and 2027-05-01 the sub-solar point sits **94-97 deg** from the
DRACO footprint centre -- a three-degree spread across three months. An eclipse happens when
Dimorphos is anti-sunward of Didymos, which pins the sub-solar longitude in the body-fixed
frame, and the footprint is a fixed patch of that frame. So an eclipse series lights the
SYNTHETICALLY FILLED hemisphere and its `delit` frames show procedural texture.

The footprint, measured from the OPC's own `LonLatRad` and `DRACO_1` layers: centre **lon
264.6, lat -1.9**, covering **46.2 %** of the body. `DRACO_1` is the real mosaic and is black
outside it; `DRACO_2` is the same mosaic with the other 54 % filled in. 137 of the 188
eclipses have AFC-1 on target, and at the best of them the footprint centre is still 75 deg
from the disk centre. If the mosaic is what you want to show, it is both facing HERA and lit
at **138 epochs**, best around **2027-02-28T22:00** (10 deg from disk centre, 11 deg from the
sub-solar point, 6.70 km, phase 20.5 deg) -- a different series, not an eclipse.

**`check-renderers.py` now compares per footing.** It used to pick "the strongest footing
both can supply", which for our renders is always the lit region — and that hid the one
measurement that isolates geometry. Each pair now gets a row per footing with its own
floor, and a floor whose footing has no frames prints `NO DATA` and fails rather than
passing silently.

**No resume.** Folders are recreated per run. Resuming is what once left a folder holding
frames from two builds 90° apart, and at 0.12 s a frame it saves nothing.

**Validation is a stage**, not a script to remember. Three gates, each failing for a
different reason, all gating the exit code: the sidecar round trip (every frame, in the
tool), the boresight invariant (every sidecar on disk), the SPICE cross-check
(`--spice-count` epochs). The verdict goes into `series.json` and the README.

**`--docs-only`** rewrites README and manifest from what is on disk, so fixing a sentence
never re-renders a finished series.

**The F# suite runs again.** It had not been run since the instrument-axis work and did not
compile (`SimulateImageOptions` gained `--pointing` without the fixture). Then it
deadlocked: the verbs called `CreateLoadRunner` ad hoc, five times, and that call marshals
onto the GLFW instance's thread — which in the test process is the main thread, sitting
inside Expecto waiting for the very test asking for a runner. PRo3D's convention is one
runner per process via `Surface.Sg.hackRunner`; all five now use it. **628 tests, 620
passed, 6 ignored, 8 failed** — all eight pre-existing SPICE kernel-swap failures that
fail in isolation too, including a `J2000 → J2000` identity. With the OBJ reader's ten
tests plus the eclipse occluder's two it is **640 run, 632 passed, 6 ignored, 8 failed** —
the same eight. One of them
bakes a known light direction into a UV sphere's texture and asserts the mesh de-shading
fit recovers it to under 2°; it comes back 0.13° off at r = 1.00, which is what makes the
V-flip and the vertex normals checkable rather than merely plausible.

## 3. The orientation question — read `AFC-image-orientation.md`

`ea3337cb` rotated every AFC image 90°, because `hera_afc_v06.ti`'s FOV diagram draws +X
image right and PRo3D mapped +X to image up. comet-toolbox renders the same epochs 90°
from that. **We now ship comet-toolbox's orientation**, which is the pre-`ea3337cb` basis,
with all three instrument entries sharing it.

That is a convention choice, not a correction: the IK still reads the other way, and the
sun-direction test that supported `ea3337cb` cannot decide the question — it confirms our
image puts the lit limb along the axis we *call* +X, whichever that is. The FOV half of
that commit stands independently: `getfov` returns ±0.04792 rad = 2.75° half-angle,
confirming 5.50° over 5.5306897076421.

What the investigation established:

- **All three pipelines use geometrically identical kernels.** comet-toolbox's
  `v182_20260805`, Pilucas' `20260817` and our `20260820` agree to 0.00 m and 0.000000° at
  five epochs. Kernels explain nothing between any pair. They are *not* stable in general
  — `v182_20260527` differs from ours by 780 m and 112–132° — they jump when the mission
  re-plans, and all three of us sit on the same side of the last jump.
- **Pilucas is not an independent renderer.** She drives PRo3D through sequenced
  bookmarks, with her own SPICE code building the camera. Same renderer, same OPC,
  identical kernels — so she tests exactly three things: the camera construction, the image
  axis convention and the FOV. Her transpose asymmetry is *correct*: her camera pose is
  `R_body⁻¹ · R_cam`, and the two matrices have different jobs. I queried it twice and was
  wrong twice.
- **comet-toolbox is, to the accuracy a screenshot allows, a SPICE ray-cast of the
  kernels' DSK.** Our `dsk_render` on their kernels reaches 0.944–0.982 silhouette overlap
  against their frames — the limit of what a downscaled screenshot can resolve.
- **The remaining difference against comet-toolbox was the shape model, and `--obj` closes
  most of it.** comet vs our OPC render: 0.856 worst / 0.905 mean over four epochs of
  2027-02-25. comet vs our OBJ render: **0.890 / 0.931**. The rest is the metric, not the
  shape — see the terminator ceiling in §5.

## 4. What is open, in order

**1. An independent eclipse reference.** The eclipse is implemented, rebuilt around a real
depth pass, and **verified against itself** — timing and floor, on 2027-02-25: first contact
just after 10:35, totality 10:55–11:45 at the ambient floor, last contact just after 12:05.
What it does not have is a second opinion, and this is the one place on the whole page where
`dsk_render` cannot be the oracle: `illumf` takes a single target body, so the ray-cast
renders an eclipsed epoch fully lit. Two ways to get one:

- **Extend `dsk_render`.** From each surface point, cast a ray toward the Sun and test it
  against the PRIMARY's DSK (`sincpt` with `DIDYMOS` as the target, the fragment as the
  ray origin). ~20 lines, no new data, and it gives the oracle the one answer it currently
  cannot produce.
- **Cosmographia.** The kernel tree ships a full configuration at `misc/cosmo/`, including
  `config/spice_hera_plan.json` — the same planning kernels we render against, and the
  Didymos DSK arcs. It is an interactive app, so this is a screenshot, like the
  comet-toolbox ones in `C:\pro3ddata\HERA\workshop3\ref\`. None of those is at an eclipsed
  epoch, so one would have to be taken.

**2. Both bodies in one frame — the missing feature.** The occluder is a shadow caster and
nothing else: it is placed, it is rendered into its own depth map, and it never appears in
the image. That is a real gap, because the pair is spectacular and reachable. At
**2027-04-25T03:00–04:00** Didymos is 2.6–3.6° from Dimorphos with an angular diameter of
**7.1°** — wider than AFC-1's whole 5.5° field — and both are inside the frame; phase drops
to 0.6°. There are 100+ such epochs (see the `didySep` column of the showcase scan). The
scene graph already has the occluder's geometry and its placement trafo; putting it in the
MAIN render as well as the shadow pass is a small change, and it would make transits,
occultations and mutual events renderable instead of merely computable.

**3. A showcase set.** Measured over 2027-02-01 .. 2027-05-01 at 10 min, with the DRACO
footprint centre at lon 264.6 / lat −1.9 (see section 2):

| what | best epoch | why |
|---|---|---|
| **dramatic terminator on real mosaic** | **2027-03-18T11:50** | 4.77 km, phase 58.2°, footprint 2.6° off disk centre, boresight on target. The best frame this tool has produced |
| closest with the mosaic in view | 2027-04-01T14:20 | 4.47 km, phase 35.7° |
| closest overall, on target | 2027-04-22T13:00 | 3.88 km — but phase 2.7°, a flat disk |
| mosaic face-on and lit | 2027-02-28T22:00 | 87.6 % of the lit disk is real DRACO_1 |
| deep partial eclipse | 2027-04-08T13:18–14:45 | dims to 19 % of unshadowed; rendered, 750 frames at 7 s |
| both bodies in frame | 2027-04-25T03:00 | **not renderable yet** — see item 2 |

Two traps in there. Epochs the user picks by eye are often **off target** — 2027-03-08T14:15
has the boresight 5.27° away, so it needs `--pointing lookat`, whose roll is a convention.
And `--distance` is what makes any of these full-frame: no eclipse gets nearer than 5.5 km,
where the body is 34 % of the frame width.

**4. Re-render both existing series** with the eclipse on and at `--shadow-bias 0.006`, and
re-run `check-renderers.py` and `check-lighting.py`. Everything delivered before this branch
carries the acne.

**5. Ship**: push the branch and open the PR.

**Also open**, not blocking:

- **COP coverage.** Ours is 4 days of the 85-day COP phase — two deliberate rotation-length
  windows, never a survey. A full COP run at 15 min is ~8160 epochs, ~⅓ on target for
  Dimorphos, ~21–24 min of rendering. To match Pilucas' completeness the primary would have
  to be in the scene, since AFC-1 looks at Didymos two thirds of the time.
- **#801 proper**: `SpiceInterfacing.fs` wraps `CooTransformation`, which exposes no
  kernel-pool access — no `getfov`, no `bodvrd`. That is why the instrument FOVs *and* now
  Didymos' radii are hand-copied constants.
- The 8 SPICE test failures and the ±6 % silhouette difference against Pilucas (a
  camera-path question, since everything else is identical).

## 5. Things that cost time, so they do not cost it again

**A test that silently compares nothing still prints PASS.** `check-renderers.py`'s
contamination gate compared raw pixel counts, so a 518 px screenshot panel against a
1020 px render looked like a different body and every comet-toolbox pair was dropped —
while the run reported success. Check a new test against values you already know.

**Our lit mask is not a ray-cast's, and that caps the lit metric at ~0.90.** Two renderings
of the SAME mesh — our `--obj` render and `dsk_render` of the DSK built from it — agree to
IoU 1.000 on the silhouette and only **0.897** on the lit region. Ours thresholds at DN 25,
the ray-cast writes every pixel with μ₀ > 0, and the ~5 % of lit area between them is the
whole difference. So a lit-region floor of 0.93 for `comet vs obj` — which is what the
previous version of this file proposed, copied from the `comet vs spice` measurement — was
a test of our *shading* wearing the label of a test of our *shape*, and no correct render
could have passed it. The acceptance criterion is now the silhouette, where it is exact.

**Correlating grey levels between renderers measures nothing.** Two shape models disagree
pixel by pixel whatever the orientation: all eight dihedral transforms came out negative on
frames whose outlines overlap at 0.9. The silhouette carries the geometry.

**Compare like with like.** comet-toolbox shades (compare lit regions); Pilucas does not
light her output at all (compare silhouettes). Comparing our lit crescent against her full
disk produced a confident 28° "disagreement" that was not there.

**The major-axis metric lies on a round body.** At 14:15 and 14:45 the lit regions overlap
at 0.835 and 0.903 while the axis difference reads −11.4° and −14.1°. Trust the overlap.

**A frame containing the other body is not a disagreement.** AFC-1 points at Didymos for
two thirds of COP and it overflows the frame — 77 % of it by 12:30. That produced an
"opposite rotation sense" that was Didymos entering the frame.

**Our two renderers are one witness.** PRo3D and `dsk_render` were written to the same
reading of the same IK diagram, so they cannot disagree about a convention they share.
`check-series.py` cannot catch that class of error; `check-renderers.py` exists for it.

**A metakernel's `PATH_VALUES` is relative to its own directory.** The process has to stay
there; use absolute paths for everything else.

**Heredocs mangle Python.** Writing `\n` inside a heredoc-fed script corrupted files
repeatedly. The same thing silently eats F# string line continuations: a trailing `\` in a
`Log.error` format string comes out flattened, with the next line's indentation left
sitting in the middle of the message. Use the Write/Edit tools for code.

**The eclipse commit broke the test build and nobody noticed.** `181aff62` added
`occluderBody`/`occluderFrame` to `SimulateImageOptions` without the fixture in
`Pro3DToolTests.fs`, so the suite would not compile at all. Fixed here. A field added to an
options record is a compile error in a project the verb itself does not reference — run the
suite after touching `Cli.fs`.

**Renders hold `PRo3D.Base.dll`**; a build during one fails with MSB3027.

**Thresholds are only as good as the data they were calibrated on.** The validation margin
was 0.02, set when the largest near-symmetry artefact seen was 0.009; a longer series
produced 0.023 and the run failed. It is 0.05 now, measured against both populations —
real axis errors separate by 0.12–0.73, artefacts by ≤0.023.
