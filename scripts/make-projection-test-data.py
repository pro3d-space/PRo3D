#!/usr/bin/env python3
"""Generate the AFC-1 projection test data set, and a PRo3D scene set up to use it.

    python scripts/make-projection-test-data.py --opc <Dimorphos OPC> --out <folder>

Renders simulated HERA/AFC-1 frames of Dimorphos with their `.mbi.json` sidecars, and
writes a `ProjectionTest.pro3d` alongside them with everything preset that projection
needs. The frames are self-referential on purpose: each is rendered *from* the shape
model and its sidecar describes *the camera that render actually used*, so projecting one
back onto the same body from its own viewpoint must reproduce it. There is no metadata to
doubt -- if it does not line up, the fault is in the projection.

    test data   the OPC itself; the frames are only meaningful on the shape model they
                were rendered against, so keep them together
    kernels     PRO3D_SPICE_KERNELS, or pass --kernel-root
                git clone https://spiftp.esac.esa.int/git/hera.git
    python      numpy (for the sidecar check); no plotting

Two frame kinds, because no single one serves every check:

    AFC1_<layer>_*   the texture as the camera sees it, unlit. Carries the surface
                     detail that registration and orientation checks need.
    AFC1_SIM_*       lit render -- Lommel-Seeliger, micro-structure, cast shadows,
                     constant albedo. Fills the disk, so its outline is the body's
                     outline and the silhouette check means something.

--texture-layer is not cosmetic. The tool otherwise draws the patch's DEFAULT layer,
which is not necessarily the one a PRo3D scene displays, and on Dimorphos the two DRACO
layers are different DART passes over different hemispheres: pick the wrong one and the
frame is nearly black, which any correlation will happily score well. Run with
--list-layers to see what an OPC declares.

See docs/ProjectionValidation.md for what the resulting data is used to prove.
"""

import argparse
import json
import os
import subprocess
import sys

import numpy as np

EPOCHS = ["14:00:00", "17:00:00", "20:00:00", "23:00:00"]
DATE = "2027-03-21"
# Dimorphos rotates in about 11.9 h, so 3 h apart is roughly a quarter turn: the four
# frames see genuinely different faces, which is what makes them useful as a stack and as
# a test that flying between them carries the scene clock along.


def run_tool(repo, args, quiet=False):
    exe = os.path.join(repo, "bin", "Release", "net9.0", "PRo3D.Tool.exe")
    cmd = ([exe] if os.path.exists(exe)
           else ["dotnet", "run", "--project",
                 os.path.join(repo, "src", "PRo3D.Tool", "PRo3D.Tool.fsproj"), "--"])
    p = subprocess.run(cmd + args, capture_output=True, text=True)
    if not quiet:
        for line in (p.stdout or "").splitlines():
            if any(k in line for k in ("[out]", "[mbi]", "[texture]", "ERROR", "round trip")):
                print("   " + line.strip())
    return p


def quat_to_matrix(w, x, y, z):
    n = np.sqrt(w * w + x * x + y * y + z * z)
    w, x, y, z = w / n, x / n, y / n, z / n
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
    ])


def header(path):
    h = {}
    for blk in json.load(open(path))["fits_hdu_headers"]:
        for k, v in blk.items():
            h.setdefault(k, v["value"])
    return h


def check_sidecars(folder):
    """Aᵀ·TRG_POS must point down the boresight, i.e. close to (0, 0, +1).

    The check a real delivery can fail -- the HERA COP set conjugates the quaternion,
    puts the camera position in TRG_POS and centres it on the wrong body. The projector
    follows a wrong sidecar faithfully, so this is worth confirming on data that is
    supposed to be beyond doubt.
    """
    bad = 0
    for f in sorted(os.listdir(folder)):
        if not f.endswith(".mbi.json"):
            continue
        h = header(os.path.join(folder, f))
        trg = np.array([h["TRG_POSX"], h["TRG_POSY"], h["TRG_POSZ"]])
        A = quat_to_matrix(h["SC_QUAT0"], h["SC_QUAT1"], h["SC_QUAT2"], h["SC_QUAT3"])
        v = A.T @ trg
        v = v / np.linalg.norm(v)
        ok = v[2] > 0.999
        bad += 0 if ok else 1
        print("   %-38s range %7.1f m   A^T*TRG_POS = (%+.5f %+.5f %+.5f) %s"
              % (f[:-9], np.linalg.norm(trg) * 1000, v[0], v[1], v[2], "" if ok else "  <-- FAILS"))
    return bad


