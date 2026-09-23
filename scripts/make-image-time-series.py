#!/usr/bin/env python3
"""Render a time series of simulated HERA/AFC-1 frames over one Dimorphos rotation.

    python scripts/make-image-time-series.py --out <folder>

Drives `pro3d-tool simulate-series` ONCE for the whole series, writing lit frames with
their `.mbi.json` sidecars, a coarsely sampled subset ready to import into the viewer, and
a scene set up to project them. Adds the independent SPICE reference and the validation
the tool cannot do for itself.

    <out>/delit/           every epoch, de-lit DRACO texture (the realistic variant)
    <out>/baked/           the same texture NOT de-lit -- the naive rendering
    <out>/micro/           every epoch, constant albedo + micro-structure
    <out>/smooth/          every epoch, constant albedo, no micro-structure
    <out>/spice/           optional: an independent SPICE DSK ray-cast of --spice-count
                           epochs, spread over the series, for checking the others
    <out>/stack/           the --stack-count subset, evenly spaced over the series
    <out>/ImageSeries.pro3d  scene with body, frame, kernel, epoch and focal length set
    <out>/series.json      one entry per epoch, naming its file in each variant
    <out>/README.md        the same, and the folder layout, for whoever receives it

Four lit variants per epoch, same camera and same --gain, so any neighbouring pair differs
in exactly one thing:

    AFC1_SMOOTH_<stamp>    constant albedo, micro-structure off -- the bare shape
    AFC1_MICRO_<stamp>     + procedural micro-structure, still no texture
    AFC1_BAKED_<stamp>     + the real DRACO texture, illumination and all
    AFC1_DELIT_<stamp>     + that baked illumination divided back out -- the realistic one

BAKED and DELIT are the pair that answers "does de-lighting actually matter for my
reconstruction?" -- a question about the reconstruction, which cannot be asked without the
frame that skips the step. All four are lit (Lommel-Seeliger, cast shadows); none is
texture-only.
Across the series the illumination and the visible face change while the exposure does
not, which is what makes it a series rather than a set of unrelated renders -- pass
--gain 0 to auto-expose each frame separately and that property is gone.

Why a rotation: Dimorphos turns in about 11.9 h, so the default span shows every face
once. A 15 min cadence over that is ~48 epochs; the projection stack caps at 32
(ProjectedImages.maxCount), and 48 layers would be unreadable anyway, so <out>/stack/
holds an evenly spaced subset -- 15 by default, i.e. one every ~48 min, ~24 deg of
rotation apart.

    test data   the OPC itself; the frames are only meaningful on the shape model they
                were rendered against, so keep them together. Defaults to
                $PRO3D_TEST_DATA/HERA/Dimorphos_opc/Dimorphos.
    --obj       renders from a Wavefront shape model instead -- the one the SPICE kernels
                ship has 0.24 m facets against the OPC's 1.96 m posts, and at 5 km an AFC
                pixel is 0.48 m, so the OPC frames are shape-limited rather than
                sensor-limited. It carries no texture: only micro and smooth, no scene,
                and the frames are for comparing renderers rather than for projecting.
    kernels     $PRO3D_SPICE_KERNELS, or --kernel-root; --kernel picks the metakernel
                (the tool's default is <root>/mk/hera_plan.tm)
    python      numpy (for the sidecar check); no plotting

Every run renders the whole series and rewrites the variant folders: no resume, no
--force, no partially-updated folder. `simulate-series` renders all the lit frames in one
process at ~0.12 s a frame, so there is nothing left for resuming to save -- and resuming
is what once left a folder holding frames from two different builds, 90 degrees apart,
with nothing in the data saying so.

Use a separate --out per cadence: the stamps of a finer cadence interleave with a coarser
earlier run, and series.json then describes the union of both.

Validation is a stage of the run, not a thing to remember afterwards. The tool verifies
every frame's sidecar against the camera that rendered it, and this script then checks the
frames against an independent SPICE ray-cast (--spice-count of them) before it writes
series.json. The verdict goes INTO series.json, so a folder carries its own proof; a
failure means a non-zero exit and a README that says so.

THE EPOCH AND THE KERNEL SET GO TOGETHER. ESA regenerates the HERA plan kernels, and they
move the spacecraft: the default start lies inside hera_plan.tm's close-orbit coverage for
the tree this was written against (v182_20260820), where ranges are 6.7-8.4 km and the lit
frames show the whole disk. Against another tree the same epoch can put the body outside
the AFC frame, and the render fails with SPICE saying so. series.json records the
metakernel's MK_IDENTIFIER for exactly this reason.
"""

import argparse
import datetime
import json
import os
import shutil
import sys
import tempfile
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pro3d_sim import (check_sidecars, dsk_render, frame_paths, list_layers,
                       print_validation, run_tool, texture_index, tool_error,
                       validate_series, write_scene)

# Dimorphos' rotation period. Tidally locked to Didymos, 11.92 h after the DART impact
# (Naidu et al. 2024) -- the pre-impact 11.92 h orbital period was shortened to ~11.37 h,
# so this is the post-impact value and only the default span, not a constant to rely on.
ROTATION_HOURS = 11.92

# 13:00, not 14:00: the camera follows HERA's CK, so an epoch is only renderable while the
# instrument is actually pointed at Dimorphos. Against hera_plan_v182_20260820_001 that
# window runs 2027-03-20T19:15 .. 2027-03-22T01:25, and a rotation started at 14:00 runs
# 20 min past its end -- the last two epochs render nothing and the verb says so. Started
# at 13:00 the whole 11.92 h fits inside the window with ~40 min to spare.
DEFAULT_START = "2027-03-21T13:00:00Z"
# ProjectedImages.maxCount in src/PRo3D.Core/ProjectedImageList-Model.fs -- the shader's
# uniform arrays are sized for it, so the viewer silently refuses layer 33.
MAX_STACK = 32
# Dimorphos' longest semi-axis, metres. Only a MARGIN for the pointing pre-flight, so that
# an epoch where the body clips the edge of the frame is kept rather than dropped; the tool
# makes the real decision from the shape model's own bounding box.
TARGET_RADIUS_M = 57.6
# The body's SMALLEST semi-axis, metres, from the shape models' bounding boxes
# (Dimorphos 179.5 x 169.4 x 115.2, Didymos 823.7 x 801.2 x 606.9).
#
# The smallest, not the largest and not the bounding-box diagonal, because this margin
# decides whether an epoch is worth rendering and the question is "is the target certainly
# IN the frame". A sphere of the smallest semi-axis fits inside the body whatever its
# orientation, so a centre within (corner half-angle + that radius) guarantees the limb is
# inside the frustum.
#
# Using the diagonal instead let through frames where Didymos -- 650 m across the diagonal
# and only 5 km away, so nearly 7 deg of angular radius -- was entirely outside the field,
# and the only thing in the image was the in-scene companion. The frame then claimed a
# TARGET it did not show.
PREFLIGHT_RADIUS_M = {"DIMORPHOS": 57.6, "DIDYMOS": 303.5}


