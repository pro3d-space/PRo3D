#!/usr/bin/env python3
"""Check our renders against other implementations of the same frames.

    python scripts/check-renderers.py --refs <dir> --ours <series>

This is the acceptance test for "do we draw the same body, in the same place, the same way
up as everyone else". It is separate from `check-series.py`, which checks a series against
our OWN SPICE ray-cast and so cannot catch anything the two share.

What it compares, and why each pairing is on a different footing:

    comet-toolbox   shades, so it has a terminator     -> compare LIT regions
    Pilucas         does not light her output at all   -> compare SILHOUETTES
                                                          (ours via --no-lighting, or the
                                                          ambient night side at DN > 1)

Comparing our lit crescent against her full disk measures nothing, and it is the mistake
this script exists to make impossible to repeat.

The metric is silhouette overlap, not grey-level correlation. Renderers using different
shape models disagree pixel by pixel whatever the orientation -- measured: all eight
dihedral transforms come out negative on frames whose outlines overlap at 0.9 -- so the
outline is the only thing that carries the geometry.

The thresholds below are measurements, not wishes; each records what was achieved and on
what. `--update-baseline` prints the values a run actually produced, for when a deliberate
change moves them.

Reference layout: `<refs>/HHMM.png`, one per epoch, full browser screenshots are fine --
the render panel is located automatically.

Needs numpy and pillow; `--dsk` additionally needs spiceypy and is slow (~50 s an epoch).
"""

import argparse
import json
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pro3d_sim import body_mask, iou, normalised, screenshot_panel

# Measured on 2027-02-25 against comet-toolbox and Pilucas' COP set, with
# hera_plan_v182_20260805/20260817/20260820 -- all three numerically identical.
BASELINE = {
    # our SPICE ray-cast of the kernels' DSK against comet-toolbox: 0.944 .. 0.982.
    # comet-toolbox is, to the accuracy a screenshot allows, that same ray-cast.
    "dsk_vs_comet": 0.93,
    # our PRo3D render off the 1.96 m OPC against comet-toolbox: 0.855 .. 0.972. Lower
    # because the OPC is four times coarser than an AFC pixel at 5 km and smooths away
    # limb detail that comet-toolbox resolves. Rendering from the kernels' OBJ should
    # lift this to dsk_vs_comet; raise the threshold when it does.
    "opc_vs_comet": 0.84,
    # our silhouette against Pilucas': 0.752 .. 0.952. She renders the same OPC through
    # PRo3D, so this tests the camera path and nothing else.
    "ours_vs_pilucas": 0.74,
}


