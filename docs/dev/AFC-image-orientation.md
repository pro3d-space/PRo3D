# AFC image orientation: a 90° disagreement with comet-toolbox

**Status: settled by convention, not by evidence.** We ship the orientation the
comet-toolbox HERA dashboard shows. The underlying question — which one the kernels
actually call for — is still open.

Our frames and comet-toolbox rendered the same epoch 90° apart. This note records what
was measured, what was assumed, and what would settle it, because the evidence that drove
`ea3337cb` turns out not to decide the question.

## The observation

comet-toolbox (<https://comet-toolbox.com/dashboard/hera/>), AFC1, 2027-02-25T06:30:00,
against our frame for the same epoch.

| | extent (of frame width) | illumination direction |
|---|---|---|
| ours | 20.9 % × 30.7 % | 347° |
| comet-toolbox | 30.7 % × 21.8 % | 76° |

*0° = right, 90° = up. Measured on the largest connected lit blob: direction from the
blob's shape centroid to its brightness centroid.*

**Difference: 88.6°.** The extents are reciprocal (0.68 vs 1.41), which is what a 90°
rotation does.

Silhouettes overlaid — ours cyan, comet-toolbox magenta, agreement white. Both are
normalised to the same centre and scale, so the picture shows orientation and shape only;
apparent size already agreed to three digits before any of this.

| as `ea3337cb` shipped it | after matching the dashboard |
|---|---|
| ![90° apart](../images/afcOrientation/overlay-before.png) | ![aligned](../images/afcOrientation/overlay-after.png) |
| silhouette IoU **0.52** | silhouette IoU **0.81** |

The residual fringe on the right-hand panel is not a rotation. The lit-region major axis
agrees to **0.4°** (165.1° against 164.7°) and a fine rotation search peaks at −1.1° with
almost no gain over 0° (IoU 0.8156 vs 0.8127) — there is no angle left to find. The
coloured edge is relief: comet-toolbox uses a finer shape model, so its terminator carries
more structure, which is also why the *brightness* centroid still differs by ~6° while the
outline does not.

## Three renderers, and what separates them

Against comet-toolbox (screenshots at five epochs) and Pilucas' COP set
(`HERA_AFC_*_COP.png`, 1020x1020, 15 min cadence), all on 2027-02-25.

### They shade differently, so compare the right thing

| | lighting | night side | so compare |
|---|---|---|---|
| comet-toolbox | shaded, models the Didymos eclipse | dark | lit region |
| **Pilucas** | **none at all** | n/a — the frame is the silhouette | silhouette |
| ours | shaded, Lommel-Seeliger + cast shadows | ambient floor | either; `--no-lighting` gives the exact silhouette |

Our `--no-lighting` variant exists for exactly this: against an unlit reference, shading is
only a source of disagreement.

### Pilucas is not an independent renderer

**She drives PRo3D itself, through sequenced bookmarks.** Both sets of frames therefore
come from the same renderer, the same OPC shape model and — measured above — identical
kernels. The only variable left is **how the camera is built**: her SPICE extraction
feeding the viewer, against `simulate-series` going through `getLookAtQuat` and
`specialTrafos`.

This matters for everything below. Two of the three "renderers" are one renderer, so the
count of independent implementations we can check against is **one — comet-toolbox** — not
two. An earlier version of this note leaned on her as corroboration; that was wrong.

It also rules out the explanations that were on the table for the ours-vs-hers residual:

| candidate | status |
|---|---|
| kernel delivery | ruled out — identical to 0.00 m and 0.000000° |
| shape model | ruled out — same OPC. Testing our OPC against the kernels' finer DSK changes the agreement with her by ±0.01 either way, i.e. nothing |
| time offset | ruled out — best-fit shifts of −8, −10, +8 min with negligible IoU gain |
| the renderer | ruled out — it is PRo3D on both sides |

What remains is the camera. The body centres agree to 1–2 px, so the pointing is
essentially identical, but the silhouette area oscillates by ±6 % across the day with the
same shape — which is what a small attitude difference does as an elongated body turns.
**One of the two camera paths is slightly wrong, and this comparison is the way to find
out which.** That is a better use of her frames than treating them as an external
reference.

### (a) Why Pilucas never had the 90° problem

She has no axis-remap step to get wrong. Her camera basis *is* the SPICE frame:

```python
heraRot = np.array(spice.pxform('HERA_AFC-1', 'J2000', et))
heraTransformation = _BuildTransformationMatrix(heraRot, heraPos)
```

PRo3D does not do that. It builds the camera with `getLookAtQuat`, whose basis is
`FromBasis(-C0, -C1, -C2)` — improper, determinant −1 — and then corrects it per
instrument through `specialTrafos`, which must also carry determinant −1 so the pair
composes back to a proper rotation. **The 90° ambiguity lives entirely in that correction
table.** Taking the instrument frame directly, as she does, leaves nothing to choose and
nothing to get wrong.

Her code does treat body and camera asymmetrically — `pxform('DIMORPHOS_FIXED','J2000')`
is transposed, `pxform('HERA_AFC-1','J2000')` is not — which on paper inverts the body's
attitude. The images rule that out: an inverted attitude is a rotation of 2θ, with θ the
full J2000→body angle, so the body would present a completely different face. At 15:00 our
silhouettes overlap at IoU 0.974. Her matrix convention evidently absorbs the transpose.
Reading code is not evidence; the measurement is.

### (b) The residual few degrees

Silhouette against silhouette — our `--no-lighting` render against her unlit frame, 40
epochs from 07:00 to 16:45:

| | |
|---|---|
| mean IoU at **zero** rotation | **0.818** |
| mean IoU gain from rotating at all | +0.024 |
| best-fit rotation | mean −8.7°, **spread 80°** |
| best epochs | 15:00 → 0.974, 15:30 → 0.972, 14:30 → 0.941 |
| worst epochs | 12:00 → 0.570, 13:30 → 0.621 |

**There is no systematic rotation.** A real one would show the same angle at every epoch;
this scatters across 80° while gaining almost nothing in overlap. The worst rows are
12:00–13:30, which is precisely when Didymos is inside AFC-1's field of view and
contaminates her silhouette.

Lit-region major axis, where both references are compared against us on their own footing:

| time | ours − comet (lit region) | ours − Pilucas (silhouette) |
|---|---|---|
| 07:45 | +4.4° | −23.7° |
| 09:00 | +1.7° | −15.3° |
| 10:30 | −0.8° | −2.8° |
| 14:15 | −11.7° | +5.8° |
| 14:45 | −13.8° | +3.5° |

Both wander by 18–30° across the day and in opposite senses, which is what a *lit-extent*
metric does when three renderers use three shape models: the terminator falls in different
places, so the lit region's axis moves even though the body does not. It is not evidence
of an attitude difference in any of the three. The silhouette numbers above are the ones
to trust, because they do not depend on where the terminator lands.

**Is Pilucas closer to comet-toolbox than we are?** Not answerable from these images. She
is unlit and he is shaded, so the two have no directly comparable region — only their
separate agreement with us can be measured, and both are within a few degrees once each is
compared on its own footing.

### Kernel deliveries: ruled out, for two of the three

The three renderers use three deliveries:

| | delivery |
|---|---|
| comet-toolbox | `v182 plan 20260805_001` |
| Pilucas | `hera_plan_v182_20260817_001` (HERA_4-5_Kernels_2026-08-19) |
| ours | `hera_plan_v182_20260820_001` |

Asked of SPICE directly rather than through a render, hers and ours are **numerically
identical** at all five comparison epochs:

| quantity | difference |
|---|---|
| HERA→Dimorphos range | 0.00 m |
| range direction | 0.000000° |
| J2000 → HERA_AFC-1 | 0.000000° |
| J2000 → DIMORPHOS_FIXED | 0.000000° (one epoch 0.000001°) |

So the delivery cannot explain anything between us and Pilucas. What is left is the shape
model and the rendering method.

comet-toolbox's `20260805` is not on this machine and remains untested.

**Do not assume it is close to ours.** An older v182 delivery, `20260527`, is available
and is nothing like either:

| `v182_20260820` vs `v182_20260527` | difference |
|---|---|
| HERA→Dimorphos range | up to **780 m** |
| J2000 → HERA_AFC-1 | **112–132°** |
| J2000 → DIMORPHOS_FIXED | **118–125°** |

So "same v182" means very little — but the pattern is not gradual drift. Eleven May
deliveries, `20260507` through `20260527`, are **identical to each other** at these epochs
(0.0 m, 0.0000°), and then differ from August by 120°. Kernels do not creep between
releases; they jump when the mission re-plans.

That bounds comet-toolbox's `20260805` without having the file. Their frames match ours in
apparent size to three digits and overlap our silhouette at IoU 0.81. Had they been on the
May plan, the AFC-1 attitude would differ by ~120° and the body would not even be in the
same part of the frame. **They are therefore on the same planning epoch as us**, and the
gross-geometry explanation for the drift in (b) is excluded.

A small CK refinement within the August batch is still possible and would still produce a
per-epoch rotation, so the file is worth having — but it can no longer explain more than a
few degrees.

This is the live hypothesis for the ours-vs-comet drift in (b). The SPK and the CK move
independently between deliveries: comet-toolbox's apparent *size* matched ours to three
digits at 06:30, so its trajectory is close to ours — but the **attitude** need not be,
and attitude is precisely what rotates the image. A CK that differs by a few degrees, and
differently at each epoch, produces exactly the drifting offset measured there.

**Getting the `20260805` delivery and re-running the diff above would close (b)**, though
the bound above already rules out the large explanation.

### (c) 11:30 is an eclipse, and we get it wrong

comet-toolbox renders 2027-02-25T11:30 **black**. It is right to: Dimorphos is inside
Didymos' shadow. From the kernels, the perpendicular distance from the Didymos–anti-Sun
axis is 0.17 km against Didymos' 0.409 km radius, and Dimorphos is on the anti-sunward
side — a true eclipse from 11:00 to 11:45.

We render it fully lit, because the scene contains only the target body: no Didymos, so
nothing to cast the shadow. Pilucas is unaffected because she does not light anything.

This is a defect in the delivered data, not just in one frame:

| series | eclipsed epochs rendered as lit |
|---|---|
| `close-2027-02-25` | **29 of 244 (11.9 %)** — runs 10:45→11:55 and 22:10→23:15 |
| `rotation-2027-03-21` | **5 of 48 (10.4 %)** — 14:00→15:00 |

That matches the ~11.6 % of COP the earlier survey found. Note the claim in the handover
that "PRo3D draws a scene and gets it for free" is **wrong for the tool**: it holds for the
viewer with both bodies loaded, not for `simulate-series`, which renders the target OPC
alone.

## Current state: what each of the three actually does

| | ours (`simulate-series`) | Pilucas | comet-toolbox |
|---|---|---|---|
| renderer | PRo3D | **PRo3D** (sequenced bookmarks) | independent |
| camera built by | `getLookAtQuat` + `specialTrafos` | her own SPICE code, into a bookmark | unknown |
| scene origin | Dimorphos | **Didymos** | unknown |
| shape | OPC `..._dtm_dimo_v003` | same OPC | own, finer |
| kernels | `v182_20260820` | `v182_20260817` — identical | `v182_20260805` — untested |
| FOV | 5.50° | the viewer's, ≈5.5307° | unknown |
| lighting | Lommel-Seeliger + cast shadows | **none** | shaded |
| Didymos eclipse | **not modelled** | n/a | modelled |

Her camera pose is `R_body⁻¹ · R_cam`, which is why her code transposes the body rotation
and not the camera's: the two matrices have different jobs. `pxform('DIMORPHOS_FIXED',
'J2000').transpose()` is `J2000 → DIMORPHOS_FIXED`, which is exactly what is needed to
express the camera in the frame the OPC lives in. The asymmetry is correct.

Her sidecars are Didymos-centric: `TRG_POS` matches HERA→DIDYMOS to five figures, not
HERA→Dimorphos. They cannot be compared with ours field by field.

**All three now agree on the gross orientation.** What remains:

| difference | size | cause |
|---|---|---|
| ours vs hers, apparent size | +0.44 % mean | our 5.50° against the viewer's ≈5.5307° — the FOV that `ea3337cb` corrected |
| ours vs hers, silhouette | IoU 0.75–0.97, area swinging ±6 % | **open** — centres agree to 1–2 px, so pointing is right; a small attitude difference between the two camera paths is the remaining candidate |
| ours vs comet, lit-region axis | +4.4° drifting to −13.8° | a metric that moves with the terminator, plus a possibly-different CK within the August batch |
| 11:00–11:45 | we render a lit body | we do not model the Didymos eclipse |

## What this is not

- **Not a different kernel delivery.** Apparent size agrees to three digits — 30.7 % of
  the frame in both. A different spacecraft ephemeris would change the range and with it
  the size.
- **Not a different body.** At this epoch the kernels put Dimorphos at 5.17 km, ~369 px
  across a 1020 px frame; Didymos would be ~1444 px, overflowing the frame.
- **Not a flip or a transpose.** The silhouettes align under a plain 90° rotation, as the
  overlay above shows. Correlating the *grey levels* instead is useless here — all eight
  dihedral transforms come out negative (−0.28 to −0.36) — because the two renders use
  different shape models and so disagree pixel by pixel whatever the orientation. The
  silhouette is what carries the orientation; the shading does not.

## What is established

1. **Our two renderers agree with each other.** The PRo3D render and the independent
   spiceypy DSK ray-cast (`pro3d_sim.dsk_render`) produce the same orientation, and the
   series validates 28/32 and 31/32 pairs under `identity`.

   This is weaker than it looks: both were written to the *same reading* of the IK. The
   ray-cast builds its ray grid with +X right and +Y down because that is how we read the
   kernel. They cannot disagree about a convention they share.

2. **The change in `ea3337cb` is what rotated us.** Frames delivered before it, still in
   the test data (`HERA/Dimorphos_opc/AFC_2027-03-21/`), relate to today's output by
   `rot90`/`rot270` — those two lead the ranking while `identity` is negative. The
   pre-fix orientation is the one comet-toolbox shows.

## The evidence that drove the fix, re-examined

`ea3337cb` changed `specialTrafos` for AFC-1/-2 and HSH, on two grounds:

**The IK diagram.** `hera_afc_v06.ti`, "Apparent FOV Layout":

```
                          1020 pixels/line
       ---              +-----------------+
        |               |      +Zafc1     |
        | 5.5 deg  1020 |        x------------> +Xafc1
        |         lines |        |        |
       ---            (0,0)------|--------+
                      Pixel      |
                                 | +Yafc1
                                 v
