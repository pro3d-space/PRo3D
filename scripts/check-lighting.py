#!/usr/bin/env python3
"""Check the SHADING against SPICE, on a shape model SPICE also has.

    python scripts/check-lighting.py

`check-renderers.py` compares outlines, because two renderers on different shape models
disagree pixel by pixel whatever the lighting does. Rendering from the kernels' own OBJ
removes that: the mesh and the DSK are the same body, so a per-pixel comparison becomes
meaningful and the residual is the SHADING -- the sun shadow map, the photometry, the
terminator -- rather than the geometry.

Three questions, each failing for a different reason:

    shadows     Does our cast-shadow mask agree with `illumf`'s own lit flag, over the
                facets that FACE the sun? Facets turned away from it are the terminator,
                which every renderer gets right, and including them grades a shadow map on
                something else.
    acne        Isolated shadowed pixels inside lit ground, and isolated lit pixels inside
                shadow. A depth-map bias that is too small stipples sun-grazing surfaces;
                one that is too large detaches shadows from their contact points. The
                ray-cast has no such failure mode, so its own speckle is the floor this is
                measured against.
    brightness  Pearson r and RMS of our DN against the ray-cast's Lommel-Seeliger radiance
                over the commonly-lit region. This is the photometry end to end, and it is
                only askable because the shape is shared.

Needs numpy, pillow and spiceypy. The ray-cast is ~50 s an epoch and dominates the runtime;
it is cached per epoch under --work.
"""

import argparse
import datetime
import json
import os
import sys
import tempfile

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pro3d_sim import dsk_illumination, run_tool, tool_error

# Epochs of 2027-02-25 spread across the phase angles the close orbit offers. A low-phase
# frame is a flat disk with no shadows in it and tests nothing; the last two are where the
# relief and its shadows actually appear.
DEFAULT_EPOCHS = ["0745", "0900", "1030", "1445", "1600"]
DEFAULT_DATE = "20270225"

# Floors, measured by this script on the run that set them (see --update-baseline).
# Floors are MEASUREMENTS, with the run that set them, not targets. 2027-02-25 at
# --shadow-bias 0.006 on g_00243mm_spc_obj_dimo_v004, the mesh the loaded DSK was built
# from. The worst of each over five epochs at phase 35.8-62.8 deg, with margin:
#
#   shadowIoU  0.891  wronglyDarkened  0.0040  acneSpeckle  0.0038
#   shadowLeak 0.029  brightnessR      0.9934
#
# The residual is a 2x2 PCF's soft edge against a ray-cast's hard one, and it concentrates
# at the most grazing epoch, which is where a one-pixel boundary difference is worth the
# most area. It is not acne: the picture (--figure) shows an outline, not a stipple.
FLOORS = {
    # our half-shadow region against illumf's lit flag, over sun-facing facets
    "shadowIoU": 0.85,
    # sun-facing ground the reference says is lit that we darkened anyway, as a fraction of
    # the sun-facing body -- acne, a soft shadow edge that overshoots, or both
    "wronglyDarkenedFrac": 0.007,
    # ...of which the ISOLATED ones. A shadow placed differently is a connected region; a
    # stipple at the scale of a shadow-map texel is acne and nothing else. At --shadow-bias
    # 0.0005 this reaches 0.067, i.e. seventeen times the floor, and the figure is solid red.
    "acneSpeckle": 0.0063,
    # reference-shadowed ground we left lit: peter-panning, as a fraction of the shadow.
    # At --shadow-bias 0.02 it reaches 0.24.
    "shadowLeak": 0.10,
    # our DN against the ray-cast's Lommel-Seeliger radiance, commonly-lit pixels only.
    # The residual is our 5 % Lambert admixture, which the reference does not have.
    "brightnessR": 0.99,
}


def lit_mean(a):
    m = a > 1
    return float(a[m].mean()) if m.any() else 0.0


def speckle(mask, inside):
    """Fraction of `inside` that is an isolated pixel of `mask`.

    Isolated = the pixel disagrees with at least three of its four neighbours. That is what
    acne looks like -- a stipple whose scale is one texel of the shadow map -- and it is
    what a correct shadow boundary does not look like, because a boundary is a curve and
    its pixels have neighbours on their own side.
    """
    agree = np.zeros(mask.shape, np.int8)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        s = np.roll(mask, (dy, dx), (0, 1))
        agree += (s == mask).astype(np.int8)
    isolated = inside & (agree <= 1)
    return float(isolated.sum()) / max(1, int(inside.sum()))


