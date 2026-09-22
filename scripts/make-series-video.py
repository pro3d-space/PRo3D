#!/usr/bin/env python3
"""Turn a rendered series folder into a video.

    python scripts/make-series-video.py --series <folder> --variant delit

An eclipse is mostly totality: on 2027-04-08 the body sits at the ambient floor for 50 of
the event's 87 minutes, so playing the frames straight gives 17 seconds of black screen out
of 30. The series keeps its uniform cadence anyway -- a series with a hole in it is not a
series -- and the compression happens HERE, where it is an editorial choice rather than a
gap in the data.

`--dark-speedup` keeps every Nth frame while the body is below `--dark-level` of its
unshadowed brightness. Ingress and egress stay at full cadence, because they are the part
worth watching; totality becomes a beat rather than a wait.

Writes MP4 through OpenCV, which is already a dependency of nothing here but is usually
present; `--gif` needs only pillow. Neither needs ffmpeg on PATH.
"""

import argparse
import json
import os
import sys

import numpy as np


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--series", required=True, help="the series folder")
    ap.add_argument("--variant", default="delit", help="which variant folder (default delit)")
    ap.add_argument("--out", default=None, help="output file (default <series>/<variant>.mp4)")
    ap.add_argument("--fps", type=int, default=25)
    ap.add_argument("--dark-level", type=float, default=0.15,
                    help="a frame is 'dark' below this fraction of the unshadowed mean "
                         "(default 0.15); 0 disables the compression entirely")
    ap.add_argument("--dark-speedup", type=int, default=12,
                    help="keep every Nth dark frame (default 12). 1 keeps them all")
    ap.add_argument("--gif", action="store_true", help="also write a GIF beside the video")
    ap.add_argument("--crop", type=int, default=0,
                    help="centre-crop to this square before encoding; 0 (default) keeps "
                         "the full frame")
    a = ap.parse_args()

    from PIL import Image
    folder = os.path.join(a.series, a.variant)
    if not os.path.isdir(folder):
        print("no such variant folder: %s" % folder)
        return 2
    files = sorted(f for f in os.listdir(folder) if f.endswith(".png"))
    if not files:
        print("no frames in %s" % folder)
        return 2

    print("reading %d frames ..." % len(files))
    frames = [np.asarray(Image.open(os.path.join(folder, f)).convert("L")) for f in files]

    # brightness over the body, which is what says whether a frame is in the umbra
    means = []
    for im in frames:
        m = im > 1
        means.append(float(im[m].mean()) if m.any() else 0.0)
    means = np.array(means)
    base = float(np.median(means[:max(1, len(means) // 20)]))

    keep = list(range(len(frames)))
    if a.dark_level > 0 and a.dark_speedup > 1:
        dark = means < a.dark_level * base
        keep = [i for i in range(len(frames))
                if not dark[i] or (i % a.dark_speedup) == 0]
        print("totality: %d of %d frames below %.0f %% of unshadowed, kept every %dth -> %d frames"
              % (int(dark.sum()), len(frames), 100 * a.dark_level, a.dark_speedup, len(keep)))

    if a.crop:
        h, w = frames[0].shape
        c = min(a.crop, h, w)
        y0, x0 = (h - c) // 2, (w - c) // 2
        frames = [f[y0:y0 + c, x0:x0 + c] for f in frames]

    out = a.out or os.path.join(a.series, "%s.mp4" % a.variant)
    h, w = frames[0].shape
    try:
        import cv2
        # mp4v rather than avc1: it needs no external codec and every player takes it
        vw = cv2.VideoWriter(out, cv2.VideoWriter_fourcc(*"mp4v"), a.fps, (w, h), isColor=False)
        if not vw.isOpened():
            raise RuntimeError("OpenCV could not open a writer for %s" % out)
        for i in keep:
            vw.write(frames[i])
        vw.release()
        print("wrote %s  --  %d frames, %.1f s at %d fps, %dx%d"
              % (out, len(keep), len(keep) / float(a.fps), a.fps, w, h))
    except Exception as ex:
        print("no video written (%s); use the frames with ffmpeg instead" % ex)

    if a.gif:
        g = os.path.splitext(out)[0] + ".gif"
        imgs = [Image.fromarray(frames[i]) for i in keep]
        imgs[0].save(g, save_all=True, append_images=imgs[1:],
                     duration=int(1000.0 / a.fps), loop=0, optimize=True)
        print("wrote %s" % g)

    js = os.path.join(a.series, "series.json")
    if os.path.exists(js):
        try:
            d = json.load(open(js, encoding="utf-8"))
            ep = d.get("epochs", [])
            if ep:
                print("covers %s .. %s" % (ep[0]["time"], ep[-1]["time"]))
        except Exception:
            pass
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
