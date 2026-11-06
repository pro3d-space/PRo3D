#!/usr/bin/env python3
"""Compare `pro3d-tool render-view` with comet-toolbox screenshots, for both pointings.

    python scripts/compare-render-view-toolbox.py --opc <Dimorphos OPC> \
        [--didymos-opc <Didymos OPC>] --kernel <metakernel> \
        [--refs $PRO3D_TEST_DATA/HERA/comet-toolbox] [--out compare] [--raycast]

The reference frames are comet-toolbox screenshots (https://comet-toolbox.com/dashboard/hera/),
kept in PRo3D.Resources.TestData under HERA/comet-toolbox with their attribution. For every
`YYYYMMDDTHHMM.png` in --refs, the epoch is rendered twice with
render-view, at the reference's size, untextured and sun-lit:

    instrument   camera = the HERA_AFC-1 frame (render-view-job.py --pointing instrument)
    spacecraft   camera = HERA_SPACECRAFT, nominal mounting (--pointing spacecraft)

Each render is compared with the reference by its lit region (pixels above DN 25): overlap
(IoU) and the distance between the two regions' centroids. Nothing is re-centred or scaled.

--raycast also checks the instrument-pointing geometry against a spiceypy ray-cast of the
kernels' own DSK, independent of PRo3D: unlit Dimorphos-only silhouettes, IoU and centroid
offset. It is slow (tens of seconds per epoch).

Writes <out>/results.json, <out>/pointing.png (one row per epoch, or --figure-epochs) and
prints a Markdown table. See docs/Pro3DTool-RenderView.md, "Pointing".
"""

import argparse
import json
import os
import re
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JOB_SCRIPT = os.path.join(REPO, "scripts", "render-view-job.py")
POINTINGS = ("instrument", "spacecraft")
THRESHOLD = 25


def tool_command(tool):
    exe = tool or os.path.join(REPO, "bin", "Release", "net9.0", "PRo3D.Tool.exe")
    if os.path.exists(exe):
        return [exe]
    return ["dotnet", "run", "--project", os.path.join(REPO, "src", "PRo3D.Tool", "PRo3D.Tool.fsproj"), "--"]


def run(cmd):
    env = dict(os.environ)
    # a VS x86 developer prompt's bitness setting breaks dotnet (see AGENTS.md)
    env.pop("__DOTNET_PREFERRED_BITNESS", None)
    p = subprocess.run(cmd, capture_output=True, text=True, env=env)
    if p.returncode != 0:
        sys.exit("failed: %s\n%s%s" % (" ".join(cmd), p.stdout[-2000:], p.stderr[-2000:]))


def epochs(refs):
    out = []
    for f in sorted(os.listdir(refs)):
        m = re.fullmatch(r"(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})\.png", f)
        if m:
            y, mo, d, h, mi = m.groups()
            out.append((f[:-4], "%s-%s-%sT%s:%s:00" % (y, mo, d, h, mi), os.path.join(refs, f)))
    return out


def render(a, tool, stamp, utc, size, pointing, out, didymos, sun):
    job = os.path.join(out, "jobs", "%s_%s%s.json" % (stamp, pointing, "" if sun else "_unlit"))
    os.makedirs(os.path.dirname(job), exist_ok=True)
    cmd = [sys.executable, JOB_SCRIPT, "--opc", a.opc, "--times", utc, "--out", job,
           "--width", str(size), "--height", str(size), "--untextured", "--pointing", pointing,
           "--kernel", a.kernel]
    if a.kernel_root:
        cmd += ["--kernel-root", a.kernel_root]
    if didymos:
        cmd += ["--didymos-opc", didymos]
    if not sun:
        cmd += ["--no-sun"]
    run(cmd)
    folder = os.path.join(out, "renders", "%s%s" % (pointing, "" if sun else "_unlit"))
    run(tool + ["render-view", "--job", job, "--out", folder])
    name = "afc1_%s" % utc.replace("-", "").replace(":", "")
    return gray(os.path.join(folder, name + ".png"))


def gray(path):
    return np.asarray(Image.open(path).convert("L")).astype(float)


def iou(a, b):
    return float((a & b).sum()) / max(1, int((a | b).sum()))


def centroid(m):
    ys, xs = np.nonzero(m)
    return np.array([xs.mean(), ys.mean()]) if xs.size else None


def offset(a, b):
    ca, cb = centroid(a), centroid(b)
    return None if ca is None or cb is None else float(np.linalg.norm(ca - cb))


