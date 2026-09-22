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

# HARD-CODED DEFAULTS, not discovered values. The script does not ask the kernels what
# they cover: doing that properly needs spiceypy (see src/Tests/spice_coverage_tests.py)
# and is not worth the dependency here. So the epoch is a plain --date/--epochs argument
# with a default that is known to work against hera_plan.tm. Point it at a different
# kernel set without changing the date and the renders will simply fail, with SPICE
# saying so.
#
# Why this one: 2027-03-21 lies inside hera_plan.tm's coverage with HERA in close orbit at
# Dimorphos, which puts the ranges at 6.7-8.4 km and the phase angle low enough that the
# lit frames show the whole disk. Dimorphos rotates in about 11.9 h, so epochs 3 h apart
# are roughly a quarter turn each: the four frames see genuinely different faces, which is
# what makes the set useful as a stack and as a test that flying between them carries the
# scene clock along.
#
# Change --date/--epochs for a different mission phase or kernel set. The sidecar check at
# the end prints each frame's range, which is the quickest way to see whether the geometry
# the new epoch produced is sensible.
DEFAULT_EPOCHS = ["14:00:00", "17:00:00", "20:00:00", "23:00:00"]
DEFAULT_DATE = "2027-03-21"


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


def write_scene(template, out, opc, texture_label, texture_index, mbi, epoch_iso, kernel_root=None):
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
    # the template names its meta-kernel by an absolute path; keep the file name, take
    # the tree the frames were rendered with
    if kernel_root and d["gisApp"].get("spiceKernel"):
        root = kernel_root if os.path.isdir(os.path.join(kernel_root, "mk")) else os.path.join(kernel_root, "kernels")
        d["gisApp"]["spiceKernel"] = os.path.join(root, "mk", d["gisApp"]["spiceKernel"].replace("\\", "/").split("/")[-1])
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
    ap.add_argument("--date", default=DEFAULT_DATE,
                    help="UTC date for the frames (default %s); must lie inside the "
                         "loaded kernels' coverage" % DEFAULT_DATE)
    ap.add_argument("--epochs", default=",".join(DEFAULT_EPOCHS),
                    help="comma-separated UTC times on that date (default %s)"
                         % ",".join(DEFAULT_EPOCHS))
    ap.add_argument("--list-layers", action="store_true",
                    help="print the OPC's texture layers and exit")
    a = ap.parse_args()
    date = a.date
    epochs = [e.strip() for e in a.epochs.split(",") if e.strip()]
    if not epochs:
        print("--epochs is empty")
        return 2
    # the scene's camera goes on one frame's axis; the third by default, which in the
    # shipped set is the closest pass (6.7 km) and so the most detailed frame
    best_epoch = epochs[2] if len(epochs) > 2 else epochs[-1]

    common = ["--opc", a.opc, "--body", "DIMORPHOS", "--frame", "DIMORPHOS_FIXED",
              "--observer", "HERA", "--instrument", "HERA_AFC-1"]
    if a.kernel_root:
        common += ["--kernel-root", a.kernel_root]

    if a.list_layers:
        # an unmatched name makes the tool print what the OPC declares
        p = run_tool(repo, ["simulate-image"] + common +
                     ["--time", "%sT%sZ" % (date, epochs[0]), "--texture-only",
                      "--texture-layer", "__list__", "--out", os.devnull], quiet=True)
        print((p.stdout or "") + (p.stderr or ""))
        return 0

    os.makedirs(a.out, exist_ok=True)
    print("rendering into %s" % a.out)

    # underscores out of the layer name, so DRACO_2 gives AFC1_DRACO2_... and the
    # epoch stays the only underscore-separated field in the stem
    tag = a.texture_layer.replace("_", "")

    failed = []
    for t in epochs:
        stamp = "%s_%s" % (date.replace("-", ""), t.replace(":", ""))
        iso = "%sT%sZ" % (date, t)
        print(" %s  unlit (%s)" % (iso, a.texture_layer))
        p1 = run_tool(repo, ["simulate-image"] + common +
                      ["--time", iso, "--texture-only", "--texture-layer", a.texture_layer,
                       "--write-mbi", "--out",
                       os.path.join(a.out, "AFC1_%s_%s.png" % (tag, stamp))])
        print(" %s  lit" % iso)
        p2 = run_tool(repo, ["simulate-image"] + common +
                      ["--time", iso, "--gain", a.gain, "--write-mbi", "--out",
                       os.path.join(a.out, "AFC1_SIM_%s.png" % stamp)])
        # A failed render leaves no files behind, and the sidecar check below only looks
        # at what IS there -- so without this a half-empty data set reports "all sidecars
        # pass". The usual cause is an epoch where the body is not in the instrument's
        # field of view, or one outside the kernels' coverage; the tool says which.
        for kind, pr in (("unlit", p1), ("lit", p2)):
            if pr.returncode != 0:
                why = [l.strip() for l in ((pr.stdout or "") + (pr.stderr or "")).splitlines()
                       if "ERROR" in l]
                failed.append((iso, kind, why[-1] if why else "exit %d" % pr.returncode))

    print("\nsidecar check -- A^T*TRG_POS must be close to (0, 0, +1):")
    bad = check_sidecars(a.out)

    # the scene's camera comes from a frame's sidecar, so there is nothing to write it
    # from when the renders failed -- report those below instead of a FileNotFoundError
    if a.scene_template and os.path.exists(a.scene_template) and not failed:
        # index is resolved by the tool from the .opcx; the viewer needs it too, and the
        # two orderings have differed between OPC exports, so read it rather than assume
        p = run_tool(repo, ["simulate-image"] + common +
                     ["--time", "%sT%sZ" % (date, epochs[0]), "--texture-only",
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
            best = "%s_%s" % (date.replace("-", ""), best_epoch.replace(":", ""))
            out = os.path.join(a.out, "ProjectionTest.pro3d")
            write_scene(a.scene_template, out, a.opc, a.texture_layer, idx,
                        os.path.join(a.out, "AFC1_%s_%s.mbi.json" % (tag, best)),
                        "%sT%s.0000000Z" % (date, best_epoch),
                        a.kernel_root or os.environ.get("PRO3D_SPICE_KERNELS"))
            print("\nwrote %s (texture %s index %d, camera on the %s frame's axis)"
                  % (out, a.texture_layer, idx, best_epoch))

    if failed:
        print("\n%d of %d renders FAILED:" % (len(failed), 2 * len(epochs)))
        for iso, kind, why in failed:
            print("   %s %-5s  %s" % (iso, kind, why))
    if bad:
        print("\n%d sidecar(s) FAILED the boresight invariant" % bad)
    if failed or bad:
        return 1
    print("\nall %d renders succeeded and every sidecar passes the boresight invariant"
          % (2 * len(epochs)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
