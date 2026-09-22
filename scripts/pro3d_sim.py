#!/usr/bin/env python3
"""Shared machinery for the scripts that drive `pro3d-tool simulate-image`.

Extracted from make-projection-test-data.py so make-image-time-series.py can use the
same tool invocation, the same sidecar check and the same scene writer rather than a
second copy that drifts. Nothing here knows about a particular data set; the epochs,
the frame kinds and the naming are the callers' business.

Needs numpy (for the sidecar check and the scene camera).
"""

import json
import os
import subprocess

import numpy as np


def run_tool(repo, args, quiet=False, stream=False):
    """Run pro3d-tool: the built binary if there is one, otherwise `dotnet run`.

    `stream` prints the interesting lines as they arrive instead of after the process
    exits. A whole series is one invocation now, so buffering it would mean a run that
    says nothing until it is over -- and a run that says nothing is indistinguishable
    from a run that is stuck.
    """
    exe = os.path.join(repo, "bin", "Release", "net9.0", "PRo3D.Tool.exe")
    cmd = ([exe] if os.path.exists(exe)
           else ["dotnet", "run", "--project",
                 os.path.join(repo, "src", "PRo3D.Tool", "PRo3D.Tool.fsproj"), "--"])
    keys = ("[out]", "[mbi]", "[texture]", "[series]", "[deshade]", "ERROR", "FAIL",
            "round trip")
    if not stream:
        p = subprocess.run(cmd + args, capture_output=True, text=True)
        if not quiet:
            for line in (p.stdout or "").splitlines():
                if any(k in line for k in keys):
                    print("   " + line.strip())
        return p

    proc = subprocess.Popen(cmd + args, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            text=True, bufsize=1)
    out = []
    for line in proc.stdout:
        out.append(line)
        if not quiet and any(k in line for k in keys):
            print("   " + line.strip(), flush=True)
    err = proc.stderr.read()
    proc.wait()
    return subprocess.CompletedProcess(cmd + args, proc.returncode, "".join(out), err)


def tool_error(p):
    """The last ERROR line of a failed run, or a bare exit code."""
    why = [l.strip() for l in ((p.stdout or "") + (p.stderr or "")).splitlines()
           if "ERROR" in l]
    return why[-1] if why else "exit %d" % p.returncode


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


def sidecar_paths(folder):
    """Every .mbi.json under `folder`, however deep.

    A series folder is flat by default and split by UTC date with --day-folders, and no
    caller should have to know which. os.walk covers both.
    """
    out = []
    for root, _, files in os.walk(folder):
        out += [os.path.join(root, f) for f in files if f.endswith(".mbi.json")]
    return out


def frame_paths(folder, suffix=".png"):
    """{stem: full path} for every frame under `folder`, however deep."""
    out = {}
    for root, _, files in os.walk(folder):
        for f in files:
            if f.endswith(suffix) and not f.endswith(".png.json"):
                out[f[:-len(suffix)]] = os.path.join(root, f)
    return out


# How far off the boresight the target may sit before a sidecar counts as broken.
#
# Not zero: with --pointing ck the camera follows the spacecraft's attitude rather than
# aiming at the body, and an 85-day set is full of off-axis frames. The bound is the
# instrument's CORNER half-angle -- AFC's field is a square, so a body in a corner is
# 3.886 deg off-axis while still being fully inside a frustum whose edges are 2.750 --
# plus the body's own angular radius, since a body that clips an edge still renders.
#
# Measured over the COP set: median 0.146 deg, p99 3.902, max 4.409. A flat 4.0 deg
# rejected 58 perfectly good frames.
#
# The failure this exists to catch -- a conjugated quaternion, or the wrong body in
# TRG_POS -- is wrong by tens of degrees, so nothing is lost by allowing the whole field.
BORESIGHT_CORNER_DEG = 3.886
# Half the diagonal of Dimorphos' bounding box, metres (179.5 x 169.4 x 115.2). The
# SEMI-AXIS is the wrong number here: the tool refuses a frame only when the projected
# bounding BOX certainly misses the frustum, so that box is what sets how far off-axis a
# rendered frame can legitimately be. Using 90 m left five frames of 4263 failing by
# 0.02-0.12 deg.
BORESIGHT_TARGET_RADIUS_M = 136.0


