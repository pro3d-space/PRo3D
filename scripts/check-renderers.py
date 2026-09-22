#!/usr/bin/env python3
"""Compare every renderer of the same frames against every other.

    python scripts/check-renderers.py --comet <dir> --pilucas <dir> --opc <series> ...

`check-series.py` compares a series with OUR OWN SPICE ray-cast, so it cannot catch
anything the two share -- and they share our reading of the instrument kernel. That is how
a 90 degree error once survived a passing validator. This is the independent check.

The five sources, and how each lights its output, which decides what can be compared:

    pilucas   PRo3D driven by her own SPICE code    no lighting  -> silhouette only
    comet     an independent implementation          shaded       -> lit region only
    spice     our spiceypy ray-cast of the DSK       shaded       -> both
    opc       our simulate-series on the 1.96 m OPC  shaded       -> both
    obj       our simulate-series on the kernels' OBJ             -> both

A pair is compared on the strongest footing both can supply: the lit region when both
shade, otherwise the silhouette. Comparing a lit crescent against a full disk measures
nothing, and doing it once produced a confident 28 degree "disagreement" that was not
there.

The metric is silhouette overlap, never grey-level correlation: two renderers on different
shape models disagree pixel by pixel whatever the orientation -- measured, all eight
dihedral transforms negative on frames whose outlines overlap at 0.9.

Epochs where a reference is not showing the same body are rejected on evidence, not by a
hardcoded list: if the lit areas differ by more than 2x, AFC-1 is pointed at Didymos.

Needs numpy and pillow; `--spice` additionally needs spiceypy and is slow (~50 s an epoch).
"""

import argparse
import itertools
import json
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pro3d_sim import body_mask, iou, normalised, screenshot_panel

# Floors are measurements with the run that set them, not targets. 2027-02-25, with
# hera_plan_v182_20260805 / 20260817 / 20260820 -- all three numerically identical.
FLOORS = {
    # comet-toolbox is, to the accuracy a screenshot allows, this same ray-cast: measured
    # 0.944 .. 0.982, at the limit a downscaled screenshot can resolve.
    ("comet", "spice"): 0.93,
    # the OPC is four times coarser than an AFC pixel at 5 km and smooths away limb detail
    # the others resolve: measured 0.855 .. 0.972.
    ("comet", "opc"): 0.84,
    # she renders the same OPC through PRo3D, so this tests the camera path and nothing
    # else: measured 0.748 .. 0.982.
    ("opc", "pilucas"): 0.74,
    # THE ACCEPTANCE CRITERION for rendering from the kernels' OBJ: it has to reach what
    # the ray-cast of that same shape reaches, otherwise the shape was not the problem.
    ("comet", "obj"): 0.93,
}

LIT, SIL = "lit", "sil"


def load(p):
    return np.asarray(Image.open(p).convert("L")).astype(float)


