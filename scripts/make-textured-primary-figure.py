#!/usr/bin/env python3
"""The figure for `--occluder-opc` / `--occluder-texture-albedo`: the same eclipse with the
primary drawn as a grey shape and with its own mosaic as albedo.

    python scripts/make-textured-primary-figure.py --renders <dir> \\
        --out docs/images/simulateImage/texturedPrimary.png

The point of the pair is that nothing but the primary's surface changes between the panels
-- same epoch, same camera, same sun, same gain, same target -- so the difference in the
image is the difference the flag makes and not a difference in framing or exposure.
"""

import argparse
import json
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont


def label(img, text, sub):
    """Caption a panel in its own top-left corner, over a dark strip."""
    d = ImageDraw.Draw(img)
    try:
        f = ImageFont.truetype("arialbd.ttf", 26)
        fs = ImageFont.truetype("arial.ttf", 19)
    except OSError:
        f = fs = ImageFont.load_default()
    d.rectangle([0, 0, img.width, 74], fill=(0, 0, 0))
    d.text((16, 8), text, fill=(255, 255, 255), font=f)
    d.text((16, 42), sub, fill=(170, 190, 210), font=fs)
    return img


def panel(path, title, sub):
    im = Image.open(path).convert("RGB")
    return label(im, title, sub)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--renders", required=True, help="folder holding the rendered frames")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()

    panels = [
        ("pair_0330_obj.png", "--occluder-obj",
         "the shape model's OBJ: no texture coordinates, so a grey body"),
        ("pair_0330.png", "--occluder-opc --occluder-texture-albedo",
         "the textured Didymos OPC, its mosaic as albedo at this frame's sun"),
    ]
    imgs = []
    for fn, t, s in panels:
        p = os.path.join(a.renders, fn)
        if not os.path.exists(p):
            raise SystemExit("missing render: %s" % p)
        imgs.append(panel(p, t, s))

    gap = 12
    w = sum(i.width for i in imgs) + gap * (len(imgs) - 1)
    h = max(i.height for i in imgs)
    sheet = Image.new("RGB", (w, h), (24, 24, 28))
    x = 0
    for i in imgs:
        sheet.paste(i, (x, 0))
        x += i.width + gap

    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    sheet.save(a.out)
    print("wrote %s (%dx%d)" % (a.out, sheet.width, sheet.height))

    # the numbers the doc quotes, measured rather than retyped
    stats = {}
    for fn, t, _ in panels:
        g = np.asarray(Image.open(os.path.join(a.renders, fn)).convert("L")).astype(float)
        lit = g[g > 8]
        stats[fn] = {
            "flag": t,
            "litPixels": int(lit.size),
            "meanDN": round(float(lit.mean()), 2) if lit.size else None,
            "stdDN": round(float(lit.std()), 2) if lit.size else None,
        }
    with open(os.path.splitext(a.out)[0] + ".json", "w", encoding="utf-8") as f:
        json.dump({"epoch": "2027-04-25T03:30:00Z", "panels": stats}, f, indent=2)
    print("wrote %s" % (os.path.splitext(a.out)[0] + ".json"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