def write_figure(path, panels, k_label):
    """ours | ray-cast | the disagreement, one row an epoch.

    The third panel is the one worth having. Two greyscale renders of the same body look
    the same at a glance whatever the shadow map is doing, and the numbers say how much
    disagrees without saying WHERE or in what pattern -- and the pattern is the diagnosis.
    A stipple is acne; a rim along one side of every shadow is a bias error; a blob is a
    shadow genuinely placed differently.

        red    we darkened ground the ray-cast says is lit
        blue   we left lit what the ray-cast says is shadowed
        grey   the reference's shadow, for context
    """
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    # one crop over every epoch, so the panels stay comparable
    union = np.zeros(panels[0]["facing"].shape, bool)
    for p in panels:
        union |= p["ref"]["hit"]
    ys, xs = np.nonzero(union)
    pad = 12
    y0, y1 = max(0, ys.min() - pad), min(union.shape[0], ys.max() + pad)
    x0, x1 = max(0, xs.min() - pad), min(union.shape[1], xs.max() + pad)
    cut = lambda z: z[y0:y1, x0:x1]

    n = len(panels)
    fig, axes = plt.subplots(n, 3, figsize=(11.0, 3.7 * n), facecolor="black", squeeze=False)
    titles = ["PRo3D", "SPICE DSK ray-cast", "disagreement"]
    for r, p in enumerate(panels):
        refimg = cut(p["ref"]["img"]).astype(float)
        refimg = refimg / refimg.max() if refimg.max() > 0 else refimg
        images = [cut(p["ours"]) / 255.0, refimg]
        for c in range(3):
            ax = axes[r][c]
            ax.set_facecolor("black")
            ax.set_xticks([]); ax.set_yticks([])
            for sp_ in ax.spines.values():
                sp_.set_visible(False)
            if c < 2:
                ax.imshow(images[c], cmap="gray", vmin=0.0, vmax=1.0, interpolation="nearest")
            else:
                rgb = np.zeros(refimg.shape + (3,), np.float32)
                ctx = cut(p["refShadow"]).astype(np.float32) * 0.28
                rgb[..., 0] = np.maximum(ctx, cut(p["darkened"]).astype(np.float32))
                rgb[..., 1] = ctx
                rgb[..., 2] = np.maximum(ctx, cut(p["leaked"]).astype(np.float32))
                ax.imshow(rgb, interpolation="nearest")
            if r == 0:
                ax.set_title(titles[c], color="0.75", fontsize=12, pad=8)
        axes[r][0].set_ylabel("%s:%s UTC" % (p["hhmm"][:2], p["hhmm"][2:]),
                              color="0.75", fontsize=11)
        axes[r][2].set_xlabel("red: darkened where lit    blue: lit where shadowed    "
                              "grey: the reference's shadow",
                              color="0.6", fontsize=9, labelpad=6)
    fig.suptitle("Shading against a ray-cast of the same mesh -- --shadow-bias %s" % k_label,
                 color="0.8", fontsize=13)
    fig.tight_layout(rect=(0, 0, 1, 0.97))
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    fig.savefig(path, facecolor="black", dpi=100)
    plt.close(fig)


