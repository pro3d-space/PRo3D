# Rebuilding the simulated image series, cleanly

**This branch is a concept spike. It is not meant to be merged as it stands.**

It grew across one very long session, in the order the questions arrived rather than in the
order a design would put them, and it shows: forty commits, six one-off analysis scripts,
two post-processing scripts that exist only to undo a decision made earlier in the same
pipeline, and a variant vocabulary that accreted a name every time someone wanted one knob
changed.

What is worth keeping is not the code. It is the **measurements** — every number below cost
a render, a ray-cast or a false start — and the handful of **design decisions that turned
out to be load-bearing**. This file is written so a clean implementation can start from
those and skip the rediscovery.

Read [HANDOVER-simulated-image-series.md](HANDOVER-simulated-image-series.md) for the
narrative of how it got here, and [AFC-image-orientation.md](AFC-image-orientation.md)
before touching `specialTrafos`.

---

## 1. What it produces, and the exact commands

Both of these ran to completion and validated. They are the acceptance target for a
rebuild: a clean implementation should produce the same two artefacts with fewer moving
parts.

Paths below: `<dimo-opc>` = `$PRO3D_TEST_DATA/HERA/Dimorphos_opc/Dimorphos`,
`<didy-opc>` = `C:/pro3ddata/HERA/workshop3/Didymos_SK_OPC__texture/Didymos_ASPECT_texture`.

### The close-orbit-phase set

8107 epochs, 2027-02-05 → 2027-04-30, 15 min, 1020², 779 MB, 85 day folders, one frame per
epoch. Both bodies in frame, each casting on the other, no procedural micro-structure.

```
# where AFC-1 is on Dimorphos: the de-lit DRACO mosaic, Didymos beside it
python scripts/make-image-time-series.py --out COP --opc <dimo-opc> \
    --body DIMORPHOS --frame DIMORPHOS_FIXED --start 2027-02-05T00:00:00 \
    --count 8108 --interval 15 --variants delitplain --stack-variant delitplain \
    --texture-layer DRACO_2 --gain 3.35 --micro-amplitude 0 --day-folders \
    --occluder-body DIDYMOS --occluder-opc <didy-opc> \
    --occluder-in-scene --occluder-texture-albedo

# where it is on Didymos: the same two bodies, roles swapped
python scripts/make-image-time-series.py --out COP-didy --opc <didy-opc> \
    --body DIDYMOS --frame DIDYMOS_FIXED --start 2027-02-05T00:00:00 \
    --count 8108 --interval 15 --variants baked --stack-variant baked \
    --texture-layer Moon --gain 3.35 --micro-amplitude 0 --day-folders \
    --occluder-body DIMORPHOS --occluder-opc <dimo-opc> \
    --occluder-texture-layer DRACO_2 --occluder-deshade --occluder-deshade-layer DRACO_2 \
    --occluder-in-scene

python scripts/merge-series.py   --into COP --add COP-didy
python scripts/relabel-series.py --series COP --map delitplain=cop --map baked=cop --flatten
```

Measured: **0.17 s/frame**, 43 kB/frame. 4035 frames target Dimorphos, 4072 Didymos.
All 8107 sidecars pass the boresight invariant.

### A showcase video

750 frames, 42 s cadence, 30.0 s at 25 fps.

```
python scripts/make-image-time-series.py --out showcase --opc <dimo-opc> \
    --body DIMORPHOS --frame DIMORPHOS_FIXED --start 2027-04-24T20:45:00 \
    --count 750 --interval-seconds 42 --variants delit --stack-variant delit \
    --texture-layer DRACO_2 --gain 4.6 --micro-scale 3.0 --micro-amplitude 0.3 \
    --width 4080 \
    --occluder-body DIDYMOS --occluder-opc <didy-opc> \
    --occluder-in-scene --occluder-texture-albedo

python scripts/make-series-video.py --series showcase --variant delit --fps 25 --dark-level 0
```

