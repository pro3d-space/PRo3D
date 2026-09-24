#!/usr/bin/env python3
"""The eclipse light curve and ingress strip behind Pro3DTool-SimulateImage.md.

    python scripts/make-eclipse-figure.py

Dimorphos passes through Didymos' shadow for about 12 % of the close-orbit phase, and the
scene holds only the target, so nothing in it casts that shadow. `--occluder-body` gives
the occluder a sun-side depth map of its own; this renders the same window three ways and
measures what came out:

    no occluder        the bug: an eclipsed epoch rendered as full daylight
    --occluder-body    the fallback geometry, a tessellation of the body's RADII
    + --occluder-obj   the primary's real shape model

The light curve is the verification. A shadow that arrives at the right minute and reaches
the ambient floor is a different claim from a shadow that merely looks plausible, and the
second is what a screenshot gets you.

The strip shows why the real shape is worth the file: through ingress the ellipsoid and the
mesh disagree by tens of percent of the disk, because that is where the occulting limb's
SHAPE decides how much sun is left, not merely where its centre is.

Needs numpy, pillow and matplotlib -- but no spiceypy: everything here comes from the tool.
"""

import argparse
import datetime
import json
import os
import sys
import tempfile

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pro3d_sim import run_tool, tool_error

# Against hera_plan_v182_20260820 this window holds one complete event: first contact just
# after 10:35, totality 10:55 .. 11:45, last contact just after 12:05.
DEFAULT_START = "2027-02-25T10:20:00Z"
DEFAULT_COUNT = 25
DEFAULT_STEP = 5


def render(repo, common, times_file, out, extra):
    p = run_tool(repo, ["simulate-series"] + common + [
        "--times-file", times_file, "--out", out,
        "--variants", "smooth", "--gain", "4.492", "--micro-amplitude", "0",
        "--keep-going",
    ] + extra, quiet=True)
    if p.returncode != 0:
        raise SystemExit("render failed: %s" % tool_error(p))
    return os.path.join(out, "smooth")


def frames(folder):
    """{stamp: image}, over whatever the run actually produced."""
    from PIL import Image
    out = {}
    for f in sorted(os.listdir(folder)):
        if f.endswith(".png"):
            stamp = f[:-4].split("_", 2)[2]
            out[stamp] = np.asarray(Image.open(os.path.join(folder, f)).convert("L")).astype(float)
    return out


def lit_mean(a):
    """Mean DN over the body. The ambient floor keeps the night side above 0, so a fully
    eclipsed body is this floor and not nothing -- which is the point of measuring it."""
    m = a > 1
    return float(a[m].mean()) if m.any() else 0.0