def check_sidecars(folder, quiet=False):
    """A^T * TRG_POS must point INTO THE FIELD OF VIEW, i.e. near (0, 0, +1).

    The check a real delivery can fail -- the HERA COP set conjugates the quaternion,
    puts the camera position in TRG_POS and centres it on the wrong body. The projector
    follows a wrong sidecar faithfully, so this is worth confirming on data that is
    supposed to be beyond doubt.

    Returns (bad, ranges): the number of failures, and {stem: range in metres}.
    """
    bad = 0
    ranges = {}
    for path in sorted(sidecar_paths(folder)):
        f = os.path.basename(path)
        h = header(path)
        trg = np.array([h["TRG_POSX"], h["TRG_POSY"], h["TRG_POSZ"]])
        A = quat_to_matrix(h["SC_QUAT0"], h["SC_QUAT1"], h["SC_QUAT2"], h["SC_QUAT3"])
        v = A.T @ trg
        v = v / np.linalg.norm(v)
        rng_m = float(np.linalg.norm(trg)) * 1000.0
        tol = np.radians(BORESIGHT_CORNER_DEG) + np.arctan(BORESIGHT_TARGET_RADIUS_M / max(1.0, rng_m))
        ok = v[2] > np.cos(tol)
        bad += 0 if ok else 1
        stem = f[:-9]
        ranges[stem] = float(np.linalg.norm(trg) * 1000)
        if not quiet:
            print("   %-38s range %7.1f m   A^T*TRG_POS = (%+.5f %+.5f %+.5f) %s"
                  % (stem, ranges[stem], v[0], v[1], v[2], "" if ok else "  <-- FAILS"))
    return bad, ranges


def list_layers(repo, common, iso):
    """The tool's own listing of the OPC's texture layers (an unmatched name prints it)."""
    p = run_tool(repo, ["simulate-image"] + common +
                 ["--time", iso, "--texture-only",
                  "--texture-layer", "__list__", "--out", os.devnull], quiet=True)
    return (p.stdout or "") + (p.stderr or "")


def texture_index(repo, common, iso, layer):
    """Resolve a texture layer name to the index the viewer stores in selectedTexture.

    The index is resolved by the tool from the .opcx; the viewer needs it too, and the two
    orderings have differed between OPC exports, so read it rather than assume.
    """
    for line in list_layers(repo, common, iso).splitlines():
        if "This OPC declares" in line:
            for part in line.split("declares:")[1].split(","):
                i, _, lbl = part.strip().partition("=")
                if lbl.strip() == layer:
                    return int(i)
    return None