def series_source(folder, sil_folder, date, name):
    """One of our simulate-series outputs: lit from smooth/, silhouette from --no-lighting."""
    def lit(st):
        p = os.path.join(folder, "smooth", "AFC1_SMOOTH_%s_%s.png" % (date, st))
        return body_mask(load(p)) if os.path.exists(p) else None

    def sil(st):
        if sil_folder:
            p = os.path.join(sil_folder, "AFC1_SMOOTH_%s_%s.png" % (date, st))
            if os.path.exists(p):
                return body_mask(load(p))
            return None
        # the ambient night side stands in for a silhouette when no --no-lighting run exists
        p = os.path.join(folder, "smooth", "AFC1_SMOOTH_%s_%s.png" % (date, st))
        return body_mask(load(p), 1) if os.path.exists(p) else None

    return {"name": name, LIT: lit, SIL: sil}


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--comet", help="comet-toolbox screenshots named HHMM.png")
    ap.add_argument("--pilucas", help="folder of HERA_AFC_*_COP.png frames")
    ap.add_argument("--opc", help="our simulate-series output rendered from the OPC")
    ap.add_argument("--opc-silhouettes", help="the matching --no-lighting run")
    ap.add_argument("--obj", help="our simulate-series output rendered from the OBJ")
    ap.add_argument("--obj-silhouettes", help="the matching --no-lighting run")
    ap.add_argument("--spice", action="store_true", help="ray-cast the DSK at each epoch")
    ap.add_argument("--kernel", help="metakernel for --spice")
    ap.add_argument("--date", default="20270225")
    ap.add_argument("--epochs", help="comma-separated HHMM; default: every epoch a "
                                     "reference exists for")
    ap.add_argument("--update-baseline", action="store_true")
    a = ap.parse_args()

    for k in ("comet", "pilucas", "opc", "opc_silhouettes", "obj", "obj_silhouettes"):
        v = getattr(a, k, None)
        if v:
            setattr(a, k, os.path.abspath(v))

    sources, stamps = {}, set()

    if a.comet and os.path.isdir(a.comet):
        frames = {f[:4] + "00": os.path.join(a.comet, f)
                  for f in sorted(os.listdir(a.comet))
                  if f.endswith(".png") and f[:4].isdigit()}
        stamps |= set(frames)
        sources["comet"] = {"name": "comet", LIT: lambda st, F=frames:
                            body_mask(screenshot_panel(F[st])) if st in F else None,
                            SIL: lambda st: None}

    if a.pilucas and os.path.isdir(a.pilucas):
        frames = {}
        for f in sorted(os.listdir(a.pilucas)):
            if f.endswith("_COP.png"):
                bits = f.split("_")
                if len(bits) > 4:
                    frames[bits[4]] = os.path.join(a.pilucas, f)
        stamps |= set(frames)
        # she does not light her output, so her frame IS the silhouette
        sources["pilucas"] = {"name": "pilucas", LIT: lambda st: None,
                              SIL: lambda st, F=frames:
                              body_mask(load(F[st])) if st in F else None}

    if a.opc:
        sources["opc"] = series_source(a.opc, a.opc_silhouettes, a.date, "opc")
    if a.obj:
        sources["obj"] = series_source(a.obj, a.obj_silhouettes, a.date, "obj")

    if a.spice:
        import spiceypy as sp
        from pro3d_sim import dsk_render
        kernel = a.kernel or os.path.join(os.environ.get("PRO3D_SPICE_KERNELS", ""),
                                          "kernels", "mk", "hera_plan.tm")
        # The metakernel's PATH_VALUES is relative to its own directory ('../..'), so the
        # process has to stay there: changing back breaks every kernel it references.
        # Absolute paths are used for everything else in this script for that reason.
        os.chdir(os.path.dirname(os.path.abspath(kernel)))
        sp.furnsh(os.path.basename(kernel))
        cache = {}

        def raycast(st, date=a.date):
            if st not in cache:
                utc = "%s-%s-%sT%s:%s:00Z" % (date[:4], date[4:6], date[6:8], st[:2], st[2:4])
                cache[st] = dsk_render(utc, "HERA_AFC-1", "DIMORPHOS", "HERA",
                                       "DIMORPHOS_FIXED", 1020, 5.50)
            return cache[st]

        sources["spice"] = {"name": "spice",
                            LIT: lambda st: body_mask(raycast(st)[0], 1e-6),
                            SIL: lambda st: body_mask(raycast(st)[1].astype(float), 0.5)}

    if len(sources) < 2:
        print("need at least two sources to compare")
        return 2

    if a.epochs:
        stamps = set(e.strip() + "00" for e in a.epochs.split(",") if e.strip())
    stamps = sorted(stamps)

    pairs = [tuple(sorted(p)) for p in itertools.combinations(sorted(sources), 2)]
    results = {p: [] for p in pairs}
    skipped = {p: [] for p in pairs}

    for st in stamps:
        for p in pairs:
            x, y = sources[p[0]], sources[p[1]]
            # strongest footing both can supply
            footing = LIT if (x[LIT](st) is not None and y[LIT](st) is not None) else SIL
            mx, my = x[footing](st), y[footing](st)
            if mx is None or my is None:
                continue
            # as a FRACTION of each frame: the sources are at different resolutions
            # (a 518 px screenshot panel against our 1020 px render), so raw pixel counts
            # would call every cross-resolution pair a different body
            ratio = (my.sum() / my.size) / max(1e-9, mx.sum() / mx.size)
            if ratio < 0.5 or ratio > 2.0:
                skipped[p].append((st, ratio))
                continue
            results[p].append((st, footing, iou(normalised(mx), normalised(my))))

    width = max(len(p[0]) + len(p[1]) + 4 for p in pairs)
    print("%-*s %8s %9s %9s %9s %8s   %s" % (width, "pair", "footing", "epochs",
                                             "worst", "mean", "floor", "verdict"))
    print("-" * (width + 58))
    failed = []
    for p in pairs:
        r = results[p]
        if not r:
            print("%-*s %8s %9d %9s %9s %8s   no overlap" % (width, "%s vs %s" % p, "-", 0, "-", "-", "-"))
            continue
        vals = [v for _, _, v in r]
        footing = r[0][1]
        floor = FLOORS.get(p)
        ok = floor is None or min(vals) >= floor
        if not ok:
            failed.append((p, min(vals), floor))
        print("%-*s %8s %9d %9.3f %9.3f %8s   %s"
              % (width, "%s vs %s" % p, footing, len(vals), min(vals), np.mean(vals),
                 "%.2f" % floor if floor else "-",
                 "ok" if floor and ok else ("BELOW" if floor else "(no floor)")))

    dropped = sum(len(v) for v in skipped.values())
    if dropped:
        worst = max((r for v in skipped.values() for _, r in v), default=0)
        print("\n%d comparison(s) skipped: the two frames are not showing the same body"
              % dropped)
        print("   lit areas differ by up to %.0fx -- AFC-1 is pointed at Didymos there" % worst)

    if a.update_baseline:
        print("\nfloors this run would support (2 %% under the worst seen):")
        print(json.dumps({"%s vs %s" % p: round(min(v for _, _, v in r) * 0.98, 3)
                          for p, r in results.items() if r}, indent=2))
        return 0

    if failed:
        print("\nFAILED: %d pair(s) below their measured floor" % len(failed))
        for p, w, f in failed:
            print("   %s vs %s: %.3f < %.3f" % (p[0], p[1], w, f))
        return 1
    print("\nPASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
