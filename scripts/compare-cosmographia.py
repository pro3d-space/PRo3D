#!/usr/bin/env python3
"""Compare one of our renders against a Cosmographia screenshot of the same epoch.

    python scripts/compare-cosmographia.py --ours ours.png --theirs cosmo.png \\
        --out docs/images/simulateImage/cosmographia.png --epoch 2027-01-29T17:45:00Z

Cosmographia is the only *independent* renderer available here: comet-toolbox differs by
the shape model and Piluca's tool drives PRo3D itself, so neither can falsify our geometry.
Cosmographia reads the same kernels and the same DSK through its own pipeline, so where it
agrees the agreement means something.

What is comparable is **geometry**, not brightness. Its default lighting saturates and it
applies its own tone curve, so this measures silhouettes and positions: each body's area
and centroid, their separation, and -- when the binary is in eclipse -- where the umbra
falls. Comparing DN would only measure two exposure policies.

Bodies are separated by connected component rather than by threshold, because the point of
the frame is that there are two of them and they must be matched to each other, not merged.
"""

import argparse
import json
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont


def components(mask, min_px=20):
    """Connected components of a boolean mask, 4-connected, as a list of index arrays.

    Written out rather than pulled from scipy: this repo's scripts depend on numpy and
    pillow only, and one flood fill is cheaper than a new dependency.
    """
    h, w = mask.shape
    seen = np.zeros_like(mask, bool)
    out = []
    for y in range(h):
        for x in range(w):
            if not mask[y, x] or seen[y, x]:
                continue
            stack = [(y, x)]
            seen[y, x] = True
            pix = []
            while stack:
                cy, cx = stack.pop()
                pix.append((cy, cx))
                for ny, nx in ((cy-1, cx), (cy+1, cx), (cy, cx-1), (cy, cx+1)):
                    if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        stack.append((ny, nx))
            if len(pix) >= min_px:
                out.append(np.array(pix))
    out.sort(key=len, reverse=True)
    return out


def describe(path, thresh):
    g = np.asarray(Image.open(path).convert("L")).astype(float) / 255.0
    body = g > thresh
    comps = components(body)
    info = {"file": os.path.basename(path), "size": list(g.shape), "bodies": []}
    masks = []
    for c in comps[:2]:
        m = np.zeros_like(body)
        m[c[:, 0], c[:, 1]] = True
        # the umbra is a hole INSIDE the silhouette: fill the component's bounding
        # region by row spans so the shadow does not shrink the measured disk
        filled = m.copy()
        for row in range(m.shape[0]):
            xs = np.flatnonzero(m[row])
            if xs.size:
                filled[row, xs[0]:xs[-1] + 1] = True
        cy, cx = c[:, 0].mean(), c[:, 1].mean()
        info["bodies"].append({
            "areaPx": int(filled.sum()),
            "litPx": int(m.sum()),
            "shadowPx": int(filled.sum() - m.sum()),
            "centroid": [round(float(cx), 2), round(float(cy), 2)],
            "radiusPx": round(float(np.sqrt(filled.sum() / np.pi)), 2),
        })
        masks.append(filled)
    return info, masks, g