def write_scene(template, out, opc, texture_label, texture_index, mbi, epoch_iso,
                kernel_root=None, kernel=None):
    """A scene with everything projection needs already set.

    From an empty PRo3D none of this is in place, and until it is, fly-to and projection
    do nothing: an observed body, a surface bound to a SPICE body, and an epoch inside the
    loaded kernels' coverage (PRo3D's default is 2025-03-10, where HERA has no ephemeris
    for Dimorphos). Focal 122.563 mm is AFC-1's 5.5307 deg, which also makes fly-to land
    exactly at the instrument: the standoff frames the footprint in the VIEWER's field of
    view, so when the two match, standoff = range. Near plane 10 because at ~8 km a 0.1
    near plane cannot separate the near and far surfaces of a 177 m body.

    What it does NOT set is the projection stack. A .pro3d carries only the projection
    *settings* (lighting mode, winding correction); the image library and the stack order
    are session-local by design -- see GisAppJson.read0 in
    src/PRo3D.Core/GisApp-Model.fs, which rebuilds them from
    ProjectedImageListModel.initial on every load. The images still have to be imported
    and stacked in the GIS tab.
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
    # the template names its meta-kernel by an absolute path; take the one the frames were
    # actually rendered with -- an explicit --kernel verbatim, otherwise the template's own
    # file name resolved inside the given tree
    if kernel and d["gisApp"].get("spiceKernel"):
        d["gisApp"]["spiceKernel"] = os.path.abspath(kernel)
    elif kernel_root and d["gisApp"].get("spiceKernel"):
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


# ---------------------------------------------------------------------------------
# Independent reference renderer: SPICE's own DSK shape model, ray-cast with spiceypy.
#
# Shares nothing with PRo3D but the kernels, so it is a genuine cross-check rather than a
# second opinion from the same code. See docs/ShapeModelCrosscheck.md.
#
# Rays are built with +X image right and +Y image down, then the frame is rotated 90 deg
# counter-clockwise to land on the orientation PRo3D emits (see
# docs/dev/AFC-image-orientation.md). Rotating the finished frame rather than the ray grid
# keeps the ray maths readable and makes the convention a single, visible step.
#
# The rotation is NOT derived from the kernels -- it is the orientation the HERA community
# tool at comet-toolbox.com shows, which is what recipients compare against. Our reading of
# hera_afc_v06.ti's FOV diagram says otherwise, and that disagreement is unresolved.

def dsk_illumination(utc, instrument, target, observer, frame, size, fov_deg):
    """One ray-cast frame, with the illumination terms it was built from.

    Returns a dict of same-shape arrays:

        img   Lommel-Seeliger radiance, 0 wherever the surface is not lit
        hit   the ray found the body -- the silhouette
        mu0   cos(incidence). > 0 means the facet FACES the sun, whether or not it is
              actually reached by it
        mu    cos(emission)
        lit   illumf's own flag: the facet is reached by the sun, i.e. not self-shadowed

    `mu0` and `lit` are what separate the two reasons a pixel is dark. A facet turned away
    from the sun (mu0 <= 0) is the terminator and every renderer agrees about it; a facet
    turned TOWARD the sun and still dark (mu0 > 0, lit false) is a cast shadow, which is
    the thing a shadow map can get wrong. Comparing total darkness against total darkness
    conflates the two and grades a shadow map on the terminator.

    Only the body's projected bounding box is traced: it covers a few percent of the
    frame, and a miss costs a raised SPICE error plus a reset, so tracing the full grid
    spends most of its time proving that space is empty.

    Still the expensive path, and it scales with how much of the frame the body fills.
    Measured at 1020x1020: ~50 s a frame on the 2027-02-25 window, where the body covers
    8 % of the frame (~84k rays hit). It does not parallelise across processes -- CSPICE
    reads the DSK in 1 KB records and the per-read overhead dominates.
    """
    import numpy as np
    import spiceypy as sp
    et = sp.str2et(utc)
    half = np.tan(np.radians(fov_deg / 2.0))
    img = np.zeros((size, size), np.float32)
    hit = np.zeros((size, size), bool)
    mu0a = np.zeros((size, size), np.float32)
    mua = np.zeros((size, size), np.float32)
    lita = np.zeros((size, size), bool)
    turn = lambda z: np.rot90(z, 1)
    out = lambda: {"img": turn(img), "hit": turn(hit), "mu0": turn(mu0a),
                   "mu": turn(mua), "lit": turn(lita)}

    pos, _ = sp.spkpos(target, et, "J2000", "NONE", observer)
    dist = float(np.linalg.norm(pos)) * 1000.0
    radius = float(max(sp.bodvrd(target, "RADII", 3)[1])) * 1000.0
    v = sp.pxform("J2000", instrument, et) @ (pos / np.linalg.norm(pos))
    if v[2] <= 0.0:
        return out()                              # body behind the camera
    cx, cy = v[0] / v[2], v[1] / v[2]         # tangent-plane centre
    ang = (radius / dist) * 1.35              # angular radius plus margin
    px = lambda t: (t / half + 1.0) * 0.5 * size - 0.5
    i0, i1 = int(np.floor(px(cx - ang))), int(np.ceil(px(cx + ang)))
    j0, j1 = int(np.floor(px(cy - ang))), int(np.ceil(px(cy + ang)))
    i0, j0 = max(0, i0), max(0, j0)
    i1, j1 = min(size - 1, i1), min(size - 1, j1)

    for j in range(j0, j1 + 1):
        y = (2.0 * (j + 0.5) / size - 1.0) * half      # +Y is image DOWN, per the IK
        for i in range(i0, i1 + 1):
            x = (2.0 * (i + 0.5) / size - 1.0) * half
            try:
                spoint = sp.sincpt("DSK/UNPRIORITIZED", target, et, frame,
                                   "NONE", observer, instrument, [x, y, 1.0])[0]
            except Exception:
                sp.reset(); continue
            hit[j, i] = True
            try:
                f = sp.illumf("DSK/UNPRIORITIZED", target, "SUN", et, frame,
                              "NONE", observer, spoint)
            except Exception:
                sp.reset(); continue
            mu0, mu = np.cos(f[3]), np.cos(f[4])
            mu0a[j, i], mua[j, i], lita[j, i] = mu0, mu, bool(f[6])
            if mu0 > 0.0 and mu > 0.0 and f[6]:        # f[6] = lit: DSK self-shadowing
                img[j, i] = 2.0 * mu0 / (mu0 + mu)     # Lommel-Seeliger
    # into the delivered orientation, so a comparison against the tool's frames measures
    # the geometry and not the convention
    return out()


def dsk_render(utc, instrument, target, observer, frame, size, fov_deg):
    """The radiance and the silhouette alone -- `dsk_illumination` without the terms.

    THE one ray-cast implementation is the function above; this is the two-tuple its
    original callers take, kept so a comparison and an illumination check cannot end up
    tracing the body two slightly different ways.
    """
    r = dsk_illumination(utc, instrument, target, observer, frame, size, fov_deg)
    return r["img"], r["hit"]


# ---------------------------------------------------------------------------------
# Validating a rendered series against the independent SPICE reference.
#
# THE ONE implementation. It lives here, not in check-series.py, because validation is a
# stage of producing a series rather than a thing to remember to run afterwards: the
# generator calls it before it writes series.json, and check-series.py calls the same
# function to re-check a folder later. Two copies would be the same failure this whole
# area already produced once -- a second path that agrees with the first until one of them
# changes, with nothing in the output saying which you ran.
#
# The question it answers: does the tool's frame agree with a ray-cast of SPICE's own DSK
# about which way the detector axes point? Anything but `identity` means it does not.

TRANSFORMS = {
    "identity":       lambda z: z,
    "rot90":          lambda z: np.rot90(z, 1),
    "rot180":         lambda z: np.rot90(z, 2),
    "rot270":         lambda z: np.rot90(z, 3),
    "flipLR":         lambda z: z[:, ::-1],
    "flipUD":         lambda z: z[::-1, :],
    "transpose":      lambda z: z.T,
    "anti-transpose": lambda z: np.rot90(z, 2).T,
}


def _crop(a, size, thr):
    """The body, centred on its own centroid.

    Correlating whole frames collapses to noise when a small field-of-view difference
    shifts the body a pixel or two, and a spurious transform then wins. Thresholding well
    above the ambient floor first is what makes the two centroids comparable at all: our
    frames carry an ambient-lit night side, the reference writes 0 where unlit.
    """
    m = a > thr
    if m.sum() < 200:
        return None
    ys, xs = np.nonzero(m)
    cx, cy, h = int(xs.mean()), int(ys.mean()), size // 2
    if cx - h < 0 or cy - h < 0 or cx + h > a.shape[1] or cy + h > a.shape[0]:
        return None
    return a[cy - h:cy + h, cx - h:cx + h].astype(float)


def _corr(x, y):
    m = (x > 0) | (y > 0)
    if m.sum() < 200:
        return 0.0
    x, y = x[m] - x[m].mean(), y[m] - y[m].mean()
    d = np.sqrt((x * x).sum() * (y * y).sum())
    return float((x * y).sum() / d) if d > 0 else 0.0


def validate_series(series_dir, manifest, size=340, threshold=25,
                    min_correlation=0.5, margin=0.05):
    """Check every epoch that has both a tool frame and a `spice/` reference.

    Returns a dict, which is what goes into series.json verbatim -- the folder then
    carries its own verdict instead of it living in a terminal that is gone by the time
    anyone asks. `verdict` is one of:

        pass      every pair aligns best under identity
        fail      at least one pair aligns better under something else by > margin
        skipped   no reference frames to compare against

    `margin` exists because Dimorphos is near-symmetric about some viewing axes, so on a
    CORRECT frame flipUD can edge out identity. It is calibrated from the two populations
    rather than guessed: measured over both series, identity beats the runner-up by 0.12
    to 0.73 wherever the view discriminates at all, and flipUD edges ahead by at most
    0.023 where it does not. The default 0.05 lies in the empty band between them. An
    earlier 0.02 was set when only a 0.005-0.009 artefact had been seen, and a longer
    series then produced a 0.023 one -- a threshold is only as good as the range of data
    it was calibrated on. `min_correlation` only marks a pair weak, never failed -- a low-phase epoch is
    a flat featureless disk with nothing to correlate, which is inconclusive, not wrong.

    A failing pair also records `selfSymmetry`: how well the frame matches its own winning
    transform. On a near-symmetric view that sits close to the identity correlation, which
    is what tells "the axes are wrong" apart from "this view cannot tell up from down".
    """
    from PIL import Image

    variants = [v for v in manifest.get("variants", {}) if v != "spice"]
    if "spice" not in manifest.get("variants", {}):
        return {"verdict": "skipped", "reason": "no spice/ reference in this series",
                "checked": 0}

    tally, weak, bad, checked = {}, [], [], 0
    for ep in manifest["epochs"]:
        files = ep.get("files", {})
        if "spice" not in files:
            continue
        ref = _crop(np.asarray(Image.open(os.path.join(series_dir, files["spice"])).convert("L")),
                    size, threshold)
        if ref is None:
            continue
        for v in variants:
            if v not in files:
                continue
            img = _crop(np.asarray(Image.open(os.path.join(series_dir, files[v])).convert("L")),
                        size, threshold)
            if img is None:
                continue
            checked += 1
            scores = sorted(((_corr(img, f(ref)), k) for k, f in TRANSFORMS.items()), reverse=True)
            best, name = scores[0]
            ident = _corr(img, ref)
            if name != "identity" and best - ident > margin:
                bad.append({"time": ep["time"], "variant": v, "best": name,
                            "bestCorrelation": round(best, 4),
                            "identityCorrelation": round(ident, 4),
                            "selfSymmetry": round(_corr(img, TRANSFORMS[name](img)), 4)})
                tally[name] = tally.get(name, 0) + 1
            elif name != "identity":
                tally["identity (tie)"] = tally.get("identity (tie)", 0) + 1
            else:
                tally["identity"] = tally.get("identity", 0) + 1
                if best < min_correlation:
                    weak.append({"time": ep["time"], "variant": v,
                                 "correlation": round(best, 4)})

    return {
        "verdict": "fail" if bad else "pass",
        "what": "each tool frame against the SPICE DSK ray-cast of the same epoch, over "
                "the eight dihedral transforms; the best must be identity",
        "checked": checked,
        "tally": tally,
        "settings": {"cropPx": size, "thresholdDn": threshold,
                     "minCorrelation": min_correlation, "margin": margin},
        "failed": bad,
        "weak": weak,
    }


def print_validation(v):
    """The same result, for a terminal."""
    if v["verdict"] == "skipped":
        print("validation SKIPPED: %s" % v.get("reason", ""))
        return
    print("checked %d frame pair(s) against the SPICE reference" % v["checked"])
    for k in sorted(v["tally"], key=lambda k: -v["tally"][k]):
        print("   best transform %-16s %4d" % (k, v["tally"][k]))
    if v["failed"]:
        print("\nFAILED: %d pair(s) align better under something other than identity"
              % len(v["failed"]))
        for f in v["failed"][:10]:
            print("   %s %-7s best=%-14s %+.3f   identity %+.3f   (matches its own %s "
                  "at %+.3f)"
                  % (f["time"], f["variant"], f["best"], f["bestCorrelation"],
                     f["identityCorrelation"], f["best"], f.get("selfSymmetry", 0.0)))
        if len(v["failed"]) > 10:
            print("   ... and %d more" % (len(v["failed"]) - 10))
        return
    if v["weak"]:
        print("\n%d pair(s) align under identity but weakly (< %.2f) -- usually a low-phase"
              % (len(v["weak"]), v["settings"]["minCorrelation"]))
        print("epoch where the disk is flat and there is little structure to match:")
        for w in v["weak"][:5]:
            print("   %s %-7s %+.3f" % (w["time"], w["variant"], w["correlation"]))
    print("\nPASS: every pair aligns best under identity (%d weak of %d)"
          % (len(v["weak"]), v["checked"]))


# ---------------------------------------------------------------------------------
# Comparing a rendered frame against another renderer's.
#
# Silhouette overlap, not correlation of grey levels: two renderers using different shape
# models disagree pixel by pixel whatever the orientation, so the grey-level correlation
# says nothing (measured: all eight dihedral transforms negative on frames that overlap at
# IoU 0.9). The outline is what carries the geometry.
#
# Compare like with like. A renderer that shades has a terminator and its mask is the LIT
# region; one that does not light its output has no dark side and its mask is the whole
# body. Comparing our crescent against a full disk measures nothing.

def largest_blob(mask):
    """The biggest connected component, so a cursor or a UI element cannot join the body."""
    from collections import deque
    if not mask.any():
        return mask
    lab = np.zeros(mask.shape, np.int32)
    best, best_id, cur = 0, 0, 0
    for sy in range(mask.shape[0]):
        for sx in range(mask.shape[1]):
            if mask[sy, sx] and lab[sy, sx] == 0:
                cur += 1
                n = 0
                q = deque([(sy, sx)])
                lab[sy, sx] = cur
                while q:
                    y, x = q.popleft()
                    n += 1
                    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        ny, nx = y + dy, x + dx
                        if 0 <= ny < mask.shape[0] and 0 <= nx < mask.shape[1] \
                           and mask[ny, nx] and lab[ny, nx] == 0:
                            lab[ny, nx] = cur
                            q.append((ny, nx))
                if n > best:
                    best, best_id = n, cur
    return lab == best_id


def body_mask(image, threshold=25):
    return largest_blob(image > threshold)


def normalised(mask, size=220):
    """Same centre and scale, so what is left is orientation and shape."""
    from PIL import Image
    ys, xs = np.nonzero(mask)
    r = int(0.62 * max(np.ptp(ys), np.ptp(xs)))
    cy, cx = int(ys.mean()), int(xs.mean())
    pad = np.zeros((2 * r, 2 * r), np.uint8)
    y0, y1 = max(0, cy - r), min(mask.shape[0], cy + r)
    x0, x1 = max(0, cx - r), min(mask.shape[1], cx + r)
    pad[(y0 - (cy - r)):(y0 - (cy - r)) + (y1 - y0),
        (x0 - (cx - r)):(x0 - (cx - r)) + (x1 - x0)] = mask[y0:y1, x0:x1]
    return np.asarray(Image.fromarray(pad * 255).resize((size, size), Image.BILINEAR)) > 127


def iou(a, b):
    return float((a & b).sum()) / max(1, (a | b).sum())


def screenshot_panel(path, dark=40):
    """The render panel out of a browser screenshot: the big near-black rectangle."""
    from PIL import Image
    a = np.asarray(Image.open(path).convert("L")).astype(float)
    rows = np.where(np.median(a, axis=1) < dark)[0]
    cols = np.where(np.median(a, axis=0) < dark)[0]
    if rows.size == 0 or cols.size == 0:
        return a
    return a[rows.min():rows.max(), cols.min():cols.max()]