Measured at 4080² (4× AFC-1's 1020, an integer oversample — the FOV does not change):
**1.63 s/frame**, 990 kB/frame, 109 MB for the MP4. `--dark-level 0` disables the totality
compression, which is wanted for a conjunction and not for an eclipse; for an eclipse use
the default, which keeps every 12th frame below 15 % of unshadowed brightness so totality
becomes a beat rather than 17 seconds of black.

---

## 2. The measurements — do not rediscover these

### Geometry and pointing

| | |
|---|---|
| OBJ render vs SPICE DSK ray-cast, silhouette | **IoU 1.000** (raw and centroid-normalised) |
| OPC render vs the same ray-cast | IoU 0.972 |
| comet-toolbox vs our OBJ, lit region | 0.890 (vs 0.856 for the OPC) |
| lit-region IoU, ours vs *anyone* | capped around **0.90** by the terminator threshold — so the acceptance criterion is the silhouette, not the lit region |
| external reference at 2027-03-29T03:00 | raw silhouette IoU **0.974** (provenance of that reference is unrecorded — treat as indicative) |
| AFC-1 frustum | square, **2.750° edge**, corner at **3.886°** |

**The boresight tolerance is the corner angle plus the body's angular radius**, and the
radius to use differs by purpose:

- **pre-flight** (is this epoch worth rendering?) → the body's *smallest semi-axis*
  (Dimorphos 57.6 m, Didymos 303.5 m). A sphere of that radius fits inside the body at any
  orientation, so the limb is guaranteed inside the frustum. Using the bounding-box
  diagonal admitted epochs where Didymos was entirely outside the field and the only thing
  in frame was the in-scene companion — the frame then claimed a `TARGET` it did not show.
- **sidecar check** (is this sidecar corrupt?) → the bounding-box *half-diagonal*
  (Dimorphos 136 m, Didymos 650 m). Its job is to catch a bad sidecar, not to second-guess
  what the renderer accepted. A flat 4.0° rejected 58 good frames; the semi-axis value still
  rejected 5.

### Shading and shadows

| | |
|---|---|
| `--shadow-bias` | **0.006**. 0.002 gave acne, 0.02 peter-panned. Swept and measured, not guessed |
| slope-scaled bias | implemented, measured, **reverted** — the map is finer than the geometry (6.6 cm/texel against 0.24 m facets), so there is nothing for it to buy |
| shading vs ray-cast at 0.006 | cast-shadow IoU 0.891–0.984, brightness r 0.9934–0.9937, RMS 3.3–4.8 % |
| sun shadow map | 4096², ~5 cm/texel at Dimorphos scale — finer than the mesh, so larger buys nothing |
| eclipse shadow bias | **10×** the target's: that map's depth range spans the gap to the occluder as well as the occluder, so a unit of normalised depth is worth far more metres |

Two bugs that will come back if the structure does:

- **The sun shadow lookup must guard depth, not only X and Y.** Without it, the in-scene
  primary acquires a dark bite exactly the shape of the target's shadow-map footprint.
- **An acne metric over a binary shadow mask is blind.** Partial darkening does not change
  the mask. Measure a shadow *factor* against `lsFull` rebuilt from mu0/mu — the binary
  version scored clean while brightness RMS went 5.5 % → 24 %.

### The two shape models and their textures

| | |
|---|---|
| Dimorphos OPC `g_01960mm_spc_dtm_dimo_...v003` | DRACO mosaic. Fit: **r = 0.40** — worth de-lighting |
| DRACO footprint | lon 264.6°, lat −1.9°, **46.2 %** of the body. `DRACO_1` is the real mosaic (black outside it); `DRACO_2` is the same with the other 54 % filled in synthetically |
| Didymos OPC `Didymos_ASPECT_texture` | **is** the kernels' DSK: against `latsrf` along the same directions, mean 0.00 m, std 0.00 m, **abs max 0.02 m**; render-vs-mesh IoU 0.9998 |
| its texture layer, named `Moon` | **is the Moon** — mare, farside highlands, a visible map seam |
| that texture's fit | **r = 0.22**, amplitude 0.055 on ambient 0.61 → *no baked light direction to divide out*. The shading in it is the Moon's relief, not this body's |
| `Workshop2/OPC/Didymos` | **rejected**: partial patch, 43 m RMS against the DSK, IoU 0.754 |
| the shape-model OBJs | `v`/`f` only — **no texture coordinates at all**. That is why a textured primary needs an OPC |

**No eclipse shows the DRACO mosaic lit.** Over all **188** umbra events between 2027-02-01
and 2027-05-01 the sub-solar point sits **94–97°** from the footprint centre — a three-degree
spread across three months, because an eclipse happens when Dimorphos is anti-sunward of
Didymos and that pins the sub-solar longitude. Measured: 2.5 % and 3.0 % of the lit disk is
real mosaic at two eclipses, against 87.6 % at 2027-02-28T22:00, which is not an eclipse.
An eclipse animation should therefore come from the OBJ, or be labelled.

### Exposure, when two bodies share a frame

The textured against the untextured primary at 2027-04-25T03:30, everything else identical:
silhouettes **370 836 vs 370 826** lit pixels (0.003 % — the OPC and the OBJ are the same
shape), mean DN **138.8 → 153.0**, spread **12.2 → 27.8**. A body that gained *pattern*, not
brightness. That is the test for the exposure rule below.

---

## 3. The four decisions worth carrying over

Everything else in this branch is negotiable. These four are not, because each one replaced
something that had already gone wrong.

**1. One `ShapeSource` abstraction over OPC and mesh.** Both verbs render through the same
camera, shader stack and shading uniforms whatever the shape model is; what an OPC and a
mesh disagree about is how the graph is built, how many frames a pass needs before the
geometry is final (OPC 8, mesh 1), and whether there is a texture to fit against. Keeping
that to one record is what stopped `--obj` from becoming a second renderer.

**2. The de-shading fit is shared, the sample sources are not.** An OPC reads a per-vertex
brightness layer; a mesh samples its texture at each vertex. Two copies of the two-pass
least squares would be the failure this area already produced once — a second path that
agrees with the first until someone changes one of them.

**3. Exposure and the fit are separate concerns.** `--texture-albedo` divides nothing; the
only number it needs is the one mapping the mean texel to the nominal albedo. Tying it to
the de-shading fit made the mode **silently** unusable on any textured OPC without
per-vertex normals and a brightness layer — it rendered the constant albedo and reported
success. The Didymos OPC has neither.

**4. Each body is surfaced with its OWN numbers.** In a series where a body is the target at
some epochs and the companion at others, the companion must be de-lit with *its own* fit:
the target's baked sun direction carves a terminator into it at the wrong angle, and the
target's exposure puts it several times too bright. Modelled as a three-case type
(`PlainAlbedo` / `TexturedAlbedo of scale` / `DeshadedTexture of fit`) rather than two
booleans, because the flag version makes "de-shaded, no fit supplied" expressible — the one
combination that renders a body at an arbitrary brightness with nothing in the output saying
so.

---

## 4. What is dead weight, and what a clean build should do instead

Named honestly, because the point of this file is to stop the next pass inheriting it.

### The merge/relabel dance should not exist

`merge-series.py` and `relabel-series.py` (`--flatten` and all) exist **only** to undo two
decisions made earlier in the same pipeline:

- the generator renders **one target per run**, so a phase where AFC-1 switches body needs
  two runs and a merge;
- the variant folder name is baked into every **filename**, so folding two runs together
  needs a rename of 16 000 files and a rewrite of every sidecar's `FILENAME` field.

A clean build should choose the target **per epoch** inside one run — the camera comes from
the CK and does not depend on the target at all — and should keep the variant out of the
filename. Then both scripts disappear, along with the bug they both had (rebuilding an epoch
from its filenames dropped every other field it carried, and `rangeMeters` with it, so the
README printed "Range 0.0 .. 0.0 km").

### The variant vocabulary accreted

`delit` / `delitplain` / `baked` / `micro` / `smooth` are not five things. They are two
independent knobs — *what supplies the albedo* (constant / texture / de-lit texture) and
*how much micro-structure* — flattened into a name list, which is why `delitplain` had to be
added later as "`delit` with micro = 0". Make them two parameters.

Related: **micro-structure is shading, not geometry.** It perturbs the normal, casts no
shadow and does not change the silhouette, so a reconstruction turns it into relief the
shape model does not have. It belongs off by default in anything delivered.

### Six analysis scripts, of which two are reusable

| | |
|---|---|
| `check-lighting.py` | **keep** — shading against a SPICE ray-cast of the same mesh, with floors that are measurements |
| `check-series.py` | **keep** — re-checks a delivered series against our own ray-cast |
| `check-renderers.py` | one-off: the three-renderer comparison. Its conclusion is four numbers, all in §2 |
| `make-shape-crosscheck-figures.py` | one-off: produced `objMatrix.png` |
| `make-eclipse-figure.py` | one-off: produced `eclipse.png` |
| `make-textured-primary-figure.py` | one-off: produced `texturedPrimary.png` |

The figures are worth keeping; the scripts that made them are not worth carrying as API.

### The three-renderer investigation is a conclusion, not a subsystem

Roughly fifteen commits of it. The whole outcome is: the deliveries are geometrically
identical, Pilucas' tool drives PRo3D and so is not independent, comet-toolbox differs by the
shape model, and lit-region IoU cannot exceed ~0.90 between any two implementations. Those
sentences are in §2. The rest is process.

---

## 5. Still open

- **No independent eclipse reference.** Nothing outside this repo has confirmed a mutual
  eclipse. Either extend `dsk_render` to trace toward the Sun against the primary's DSK, or
  get a Cosmographia screenshot of one.
- **Two older eclipse series still carry the shadow acne** from bias 0.002
  (`eclipse-2027-02-25`, `eclipse-2027-04-08-fullframe`). Re-render or delete.
- **The penumbra is a blur, not an integration** over the solar disk. A few metres wide at
  Dimorphos scale; the gradient's shape is a 3×3 kernel's, not a limb-darkened Sun's.
- **The target does not cast onto the occluder** — the target's shadow map is fitted to its
  own bounds, and a fragment a kilometre past its far plane is explicitly treated as lit.
- **One epoch of the delivered set is missing**, 2027-04-30T10:45: past the end of the
  metakernel's HERA ephemeris. The grid overshot by one step.