```

with "Boresight (+Z axis) is into the page" and "Pixel (0,0) is in the lower left corner
of the image". Read literally: **+X is image right, +Y is image down.** Pixel (0,0) at
lower-left means the line index runs opposite to +Y — an array-order detail, not a
rotation. PRo3D previously mapped +X to image *up*, so this is a documented 90° mismatch.

**The sun-direction test.** SPICE gives the sun direction in the HERA_AFC-1 frame; if +X
is image right, the lit limb must be on the right. Before the fix it was 26 px *up*;
after it, the lit limb sits along +X to within 7° over 12 frames spanning both series.

**Why that test does not settle it.** It establishes that our image places the lit limb
along the axis we *call* +X. If the true convention were +X = image up, the same test run
against the pre-fix renders would have passed just as cleanly. The test ties PRo3D's
camera basis to SPICE's frame chain **given an axis reading**; it cannot choose the
reading. The reading came from the diagram, and the diagram is exactly what a second
implementation might read differently.

The FOV correction in the same commit is unaffected: `getfov` returns symmetric
±0.04792 corners, i.e. 2.75° half-angle = 5.50° full, confirming 5.50 over the previous
5.5307 independently of any orientation question. `getfov`'s corner ordering does **not**
pin pixel (0,0), so it says nothing about rotation.

## The two possibilities

| | if our orientation is right | if comet-toolbox's is right |
|---|---|---|
| the IK diagram | read correctly | read correctly by us, but the detector's readout differs from the apparent-FOV drawing |
| comet-toolbox | has a 90° error, shared with pre-fix PRo3D | is right, and `ea3337cb` introduced the error |
| our delivered series | correct | rotated 90°, and must be re-rendered |

Both of our renderers sit on the same side of this, so the count of implementations that
agree with us is one, not two.

## What would settle it

In rough order of strength:

1. **A real AFC or DRACO frame with documented sky orientation.** The frames in our test
   data are our own output (`ORIGIN: pro3d-tool simulate-image`), so they are not
   evidence. An ESA-delivered image would end the discussion.
2. **The HERA FK plus detector readout documentation** — how the CCD's first line maps to
   the spacecraft frame, rather than how the apparent FOV is drawn.
3. **Asking the comet-toolbox author** which convention they implement and from which
   document. If they derived it from the same IK, one of the two readings is wrong and it
   can be argued from the text.

## What we did

We rotated to match comet-toolbox.

`specialTrafos` for `HERA_AFC-1`, `HERA_AFC-2` and `HERA_HSH` is back to the pre-`ea3337cb`
basis `FromOrthoNormalBasis(-V3d.OIO, -V3d.IOO, V3d.OOI)` — with the one change that all
three now share it, since the IK gives AFC-1 and AFC-2 the same layout and the old code
had them 180° apart. Verified on 2027-02-25T06:30: extent 31.2 % × 20.4 % against
comet-toolbox's 30.7 % × 21.8 %, and the lit-region major axis within 0.4°.

The kernel delivery is not a factor: rendering the same epoch against
`hera_plan_v182_20260817_001` (the workshop-3 tree) instead of `v182_20260820_001` gives
the same size, the same illumination direction and the same major axis.

`pro3d_sim.dsk_render` rotates its output by the same 90°, so the SPICE cross-check keeps
measuring geometry rather than re-measuring the convention.

**The FOV fix from `ea3337cb` stays.** 5.50° is confirmed by `getfov` independently of any
orientation question, and nothing here touches it.

### Why, given that the IK reads the other way

Because the recipients of this data compare it against comet-toolbox, and a dataset that
disagrees with the tool the community uses is not useful, whichever of the two is right.
This is a deliberate choice to match an external convention, not a correction — if the IK
reading is vindicated, this has to be rotated back, and the note above says how to tell.

Both orientations exist in the repository's history, one commit apart, so switching is a
three-line change plus a re-render (~7 min per series).

### What is still owed

The checks that would settle it, from the section above — a real ESA-delivered AFC frame,
the detector readout documentation, or the comet-toolbox author's source. Until then this
note travels with the data.
