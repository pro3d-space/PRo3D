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

| Instrument | H × V FOV | Pixels | Source |
|---|---|---|---|
| `HERA_AFC-1`, `HERA_AFC-2` | 5.5 × 5.5° | 1020 × 1020 | `hera_afc_v06.ti`, `FOV_REF_ANGLE` = `FOV_CROSS_ANGLE` = 2.75° |
| `HERA_HSH` | 15.5 × 8.3° | 409 × 217 macro pixels | `hera_hsh_v03.ti`, `HERA_HSH_PARAXIAL` (INS-91401) |
| `HERA_TIRI` | 13.3 × 10.0° | 1024 × 768 | `hera_tiri_v03.ti`, INS-91200 |
| `MILANI_ASPECT_NIR1` | 6.7 × 5.4° | | `hera_milani_aspect_v02.ti`, INS-9102120 |

Notes on these values:

- **AFC.** The IK's own pinhole calibration (`DIST_FX` = 10600 px over 1020 px, i.e.
  106 mm focal length with 10 µm pixels) gives 5.509°. The kernel's 5.5° is that value,
  rounded. Up to 6.3, PRo3D used 5.5307°, which was 0.56 % too wide (fixed in 6.4.0, #801). The
  real AFC-1 image metadata (`AF1_…_1B_AFC1.tif.json`) carries the 1020 × 1020 size but
  no optics.
- **HSH.** The kernel defines two fields. The *real* one, 15.9 × 9.9°, is "extremely
  distorted at the edges (almost 1 degree)". The *paraxial* one, 15.5 × 8.3°, is its
  small-angle core and fits a pinhole frustum better, so it is the default. Up to 6.4.4,
  PRo3D used 15.24 × 8.09°, which matches neither (#882).
- **TIRI** is new. Its image axes (which way is up, and whether the image is mirrored)
  have **not been checked against a real TIRI image** yet.

## Saved with the scene

Only the instruments you changed are saved, under the scene's `instrumentFovs` key. A
scene where nothing was changed is saved exactly as before, and older PRo3D releases
ignore the key. An instrument you did not change follows the kernel default of the PRo3D
version that opens the scene, so a corrected default reaches old scenes too.

## Not covered

- **Lens distortion.** `hera_afc_v06.ti` gives a radial term from in-flight calibration
  (AFC-1 k1 = −1.28449, AFC-2 −0.40565). On AFC-1 it moves edge pixels about 0.3 %
  outwards (≈1.5 px at the edge, ≈3 px in the corners). The pinhole frustum ignores it
  (#915).
- **pro3d-tool** (`simulate-image`, `sample-layers`) always uses the kernel defaults.
  Its renders are what the defaults describe.
