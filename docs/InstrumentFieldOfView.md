# Instrument field of view

Image projection places each instrument image with a camera frustum. The frustum's
field of view (FOV) comes from the instrument, and it decides how large a projected
image lands on the terrain: a FOV 0.5 % too wide paints every image 0.5 % too large
around its centre.

PRo3D prefills each instrument's FOV from the SPICE instrument kernels (IK). You can
change it per scene in the GIS tab, under *Projected Images → Instruments*.

## The Instruments section

GIS tab → **Projected Images** → **Instruments** lists every instrument PRo3D can
project, by SPICE frame name:

| column | meaning |
|---|---|
| **Instrument** | the SPICE frame, e.g. `HERA_AFC-1`. A yellow `*` means this scene changed it |
| **H FOV (deg)** | full field of view along the image **width** |
| **V FOV (deg)** | full field of view along the image **height** |
| ↶ | back to the kernel value (the tooltip shows it) |

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

## Saved with the scene

Only the instruments you changed are saved, under the scene's `instrumentFovs` key. A
scene where nothing was changed is saved exactly as before, and older PRo3D releases
ignore the key. An instrument you did not change follows the kernel default of the PRo3D
version that opens the scene, so a corrected default reaches old scenes too.

## Not covered

- **Lens distortion** is not modelled; the pinhole frustum ignores it (#915).
- **pro3d-tool** (`simulate-image`, `sample-layers`) always uses the kernel defaults.
  Its renders are what the defaults describe.