def parse_iso(s):
    t = s.strip().replace("Z", "+00:00")
    d = datetime.datetime.fromisoformat(t)
    return d.replace(tzinfo=None) if d.tzinfo is None else d.astimezone(datetime.timezone.utc).replace(tzinfo=None)


def iso(d):
    return d.strftime("%Y-%m-%dT%H:%M:%SZ")


def stamp(d):
    return d.strftime("%Y%m%d_%H%M%S")


def resolve_kernel(kernel, kernel_root):
    """The metakernel the tool will actually use, for the record in series.json."""
    if kernel:
        return os.path.abspath(kernel)
    root = kernel_root or os.environ.get("PRO3D_SPICE_KERNELS")
    if not root:
        return None
    if not os.path.isdir(os.path.join(root, "mk")):
        root = os.path.join(root, "kernels")
    return os.path.join(root, "mk", "hera_plan.tm")


def mk_identifier(path):
    """MK_IDENTIFIER out of a metakernel: the version string to quote when a render is
    questioned. The kernels move between releases and the images move with them."""
    if not path or not os.path.exists(path):
        return None
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            for line in f:
                if "MK_IDENTIFIER" in line:
                    _, _, rest = line.partition("=")
                    return rest.strip().strip("(){}").strip().strip("'\"") or None
    except OSError:
        return None
    return None


VARIANTS = {
    "delitplain": "the de-lit DRACO mosaic WITHOUT procedural micro-structure: the shape "
                  "model relief and nothing invented",
    # One line each. The detail belongs in the README's prose, not repeated per folder.
    # alphabetical in the listing, so baked/ must stand on its own and delit/ refers back
    "baked":  "DRACO image where available, rest filled with random texture. Since the "
              "DRACO image has lighting baked in, this one might be confusing for "
              "reconstruction methods",
    "delit":  "the same, with that baked-in lighting fitted and removed",
    "micro":  "no mosaic: one uniform brightness plus procedural surface roughness",
    "smooth": "no mosaic, no roughness: the bare shape model, lit",
    "spice":  "a few epochs ray-cast by SPICE from its own shape model, as a reference. "
              "No sidecars, not for projecting",
}


def folder_tree(variant_dirs, chosen, stack_variant, scene):
    """The folder layout, drawn, for the README that travels with the data."""
    rows = [(v + '/', VARIANTS.get(v, '')) for v in variant_dirs]
    rows.append(("stack/", "%d %s frames, thinned for the 32-layer projection stack"
                 % (len(chosen), stack_variant)))
    if scene:
        rows.append((os.path.basename(scene),
                     'PRo3D scene: body, frame, kernel, epoch, focal length'))
    rows.append(('README.md', 'this file'))
    rows.append(('series.json', 'the same, machine-readable, one entry per epoch'))
    out = []
    for i, (name, what) in enumerate(rows):
        stem = '`- ' if i == len(rows) - 1 else '+- '
        out.append('%s%-14s %s' % (stem, name, what))
    return chr(10).join(out) + chr(10)


def tool_fingerprint(repo):
    """Identity of the binary that renders the frames.

    Frames from different builds must never be mixed. A change to the instrument axis
    map or the FOV silently rotates or rescales every frame rendered after it, while the
    resume logic happily keeps the earlier ones -- which is exactly what happened here:
    a folder ended up holding `smooth/` frames 90 degrees away from its `delit/` frames,
    and nothing in the output said so. Recorded in series.json and compared on every run.
    """
    import hashlib
    parts = []
    for n in ("PRo3D.Tool.exe", "PRo3D.Base.dll"):
        f = os.path.join(repo, "bin", "Release", "net9.0", n)
        if os.path.exists(f):
            st = os.stat(f)
            parts.append("%s:%d:%d" % (n, st.st_size, int(st.st_mtime)))
    if not parts:
        return "unknown"
    return hashlib.sha1("|".join(parts).encode("utf-8")).hexdigest()[:12]


def subset(items, n):
    """n items spread evenly over the list, first and last included.

    Not items[::step]: a step that does not divide the length drops the end of the series,
    and the last frame is the one that closes the rotation.
    """
    if n >= len(items):
        return list(items)
    if n <= 1:
        return [items[0]]
    last = len(items) - 1
    idx = sorted({int(round(i * last / (n - 1.0))) for i in range(n)})
    return [items[i] for i in idx]


class _Result(object):
    """Mimics subprocess.CompletedProcess so the render loop treats both paths alike."""
    def __init__(self, returncode, stdout=""):
        self.returncode, self.stdout, self.stderr = returncode, stdout, ""


def spice_frame(kernel, utc, out, gain, albedo):
    """One reference frame from SPICE's DSK, written as 8-bit PNG."""
    try:
        import spiceypy as sp
        from PIL import Image
    except ImportError as ex:
        return _Result(1, "ERROR: spice variant needs spiceypy and pillow (%s)" % ex)
    cwd = os.getcwd()
    try:
        sp.kclear()
        os.chdir(os.path.dirname(kernel))
        sp.furnsh(os.path.basename(kernel))
        img, hit = dsk_render(utc, "HERA_AFC-1", "DIMORPHOS", "HERA", "DIMORPHOS_FIXED",
                              1020, 5.50)
        if not hit.any():
            return _Result(1, "ERROR: DIMORPHOS is not in the frame at %s" % utc)
        dn = np.clip(gain * albedo * img * 255.0, 0, 255).astype(np.uint8)
        Image.fromarray(dn).save(out)
        return _Result(0, "[out] %s" % out)
    except Exception as ex:
        return _Result(1, "ERROR: %s" % ex)
    finally:
        os.chdir(cwd)
        try:
            import spiceypy as sp2
            sp2.kclear()
        except Exception:
            pass