def iou(a, b):
    u = np.logical_or(a, b).sum()
    return float(np.logical_and(a, b).sum()) / u if u else 0.0


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--ours", required=True)
    ap.add_argument("--theirs", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--epoch", required=True)
    ap.add_argument("--threshold", type=float, default=0.12,
                    help="a pixel is body above this fraction of full scale (default 0.12)")
    a = ap.parse_args()

    ours, om, og = describe(a.ours, a.threshold)
    theirs, tm, tg = describe(a.theirs, a.threshold)
    if len(om) < 2 or len(tm) < 2:
        print("WARNING: expected two bodies, found %d and %d" % (len(om), len(tm)))

    n = min(len(om), len(tm))

    # Align on the PRIMARY's centroid before comparing shapes. The frame origin is not a
    # shared quantity: a Cosmographia screenshot is aimed at the body, while our camera
    # takes the spacecraft attitude from the CK, and the CK's boresight is not exactly on
    # the target. Raw IoU therefore measures that offset and not the geometry, which is
    # what the comparison is for. Both numbers are reported -- the raw one is still the
    # honest answer to "would these two images overlay".
    shift = (0, 0)
    if n:
        o0, t0 = ours["bodies"][0]["centroid"], theirs["bodies"][0]["centroid"]
        shift = (int(round(t0[1] - o0[1])), int(round(t0[0] - o0[0])))   # (dy, dx)
    def shifted(m):
        return np.roll(np.roll(m, shift[0], axis=0), shift[1], axis=1)

    rows = []
    for i in range(n):
        name = "primary" if i == 0 else "secondary"
        o, t = ours["bodies"][i], theirs["bodies"][i]
        rows.append({
            "body": name,
            "silhouetteIoU": round(iou(om[i], tm[i]), 4),
            "silhouetteIoUAligned": round(iou(shifted(om[i]), tm[i]), 4),
            "areaPx": [o["areaPx"], t["areaPx"]],
            "areaRatio": round(o["areaPx"] / max(1, t["areaPx"]), 4),
            "centroidOffsetPx": round(float(np.hypot(o["centroid"][0] - t["centroid"][0],
                                                     o["centroid"][1] - t["centroid"][1])), 2),
            "shadowPx": [o["shadowPx"], t["shadowPx"]],
        })
    if n == 2:
        sep = lambda d: float(np.hypot(d["bodies"][0]["centroid"][0] - d["bodies"][1]["centroid"][0],
                                       d["bodies"][0]["centroid"][1] - d["bodies"][1]["centroid"][1]))
        rows.append({"body": "separation", "px": [round(sep(ours), 2), round(sep(theirs), 2)]})

    report = {"epoch": a.epoch, "alignmentShiftPx": {"dx": shift[1], "dy": shift[0]},
              "ours": ours, "theirs": theirs, "comparison": rows}
    for r in rows:
        print("  " + json.dumps(r))

    # the figure: theirs, ours, and the two silhouettes overlaid
    def panel(path, title, sub):
        im = Image.open(path).convert("RGB")
        d = ImageDraw.Draw(im)
        try:
            f = ImageFont.truetype("arialbd.ttf", 17)
            fs = ImageFont.truetype("arial.ttf", 13)
        except OSError:
            f = fs = ImageFont.load_default()
        d.rectangle([0, 0, im.width, 46], fill=(0, 0, 0))
        d.text((10, 4), title, fill=(255, 255, 255), font=f)
        d.text((10, 26), sub, fill=(170, 190, 210), font=fs)
        return im

    allo = np.any(np.stack(om), axis=0) if om else np.zeros(og.shape, bool)
    allt = np.any(np.stack(tm), axis=0) if tm else np.zeros(og.shape, bool)

    def overlay(ours_mask, title, sub):
        ov = np.zeros(og.shape + (3,), np.uint8)
        ov[..., 0] = np.where(allt, 235, 0)          # Cosmographia in red
        ov[..., 1] = np.where(ours_mask, 235, 0)     # ours in green
        ov[..., 2] = np.where(np.logical_and(ours_mask, allt), 235, 0)
        im = Image.fromarray(ov)
        d = ImageDraw.Draw(im)
        try:
            f = ImageFont.truetype("arialbd.ttf", 17)
            fs = ImageFont.truetype("arial.ttf", 13)
        except OSError:
            f = fs = ImageFont.load_default()
        d.rectangle([0, 0, im.width, 46], fill=(0, 0, 0))
        d.text((10, 4), title, fill=(255, 255, 255), font=f)
        d.text((10, 26), sub, fill=(170, 190, 210), font=fs)
        return im

    # Two overlays, because they answer different questions. The raw one shows the frame
    # origins disagree; the aligned one shows whether the SHAPES do, which is what a
    # geometry cross-check is actually for.
    imgs = [panel(a.theirs, "Cosmographia", "same kernels, same DSK, its own renderer"),
            panel(a.ours, "PRo3D simulate-image", "--obj on both bodies, --occluder-in-scene"),
            overlay(allo, "overlaid, as rendered",
                    "white = agree, red = Cosmographia only, green = ours only"),
            overlay(shifted(allo), "overlaid, centroids aligned",
                    "the %+d,%+d px shift removed" % (shift[1], shift[0]))]
    gap = 10
    w = sum(i.width for i in imgs) + gap * (len(imgs) - 1)
    sheet = Image.new("RGB", (w, max(i.height for i in imgs)), (24, 24, 28))
    x = 0
    for i in imgs:
        sheet.paste(i, (x, 0))
        x += i.width + gap
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    sheet.save(a.out)
    with open(os.path.splitext(a.out)[0] + ".json", "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=2)
    print("wrote %s and %s" % (a.out, os.path.splitext(a.out)[0] + ".json"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