def main():
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    testdata = os.environ.get("PRO3D_TEST_DATA")
    kernels = os.environ.get("PRO3D_SPICE_KERNELS", "")

    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--obj", default=(os.path.join(testdata, "HERA", "Dimorphos_dsk",
                                                   "g_00243mm_spc_obj_dimo_0000n00000_v004.obj.gz")
                                      if testdata else None),
                    help="the target's shape model (default: $PRO3D_TEST_DATA/HERA/Dimorphos_dsk/...)")
    ap.add_argument("--occluder-obj",
                    default=os.path.join(kernels, "kernels", "dsk",
                                         "g_01165mm_spc_obj_didy_0000n00000_v003.obj") if kernels else None,
                    help="Didymos' shape model (default: the kernels' own dsk/)")
    ap.add_argument("--out", default=os.path.join(repo, "docs", "images", "simulateImage"))
    ap.add_argument("--name", default="eclipse")
    ap.add_argument("--work", default=None)
    ap.add_argument("--start", default=DEFAULT_START)
    ap.add_argument("--count", type=int, default=DEFAULT_COUNT)
    ap.add_argument("--step", type=float, default=DEFAULT_STEP, help="minutes (default 5)")
    ap.add_argument("--kernel", default=None)
    ap.add_argument("--kernel-root", default=None)
    a = ap.parse_args()

    if not a.obj or not os.path.exists(a.obj):
        print("pass --obj (or set PRO3D_TEST_DATA): %s" % a.obj)
        return 2
    have_occluder_obj = a.occluder_obj and os.path.exists(a.occluder_obj)
    if not have_occluder_obj:
        print("note: no occluder mesh at %s -- the real-shape column is skipped"
              % a.occluder_obj)

    t0 = datetime.datetime.strptime(a.start, "%Y-%m-%dT%H:%M:%SZ")
    times = [t0 + datetime.timedelta(minutes=a.step * i) for i in range(a.count)]

    work = a.work or tempfile.mkdtemp(prefix="pro3d-eclipse-")
    os.makedirs(work, exist_ok=True)
    times_file = os.path.join(work, "epochs.txt")
    with open(times_file, "w", encoding="utf-8") as f:
        f.write("# eclipse window; written by %s\n" % os.path.basename(__file__))
        for t in times:
            f.write(t.strftime("%Y-%m-%dT%H:%M:%SZ") + "\n")

    common = ["--obj", a.obj, "--body", "DIMORPHOS", "--frame", "DIMORPHOS_FIXED",
              "--observer", "HERA", "--instrument", "HERA_AFC-1"]
    if a.kernel:
        common += ["--kernel", a.kernel]
    if a.kernel_root:
        common += ["--kernel-root", a.kernel_root]

    runs = [("none", [], "no occluder"),
            ("radii", ["--occluder-body", "DIDYMOS"], "occluder = reference radii")]
    if have_occluder_obj:
        runs.append(("mesh", ["--occluder-body", "DIDYMOS", "--occluder-obj", a.occluder_obj],
                     "occluder = Didymos shape model"))

    sets = {}
    for tag, extra, label in runs:
        print("rendering %s ..." % label)
        sets[tag] = frames(render(repo, common, times_file, os.path.join(work, tag), extra))

    stamps = sorted(set.intersection(*[set(v) for v in sets.values()]))
    if not stamps:
        print("no epoch rendered in every configuration")
        return 1
    hhmm = [s.split("_")[1][:4] for s in stamps]
    curves = {tag: [lit_mean(sets[tag][s]) for s in stamps] for tag in sets}

    # ONE crop for every panel, from where the body ever is across the whole window. The
    # body covers a quarter of the AFC frame here, and the strip is about the shadow's edge
    # -- which is not visible at a sixth of the panel's width. A shared box keeps the
    # panels comparable and the motion across the window real rather than re-centred.
    union = np.zeros_like(next(iter(sets["none"].values())), bool)
    for s in stamps:
        union |= sets["none"][s] > 1
    ys, xs = np.nonzero(union)
    pad = 20
    y0, y1 = max(0, ys.min() - pad), min(union.shape[0], ys.max() + pad)
    x0, x1 = max(0, xs.min() - pad), min(union.shape[1], xs.max() + pad)
    crop = lambda z: z[y0:y1, x0:x1]

    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    # the six epochs that carry the event: first contact, ingress, totality, egress
    interesting = [i for i, tag in enumerate(stamps)
                   if curves[runs[-1][0]][i] < 0.995 * curves["none"][i]]
    if interesting:
        lo, hi = max(0, interesting[0] - 1), min(len(stamps) - 1, interesting[-1] + 1)
    else:
        lo, hi = 0, len(stamps) - 1
    picks = sorted({int(round(lo + i * (hi - lo) / 5.0)) for i in range(6)})

    ncol = len(picks)
    aspect = (y1 - y0) / float(x1 - x0)
    panel = 2.6
    fig = plt.figure(figsize=(panel * ncol, 2.0 * panel * aspect + 3.4), facecolor="black")
    gs = fig.add_gridspec(3, ncol, height_ratios=[panel * aspect, panel * aspect, 3.0],
                          hspace=0.10, wspace=0.03)

    strips = [("radii", "occluder: reference radii"),
              ("mesh", "occluder: Didymos shape model")]
    for r, (tag, label) in enumerate(strips):
        if tag not in sets:
            continue
        for c, i in enumerate(picks):
            ax = fig.add_subplot(gs[r, c])
            ax.set_facecolor("black")
            ax.imshow(crop(sets[tag][stamps[i]]) / 255.0, cmap="gray", vmin=0.0, vmax=1.0)
            ax.set_xticks([]); ax.set_yticks([])
            for s in ax.spines.values():
                s.set_visible(False)
            if r == 0:
                ax.set_title("%s:%s" % (hhmm[i][:2], hhmm[i][2:]), color="0.75", fontsize=11)
            if c == 0:
                ax.set_ylabel(label, color="0.75", fontsize=10)

    ax = fig.add_subplot(gs[2, :])
    ax.set_facecolor("black")
    styles = {"none": ("0.55", "--", "no occluder -- the bug"),
              "radii": ("tab:orange", "-", "occluder: reference radii"),
              "mesh": ("tab:cyan", "-", "occluder: Didymos shape model")}
    x = np.arange(len(stamps))
    for tag, (color, ls, label) in styles.items():
        if tag in curves:
            ax.plot(x, curves[tag], ls, color=color, label=label, linewidth=2)
    for i in picks:
        ax.axvline(i, color="0.25", linewidth=0.8, zorder=0)
    ax.set_xticks(x[::2])
    ax.set_xticklabels(["%s:%s" % (h[:2], h[2:]) for h in hhmm[::2]], color="0.75", fontsize=9)
    ax.set_ylabel("mean DN over the body", color="0.75", fontsize=10)
    ax.tick_params(colors="0.75")
    for s in ax.spines.values():
        s.set_color("0.3")
    leg = ax.legend(facecolor="black", edgecolor="0.3", fontsize=10, loc="center left")
    for t in leg.get_texts():
        t.set_color("0.85")

    fig.suptitle("Dimorphos in Didymos' shadow, %s" % t0.strftime("%Y-%m-%d"),
                 color="0.8", fontsize=13)
    os.makedirs(a.out, exist_ok=True)
    png = os.path.join(a.out, a.name + ".png")
    fig.savefig(png, facecolor="black", dpi=100, bbox_inches="tight")
    print("wrote %s" % png)

    js = os.path.join(a.out, a.name + ".json")
    with open(js, "w", encoding="utf-8") as f:
        json.dump({
            "generated": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "generator": "scripts/make-eclipse-figure.py",
            "what": "mean DN over the body, same camera and same fixed gain, rendered with "
                    "no occluder / the reference radii / the primary's shape model",
            "target": os.path.abspath(a.obj),
            "occluder": os.path.abspath(a.occluder_obj) if have_occluder_obj else None,
            "epochs": [
                dict({"time": "%s-%s-%sT%s:%s:00Z" % (s[:4], s[4:6], s[6:8],
                                                      s.split("_")[1][:2], s.split("_")[1][2:4])},
                     **{tag: round(curves[tag][i], 2) for tag in curves})
                for i, s in enumerate(stamps)
            ],
        }, f, indent=2)
    print("wrote %s" % js)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
