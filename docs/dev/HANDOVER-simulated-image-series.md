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
| `src/PRo3D.Tool/SimulateImage.fs` | shared shading params, de-shading fit, eclipse occluder, pointing pre-flight |
| `scripts/make-image-time-series.py` | drives it once: epochs, SPICE reference, validation, stack, scene, manifest |
| `scripts/check-series.py` | re-checks a series against **our own** ray-cast |
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
fail in isolation too, including a `J2000 → J2000` identity.

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
- **The remaining difference against comet-toolbox is the shape model.** Our OPC gives
  0.856–0.972; the DSK gives 0.944–0.982 on the same epochs and kernels.

## 4. What is open, in order

**1. OBJ support — the next task.** The kernels ship `g_00243mm_spc_obj_dimo_v004.obj`
(175 MB ASCII, 1.58 M vertices, 3.15 M faces) with a 537 KB texture.
`Aardvark.Data.Wavefront 5.3.10` is already in `paket.lock` and used by `PRo3D.Core`; it
needs a line in `src/PRo3D.Tool/paket.references`.

Why it matters beyond agreeing with comet-toolbox: **AFC is 1020 × 1020 at 93.7 µrad/px, so
at 5 km a pixel is 0.48 m while an OPC post is 1.96 m.** One post covers about 4 × 4
detector pixels — the delivered frames are shape-limited, not sensor-limited, and do not
resolve what AFC would see. The DSK at 0.24 m is twice as fine as a pixel.

The mesh path is *simpler* than the OPC path: body-local position and normals come straight
from the file, and `stableTrafo`'s precision trick is unnecessary at 177 m. The parse cost
is paid once per `simulate-series` process. `delit` is the hard part — the de-shading fit
reads per-vertex `.aara` layers that an OBJ does not have, so it would need refitting
against the texture.

**Acceptance criterion, already encoded**: `FLOORS[("comet", "obj")] = 0.93` in
`check-renderers.py`. Rendering from the OBJ must reach what the ray-cast of that same
shape reaches; if it does not, the shape was not the problem.

**The cost to weigh**: the sidecars currently promise that projecting a frame back onto
*this OPC* reproduces it. Rendering from the OBJ breaks that unless the delivery moves to
the OBJ shape as a whole.

**2. Verify the eclipse.** Implemented (`181aff62`) and **unverified** — it builds, the
geometry is computed, no render has been checked. Render 10:30–12:10 on 2027-02-25 and
confirm the body goes dark 10:45–12:00 with partial phases at 10:35 and 12:00, then
compare 11:30 against comet-toolbox, which renders it black.

**3. Re-render both series** once the OBJ and the eclipse are in, and re-run
`check-renderers.py`.

**4. The export scripts** still need adapting to whatever the OBJ path changes.

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
repeatedly. Use the Write/Edit tools for code.

**Renders hold `PRo3D.Base.dll`**; a build during one fails with MSB3027.

**Thresholds are only as good as the data they were calibrated on.** The validation margin
was 0.02, set when the largest near-symmetry artefact seen was 0.009; a longer series
produced 0.023 and the run failed. It is 0.05 now, measured against both populations —
real axis errors separate by 0.12–0.73, artefacts by ≤0.023.