def write_scene(template, out, opc, texture_label, texture_index, mbi, epoch_iso):
    """A scene with everything projection needs already set.

    From an empty PRo3D none of this is in place, and until it is, fly-to and projection
    do nothing: an observed body, a surface bound to a SPICE body, and an epoch inside the
    loaded kernels' coverage (PRo3D's default is 2025-03-10, where HERA has no ephemeris
    for Dimorphos). Focal 122.563 mm is AFC-1's 5.5307 deg, which also makes fly-to land
    exactly at the instrument: the standoff frames the footprint in the VIEWER's field of
    view, so when the two match, standoff = range. Near plane 10 because at ~8 km a 0.1
    near plane cannot separate the near and far surfaces of a 177 m body.
    """
    d = json.load(open(template, encoding="utf-8-sig"))
    s = d["surfaceModel"]["surfaces"]["flat"][0]["Surfaces"]
    name = s["opcNames"][0]
    s["importPath"] = opc
    s["opcPaths"] = [os.path.join(opc, name)]
    opcx = [f for f in os.listdir(opc) if f.endswith(".opcx")]
    if opcx:
        s["opcxPath"] = os.path.join(opc, opcx[0])
    s["selectedTexture"] = {"index": texture_index, "label": texture_label, "version": 0}

    h = header(mbi)
    pos = -np.array([h["TRG_POSX"], h["TRG_POSY"], h["TRG_POSZ"]]) * 1000.0
    fwd = -pos / np.linalg.norm(pos)
    sky = np.array([0.0, 0.0, 1.0])
    right = np.cross(fwd, sky); right /= np.linalg.norm(right)
    up = np.cross(right, fwd); up /= np.linalg.norm(up)
    fmt = lambda a: "[%s]" % ", ".join(repr(float(x)) for x in a)
    d["cameraView"]["view"] = [fmt(sky), fmt(pos), fmt(fwd), fmt(up), fmt(right)]

    d["gisApp"]["defaultObservationInfo"]["time"] = epoch_iso
    # entities the loaded kernels cannot place cost a failing SPICE call per frame, and
    # SPICE calls serialise on a global lock -- a handful is enough to stall the UI
    keep = {"Dimorphos", "Didymos", "HERA"}
    d["gisApp"]["entities"] = [e for e in d["gisApp"]["entities"]
                               if e["spiceName"]["EntitySpiceName"] in keep]

    focal = 122.563
    t = 11.84 / (2.0 * focal)
    d["config"]["frustumModel"]["focal"] = focal
    f = d["config"]["frustumModel"]["frustum"]
    aspect = f["top"] / f["right"]
    n = 0.1
    f["right"], f["left"] = n * t, -n * t
    f["top"], f["bottom"] = n * t * aspect, -n * t * aspect
    d["config"]["nearPlane"]["value"] = 10.0

    d["scenePath"] = out
    json.dump(d, open(out, "w", encoding="utf-8"), indent=2)