def dsk_silhouette(utc, size, fov_deg):
    """Which pixels of HERA_AFC-1 hit Dimorphos' DSK: sincpt per pixel over the body's
    projected bounding box (the same ray-cast as pro3d_sim.dsk_render on features/801)."""
    import spiceypy as sp
    et = sp.str2et(utc)
    half = np.tan(np.radians(fov_deg / 2.0))
    hit = np.zeros((size, size), bool)
    pos, _ = sp.spkpos("DIMORPHOS", et, "J2000", "NONE", "HERA")
    dist = float(np.linalg.norm(pos))
    radius = float(max(sp.bodvrd("DIMORPHOS", "RADII", 3)[1]))
    v = sp.pxform("J2000", "HERA_AFC-1", et) @ (pos / dist)
    if v[2] <= 0.0:
        return np.rot90(hit, 1)
    cx, cy = v[0] / v[2], v[1] / v[2]
    ang = (radius / dist) * 1.35
    px = lambda t: (t / half + 1.0) * 0.5 * size - 0.5
    i0, i1 = max(0, int(np.floor(px(cx - ang)))), min(size - 1, int(np.ceil(px(cx + ang))))
    j0, j1 = max(0, int(np.floor(px(cy - ang)))), min(size - 1, int(np.ceil(px(cy + ang))))
    for j in range(j0, j1 + 1):
        y = (2.0 * (j + 0.5) / size - 1.0) * half
        for i in range(i0, i1 + 1):
            x = (2.0 * (i + 0.5) / size - 1.0) * half
            try:
                sp.sincpt("DSK/UNPRIORITIZED", "DIMORPHOS", et, "DIMORPHOS_FIXED", "NONE",
                          "HERA", "HERA_AFC-1", [x, y, 1.0])
                hit[j, i] = True
            except Exception:
                sp.reset()
    # instrument (x, y) -> image (row, column): image up = +X, right = +Y
    return np.rot90(hit, 1)


def outline(m):
    e = m & ~(np.roll(m, 1, 0) & np.roll(m, -1, 0) & np.roll(m, 1, 1) & np.roll(m, -1, 1))
    return e | np.roll(e, 1, 0) | np.roll(e, 1, 1)


def tile(base, masks, scale):
    im = np.stack([np.clip(base, 0, 255).astype(np.uint8)] * 3, -1)
    for m, colour in masks:
        im[outline(m)] = colour
    n = int(round(im.shape[0] * scale))
    t = Image.fromarray(im).resize((n, n), Image.NEAREST)
    d = ImageDraw.Draw(t)
    c = n / 2
    d.line([(c - 10, c), (c + 10, c)], fill=(255, 255, 255))
    d.line([(c, c - 10), (c, c + 10)], fill=(255, 255, 255))
    d.rectangle([0, 0, n - 1, n - 1], outline=(70, 70, 70))
    return t


