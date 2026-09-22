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

## What this is not

- **Not a different kernel delivery.** Apparent size agrees to three digits — 30.7 % of
  the frame in both. A different spacecraft ephemeris would change the range and with it
  the size.
- **Not a different body.** At this epoch the kernels put Dimorphos at 5.17 km, ~369 px
  across a 1020 px frame; Didymos would be ~1444 px, overflowing the frame.
- **Not a flip or a transpose.** All eight dihedral transforms correlate negatively
  (−0.28 to −0.36), and a continuous rotation search peaks at −0.43. Only the rotation
  *angle* matches; the shading does not, because the two use different shape models.

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
comet-toolbox's 30.7 % × 21.8 %, illumination direction within 6.3°, which is the same
order as the residual between two different shape models.

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
