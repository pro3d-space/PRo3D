#!/usr/bin/env python3
"""Write a `pro3d-tool render-view` job: HERA AFC-1 views of Dimorphos from SPICE.

The job holds plain geometry -- camera location / forward / up, horizontal field of view,
sun position, OPC placements -- all in DIMORPHOS_FIXED, metres, so the tool itself needs
no SPICE. See docs/Pro3DTool-RenderView.md.

    python scripts/render-view-job.py --opc <Dimorphos OPC> --times 2027-03-21T14:00:00 \
        --out job.json [--didymos-opc <Didymos OPC>] [--width 1020 --height 1020]

Conventions, matching `pro3d-tool simulate-image --pointing ck` for HERA_AFC-1
(InstrumentProjection.specialTrafos composed with SimulateImage's camera basis):

- camera location: HERA relative to DIMORPHOS, no aberration correction
- R = pxform(HERA_AFC-1 -> DIMORPHOS_FIXED): forward = R[:,2] (+Z, the boresight),
  image up = R[:,0] (+X), image right = R[:,1] (+Y) = forward x up
- fov: getfov's FOV, full angle across the image's width (AFC-1: square, 5.5 deg)
- sun: SUN relative to DIMORPHOS (the tool turns it into the direction from the OPCs'
  centre)
- Didymos (optional): placed by pxform(DIDYMOS_FIXED -> DIMORPHOS_FIXED) and the Didymos
  position relative to Dimorphos

Kernels: --kernel-root or $PRO3D_SPICE_KERNELS (the tree or its `kernels` dir), metakernel
mk/hera_plan.tm unless --kernel names one.
"""

import argparse
import json
import math
import os
import sys

import numpy as np
import spiceypy as sp

FRAME = "DIMORPHOS_FIXED"
BODY = "DIMORPHOS"
INSTRUMENT = "HERA_AFC-1"
OBSERVER = "HERA"


def kernel_root(arg):
    root = arg or os.environ.get("PRO3D_SPICE_KERNELS")
    if not root:
        sys.exit("no kernel tree: pass --kernel-root or set PRO3D_SPICE_KERNELS")
    return root if os.path.isdir(os.path.join(root, "mk")) else os.path.join(root, "kernels")


def load_kernel(root, kernel):
    mk = os.path.abspath(kernel or os.path.join(root, "mk", "hera_plan.tm"))
    if not os.path.isfile(mk):
        sys.exit("no metakernel at %s" % mk)
    # the metakernel's PATH_VALUES are relative to its own directory
    os.chdir(os.path.dirname(mk))
    sp.furnsh(os.path.basename(mk))
    return mk


def horizontal_fov_deg(instrument):
    """Full angle across the image width, from the IK's FOV definition."""
    shape, frame, bsight, n, bounds = sp.getfov(sp.bods2c(instrument), 4)
    bsight = np.asarray(bsight) / np.linalg.norm(bsight)
    if shape == "RECTANGLE":
        # corners in the instrument frame; the image's width runs along +Y (see module doc)
        ys = [b[1] / b[2] for b in bounds]
        return math.degrees(math.atan(max(ys)) - math.atan(min(ys)))
    # circle / ellipse: twice the angle to the first boundary vector
    b = np.asarray(bounds[0]) / np.linalg.norm(bounds[0])
    return 2.0 * math.degrees(math.acos(float(np.clip(np.dot(b, bsight), -1.0, 1.0))))


def view_at(utc, fov, width, height, shadows):
    et = sp.str2et(utc)
    loc, _ = sp.spkpos(OBSERVER, et, FRAME, "NONE", BODY)
    sun, _ = sp.spkpos("SUN", et, FRAME, "NONE", BODY)
    r = np.asarray(sp.pxform(INSTRUMENT, FRAME, et))
    forward, up = r[:, 2], r[:, 0]
    name = "afc1_" + utc.replace("-", "").replace(":", "").split(".")[0]
    v = {
        "name": name,
        "location": [float(x) * 1000.0 for x in loc],
        "forward": [float(x) for x in forward],
        "up": [float(x) for x in up],
        "fov": fov,
        "sun": {"position": [float(x) * 1000.0 for x in sun], "shadows": shadows},
    }
    if width:
        v["width"] = width
    if height:
        v["height"] = height
    return v


def didymos_trafo(utc):
    """Row-major 4x4: DIDYMOS_FIXED metres -> DIMORPHOS_FIXED metres, at `utc`."""
    et = sp.str2et(utc)
    rot = np.asarray(sp.pxform("DIDYMOS_FIXED", FRAME, et))
    pos, _ = sp.spkpos("DIDYMOS", et, FRAME, "NONE", BODY)
    m = np.eye(4)
    m[:3, :3] = rot
    m[:3, 3] = np.asarray(pos) * 1000.0
    return [float(x) for x in m.reshape(-1)]


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--opc", required=True, help="Dimorphos OPC directory (in DIMORPHOS_FIXED)")
    ap.add_argument("--didymos-opc", help="Didymos OPC directory (in DIDYMOS_FIXED), placed at the --times epoch (then only one)")
    ap.add_argument("--times", required=True, help="UTC epochs, comma separated, e.g. 2027-03-21T14:00:00")
    ap.add_argument("--out", required=True, help="job file to write")
    ap.add_argument("--width", type=int, default=1020)
    ap.add_argument("--height", type=int, default=1020)
    ap.add_argument("--no-shadows", action="store_true", help="sun shading without cast shadows")
    ap.add_argument("--no-sun", action="store_true", help="leave the sun out: unlit renders")
    ap.add_argument("--untextured", action="store_true", help="render the OPCs without texture (plain grey): geometry-only, for silhouette comparisons")
    ap.add_argument("--kernel-root")
    ap.add_argument("--kernel", help="explicit metakernel (default <root>/mk/hera_plan.tm)")
    a = ap.parse_args()

    out = os.path.abspath(a.out)
    opc = os.path.abspath(a.opc)
    didymos = os.path.abspath(a.didymos_opc) if a.didymos_opc else None
    times = [t.strip() for t in a.times.split(",") if t.strip()]
    if not times:
        sys.exit("--times is empty")
    if didymos and len(times) > 1:
        # the job places each OPC once, but Didymos moves around Dimorphos (11.9 h orbit)
        sys.exit("--didymos-opc places Didymos at one epoch: write one job per epoch")

    mk = load_kernel(kernel_root(a.kernel_root), a.kernel)
    fov = horizontal_fov_deg(INSTRUMENT)

    opcs = [{"path": opc}]
    if didymos:
        opcs.append({"path": didymos, "trafo": didymos_trafo(times[0])})
    if a.untextured:
        for o in opcs:
            o["textured"] = False

    views = []
    for t in times:
        v = view_at(t, fov, None, None, not a.no_shadows)
        if a.no_sun:
            del v["sun"]
        views.append(v)

    job = {
        "coordinatesAreIn": "%s, metres (SPICE %s, observer %s, instrument %s)" % (FRAME, os.path.basename(mk), OBSERVER, INSTRUMENT),
        "opcs": opcs,
        "width": a.width,
        "height": a.height,
        "views": views,
    }
    with open(out, "w") as f:
        json.dump(job, f, indent=2)
    print("wrote %d view(s) to %s (fov %.4f deg)" % (len(views), out, fov))
    return 0


if __name__ == "__main__":
    sys.exit(main())