def load(path):
    return np.asarray(Image.open(path).convert("L")).astype(float)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--refs", help="comet-toolbox screenshots, named HHMM.png")
    ap.add_argument("--pilucas", help="a folder of HERA_AFC_*_COP.png frames")
    ap.add_argument("--ours", required=True, help="our series folder (uses smooth/)")
    ap.add_argument("--silhouettes", help="our --no-lighting renders, for the Pilucas "
                                          "comparison; without it the ambient night side "
                                          "at DN > 1 is used instead")
    ap.add_argument("--date", default="20270225", help="yyyymmdd of the epochs (default 20270225)")
    ap.add_argument("--dsk", action="store_true",
                    help="also ray-cast the kernels' DSK at each epoch and compare that "
                         "(slow: ~50 s an epoch, needs spiceypy)")
    ap.add_argument("--kernel", help="metakernel for --dsk")
    ap.add_argument("--update-baseline", action="store_true",
                    help="print the thresholds this run would support, and pass regardless")
    a = ap.parse_args()

    rows, skipped = [], []
    refs = {}
    if a.refs and os.path.isdir(a.refs):
        for f in sorted(os.listdir(a.refs)):
            if f.endswith(".png") and f[:4].isdigit():
                refs[f[:4] + "00"] = os.path.join(a.refs, f)
    pilu = {}
    if a.pilucas and os.path.isdir(a.pilucas):
        for f in sorted(os.listdir(a.pilucas)):
            if f.endswith("_COP.png"):
                bits = f.split("_")
                if len(bits) > 4:
                    pilu[bits[4]] = os.path.join(a.pilucas, f)

    dsk = None
    if a.dsk:
        import spiceypy as sp
        from pro3d_sim import dsk_render
        kernel = a.kernel or os.path.join(os.environ.get("PRO3D_SPICE_KERNELS", ""),
                                          "kernels", "mk", "hera_plan.tm")
        os.chdir(os.path.dirname(kernel))
        sp.furnsh(os.path.basename(kernel))
        dsk = dsk_render

    stamps = sorted(set(list(refs) + list(pilu)))
    if not stamps:
        print("no reference frames found")
        return 2

    print("%-7s %16s %16s %18s" % ("epoch", "OPC vs comet", "DSK vs comet", "ours vs pilucas"))
    print("%-7s-%16s-%16s-%18s" % ("-" * 7, "-" * 16, "-" * 16, "-" * 18))
    for st in stamps:
        our_path = os.path.join(a.ours, "smooth", "AFC1_SMOOTH_%s_%s.png" % (a.date, st))
        if not os.path.exists(our_path):
            continue
        ours_lit = normalised(body_mask(load(our_path)))
        cells = {}

        if st in refs:
            comet = normalised(body_mask(screenshot_panel(refs[st])))
            cells["opc_vs_comet"] = iou(ours_lit, comet)
            if dsk is not None:
                utc = "%s-%s-%sT%s:%s:00Z" % (a.date[:4], a.date[4:6], a.date[6:8],
                                              st[:2], st[2:4])
                img, _ = dsk(utc, "HERA_AFC-1", "DIMORPHOS", "HERA", "DIMORPHOS_FIXED",
                             1020, 5.50)
                cells["dsk_vs_comet"] = iou(normalised(body_mask(img, 1e-6)), comet)

        if st in pilu and (a.silhouettes is None or os.path.exists(
                os.path.join(a.silhouettes, "AFC1_SMOOTH_%s_%s.png" % (a.date, st)))):
            if a.silhouettes:
                sp_ = os.path.join(a.silhouettes, "AFC1_SMOOTH_%s_%s.png" % (a.date, st))
                ours_sil = normalised(body_mask(load(sp_))) if os.path.exists(sp_) else None
            else:
                ours_sil = normalised(body_mask(load(our_path), 1))
            theirs = body_mask(load(pilu[st]))
            if ours_sil is not None:
                # A frame showing a different body is not a disagreement about this one.
                # AFC-1 points at Didymos for two thirds of the close-orbit phase, and it
                # overflows the frame: at 12:30 one blob covers 77 % of it against
                # Dimorphos' 7 %. Compare areas before comparing shapes.
                mine = body_mask(load(our_path), 1 if not a.silhouettes else 25)
                ratio = theirs.sum() / max(1, mine.sum())
                if ratio < 0.5 or ratio > 2.0:
                    skipped.append((st, ratio))
                else:
                    cells["ours_vs_pilucas"] = iou(ours_sil, normalised(theirs))

        if not cells:
            continue
        rows.append((st, cells))
        print("%-7s %16s %16s %18s"
              % (st[:2] + ":" + st[2:4],
                 "%.3f" % cells["opc_vs_comet"] if "opc_vs_comet" in cells else "-",
                 "%.3f" % cells["dsk_vs_comet"] if "dsk_vs_comet" in cells else "-",
                 "%.3f" % cells["ours_vs_pilucas"] if "ours_vs_pilucas" in cells else "-"))

    if skipped:
        print("\n%d epoch(s) skipped: the reference frame is not showing the same body"
              % len(skipped))
        print("   (its lit area differs by more than 2x -- AFC-1 is pointed at Didymos)")
        print("   %s" % ", ".join("%s:%s (%.1fx)" % (t[:2], t[2:4], r) for t, r in skipped[:8]))

    failed = []
    print()
    for key, floor in BASELINE.items():
        vals = [c[key] for _, c in rows if key in c]
        if not vals:
            continue
        worst = min(vals)
        ok = worst >= floor
        print("%-18s %d epochs, worst %.3f, floor %.3f   %s"
              % (key, len(vals), worst, floor, "ok" if ok else "BELOW FLOOR"))
        if not ok:
            failed.append((key, worst, floor))

    if a.update_baseline:
        print("\nthresholds this run would support (2 %% below the worst seen):")
        print(json.dumps({k: round(min(c[k] for _, c in rows if k in c) * 0.98, 3)
                          for k in BASELINE
                          if any(k in c for _, c in rows)}, indent=2))
        return 0

    if failed:
        print("\nFAILED: %d comparison(s) below their measured floor" % len(failed))
        for k, w, f in failed:
            print("   %s: %.3f < %.3f" % (k, w, f))
        return 1
    print("\nPASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
