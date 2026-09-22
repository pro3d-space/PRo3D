#!/usr/bin/env python3
"""The shape-model comparison matrix behind ShapeModelCrosscheck.md.

    python scripts/make-shape-crosscheck-figures.py --obj <shape.obj.gz>

One row per epoch, five panels:

    OPC | OBJ | SPICE DSK | OPC outline vs DSK | OBJ outline vs DSK

The last two are the point. The OPC's outline against a ray-cast of the kernels' own DSK
leaves a visible red/green fringe; the OBJ's -- the same mesh that DSK was built from,
through a completely different renderer -- leaves none, because the two agree at IoU 1.000.
A table of numbers cannot show "there is nothing to see here"; a picture can.

Everything is rendered fresh, so the figure cannot drift from the tool:

    the two lit panels      `simulate-series --variants smooth`, one run per shape model
    the two outline panels  the same, with --no-lighting, against the DSK's hit mask
    the DSK panel           `pro3d_sim.dsk_render`, the spiceypy ray-cast -- no PRo3D
                            code involved in finding the surface

The ray-cast is the slow part: ~50 s an epoch, and it is most of the runtime. The renders
take about a second each.

Panels of one row share ONE crop box, computed from the union of the masks, so the
alignment on screen is the real alignment -- nothing here is re-centred. (The IoU printed
on the overlays is `pro3d_sim.iou` over `normalised` masks, i.e. the same number
check-renderers.py reports, which IS centroid-aligned. On these epochs the two agree to
three decimals; where they would not, the picture is the honest one.)

Needs numpy, pillow, matplotlib and spiceypy.
"""

import argparse
import datetime
import json
import os
import subprocess
import sys
import tempfile

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pro3d_sim import body_mask, dsk_render, iou, normalised, run_tool, tool_error

# The epochs the renderer comparison is measured on (2027-02-25, close orbit), chosen
# because comet-toolbox reference screenshots exist for them -- see check-renderers.py.
DEFAULT_EPOCHS = ["0745", "0900", "1030", "1445"]
DEFAULT_DATE = "20270225"


def edge(mask):
    """The one-pixel outline of a mask, by shifts alone -- no scipy, no skimage."""
    e = np.zeros_like(mask)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        s = np.roll(mask, (dy, dx), (0, 1))
        # a rolled edge wraps; clear the row/column it wrapped into
        if dy == 1:
            s[0, :] = False
        elif dy == -1:
            s[-1, :] = False
        elif dx == 1:
            s[:, 0] = False
        else:
            s[:, -1] = False
        e |= mask & ~s
    return e


def render(repo, common, epochs, out, extra):
    """One simulate-series run: the lit or unlit `smooth` frames of every epoch."""
    fd, times = tempfile.mkstemp(prefix="pro3d-fig-", suffix=".txt", text=True)
    os.close(fd)
    with open(times, "w", encoding="utf-8") as f:
        f.write("# written by %s\n" % os.path.basename(__file__))
        for e in epochs:
            f.write(e + "\n")
    try:
        p = run_tool(repo, ["simulate-series"] + common + [
            "--times-file", times, "--out", out,
            "--variants", "smooth", "--gain", "4.492", "--micro-amplitude", "0",
        ] + extra, quiet=True)
        if p.returncode != 0:
            raise SystemExit("render failed: %s" % tool_error(p))
    finally:
        try:
            os.remove(times)
        except OSError:
            pass
    return os.path.join(out, "smooth")


def frame(folder, date, hhmm):
    from PIL import Image
    p = os.path.join(folder, "AFC1_SMOOTH_%s_%s00.png" % (date, hhmm))
    if not os.path.exists(p):
        raise SystemExit("missing render: %s" % p)
    return np.asarray(Image.open(p).convert("L")).astype(float)


