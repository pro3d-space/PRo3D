# Instrument field of view

Image projection places each instrument image with a camera frustum. The frustum's
field of view (FOV) comes from the instrument, and it decides how large a projected
image lands on the terrain: a FOV 0.5 % too wide paints every image 0.5 % too large
around its centre.

PRo3D prefills each instrument's FOV from the SPICE instrument kernels (IK). You can
change it per scene in the GIS tab, under *Projected Images → Instrument Settings*.

## The Instrument Settings section

GIS tab → **Projected Images** → **Instrument Settings** lists every instrument PRo3D can
project, by SPICE frame name:

| item | meaning |
|---|---|
| **Instrument** | the SPICE frame, e.g. `HERA_AFC-1`. A yellow `*` means this scene changed it |
| **H FOV (deg)** | full field of view along the image **width** |
| **V FOV (deg)** | full field of view along the image **height** |
| **flip** | mirror this instrument's images vertically -- see [Flipped rows](#flipped-rows) |
| ↶ | back to the kernel value (the tooltip shows it) and the instrument's default flip |

The change applies right away to every projected image of that instrument: the stack,
the hover preview, the footprint and the Image Inspector.

The projection is a pinhole: a centred, rectangular frustum with these two angles.
Lens distortion is not modelled (see below).

## Kernel defaults

| Instrument | H × V FOV |
|---|---|
| `HERA_AFC-1`, `HERA_AFC-2` | 5.5 × 5.5° |
| `HERA_HSH` | 15.5 × 8.3° |
| `HERA_TIRI` | 13.3 × 10.0° |
| `MILANI_ASPECT_NIR1` | 6.7 × 5.4° |

Each is twice the instrument kernel's `FOV_REF_ANGLE` / `FOV_CROSS_ANGLE`. Notes:

- **AFC.** Up to 6.3, PRo3D used 5.5307°, which was 0.56 % too wide (fixed in 6.4.0, #801).
- **HSH.** The default is the kernel's paraxial field, which fits a pinhole frustum
  better than the full field. Up to 6.4.4, PRo3D used 15.24 × 8.09° (#882).
- **TIRI** is new. Its image axes (which way is up, and whether the image is mirrored)
  have **not been checked against a real TIRI image** yet.

## Flipped rows

Some image files store their rows bottom-up: the file's first row is the **bottom** of
the frame, as in FITS, where PRo3D's projector expects the top. Such an image lands on the
body **mirrored** — the terminator where the lit limb should be, a moon on the wrong side.
No *Registration* roll, pitch or yaw can repair a mirror image; tick **flip** for that
instrument instead. It mirrors the projection vertically for every image of the
instrument and every orientation source, and leaves the projector's position, boresight
and field of view alone, so a Registration correction set before still means the same.

How to tell: with *Lighting* on, compare the projected image's bright side with the
rendered sun direction, or check where a companion body (Dimorphos) sits against its
orbit. A flipped file correlates with the rendered shading only after flipping.

### Where the default comes from: the instrument kernel's pixel layout

Each instrument kernel (IK) draws its detector in the instrument frame: for the HERA
cameras, pixel (0,0) is in the **lower left** corner, samples run along +X, lines
along −Y, and the boresight is +Z. A file that stores line 0 first therefore stores its
rows bottom-up, and the camera PRo3D needs is right = +X, up = −Y with the rows flipped.

`InstrumentProjection.imageLayouts` holds that layout per instrument (direction of
increasing column and of increasing row, in the instrument frame); the camera's image
axes and the row flip are both derived from it (`layoutCamera`), so they cannot
disagree. The flip default is **on for the AFC cameras** (`HERA_AFC-1`, `HERA_AFC-2`) for
that reason, and off for instruments without a layout entry — they keep the hand-written
axis swap of `InstrumentProjection.specialTrafos` until a real frame confirms their IK
layout (HSH and TIRI do not match their IK drawing with that swap).

The viewer, `pro3d-tool simulate-image`, `sample-layers`, `sun-angles` and `unproject`
all take this default, so a frame written by `simulate-image` (line 0 first, i.e. the
instrument's own row order) and a real frame stored the same way project alike. The
**flip** checkbox overrides it per scene, for a delivery whose files were re-ordered.

## Saved with the scene

Only the instruments you changed (field of view, or flip against the instrument's
default) are saved, under the scene's `instrumentFovs` key, each entry with its
`"flipRows"`. An entry without it (scenes from before the AFC default) takes the
instrument's default. A
scene where nothing was changed is saved exactly as before, and older PRo3D releases
ignore the key. An instrument you did not change follows the kernel default of the PRo3D
version that opens the scene, so a corrected default reaches old scenes too.

## Not covered

- **Lens distortion** is not modelled; the pinhole frustum ignores it (#915).
- **pro3d-tool** has no scene: it always uses the kernel field of view and the
  instrument's default row flip (above), never a scene override. Their per-pixel
  outputs are in the instrument's row order, pixel-aligned with the input frame.
