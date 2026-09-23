#!/usr/bin/env python3
"""Merge per-target series into one, so a phase is one folder rather than several.

    python scripts/merge-series.py --into COP-2027 --from COP-2027-didymos

AFC-1 switches target across a mission phase: over HERA's close-orbit phase it is on
Dimorphos for about half the epochs and on Didymos for most of the rest. Rendering that
needs one run per target, because the target decides the shape model, the body-fixed frame
and what the sidecar's TARGET means -- but the RESULT is one series, not two. An epoch is
covered or it is not.

So this moves the frames of the secondary runs into the primary one and rebuilds the
manifest over the union. Where an epoch exists in more than one run -- both bodies were in
frame, and the camera is the same either way, since it comes from the CK and not from the
target -- the FIRST run listed wins and the others' frames are dropped. That keeps one
rendering per epoch per surface treatment, and the first run should be the one whose target
carries the richer surface.

Moves rather than copies: same volume, so it is a rename, and there is no moment where the
set exists twice on disk.
"""

import argparse
import json
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pro3d_sim import frame_paths


def epochs_of(folder):
    """{epoch iso: {variant: stem}} from a series folder's manifest."""
    js = os.path.join(folder, "series.json")
    if not os.path.exists(js):
        raise SystemExit("no series.json in %s" % folder)
    d = json.load(open(js, encoding="utf-8"))
    out = {}
    for e in d["epochs"]:
        out[e["time"]] = {v: os.path.basename(p)[:-4] for v, p in e["files"].items()}
    return d, out


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--into", required=True, help="the series that keeps its folder")
    ap.add_argument("--add", required=True, nargs="+", help="series to fold into it")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()

    base, kept = epochs_of(a.into)
    print("%-28s %5d epochs, variants %s"
          % (os.path.basename(a.into), len(kept), ",".join(sorted(base["variants"]))))

    merged = dict(kept)
    targets = {t: base["observation"]["body"] for t in kept}
    variant_body = {v: base["observation"]["body"] for v in base["variants"]}
    moved = dropped = 0

    for src in a.add:
        d, eps = epochs_of(src)
        body = d["observation"]["body"]
        for v in d["variants"]:
            variant_body[v] = body
        new = [t for t in eps if t not in merged]
        dup = [t for t in eps if t in merged]
        print("%-28s %5d epochs, variants %s -- %d new, %d already covered"
              % (os.path.basename(src), len(eps), ",".join(sorted(d["variants"])),
                 len(new), len(dup)))
        if a.dry_run:
            continue
        for t in new:
            day = t[:10]
            for v, stem in eps[t].items():
                sd = os.path.join(src, v, day)
                dd = os.path.join(a.into, v, day)
                os.makedirs(dd, exist_ok=True)
                for ext in (".png", ".mbi.json", ".png.json"):
                    s = os.path.join(sd, stem + ext)
                    if os.path.exists(s):
                        shutil.move(s, os.path.join(dd, stem + ext))
                moved += 1
            merged[t] = eps[t]
            targets[t] = body
        dropped += len(dup)
        # whatever is left in the source is a duplicate epoch, by construction
        if not a.dry_run:
            for v in d["variants"]:
                p = os.path.join(src, v)
                if os.path.isdir(p):
                    shutil.rmtree(p)

    if a.dry_run:
        print("\ndry run: nothing moved")
        return 0

    print("\nmoved %d frames, dropped %d duplicate epochs" % (moved, dropped))

    # Rebuild the manifest over the union. Per-epoch `target` is the point of the merge:
    # in a phase where the instrument switches body, "which body is this frame of" is a
    # property of the epoch and not of the series.
    files = {}
    for v in sorted(variant_body):
        d = os.path.join(a.into, v)
        if os.path.isdir(d):
            for stem, full in frame_paths(d).items():
                bits = stem.split("_")
                if len(bits) < 4:
                    continue
                day, hms = bits[-2], bits[-1]
                t = "%s-%s-%sT%s:%s:%sZ" % (day[0:4], day[4:6], day[6:8],
                                            hms[0:2], hms[2:4], hms[4:6])
                files.setdefault(t, {})[v] = "%s/%s/%s.png" % (v, t[:10], stem)

    base["epochs"] = [
        {"time": t, "target": targets.get(t), "files": files[t]}
        for t in sorted(files)
    ]
    base["observation"]["body"] = "DIMORPHOS+DIDYMOS"
    base["observation"]["targetPerEpoch"] = True
    base["variants"] = {
        v: dict(base["variants"].get(v, {}),
                body=variant_body[v],
                frames=sum(1 for e in base["epochs"] if v in e["files"]))
        for v in sorted(variant_body)
    }
    base["series"]["epochs"] = len(base["epochs"])
    base["merged"] = {
        "sources": [os.path.basename(x) for x in [a.into] + a.add],
        "rule": "an epoch present in more than one source keeps the first source's frames",
    }
    with open(os.path.join(a.into, "series.json"), "w", encoding="utf-8") as f:
        json.dump(base, f, indent=2)
    print("wrote %s  (%d epochs)" % (os.path.join(a.into, "series.json"), len(base["epochs"])))

    for src in a.add:
        left = [x for x in os.listdir(src) if x not in ("series.json", "README.md", "stack")]
        if not left:
            shutil.rmtree(src)
            print("removed %s" % src)
        else:
            print("left %s in place; it still holds %s" % (src, left))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