def preflight(epochs, kernel, instrument, target, observer, target_radius=None):
    """Which epochs actually have the instrument pointed at the target.

    The camera follows the CK, so an epoch where the spacecraft was observing something
    else renders nothing -- correctly, but only after the OPC load and the shadow map. Over
    a rotation that is minutes of wasted work at the end of a run, and the reason is not
    obvious from a missing file. Checking first costs milliseconds.

    Needs spiceypy. Without it the check is skipped and the tool still reports each miss
    per frame, so this is an optimisation and a clearer report, never a correctness
    requirement.

    Returns (angles, halffov, None) or (None, None, reason-it-was-skipped).

    `angles` is NOT an angle from the boresight. It is how far the body's nearest limb
    falls OUTSIDE the frustum edge, in degrees -- negative when the body is comfortably
    inside. That is the quantity the caller filters on, and calling it a boresight offset
    in the log said something untrue about frames where the body is simply large.
    """
    try:
        import spiceypy as sp
    except ImportError:
        return None, None, "spiceypy not installed"
    if not kernel or not os.path.exists(kernel):
        return None, None, "metakernel not found (%s)" % kernel
    cwd = os.getcwd()
    try:
        sp.kclear()
        os.chdir(os.path.dirname(kernel))
        sp.furnsh(os.path.basename(kernel))
        try:
            # the FOV's own bounds, rather than a number repeated from the docs
            code = sp.bodn2c(instrument)
            _, _, bore, _, bounds = sp.getfov(code, 8)
            # AFC's FOV is a SQUARE, and the angle to a corner (3.886 deg) is not the angle
            # to an edge (2.750 deg). Testing against the corner as a cone accepts epochs
            # the tool then refuses -- which, without --keep-going, aborted the run. Take
            # the per-axis half-angles from the bounds instead, which is the frustum the
            # renderer actually builds.
            tx = max(abs(b[0] / b[2]) for b in bounds)
            ty = max(abs(b[1] / b[2]) for b in bounds)
            half = float(np.degrees(np.arctan(max(tx, ty))))
        except Exception:
            return None, None, "could not read %s's FOV from the kernels" % instrument
        angles = {}
        for e in epochs:
            try:
                et = sp.str2et(iso(e).replace("Z", ""))
                pos, _ = sp.spkpos(target, et, "J2000", "NONE", observer)
                r = np.linalg.norm(pos)
                v = sp.pxform("J2000", instrument, et) @ (pos / r)
                if v[2] <= 0.0:
                    angles[iso(e)] = 180.0            # behind the camera
                    continue
                # How far outside the square frustum the body CENTRE is, in degrees, with
                # its own angular radius allowed: the tool keeps a body that clips an edge,
                # so dropping one here would throw away a frame it would have rendered.
                radius = (target_radius if target_radius
                          else PREFLIGHT_RADIUS_M.get(target, TARGET_RADIUS_M))
                margin = np.degrees(np.arctan(radius / (r * 1000.0)))
                ax = np.degrees(np.arctan(abs(v[0] / v[2])))
                ay = np.degrees(np.arctan(abs(v[1] / v[2])))
                angles[iso(e)] = float(max(ax, ay) - margin)
            except Exception:
                angles[iso(e)] = None      # no attitude or no ephemeris: let the tool say so
        return angles, float(half), None
    except Exception as ex:
        return None, None, str(ex)
    finally:
        os.chdir(cwd)
        try:
            import spiceypy as sp
            sp.kclear()
        except Exception:
            pass


README = """# Simulated {instrument} images -- {body}

Rendered from a shape model with PRo3D using SPICE geometry.

| | |
|---|---|
| kernel | `{mkid}` |
| shape model | `{opcname}` ({shapekind}) |
| eclipse | {eclipse} |
| body / frame | {body} / {frame} |
| observer / instrument | {observer} / {instrument} |
| epochs | {start} .. {end} UTC |
| cadence | {cadence}, {count} epochs ({span:.2f} h = {rotations:.2f} rotations) |
| range | {rmin:.2f} .. {rmax:.2f} km |
| image | {width} px, 8-bit greyscale PNG |
| gain | {gain} (fixed, not auto-exposed) |
| generated | {generated} |

## What is in this folder

```
{folder}/
{tree}```

{whatvariants}
A frame is named `AFC1_<VARIANT>_<date>_<time>`, so one epoch is the same stamp in every
folder. Alongside each image:

| file | what it is |
|---|---|
| `.png` | the image itself: {width}, 8-bit greyscale |
| `.mbi.json` | where the camera was and how it was pointed, in the usual instrument-image form: `SC_QUAT0..3` rotates spacecraft to J2000, `TRG_POSX/Y/Z` is the body's position relative to the spacecraft in km, J2000. This is what lets the image be projected back onto the shape model |
| `.png.json` | per-image numbers that go with it, including the ground size of a pixel |

`series.json` is the same information for the whole series in machine-readable form: one
entry per epoch naming its file in each folder, plus the settings everything was rendered
with.

{usinginpro3d}
## What these images are, and are not

- **Pointing is the planned spacecraft attitude.** Frames are not centred on the body;
  they use where the plan actually pointed {instrument}, and epochs where it looked
  elsewhere are simply absent from this series.
- **No detector effects**: no blur, no noise, no 12-bit quantisation.
- **No phase function.** Brightness is comparable across the series, but it is not
  absolute radiometry.
- **Surface roughness is shading only.** It is not in the geometry, so a reconstruction
  will happily turn it into relief that the shape model does not have.
- **Geometric positions**: no light-time or stellar aberration correction.
"""


# The "what are these folders" paragraph, written from the variants the folder actually
# holds. A fixed paragraph about four folders and the DRACO mosaic was wrong the moment a
# run produced two folders and no mosaic -- which is what an OBJ series is.
WHAT_VARIANTS = {
    "many": """The {n} image folders are the **same frames rendered {n} ways**: same camera, same
epochs, same exposure, differing only in what the surface is made of. Pick the one that
matches what you are testing, or compare a pair.
""",
    "one": """`{only}/` holds the lit frames: one rendering of each epoch.
""",
    "texture": """
`baked/` and `delit/` both take surface brightness from the DRACO image where it covers
the body, with random texture filling the rest. The DRACO image has lighting baked into
it, which might confuse reconstruction methods -- `delit/` fits that lighting and removes
it, `baked/` leaves it in. Both are here so you can see which your pipeline needs.
""",
    "noTexture": """
There is no mosaic in this series: the shape model carries no texture, so the surface is a
constant albedo and every difference between frames is geometry and illumination.
""",
}


USING_IN_PRO3D = """## Using them in PRo3D

Open `{scene}`, then: GIS tab -> Projected Images -> Import Directory -> `stack/`, `+` on
each row you want, Orientation Source **MBI**, Transfer Function **off**.

`stack/` holds {nstack} evenly spaced {stackvariant} frames rather than all {count},
because the viewer projects at most 32 layers at once and frames minutes apart see nearly
the same face.
"""

# Without a scene there is nothing to open, and the projection needs an OPC surface to
# land on. Saying so is more use than instructions for a file that is not in the folder.
NO_SCENE = """## Projecting them in PRo3D

These frames were rendered from a Wavefront shape model, which the viewer cannot bind a
projection to -- projection lands on an OPC surface. They are here to be compared against
other renderings of the same epochs, not to be projected.
"""


def what_variants(lit):
    """The paragraph describing the lit variant folders of THIS series."""
    out = (WHAT_VARIANTS["many"].format(n=len(lit)) if len(lit) > 1
           else WHAT_VARIANTS["one"].format(only=(lit or ["(none)"])[0]))
    textured = [v for v in lit if v in ("delit", "delitplain", "baked")]
    return out + (WHAT_VARIANTS["texture"] if len(textured) == 2
                  else "" if textured else WHAT_VARIANTS["noTexture"])


def write_readme(path, **kw):
    with open(path, "w", encoding="utf-8") as f:
        f.write(README.format(**kw))