def main():
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    testdata = os.environ.get("PRO3D_TEST_DATA")

    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--opc", default=(os.path.join(testdata, "HERA", "Dimorphos_opc", "Dimorphos")
                                      if testdata else None),
                    help="Dimorphos OPC (default: $PRO3D_TEST_DATA/HERA/Dimorphos_opc/Dimorphos)")
    ap.add_argument("--obj", default=(os.path.join(testdata, "HERA", "Dimorphos_dsk",
                                                   "g_00243mm_spc_obj_dimo_0000n00000_v004.obj.gz")
                                      if testdata else None),
                    help="the kernels' Dimorphos mesh (default: $PRO3D_TEST_DATA/HERA/Dimorphos_dsk/...)")
    ap.add_argument("--out", default=os.path.join(repo, "docs", "images", "shapeCrosscheck"),
                    help="where the figure goes (default: docs/images/shapeCrosscheck)")
    ap.add_argument("--name", default="objMatrix",
                    help="basename of the figure and its metrics (default objMatrix)")
    ap.add_argument("--work", default=None,
                    help="scratch directory for the renders; a temp dir by default")
    ap.add_argument("--date", default=DEFAULT_DATE)
    ap.add_argument("--epochs", default=",".join(DEFAULT_EPOCHS),
                    help="comma-separated HHMM (default %s)" % ",".join(DEFAULT_EPOCHS))
    ap.add_argument("--kernel", default=None, help="explicit metakernel")
    ap.add_argument("--kernel-root", default=None, help="defaults to $PRO3D_SPICE_KERNELS")
    a = ap.parse_args()

    if not a.opc or not a.obj:
        print("pass --opc and --obj (or set PRO3D_TEST_DATA)")
        return 2
    for p in (a.opc, a.obj):
        if not os.path.exists(p):
            print("not found: %s" % p)
            return 2

    hhmms = [e.strip() for e in a.epochs.split(",") if e.strip()]
    d = a.date
    epochs = ["%s-%s-%sT%s:%s:00Z" % (d[:4], d[4:6], d[6:8], h[:2], h[2:4]) for h in hhmms]

    body = ["--body", "DIMORPHOS", "--frame", "DIMORPHOS_FIXED",
            "--observer", "HERA", "--instrument", "HERA_AFC-1"]
    if a.kernel:
        body += ["--kernel", a.kernel]
    if a.kernel_root:
        body += ["--kernel-root", a.kernel_root]

    work = a.work or tempfile.mkdtemp(prefix="pro3d-crosscheck-")
    os.makedirs(work, exist_ok=True)
    print("renders -> %s" % work)

    sets = {}
    for tag, shape in (("opc", ["--opc", a.opc]), ("obj", ["--obj", a.obj])):
        for lit, extra in (("lit", []), ("sil", ["--no-lighting"])):
            print("rendering %s/%s ..." % (tag, lit))
            sets[(tag, lit)] = render(repo, shape + body, epochs,
                                      os.path.join(work, "%s-%s" % (tag, lit)), extra)

    # The independent reference. The metakernel's PATH_VALUES is relative to its own
    # directory, so the process has to stay there once furnsh'd.
    import spiceypy as sp
    kernel = a.kernel or os.path.join(
        a.kernel_root or os.environ.get("PRO3D_SPICE_KERNELS", ""), "kernels", "mk", "hera_plan.tm")
    if not os.path.exists(kernel):
        print("metakernel not found: %s" % kernel)
        return 2
    cwd = os.getcwd()
    os.chdir(os.path.dirname(os.path.abspath(kernel)))
    sp.furnsh(os.path.basename(kernel))

    # The ray-cast is ~50 s an epoch and depends only on (epoch, kernels) -- never on
    # anything this script lays out. Caching it turns "fix the label placement" from a
    # four-minute round trip into a two-second one.
    cache_dir = os.path.join(work, "dsk-cache")
    os.makedirs(cache_dir, exist_ok=True)

    rows, metrics = [], []
    for hhmm, utc in zip(hhmms, epochs):
        cached = os.path.join(cache_dir, "%s.npz" % utc.replace(":", "").replace("-", ""))
        if os.path.exists(cached):
            z = np.load(cached)
            dsk, hit = z["img"], z["hit"]
            print("ray-cast at %s: cached" % utc)
        else:
            print("ray-casting the DSK at %s (~50 s) ..." % utc)
            dsk, hit = dsk_render(utc, "HERA_AFC-1", "DIMORPHOS", "HERA", "DIMORPHOS_FIXED", 1020, 5.50)
            np.savez_compressed(cached, img=dsk, hit=hit)
        if not hit.any():
            print("   DIMORPHOS is not in the frame at %s -- skipped" % utc)
            continue

        panels = {t: frame(sets[(t, "lit")], d, hhmm) for t in ("opc", "obj")}
        masks = {t: body_mask(frame(sets[(t, "sil")], d, hhmm), 25) for t in ("opc", "obj")}
        masks["dsk"] = body_mask(hit.astype(float), 0.5)

        rows.append({"hhmm": hhmm, "utc": utc, "dsk": dsk, "panels": panels, "masks": masks})
        metrics.append({
            "utc": utc,
            "opcVsDskSilhouette": round(iou(normalised(masks["opc"]), normalised(masks["dsk"])), 4),
            "objVsDskSilhouette": round(iou(normalised(masks["obj"]), normalised(masks["dsk"])), 4),
            "opcVsDskRaw": round(iou(masks["opc"], masks["dsk"]), 4),
            "objVsDskRaw": round(iou(masks["obj"], masks["dsk"]), 4),
            "dskPixels": int(masks["dsk"].sum()),
            "opcPixels": int(masks["opc"].sum()),
            "objPixels": int(masks["obj"].sum()),
        })
    os.chdir(cwd)

    if not rows:
        print("no epoch produced a frame")
        return 1

    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    titles = ["OPC  (1.96 m posts)", "OBJ  (0.243 m facets)", "SPICE DSK ray-cast",
              "OPC vs DSK", "OBJ vs DSK"]
    fig, axes = plt.subplots(len(rows), 5, figsize=(16, 3.3 * len(rows)),
                             facecolor="black", squeeze=False)
    for r, row in enumerate(rows):
        # ONE crop for the whole row, from the union of the masks: what the panels show
        # is then the real alignment, not a re-centring of it
        union = row["masks"]["opc"] | row["masks"]["obj"] | row["masks"]["dsk"]
        ys, xs = np.nonzero(union)
        pad = 18
        y0, y1 = max(0, ys.min() - pad), min(union.shape[0], ys.max() + pad)
        x0, x1 = max(0, xs.min() - pad), min(union.shape[1], xs.max() + pad)
        cut = lambda z: z[y0:y1, x0:x1]

        dsk = cut(row["dsk"])
        dsk = dsk / dsk.max() if dsk.max() > 0 else dsk
        images = [cut(row["panels"]["opc"]) / 255.0, cut(row["panels"]["obj"]) / 255.0, dsk]

        for c in range(5):
            ax = axes[r][c]
            ax.set_facecolor("black")
            ax.set_xticks([]); ax.set_yticks([])
            for s in ax.spines.values():
                s.set_visible(False)
            if c < 3:
                ax.imshow(images[c], cmap="gray", vmin=0.0, vmax=1.0, interpolation="nearest")
            else:
                ours = "opc" if c == 3 else "obj"
                rgb = np.zeros(dsk.shape + (3,), np.float32)
                rgb[..., 0] = edge(cut(row["masks"][ours]))     # red: ours
                rgb[..., 1] = edge(cut(row["masks"]["dsk"]))    # green: the ray-cast
                ax.imshow(rgb, interpolation="nearest")
                m = metrics[r]["opcVsDskSilhouette" if c == 3 else "objVsDskSilhouette"]
                # below the axes, not inside them: on a body that fills the panel the
                # label lands on the very outline it is describing
                ax.set_xlabel("IoU %.3f" % m, color="0.85", fontsize=12, labelpad=6)
            if r == 0:
                ax.set_title(titles[c], color="0.75", fontsize=11, pad=10)
            if c == 0:
                ax.set_ylabel("%s:%s UTC" % (row["hhmm"][:2], row["hhmm"][2:]),
                              color="0.75", fontsize=11)

    fig.suptitle("Dimorphos, %s-%s-%s -- red: PRo3D, green: SPICE DSK ray-cast"
                 % (d[:4], d[4:6], d[6:8]), color="0.75", fontsize=12)
    fig.tight_layout(rect=(0, 0, 1, 0.97))
    os.makedirs(a.out, exist_ok=True)
    png = os.path.join(a.out, a.name + ".png")
    fig.savefig(png, facecolor="black", dpi=100)
    print("wrote %s" % png)

    js = os.path.join(a.out, a.name + ".json")
    with open(js, "w", encoding="utf-8") as f:
        json.dump({
            "generated": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "generator": "scripts/make-shape-crosscheck-figures.py",
            "kernel": os.path.abspath(kernel),
            "opc": os.path.abspath(a.opc),
            "obj": os.path.abspath(a.obj),
            "what": "silhouette IoU of each PRo3D render against a spiceypy ray-cast of the "
                    "kernels' DSK; 'Raw' is the same without centroid alignment",
            "epochs": metrics,
        }, f, indent=2)
    print("wrote %s" % js)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