def figure(rows, path, scale):
    yellow, magenta, cyan = (255, 210, 60), (255, 60, 200), (0, 255, 255)
    n = int(round(rows[0]["ref"].shape[0] * scale))
    gap, top, foot = 16, 46, 24
    w = 3 * n + 4 * gap
    h = top + len(rows) * (n + foot + gap) + 40
    out = Image.new("RGB", (w, h), (18, 18, 22))
    d = ImageDraw.Draw(out)
    heads = ["comet-toolbox", "render-view, HERA_AFC-1", "render-view, HERA_SPACECRAFT"]
    for i, t in enumerate(heads):
        d.text((gap + i * (n + gap), 16), t, fill=(230, 230, 230))
    y = top
    for r in rows:
        ref = r["ref"] > THRESHOLD
        out.paste(tile(r["ref"], [(ref, yellow)], scale), (gap, y))
        for k, (p, colour) in enumerate((("instrument", magenta), ("spacecraft", cyan))):
            img = r[p]
            out.paste(tile(img, [(img > THRESHOLD, colour), (ref, yellow)], scale), (gap + (k + 1) * (n + gap), y))
            d.text((gap + (k + 1) * (n + gap), y + n + 4), "IoU %.2f, centroids %.1f px apart" % (
                r["result"][p]["iou"], r["result"][p]["centroidOffsetPx"]), fill=colour)
        d.text((gap, y + n + 4), r["utc"].replace("T", " ") + " UTC", fill=(200, 200, 200))
        y += n + foot + gap
    d.text((gap, y), "yellow: comet-toolbox lit region (on every panel); magenta / cyan: render-view lit region; cross: image centre",
           fill=(200, 200, 200))
    out.save(path)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--opc", required=True, help="Dimorphos OPC directory")
    ap.add_argument("--didymos-opc", help="Didymos OPC directory: also render Didymos (needed where it is in the frame)")
    ap.add_argument("--refs", help="reference frames (default: $PRO3D_TEST_DATA/HERA/comet-toolbox)")
    ap.add_argument("--kernel", required=True, help="metakernel the references were made with")
    ap.add_argument("--kernel-root")
    ap.add_argument("--tool", help="PRo3D.Tool executable (default: bin/Release/net9.0, else dotnet run)")
    ap.add_argument("--out", default="compare-render-view-toolbox")
    ap.add_argument("--raycast", action="store_true", help="also check the geometry against a DSK ray-cast")
    ap.add_argument("--figure-epochs", help="comma-separated YYYYMMDDTHHMM stamps for the figure (default: all)")
    ap.add_argument("--figure-scale", type=float, default=1.0)
    a = ap.parse_args()

    a.opc = os.path.abspath(a.opc)
    a.kernel = os.path.abspath(a.kernel)
    if not a.refs:
        data = os.environ.get("PRO3D_TEST_DATA")
        if not data:
            sys.exit("no reference frames: pass --refs or set PRO3D_TEST_DATA")
        a.refs = os.path.join(data, "HERA", "comet-toolbox")
    a.refs = os.path.abspath(a.refs)
    if a.kernel_root:
        a.kernel_root = os.path.abspath(a.kernel_root)
    didymos = os.path.abspath(a.didymos_opc) if a.didymos_opc else None
    out = os.path.abspath(a.out)
    os.makedirs(out, exist_ok=True)
    tool = tool_command(a.tool)

    found = epochs(a.refs)
    if not found:
        sys.exit("no YYYYMMDDTHHMM.png references in %s" % a.refs)

    if a.raycast:
        import spiceypy as sp
        # PATH_VALUES are relative to the metakernel, and CSPICE reopens kernels by those
        # paths later: stay there (every other path here is absolute)
        os.chdir(os.path.dirname(a.kernel))
        sp.furnsh(os.path.basename(a.kernel))

    rows, results = [], []
    for stamp, utc, path in found:
        ref = gray(path)
        size = ref.shape[1]
        if ref.shape[0] != size:
            sys.exit("%s is not square" % path)
        refMask = ref > THRESHOLD
        row = {"utc": utc, "ref": ref, "result": {"epoch": utc}}
        for p in POINTINGS:
            img = render(a, tool, stamp, utc, size, p, out, didymos, True)
            m = img > THRESHOLD
            row[p] = img
            row["result"][p] = {"iou": iou(refMask, m), "centroidOffsetPx": offset(refMask, m)}
        if a.raycast:
            sil = render(a, tool, stamp, utc, size, "instrument", out, None, False) > 5
            hit = dsk_silhouette(utc + "Z", size, 5.5)
            row["result"]["raycast"] = {"silhouetteIou": iou(sil, hit), "centroidOffsetPx": offset(sil, hit)}
        rows.append(row)
        results.append(row["result"])
        print("%s done" % utc, flush=True)

    with open(os.path.join(out, "results.json"), "w") as f:
        json.dump({"kernel": os.path.basename(a.kernel), "threshold": THRESHOLD,
                   "didymos": bool(didymos), "epochs": results}, f, indent=2)

    print()
    print("| Epoch (UTC) | IoU, HERA_AFC-1 | IoU, HERA_SPACECRAFT | Centroid offset, AFC-1 / spacecraft |%s" % (
        " DSK silhouette IoU |" if a.raycast else ""))
    print("|---|---|---|---|%s" % ("---|" if a.raycast else ""))
    for r in results:
        line = "| %s | %.2f | %.2f | %.1f / %.1f px |" % (
            r["epoch"][:16].replace("T", " "), r["instrument"]["iou"], r["spacecraft"]["iou"],
            r["instrument"]["centroidOffsetPx"], r["spacecraft"]["centroidOffsetPx"])
        if a.raycast:
            line += " %.3f |" % r["raycast"]["silhouetteIou"]
        print(line)

    wanted = set(s.strip() for s in a.figure_epochs.split(",")) if a.figure_epochs else None
    chosen = [r for (stamp, _, _), r in zip(found, rows) if wanted is None or stamp in wanted]
    if chosen:
        figure(chosen, os.path.join(out, "pointing.png"), a.figure_scale)
        print("\nfigure: %s" % os.path.join(out, "pointing.png"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