def main():
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    testdata = os.environ.get("PRO3D_TEST_DATA")
    kernels = os.environ.get("PRO3D_SPICE_KERNELS", "")

    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--obj", default=(os.path.join(testdata, "HERA", "Dimorphos_dsk",
                                                   "g_00243mm_spc_obj_dimo_0000n00000_v004.obj.gz")
                                      if testdata else None),
                    help="the shape model -- must be the one the loaded DSK was built from, "
                         "or this measures the shape difference instead of the shading")
    ap.add_argument("--work", default=None, help="scratch dir; also the ray-cast cache")
    ap.add_argument("--date", default=DEFAULT_DATE)
    ap.add_argument("--epochs", default=",".join(DEFAULT_EPOCHS))
    ap.add_argument("--albedo", default="0.16")
    ap.add_argument("--gain", default="4.492")
    ap.add_argument("--ambient", default="0.02")
    ap.add_argument("--shadow-bias", default=None, help="override --shadow-bias, to sweep it")
    ap.add_argument("--kernel", default=None)
    ap.add_argument("--kernel-root", default=None)
    ap.add_argument("--json", default=None, help="also write the numbers here")
    ap.add_argument("--figure", default=None,
                    help="also write a PNG: ours | ray-cast | where the two disagree")
    ap.add_argument("--update-baseline", action="store_true")
    a = ap.parse_args()

    if not a.obj or not os.path.exists(a.obj):
        print("pass --obj (or set PRO3D_TEST_DATA): %s" % a.obj)
        return 2

    hhmms = [e.strip() for e in a.epochs.split(",") if e.strip()]
    d = a.date
    epochs = ["%s-%s-%sT%s:%s:00Z" % (d[:4], d[4:6], d[6:8], h[:2], h[2:4]) for h in hhmms]

    work = a.work or tempfile.mkdtemp(prefix="pro3d-lighting-")
    os.makedirs(work, exist_ok=True)
    times_file = os.path.join(work, "epochs.txt")
    with open(times_file, "w", encoding="utf-8") as f:
        f.write("# written by %s\n" % os.path.basename(__file__))
        for e in epochs:
            f.write(e + "\n")

    args = ["simulate-series", "--obj", a.obj,
            "--body", "DIMORPHOS", "--frame", "DIMORPHOS_FIXED",
            "--observer", "HERA", "--instrument", "HERA_AFC-1",
            "--times-file", times_file, "--out", os.path.join(work, "render"),
            "--variants", "smooth", "--gain", a.gain, "--albedo", a.albedo,
            "--ambient", a.ambient, "--micro-amplitude", "0", "--keep-going"]
    if a.shadow_bias:
        args += ["--shadow-bias", a.shadow_bias]
    if a.kernel:
        args += ["--kernel", a.kernel]
    if a.kernel_root:
        args += ["--kernel-root", a.kernel_root]
    print("rendering %d epoch(s) ..." % len(epochs))
    p = run_tool(repo, args, quiet=True)
    if p.returncode != 0:
        print("render failed: %s" % tool_error(p))
        return 1
    folder = os.path.join(work, "render", "smooth")

    import spiceypy as sp
    from PIL import Image
    kernel = a.kernel or os.path.join(
        a.kernel_root or kernels, "kernels", "mk", "hera_plan.tm")
    if not os.path.exists(kernel):
        print("metakernel not found: %s" % kernel)
        return 2
    cwd = os.getcwd()
    os.chdir(os.path.dirname(os.path.abspath(kernel)))
    sp.furnsh(os.path.basename(kernel))
    cache = os.path.join(work, "dsk-cache")
    os.makedirs(cache, exist_ok=True)

    gain, albedo = float(a.gain), float(a.albedo)
    ambient = float(a.ambient)
    # what the tool writes for a fully shadowed, sun-facing facet: only the ambient term
    floor_dn = ambient * albedo * gain * 255.0

    rows, panels = [], []
    for hhmm, utc in zip(hhmms, epochs):
        png = os.path.join(folder, "AFC1_SMOOTH_%s_%s00.png" % (d, hhmm))
        if not os.path.exists(png):
            print("%s: no frame rendered -- off target?" % utc)
            continue
        ours = np.asarray(Image.open(png).convert("L")).astype(float)

        c = os.path.join(cache, "%s.npz" % utc.replace(":", "").replace("-", ""))
        if os.path.exists(c):
            z = np.load(c)
            ref = {k: z[k] for k in ("img", "hit", "mu0", "mu", "lit")}
            print("%s: ray-cast cached" % utc)
        else:
            print("%s: ray-casting (~50 s) ..." % utc)
            ref = dsk_illumination(utc, "HERA_AFC-1", "DIMORPHOS", "HERA",
                                   "DIMORPHOS_FIXED", 1020, 5.50)
            np.savez_compressed(c, **ref)
        if not ref["hit"].any():
            print("%s: not in the frame" % utc)
            continue

        et = sp.str2et(utc.replace("Z", ""))
        spos, _ = sp.spkpos("SUN", et, "J2000", "NONE", "DIMORPHOS")
        opos, _ = sp.spkpos("HERA", et, "J2000", "NONE", "DIMORPHOS")
        phase = float(np.degrees(np.arccos(np.clip(
            np.dot(spos / np.linalg.norm(spos), opos / np.linalg.norm(opos)), -1, 1))))

        # The facets that face the sun. Everything else is the terminator, which is not
        # what a shadow map is responsible for. The 0.1 floor keeps grazing facets out:
        # there mu0 -> 0 makes "lit" a coin toss for any renderer.
        facing = ref["hit"] & (ref["mu0"] > 0.1) & (ref["mu"] > 0.05)
        if facing.sum() < 500:
            print("%s: only %d sun-facing pixels -- skipped" % (utc, facing.sum()))
            continue

        # The radiance the surface WOULD have if nothing shadowed it. `ref["img"]` is this
        # masked by illumf's lit flag, so it is zero exactly where the interesting pixels
        # are; rebuilding it from the angles is what makes "how much did we darken this
        # pixel" answerable at all.
        mu0, mu = ref["mu0"], ref["mu"]
        lsFull = np.zeros_like(mu0)
        ok = facing & (mu0 + mu > 1e-6)
        lsFull[ok] = 2.0 * mu0[ok] / (mu0[ok] + mu[ok])

        # One scale from the pixels both agree are lit, by the MEDIAN ratio rather than a
        # least-squares fit: if the render has acne, the pixels it wrongly darkened would
        # drag a fitted scale down and so hide themselves.
        both = facing & ref["lit"] & (lsFull > 0.05)
        if both.sum() < 500:
            print("%s: too little commonly-lit surface -- skipped" % utc)
            continue
        k = float(np.median((ours[both] - floor_dn) / np.maximum(1e-6, lsFull[both])))

        # How much of the sun our render thinks is blocked, 0..1 per pixel. This is the
        # quantity a shadow map produces and it is NOT binary: PCF makes the boundary a
        # ramp, so grading the fully dark CORE against the reference's hard mask counts the
        # soft edge as a disagreement and hides everything else -- which is exactly how a
        # first attempt at this missed acne entirely.
        expected = np.maximum(1e-6, k * lsFull)
        factor = np.clip(1.0 - (ours - floor_dn) / expected, 0.0, 1.0)

        refShadow = facing & ~ref["lit"]
        ourShadow = facing & (factor > 0.5)
        inter = float((refShadow & ourShadow).sum())
        union = float((refShadow | ourShadow).sum())
        shadow_iou = inter / union if union > 0 else 1.0

        # ACNE: sun-facing ground the reference says is lit, that we darkened anyway.
        # `speckle` then asks whether those pixels are ISOLATED -- a shadow we merely place
        # differently is a connected region; acne is a stipple at the scale of a texel.
        darkened = facing & ref["lit"] & (factor > 0.25)
        acne = speckle(darkened, facing)
        darkened_frac = float(darkened.sum()) / max(1, int(facing.sum()))

        # PETER-PANNING: ground the reference says is shadowed, that we left lit.
        leaked = refShadow & (factor < 0.5)
        leak = float(leaked.sum()) / max(1, int(refShadow.sum()))

        x = lsFull[both].astype(float)
        y = (ours[both] - floor_dn).astype(float)
        xm, ym = x - x.mean(), y - y.mean()
        den = np.sqrt((xm * xm).sum() * (ym * ym).sum())
        r = float((xm * ym).sum() / den) if den > 0 else 0.0
        rms = float(np.sqrt(((y - k * x) ** 2).mean()) / max(1e-9, y.mean()))

        panels.append({
            "hhmm": hhmm, "ours": ours, "ref": ref, "facing": facing,
            "expected": expected, "factor": factor,
            "darkened": darkened, "leaked": leaked, "refShadow": refShadow,
        })
        rows.append({
            "time": utc, "phaseDeg": round(phase, 2),
            "sunFacingPx": int(facing.sum()),
            "refShadowPx": int(refShadow.sum()), "ourShadowPx": int(ourShadow.sum()),
            "shadowIoU": round(shadow_iou, 4),
            "shadowAreaRatio": round(float(ourShadow.sum()) / max(1, int(refShadow.sum())), 4),
            "wronglyDarkenedFrac": round(darkened_frac, 5),
            "acneSpeckle": round(acne, 5),
            "refSpeckle": round(speckle(refShadow, facing), 5),
            "shadowLeak": round(leak, 4),
            "brightnessR": round(r, 4),
            "brightnessRmsPct": round(100.0 * rms, 2),
        })
    os.chdir(cwd)

    if not rows:
        print("nothing to compare")
        return 1

    print("\n%-6s %6s %9s %9s %9s %9s %8s %7s %8s" %
          ("epoch", "phase", "shadow px", "shadowIoU", "areaRatio", "darkened",
           "acne", "leak", "bright r"))
    print("-" * 82)
    for r in rows:
        print("%-6s %5.1fd %9d %9.3f %9.3f %9.5f %8.5f %7.3f %8.4f"
              % (r["time"][11:16], r["phaseDeg"], r["refShadowPx"],
                 r["shadowIoU"], r["shadowAreaRatio"], r["wronglyDarkenedFrac"],
                 r["acneSpeckle"], r["shadowLeak"], r["brightnessR"]))

    shadowed = [r for r in rows if r["refShadowPx"] > 500]
    print("\nbrightness: r %.4f .. %.4f, RMS %.2f .. %.2f %% of the mean"
          % (min(r["brightnessR"] for r in rows), max(r["brightnessR"] for r in rows),
             min(r["brightnessRmsPct"] for r in rows), max(r["brightnessRmsPct"] for r in rows)))
    if shadowed:
        print("cast shadows:  IoU %.3f .. %.3f over %d epoch(s) with real shadow in them"
              % (min(r["shadowIoU"] for r in shadowed),
                 max(r["shadowIoU"] for r in shadowed), len(shadowed)))
    else:
        print("cast shadows:  NO epoch here has any -- pick higher-phase epochs")

    if a.figure and panels:
        write_figure(a.figure, panels, k_label=a.shadow_bias or "default")
        print("wrote %s" % a.figure)

    if a.json:
        with open(a.json, "w", encoding="utf-8") as f:
            json.dump({
                "generated": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
                "generator": "scripts/check-lighting.py",
                "obj": os.path.abspath(a.obj), "kernel": os.path.abspath(kernel),
                "settings": {"albedo": albedo, "gain": gain, "ambient": ambient,
                             "shadowBias": a.shadow_bias},
                "floors": FLOORS, "epochs": rows,
            }, f, indent=2)
        print("wrote %s" % a.json)

    if a.update_baseline:
        print("\nfloors this run would support:")
        print(json.dumps({
            "shadowIoU": round(min([r["shadowIoU"] for r in shadowed] or [1.0]) * 0.98, 3),
            "wronglyDarkenedFrac": round(max(r["wronglyDarkenedFrac"] for r in rows) * 1.5 + 0.001, 4),
            "acneSpeckle": round(max(r["acneSpeckle"] for r in rows) * 1.5 + 0.0005, 4),
            "shadowLeak": round(max([r["shadowLeak"] for r in shadowed] or [0.0]) * 1.2 + 0.02, 3),
            "brightnessR": round(min(r["brightnessR"] for r in rows) * 0.999, 4),
        }, indent=2))
        return 0

    bad = []
    for r in rows:
        t = r["time"][11:16]
        if r["refShadowPx"] > 500 and r["shadowIoU"] < FLOORS["shadowIoU"]:
            bad.append("%s: cast shadows agree at only %.3f" % (t, r["shadowIoU"]))
        if r["wronglyDarkenedFrac"] > FLOORS["wronglyDarkenedFrac"]:
            bad.append("%s: %.2f %% of the sun-facing body is darkened where the reference "
                       "says it is lit" % (t, 100.0 * r["wronglyDarkenedFrac"]))
        if r["acneSpeckle"] > FLOORS["acneSpeckle"]:
            bad.append("%s: %.3f %% of it is ISOLATED pixels -- shadow acne"
                       % (t, 100.0 * r["acneSpeckle"]))
        if r["refShadowPx"] > 500 and r["shadowLeak"] > FLOORS["shadowLeak"]:
            bad.append("%s: %.1f %% of the reference's shadow is lit in ours -- peter-panning"
                       % (t, 100.0 * r["shadowLeak"]))
        if not np.isnan(r["brightnessR"]) and r["brightnessR"] < FLOORS["brightnessR"]:
            bad.append("%s: brightness correlates at only %.4f" % (t, r["brightnessR"]))
    if bad:
        print("\nFAILED:")
        for b in bad:
            print("   " + b)
        return 1
    print("\nPASS -- shading, shadows and photometry agree with the ray-cast of the same shape")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