def main():
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--opc", required=True, help="Dimorphos OPC directory")
    ap.add_argument("--out", required=True, help="where to write the data set")
    ap.add_argument("--texture-layer", default="DRACO_2",
                    help="texture layer for the unlit frames (default DRACO_2)")
    ap.add_argument("--kernel-root", default=None,
                    help="SPICE kernel tree; defaults to $PRO3D_SPICE_KERNELS")
    ap.add_argument("--gain", default="4.492",
                    help="fixed gain for the lit frames, so a series is comparable")
    ap.add_argument("--scene-template", default=None,
                    help="a .pro3d to derive ProjectionTest.pro3d from; skipped if absent")
    ap.add_argument("--list-layers", action="store_true",
                    help="print the OPC's texture layers and exit")
    a = ap.parse_args()

    common = ["--opc", a.opc, "--body", "DIMORPHOS", "--frame", "DIMORPHOS_FIXED",
              "--observer", "HERA", "--instrument", "HERA_AFC-1"]
    if a.kernel_root:
        common += ["--kernel-root", a.kernel_root]

    if a.list_layers:
        # an unmatched name makes the tool print what the OPC declares
        p = run_tool(repo, ["simulate-image"] + common +
                     ["--time", "%sT14:00:00Z" % DATE, "--texture-only",
                      "--texture-layer", "__list__", "--out", os.devnull], quiet=True)
        print((p.stdout or "") + (p.stderr or ""))
        return 0

    os.makedirs(a.out, exist_ok=True)
    print("rendering into %s" % a.out)

    # underscores out of the layer name, so DRACO_2 gives AFC1_DRACO2_... and the
    # epoch stays the only underscore-separated field in the stem
    tag = a.texture_layer.replace("_", "")

    for t in EPOCHS:
        stamp = "%s_%s" % (DATE.replace("-", ""), t.replace(":", ""))
        iso = "%sT%sZ" % (DATE, t)
        print(" %s  unlit (%s)" % (iso, a.texture_layer))
        run_tool(repo, ["simulate-image"] + common +
                 ["--time", iso, "--texture-only", "--texture-layer", a.texture_layer,
                  "--write-mbi", "--out",
                  os.path.join(a.out, "AFC1_%s_%s.png" % (tag, stamp))])
        print(" %s  lit" % iso)
        run_tool(repo, ["simulate-image"] + common +
                 ["--time", iso, "--gain", a.gain, "--write-mbi", "--out",
                  os.path.join(a.out, "AFC1_SIM_%s.png" % stamp)])

    print("\nsidecar check -- A^T*TRG_POS must be close to (0, 0, +1):")
    bad = check_sidecars(a.out)

    if a.scene_template and os.path.exists(a.scene_template):
        # index is resolved by the tool from the .opcx; the viewer needs it too, and the
        # two orderings have differed between OPC exports, so read it rather than assume
        p = run_tool(repo, ["simulate-image"] + common +
                     ["--time", "%sT14:00:00Z" % DATE, "--texture-only",
                      "--texture-layer", "__list__", "--out", os.devnull], quiet=True)
        idx = None
        for line in ((p.stdout or "") + (p.stderr or "")).splitlines():
            if "This OPC declares" in line:
                for part in line.split("declares:")[1].split(","):
                    i, _, lbl = part.strip().partition("=")
                    if lbl.strip() == a.texture_layer:
                        idx = int(i)
        if idx is None:
            print("could not resolve '%s' to an index; scene not written" % a.texture_layer)
        else:
            best = "%s_%s" % (DATE.replace("-", ""), EPOCHS[2].replace(":", ""))
            out = os.path.join(a.out, "ProjectionTest.pro3d")
            write_scene(a.scene_template, out, a.opc, a.texture_layer, idx,
                        os.path.join(a.out, "AFC1_%s_%s.mbi.json" % (tag, best)),
                        "%sT%s.0000000Z" % (DATE, EPOCHS[2]))
            print("\nwrote %s (texture %s index %d, camera on the %s frame's axis)"
                  % (out, a.texture_layer, idx, EPOCHS[2]))

    if bad:
        print("\n%d sidecar(s) FAILED the boresight invariant" % bad)
        return 1
    print("\nall sidecars pass the boresight invariant")
    return 0


if __name__ == "__main__":
    sys.exit(main())