def main():
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    testdata = os.environ.get("PRO3D_TEST_DATA")
    default_opc = os.path.join(testdata, "HERA", "Dimorphos_opc", "Dimorphos") if testdata else None
    default_template = (os.path.join(testdata, "HERA", "Dimorphos_opc", "AFC_2027-03-21",
                                     "ProjectionTest.pro3d") if testdata else None)

    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--opc", default=default_opc,
                    help="Dimorphos OPC directory (default: $PRO3D_TEST_DATA/HERA/Dimorphos_opc/Dimorphos)")
    ap.add_argument("--obj", default=None,
                    help="render from a Wavefront OBJ shape model instead of the OPC "
                         "(`.obj.gz` works). The kernels' own Dimorphos model has 0.24 m "
                         "facets against the OPC's 1.96 m posts -- at 5 km an AFC pixel is "
                         "0.48 m, so OPC frames are shape-limited rather than sensor-limited. "
                         "It carries no texture, so only micro and smooth can be rendered "
                         "from it unless --obj-texture is given")
    ap.add_argument("--obj-scale", default="1000",
                    help="metres per --obj file unit (default 1000: the SPICE DSK shape "
                         "models are in kilometres)")
    ap.add_argument("--occluder-body", default=None,
                    help="cast the other body of the binary as a shadow -- 'DIDYMOS' when "
                         "rendering Dimorphos. Without it an eclipsed epoch renders in full "
                         "daylight, and Dimorphos is inside Didymos' umbra for ~12 %% of the "
                         "close-orbit phase")
    ap.add_argument("--occluder-obj", default=None,
                    help="the occluder's shape model, so the shadow has the primary's real "
                         "limb; without it a tessellation of its reference radii is used")
    ap.add_argument("--occluder-obj-scale", default="1000",
                    help="metres per --occluder-obj file unit (default 1000)")
    ap.add_argument("--occluder-opc", default=None,
                    help="the occluder's shape model as an OPC instead of --occluder-obj. "
                         "The only way it can carry a texture: the shape-model OBJs have no "
                         "texture coordinates, so with --occluder-in-scene the primary is "
                         "otherwise a grey body")
    ap.add_argument("--occluder-texture-albedo", action="store_true",
                    help="draw the occluder with its own texture as albedo. Only its level "
                         "is normalised (mean texel -> --albedo); nothing is de-shaded")
    ap.add_argument("--day-folders", action="store_true",
                    help="split each variant folder by UTC date (<variant>/yyyy-MM-dd/). "
                         "An 85-day set is thousands of files and one flat directory is "
                         "neither navigable nor what the reference deliveries look like")
    ap.add_argument("--occluder-in-scene", action="store_true",
                    help="draw the occluder in the image too, not only as a shadow caster: "
                         "both bodies then go through one sun and one photometry")
    ap.add_argument("--obj-texture", default=None,
                    help="image to drape on --obj, for the delit/baked variants. The .png "
                         "beside each .bds in the kernel set is a PREVIEW RENDER, not a map")
    ap.add_argument("--out", required=True, help="where to write the series")
    ap.add_argument("--body", default="DIMORPHOS",
                    help="SPICE body being imaged (default DIMORPHOS). Use DIDYMOS with a "
                         "Didymos shape model to cover the epochs where AFC-1 is pointed at "
                         "the primary instead")
    ap.add_argument("--frame", default=None,
                    help="its body-fixed frame (default <body>_FIXED)")
    ap.add_argument("--target-radius", type=float, default=None,
                    help="the body's smallest semi-axis, metres; only a margin for the "
                         "pointing pre-flight, and the smallest is what guarantees the "
                         "target is actually in frame. Defaults per body")
    ap.add_argument("--start", default=DEFAULT_START,
                    help="first epoch, ISO-8601 UTC (default %s); must lie inside the "
                         "loaded kernels' coverage" % DEFAULT_START)
    ap.add_argument("--interval", type=float, default=15.0,
                    help="cadence in minutes (default 15)")
    ap.add_argument("--interval-seconds", type=float, default=None,
                    help="cadence in SECONDS, overriding --interval. For a video cadence: "
                         "8 s expressed as 0.1333.. minutes accumulates float error that "
                         "truncates a stamp to the wrong second, and the stamp is the "
                         "filename")
    ap.add_argument("--duration", type=float, default=ROTATION_HOURS,
                    help="span in hours (default %.2f, one Dimorphos rotation); ignored "
                         "when --count is given" % ROTATION_HOURS)
    ap.add_argument("--count", type=int, default=None,
                    help="number of epochs; overrides --duration")
    ap.add_argument("--kernel", default=None,
                    help="explicit SPICE metakernel (default: <kernel-root>/mk/hera_plan.tm)")
    ap.add_argument("--kernel-root", default=None,
                    help="SPICE kernel tree; defaults to $PRO3D_SPICE_KERNELS")
    ap.add_argument("--gain", default="4.492",
                    help="fixed I/F->DN gain, so the series is radiometrically comparable "
                         "(default 4.492); 0 auto-exposes every frame on its own")
    ap.add_argument("--albedo", default=None, help="normal reflectance (tool default 0.16)")
    ap.add_argument("--micro-scale", default="3.0",
                    help="micro-structure feature size in metres (default 3.0). Bigger "
                         "than the tool's 0.5 on purpose: at 7-8 km AFC sees ~0.7 m/px, "
                         "and structure below the pixel averages out to nothing")
    ap.add_argument("--micro-amplitude", default="0.3",
                    help="normal perturbation strength of the MICRO variant (default 0.3); "
                         "the SMOOTH variant is always 0")
    ap.add_argument("--distance", default=None,
                    help="camera range override in metres; default is the spacecraft's real range")
    ap.add_argument("--variants", default="delit,baked,micro,smooth",
                    help="which variants to render, comma-separated (default delit,baked,micro,smooth); "
                         "'delitplain' is delit without the micro-structure. "
                         "'spice' adds an independent reference ray-cast from SPICE's own DSK "
                         "shape model -- same epochs, same camera, no PRo3D code involved")
    ap.add_argument("--stack-count", type=int, default=15,
                    help="how many frames to copy into <out>/stack/ (default 15, cap %d)" % MAX_STACK)
    ap.add_argument("--stack-variant", default="delit",
                    choices=["delit", "delitplain", "baked", "micro", "smooth"],
                    help="which variant the stack subset takes (default delit)")
    ap.add_argument("--texture-layer", default="DRACO_2",
                    help="texture layer the scene displays under the projection (default DRACO_2)")
    ap.add_argument("--scene-template", default=default_template,
                    help="a .pro3d to derive ImageSeries.pro3d from; skipped if absent")
    ap.add_argument("--margin", type=float, default=0.05,
                    help="validation: %s" % (
                        "how far another dihedral transform must beat identity before a "
                        "frame counts as failed (default 0.05). Calibrated from the data: "
                        "a correct frame's identity wins by 0.12-0.73 where the view "
                        "discriminates, and flipUD edges ahead by at most 0.023 where it "
                        "does not"))
    ap.add_argument("--spice-hours", type=float, default=0.0,
                    help="confine the reference frames to the first N hours of the series "
                         "(default 0 = spread over all of it). The reference validates the "
                         "RENDERER, and the renderer does not change with the epoch -- so a "
                         "series extended beyond the span that was checked does not need its "
                         "own references, and rendering them at ~50 s each would be the "
                         "longest part of the run for no extra answer")
    ap.add_argument("--spice-count", type=int, default=8,
                    help="how many spice reference frames to render, spread evenly over "
                         "the series (default 8; 0 renders one per epoch). The reference "
                         "costs ~50 s a frame and tests the renderer, not the epoch, so a "
                         "few spread over the series answer the same question as all of them")
    ap.add_argument("--docs-only", action="store_true",
                    help="rewrite README.md and series.json from the frames already in "
                         "<out>, rendering nothing. For fixing the text without touching "
                         "the data -- re-rendering a finished series to correct a sentence "
                         "is a good way to damage it")
    ap.add_argument("--list-layers", action="store_true",
                    help="print the OPC's texture layers and exit")
    a = ap.parse_args()

    if not a.frame:
        a.frame = a.body + "_FIXED"

    try:
        start = parse_iso(a.start)
    except ValueError:
        print("--start is not an ISO-8601 time: %s" % a.start)
        return 2
    # Seconds are the honest unit below about a minute; --interval stays the default
    # because every existing series is expressed in minutes.
    step_s = a.interval_seconds if a.interval_seconds else a.interval * 60.0
    if step_s <= 0:
        print("--interval must be positive")
        return 2
    a.interval = step_s / 60.0

    count = a.count if a.count else max(1, int(round(a.duration * 3600.0 / step_s)))
    # integer seconds where the cadence is integral, so a stamp cannot land a second off
    if abs(step_s - round(step_s)) < 1e-9:
        epochs = [start + datetime.timedelta(seconds=int(round(step_s)) * i) for i in range(count)]
    else:
        epochs = [start + datetime.timedelta(seconds=step_s * i) for i in range(count)]
    span = (epochs[-1] - epochs[0]).total_seconds() / 3600.0

    variants = [v.strip().lower() for v in a.variants.split(",") if v.strip()]
    unknown = [v for v in variants
               if v not in ("micro", "smooth", "delit", "delitplain", "baked", "spice")]
    if unknown or not variants:
        print("--variants takes any of delit, delitplain, baked, micro, smooth, spice -- got %s"
              % a.variants)
        return 2

    # Exactly one shape model. With both there is no answer to "which body is this frame
    # of?", and a precedence rule would make that answer depend on a line of code.
    if a.obj and a.opc and a.opc != default_opc:
        print("--opc and --obj are two different shape models; pass one of them")
        return 2
    if not a.obj and not a.opc:
        print("no shape model: pass --opc <dir> or --obj <file>")
        return 2
    if a.obj and not os.path.exists(a.obj):
        print("--obj not found: %s" % a.obj)
        return 2
    if not a.obj and not os.path.isdir(a.opc):
        print("--opc directory not found: %s" % a.opc)
        return 2
    # The textured variants are refused rather than degraded, for the same reason the tool
    # refuses them: an untextured `delit` frame is pixel for pixel a `micro` frame, and
    # nothing in the delivery would say so.
    if a.obj and not a.obj_texture:
        textured = [v for v in variants if v in ("delit", "delitplain", "baked")]
        if textured:
            print("--obj carries no texture, so %s cannot be rendered from it "
                  "(they would be identical to micro). Pass --obj-texture, or "
                  "--variants %s"
                  % (", ".join(textured),
                     ",".join(v for v in variants if v not in textured) or "micro,smooth"))
            return 2
    if a.stack_variant not in variants:
        # Not fatal: a pass that renders only one variant (e.g. the spice reference) into
        # an existing series should leave the stack it already has alone rather than
        # refuse to run.
        print("note: --stack-variant %s is not being rendered this run; leaving stack/ as it is"
              % a.stack_variant)
    if a.stack_count > MAX_STACK:
        print("note: --stack-count %d exceeds the viewer's cap of %d; clamping"
              % (a.stack_count, MAX_STACK))
    stack_count = max(0, min(a.stack_count, MAX_STACK))

    shape = ["--obj", a.obj, "--obj-scale", a.obj_scale] if a.obj else ["--opc", a.opc]
    if a.obj and a.obj_texture:
        shape += ["--obj-texture", a.obj_texture]
    common = shape + ["--body", a.body, "--frame", a.frame,
                      "--observer", "HERA", "--instrument", "HERA_AFC-1"]
    # The occluder goes only to the RENDER, never to the scene or layer queries: it is a
    # shadow caster, not part of the body being imaged.
    eclipse = []
    if a.occluder_body:
        eclipse = ["--occluder-body", a.occluder_body]
        if a.occluder_opc:
            eclipse += ["--occluder-opc", a.occluder_opc]
        elif a.occluder_obj:
            eclipse += ["--occluder-obj", a.occluder_obj,
                        "--occluder-obj-scale", a.occluder_obj_scale]
        if a.occluder_in_scene:
            eclipse += ["--occluder-in-scene"]
        if a.occluder_texture_albedo:
            eclipse += ["--occluder-texture-albedo"]

    # What the frames were rendered against, for series.json and the README. The product
    # id -- an OPC's .opcx basename, an OBJ's file name -- carries the GSD, the source and
    # the version; the absolute path is this machine's and means nothing to a recipient.
    shape_path = os.path.abspath(a.obj or a.opc)
    if a.obj:
        shape_product = os.path.basename(shape_path)
    else:
        shape_product = next((os.path.splitext(f)[0] for f in sorted(os.listdir(a.opc))
                              if f.endswith(".opcx")), os.path.basename(shape_path))
    if a.kernel:
        common += ["--kernel", a.kernel]
    if a.kernel_root:
        common += ["--kernel-root", a.kernel_root]

    if a.list_layers:
        if a.obj:
            print("--list-layers lists an OPC's texture layers; a mesh draws --obj-texture")
            return 2
        print(list_layers(repo, common, iso(epochs[0])))
        return 0

    kernel = resolve_kernel(a.kernel, a.kernel_root)
    mkid = mk_identifier(kernel)

    # Recorded as provenance, not used to decide anything: `simulate-series` renders the
    # whole series in one process and the folders are written fresh, so there is no
    # half-updated folder for a build fingerprint to protect against any more. It stays
    # in series.json because "which binary made these frames" is still the question
    # nobody can answer from the frames themselves.
    build = tool_fingerprint(repo)

    # One folder per variant, so each is directly importable: PRo3D's Import Directory
    # takes a folder, and a folder holding two variants would load two layers per epoch.
    # It also keeps the README, the scene and the stack out of the data. The tool
    # recreates the lit-variant folders itself; the reference is ours to manage.
    vdir = {v: os.path.join(a.out, v) for v in variants}
    os.makedirs(a.out, exist_ok=True)
    if "spice" in vdir and not a.docs_only:
        if os.path.isdir(vdir["spice"]):
            shutil.rmtree(vdir["spice"])
        os.makedirs(vdir["spice"])

    print("%d epochs, %s .. %s, every %g min (%.2f h, %.2f rotations)"
          % (count, iso(epochs[0]), iso(epochs[-1]), a.interval, span, span / ROTATION_HOURS))
    print("kernel %s%s" % (kernel or "(tool default)", "  [%s]" % mkid if mkid else ""))

    # Pointing pre-flight. The camera is aimed by the kernels, so epochs where the
    # instrument was observing something else cannot produce a frame -- drop them here,
    # visibly, instead of rendering into a guaranteed failure at the end of a long run.
    offtarget = []
    angles, halffov, why = preflight(epochs, kernel, "HERA_AFC-1", a.body, "HERA",
                                     a.target_radius)
    if angles is None:
        print("pointing pre-flight skipped (%s); the tool still checks each frame" % why)
    else:
        keep = []
        for e in epochs:
            ang = angles.get(iso(e))
            if ang is not None and ang > halffov:
                offtarget.append((iso(e), ang))
            else:
                keep.append(e)
        if offtarget:
            print("pointing: %d of %d epochs are OFF TARGET (%s frustum edge %.3f deg) and are excluded:"
                  % (len(offtarget), len(epochs), "HERA_AFC-1", halffov))
            for t, ang in offtarget[:6]:
                print("   %s  %.3f deg outside the field" % (t, ang))
            if len(offtarget) > 6:
                print("   ... and %d more" % (len(offtarget) - 6))
            print("   (the instrument was pointed elsewhere; these epochs cannot be rendered)")
        if not keep:
            print("\nno epoch in this range has HERA_AFC-1 pointed at %s -- nothing to render."
                  % a.body)
            return 2
        epochs = keep
        count = len(epochs)
        span = (epochs[-1] - epochs[0]).total_seconds() / 3600.0
        print("pointing: %d epochs on target (worst limb %.3f deg from the edge, edge at %.3f deg)"
              % (count, max((angles[iso(e)] or 0.0) for e in epochs), halffov))
    print("rendering %d frame(s) per epoch into %s" % (len(variants), ", ".join(sorted(vdir))))

    lit = [v for v in variants if v != "spice"]
    failed = []

    if a.docs_only:
        # Everything below the render is a function of what is on disk, so skipping both
        # render stages regenerates the documents and nothing else.
        lit = []

    # ONE tool invocation for every lit frame in the series.
    #
    # `simulate-image` is a complete program per frame: GL context, OPC load, de-shading
    # fit, scene graph, LOD warm-up, all to emit one PNG. None of that depends on the
    # epoch, so driving it 429 times put this series at ~50 minutes of which the rendering
    # was a rounding error. `simulate-series` hoists all of it and renders the whole set
    # in one process, at ~0.3 s a frame -- and it verifies every sidecar as it goes.
    if lit:
        # The epoch list is an INPUT to the tool, not part of the delivery -- series.json
        # already names every epoch. Keeping it out of <out> keeps the data folder to
        # things the recipient wants.
        fd, times_file = tempfile.mkstemp(prefix="pro3d-epochs-", suffix=".txt", text=True)
        os.close(fd)
        with open(times_file, "w", encoding="utf-8") as f:
            f.write("# epochs of this series, on target for HERA_AFC-1; written by %s\n"
                    % os.path.basename(__file__))
            for e in epochs:
                f.write(iso(e) + "\n")

        args = ["simulate-series"] + common + [
            "--times-file", times_file,
            "--out", a.out,
            "--variants", ",".join(lit),
            "--gain", a.gain,
            "--micro-scale", a.micro_scale,
            "--micro-amplitude", a.micro_amplitude,
            # The pre-flight above and the tool's own test are not identical -- the tool
            # projects the shape model's bounding box, this projects a sphere around the
            # body centre -- so a handful of epochs can still be refused at render time.
            # Over 85 days that must not abort the run; the verb still exits non-zero and
            # names every frame it could not render, and series.json records them.
            "--keep-going",
        ]
        if a.day_folders:
            args += ["--day-folders"]
        if not a.obj:
            # --deshade-layer drives both the fit and the divisor; DELIT is the only
            # variant that uses it, and the other two never sample the texture. A mesh
            # has no layers -- it draws --obj-texture, already in `shape`.
            args += ["--deshade-layer", a.texture_layer]
        args += eclipse
        if a.albedo:
            args += ["--albedo", a.albedo]
        if a.distance:
            args += ["--distance", a.distance]

        print("rendering %d epochs x %d variants in one process" % (count, len(lit)))
        t0 = time.time()
        p = run_tool(repo, args, stream=True)
        if p.returncode != 0:
            # The verb names every frame it could not render, and has already said why on
            # stderr. One entry here so the manifest records that this run failed.
            failed.append((iso(epochs[0]), ",".join(lit), tool_error(p)))
            print("\nsimulate-series FAILED (exit %d) -- see above" % p.returncode)
        else:
            print("   %d frames in %.1f s" % (count * len(lit), time.time() - t0))
        try:
            os.remove(times_file)
        except OSError:
            pass

    # The SPICE reference is the expensive one: ~50 s a frame against the tool's ~0.3 s,
    # and it does not parallelise -- CSPICE reads the DSK in 1 KB records through its DAS
    # layer, and fifteen worker processes spent thirteen minutes reading 20 GB each before
    # delivering their first frames.
    #
    # It is also not needed at every epoch. The reference exists to show that the detector
    # axes and the FOV agree with the kernels, and that is a property of the *renderer*,
    # not of the epoch: a handful of frames spread over the series tests it under the
    # illuminations and visible faces the series contains.
    if "spice" in variants and not a.docs_only:
        # The pool the references are drawn from, which is not necessarily the whole
        # series: --spice-hours confines them to the front of it.
        pool = epochs
        if a.spice_hours > 0:
            cutoff = a.spice_hours * 3600.0
            pool = [e for e in epochs if (e - epochs[0]).total_seconds() <= cutoff] or epochs
        n = len(pool) if a.spice_count <= 0 else min(a.spice_count, len(pool))
        chosen_spice = subset(pool, n)
        print("spice: %d reference frame(s) spread over %d of %d epochs (%s), ~50 s each"
              % (len(chosen_spice), len(pool), len(epochs),
                 "the whole series" if len(pool) == len(epochs)
                 else "the first %g h; the rest is unreferenced by design" % a.spice_hours))
        t0 = time.time()
        for i, e in enumerate(chosen_spice):
            png = os.path.join(vdir["spice"], "AFC1_SPICE_%s.png" % stamp(e))
            left = (len(chosen_spice) - i - 1) * ((time.time() - t0) / max(1, i))
            print(" [%d/%d] %s  spice%s"
                  % (i + 1, len(chosen_spice), iso(e),
                     "  ~%d min left" % int(left / 60.0) if i else ""))
            # Scaled through the same albedo and gain as the tool's frames so the two are
            # radiometrically comparable, not just geometrically.
            p = spice_frame(kernel, iso(e), png, float(a.gain or 4.492),
                            float(a.albedo or 0.16))
            if p.returncode != 0:
                failed.append((iso(e), "spice", tool_error(p)))

    # Everything below this line describes the FOLDER, not this run. Building it from the
    # frames this invocation happened to render meant that a pass over one variant (say
    # --variants micro, to repair a single frame) rewrote series.json as if the other
    # variants did not exist -- which silently dropped the spice reference and made the
    # validator report "nothing to validate against" -- and left the README claiming one
    # epoch over a folder holding forty-eight.
    rendered = []
    paths = {}
    for v in sorted(VARIANTS):
        d = os.path.join(a.out, v)
        if not os.path.isdir(d):
            continue
        # walks day folders as well as a flat one, so --day-folders needs no second path
        for st, full in sorted(frame_paths(d).items()):
            if not st.startswith("AFC1_"):
                continue
            bits = st.split("_")
            if len(bits) < 4:
                continue
            day, hms = bits[-2], bits[-1]
            t = "%s-%s-%sT%s:%s:%sZ" % (day[0:4], day[4:6], day[6:8],
                                        hms[0:2], hms[2:4], hms[4:6])
            rendered.append((st, t, v))
            paths[(v, st)] = full
    variants = sorted({v for (_, _, v) in rendered}) or variants
    vdir = {v: os.path.join(a.out, v) for v in variants}
    times = sorted({t for (_, t, _) in rendered})
    if times:
        count = len(times)
        span = (parse_iso(times[-1]) - parse_iso(times[0])).total_seconds() / 3600.0
        if count > 1:
            # the median gap, not a.interval: a re-run over a sub-range must not restate
            # the folder's cadence, and off-target epochs leave gaps that are multiples
            gaps = sorted((parse_iso(b) - parse_iso(c)).total_seconds() / 60.0
                          for c, b in zip(times, times[1:]))
            interval = gaps[len(gaps) // 2]
        else:
            interval = a.interval
    else:
        times, interval = [iso(e) for e in epochs], a.interval

    print("\nsidecar check -- A^T*TRG_POS must be close to (0, 0, +1):")
    bad, ranges = 0, {}
    # the bounding-box half-diagonal here, not the inscribed radius above: this check
    # must not reject a frame the renderer was right to produce
    radius = {"DIMORPHOS": 136.0, "DIDYMOS": 650.0}.get(a.body, 136.0)
    for v in sorted(vdir):
        b, r = check_sidecars(vdir[v], target_radius_m=radius)
        bad += b
        ranges.update(r)

    # the subset comes from the epochs that actually produced a frame, so a failed render
    # in the middle shifts the sampling instead of putting a hole in the stack
    ok = [(s, t) for (s, t, v) in rendered if v == a.stack_variant and s in ranges]
    ok.sort(key=lambda x: x[1])
    chosen = subset(ok, stack_count) if stack_count else []
    stack_dir = os.path.join(a.out, "stack")
    if chosen:
        if os.path.isdir(stack_dir):
            shutil.rmtree(stack_dir)
        os.makedirs(stack_dir)
        for stem, _ in chosen:
            # .png.json, not .json: --write-mbi names the statistics sidecar after the
            # image file, extension included. Copying "<stem>.json" silently copies
            # nothing and the subset arrives without the pixel size unproject needs.
            base = paths.get((a.stack_variant, stem))
            if not base:
                continue
            root = base[:-4]                     # drop ".png"; the sidecars sit beside it
            for ext, src in ((".png", base),
                             (".mbi.json", root + ".mbi.json"),
                             (".png.json", base + ".json")):
                if os.path.exists(src):
                    shutil.copy2(src, os.path.join(stack_dir, stem + ext))
        first, last = chosen[0][1], chosen[-1][1]
        step = (parse_iso(last) - parse_iso(first)).total_seconds() / 60.0 / max(1, len(chosen) - 1)
        print("\nwrote %s: %d %s frames, %s .. %s, ~%.0f min apart"
              % (stack_dir, len(chosen), a.stack_variant, first, last, step))
        print("   import this folder in the GIS tab (Projected Images -> Import Directory)")

    scene = None
    if a.obj and chosen:
        # The .pro3d template binds an OPC surface; a mesh shape model has no .opcx, no
        # texture layer and no selectedTexture to point the scene at. Rendering from the
        # OBJ is a comparison against other renderers, not a delivery to project in the
        # viewer -- and saying so beats writing a scene that opens onto nothing.
        print("no scene written: --obj frames are not projected in the viewer "
              "(the scene binds an OPC surface)")
    elif a.scene_template and os.path.exists(a.scene_template) and chosen:
        idx = texture_index(repo, common, times[0], a.texture_layer)
        if idx is None:
            print("could not resolve '%s' to an index; scene not written" % a.texture_layer)
        else:
            # the camera goes on the middle subset frame's axis: the series' own midpoint,
            # and the scene clock with it, so the sun matches what that frame saw
            mid_stem, mid_iso = chosen[len(chosen) // 2]
            scene = os.path.join(a.out, "ImageSeries.pro3d")
            # from the STACK copy, which is flat whatever --day-folders did to the
            # variant folders -- and is the frame the scene's camera is placed on anyway
            write_scene(a.scene_template, scene, a.opc, a.texture_layer, idx,
                        os.path.join(stack_dir, mid_stem + ".mbi.json"),
                        mid_iso.replace("Z", ".0000000Z"),
                        a.kernel_root or os.environ.get("PRO3D_SPICE_KERNELS"), a.kernel)
            print("wrote %s (texture %s index %d, camera on %s)"
                  % (scene, a.texture_layer, idx, mid_stem))

    # One entry per EPOCH, each naming its file per variant -- the same shape as the
    # folders on disk (epoch x variant), and the shape a consumer actually wants: "give
    # me every rendition of time T". The old flat list repeated each epoch once per
    # variant with the variant encoded inside a filename, and had nowhere to say what a
    # variant meant or where its files were.
    by_stamp = {}
    for (stem, t, v) in rendered:
        by_stamp.setdefault(t, {})[v] = "%s/%s.png" % (v, stem)
    ranges_by_time = {}
    for (stem, t, v) in rendered:
        if ranges.get(stem):
            ranges_by_time[t] = round(ranges[stem], 1)

    manifest = {
        "schema": "pro3d.image-series/1",
        "generated": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "provenance": {
            "generator": "scripts/make-image-time-series.py",
            "toolBuild": build,
            "kernel": kernel,
            "mkIdentifier": mkid,
            "shapeModel": shape_path,
            "shapeModelKind": "obj" if a.obj else "opc",
            "shapeModelProduct": shape_product,
            # kept under their old names so an existing consumer of series.json does not
            # break; they are null for an OBJ series
            "opc": os.path.abspath(a.opc) if not a.obj else None,
            "opcProduct": shape_product if not a.obj else None,
        },
        "observation": {
            "body": a.body, "frame": a.frame,
            "observer": "HERA", "instrument": "HERA_AFC-1",
            "imageSize": [1020, 1020],
            "textureLayer": a.texture_layer,
            "occluderBody": a.occluder_body,
            "occluderShapeModel": (os.path.abspath(a.occluder_opc) if a.occluder_opc
                                   else os.path.abspath(a.occluder_obj) if a.occluder_obj
                                   else ("reference radii" if a.occluder_body else None)),
            "occluderInScene": bool(a.occluder_in_scene),
            "occluderTextureAlbedo": bool(a.occluder_texture_albedo),
        },
        "series": {
            "start": times[0], "end": times[-1],
            "intervalMinutes": interval, "intervalSeconds": round(interval * 60.0, 3),
            "epochs": count,
            "spanHours": round(span, 3),
            "rotations": round(span / ROTATION_HOURS, 3),
            "rotationPeriodHours": ROTATION_HOURS,
        },
        "radiometry": {
            "gain": a.gain, "albedo": a.albedo, "fixedExposure": str(a.gain) != "0",
            "microScale": a.micro_scale, "microAmplitude": a.micro_amplitude,
        },
        "variants": {
            v: {
                "dir": v,
                "description": VARIANTS.get(v, ""),
                "renderer": "spiceypy DSK ray-cast" if v == "spice" else "pro3d-tool simulate-image",
                "sidecars": v != "spice",
                "frames": sum(1 for (_, _, vv) in rendered if vv == v),
            } for v in variants
        },
        "stack": {
            "dir": "stack",
            "variant": a.stack_variant,
            "frames": len(chosen),
            "note": "copies of the %s frames, thinned for PRo3D's 32-layer projection stack"
                    % a.stack_variant,
            "times": [t for _, t in chosen],
        },
        "scene": os.path.basename(scene) if scene else None,
        "epochs": [
            {
                "time": t,
                "stamp": stamp(parse_iso(t)),
                "rangeMeters": ranges_by_time.get(t),
                "files": by_stamp[t],
            }
            for t in sorted(by_stamp)
        ],
        "issues": {
            "failed": [{"time": t, "variant": v, "error": w} for (t, v, w) in failed],
            "skippedOffTarget": [{"time": t, "degOutsideField": round(ang, 4)}
                                 for (t, ang) in offtarget],
        },
    }
    # Validation is a STAGE, not an afterthought: the series is checked against the
    # independent SPICE ray-cast here, and the verdict goes into the manifest. A folder
    # then carries its own proof instead of it living in a terminal that is gone by the
    # time anyone asks whether these frames were ever checked.
    #
    # The check itself is pro3d_sim.validate_series -- the same function check-series.py
    # calls, so a re-check later cannot disagree with this one for any reason but the data.
    print("\nvalidation -- each frame against the SPICE reference of its epoch:")
    validation = validate_series(a.out, manifest, margin=a.margin)
    print_validation(validation)
    manifest["validation"] = validation

    with open(os.path.join(a.out, "series.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
    print("wrote %s" % os.path.join(a.out, "series.json"))

    # A README travels with the data: whoever receives this folder has neither this
    # terminal nor the repo, and the kernel version is the one thing they cannot recover
    # from the files themselves.
    rs = [v for v in ranges.values() if v]
    readme = os.path.join(a.out, "README.md")
    write_readme(readme,
                 instrument="HERA_AFC-1", body=a.body, frame=a.frame,
                 observer="HERA", generated=manifest["generated"],
                 kernel=kernel or "(tool default)", mkid=mkid or "UNKNOWN -- record it by hand",
                 shapekind="Wavefront OBJ" if a.obj else "OPC",
                 whatvariants=what_variants([v for v in variants if v != "spice"]),
                 opc=shape_path,
                 # the .opcx basename (or the OBJ's file name) is the product id -- GSD,
                 # source, version; the path above is this machine's and means nothing to
                 # whoever receives the data
                 opcname=shape_product,
                 start=times[0], end=times[-1],
                 # a video cadence in minutes reads as "0.133333 min"; below a minute the
                 # honest unit is seconds
                 cadence=("%g s" % round(interval * 60.0, 3) if interval < 1.0
                          else "%g min" % interval),
                 eclipse=("none -- an eclipsed epoch in this series renders in full daylight"
                          if not a.occluder_body else
                          "%s, cast from %s%s" % (a.occluder_body,
                                                  os.path.basename(a.occluder_opc.rstrip("/\\"))
                                                  if a.occluder_opc
                                                  else os.path.basename(a.occluder_obj) if a.occluder_obj
                                                  else "its reference radii",
                                                  " -- and drawn in the image" if a.occluder_in_scene
                                                  else "")),
                 count=count, span=span,
                 rotations=span / ROTATION_HOURS,
                 rmin=(min(rs) / 1000.0 if rs else 0.0), rmax=(max(rs) / 1000.0 if rs else 0.0),
                 width="1020x1020", gain=a.gain,
                 nstack=len(chosen), stackvariant=a.stack_variant,
                 usinginpro3d=(USING_IN_PRO3D.format(
                                   scene=os.path.basename(scene), nstack=len(chosen),
                                   stackvariant=a.stack_variant, count=count)
                               if scene else NO_SCENE),
                 folder=os.path.basename(os.path.abspath(a.out)),
                 tree=folder_tree(sorted(vdir), chosen, a.stack_variant, scene))
    print("wrote %s" % readme)

    # Three independent gates, and the run passes only if all three do. They fail for
    # different reasons on purpose: a render can fail, a sidecar can describe a camera the
    # viewer does not reconstruct, and the frames can disagree with the kernels about
    # which way the detector axes point -- none of which implies either of the others.
    if failed:
        print("\n%d render step(s) FAILED:" % len(failed))
        for t, v, w in failed:
            print("   %s %-6s  %s" % (t, v, w))
    if bad:
        print("\n%d sidecar(s) FAILED the boresight invariant" % bad)
    if validation["verdict"] == "fail":
        print("\nVALIDATION FAILED -- this series disagrees with the SPICE reference; "
              "see series.json for every failing pair")
    if failed or bad or validation["verdict"] == "fail":
        return 1
    print("\nall %d frames present, every sidecar passes the boresight invariant, and "
          "%s" % (len(rendered),
                  "the series validates against the SPICE reference"
                  if validation["verdict"] == "pass"
                  else "there was no reference to validate against (--variants spice)"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
