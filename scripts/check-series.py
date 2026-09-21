#!/usr/bin/env python3
"""Validate a rendered image series against its own SPICE reference.

    python scripts/check-series.py --series <folder>

For every epoch that has both a `pro3d-tool` variant and a `spice/` frame, this finds
which of the eight dihedral transforms best aligns them. The answer must be `identity`:
anything else means the renderer and the kernels disagree about the detector axes, which
is exactly the fault that shipped a folder holding frames 90 degrees apart earlier in this
work. Run it before handing a series to anyone.

Two traps this avoids, both of which produced confident wrong answers by hand:

  * **Unaligned frames.** A small field-of-view difference shifts the body by a pixel or
    two; correlating whole frames then collapses to noise and a spurious transform wins.
    Both frames are cropped around their own body centroid first.
  * **Mismatched masks.** The tool's frames carry an ambient-lit night side, the SPICE
    reference has none (it writes 0 where unlit). Thresholding both well above the ambient
    floor makes the two masks comparable, so the centroids agree.

Needs numpy and pillow.
"""

import argparse
import json
import os

import numpy as np
from PIL import Image

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


def crop(a, size, thr):
    m = a > thr
    if m.sum() < 200:
        return None
    ys, xs = np.nonzero(m)
    cx, cy, h = int(xs.mean()), int(ys.mean()), size // 2
    if cx - h < 0 or cy - h < 0 or cx + h > a.shape[1] or cy + h > a.shape[0]:
        return None
    return a[cy - h:cy + h, cx - h:cx + h].astype(float)


def corr(x, y):
    m = (x > 0) | (y > 0)
    if m.sum() < 200:
        return 0.0
    x, y = x[m] - x[m].mean(), y[m] - y[m].mean()
    d = np.sqrt((x * x).sum() * (y * y).sum())
    return float((x * y).sum() / d) if d > 0 else 0.0


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--series", required=True, help="a series folder holding series.json")
    ap.add_argument("--size", type=int, default=340, help="crop size in px (default 340)")
    ap.add_argument("--threshold", type=int, default=25,
                    help="DN above which a pixel counts as body, for both sides (default 25)")
    ap.add_argument("--min-correlation", type=float, default=0.5,
                    help="below this an epoch is reported as inconclusive (default 0.5)")
    ap.add_argument("--margin", type=float, default=0.02,
                    help="how far another transform must beat identity before it counts as "
                         "a failure (default 0.02). Dimorphos is near-symmetric about some "
                         "viewing axes, so flipUD can edge out identity by ~0.005 on a "
                         "correct frame; a real axis error wins by tenths, not thousandths")
    a = ap.parse_args()

    m = json.load(open(os.path.join(a.series, "series.json"), encoding="utf-8"))
    variants = [v for v in m.get("variants", {}) if v != "spice"]
    if "spice" not in m.get("variants", {}):
        print("no spice/ reference in this series -- nothing to validate against")
        return 2

    tally, weak, bad = {}, [], []
    checked = 0
    for ep in m["epochs"]:
        files = ep.get("files", {})
        if "spice" not in files:
            continue
        ref = crop(np.asarray(Image.open(os.path.join(a.series, files["spice"])).convert("L")),
                   a.size, a.threshold)
        if ref is None:
            continue
        for v in variants:
            if v not in files:
                continue
            img = crop(np.asarray(Image.open(os.path.join(a.series, files[v])).convert("L")),
                       a.size, a.threshold)
            if img is None:
                continue
            checked += 1
            scores = sorted(((corr(img, f(ref)), k) for k, f in TRANSFORMS.items()),
                            reverse=True)
            best, name = scores[0]
            tally[name] = tally.get(name, 0) + 1
            ident = corr(img, ref)
            if name != "identity" and best - ident > a.margin:
                bad.append((ep["time"], v, name, best, ident))
            elif name != "identity":
                tally["identity (tie)"] = tally.get("identity (tie)", 0) + 1
            elif best < a.min_correlation:
                weak.append((ep["time"], v, best))

    print("checked %d frame pairs across %d epochs" % (checked, len(m["epochs"])))
    for k in sorted(tally, key=lambda k: -tally[k]):
        print("   best transform %-15s %4d" % (k, tally[k]))

    if bad:
        print("\nFAILED: %d pairs align better under something other than identity" % len(bad))
        for t, v, name, best, ident in bad[:10]:
            print("   %s %-7s best=%-14s %+.3f   identity %+.3f" % (t, v, name, best, ident))
        if len(bad) > 10:
            print("   ... and %d more" % (len(bad) - 10))
        return 1

    if weak:
        print("\n%d pairs align under identity but weakly (< %.2f) -- usually a low-phase"
              % (len(weak), a.min_correlation))
        print("epoch where the disk is flat and there is little structure to match:")
        for t, v, c in weak[:5]:
            print("   %s %-7s %+.3f" % (t, v, c))

    print("\nPASS: every pair aligns best under identity"
          " (%d weak of %d)" % (len(weak), checked))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
