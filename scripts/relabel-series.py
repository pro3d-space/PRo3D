#!/usr/bin/env python3
"""Re-label a delivered series by what the frames LOOK like, not by how they were made.

    python scripts/relabel-series.py --series COP-2027 \\
        --map delitplain=plain --map smooth=plain \\
        --map delit=micro     --map micro=micro

The renderer's variant names describe a recipe -- `delitplain` is "de-lit texture, no
micro-structure", `smooth` is "constant albedo, no micro-structure" -- and which recipe a
frame got depends on the body, because there is a DRACO mosaic of Dimorphos and none of
Didymos. That is an implementation fact. A consumer of a mission-phase series wants the
folders to be the knob they care about, with the body left where it belongs: in the
sidecar, per frame.

So this folds several variant folders into one, renames each frame to the destination's
tag, and fixes the `FILENAME` the .mbi.json carries -- a rename that left that field
pointing at a file no longer on disk would break the one thing the sidecar is for.

Merging two folders into one requires their epochs to be disjoint, and the script refuses
if they are not: silently dropping one of two frames that want the same name is how a
series ends up quietly missing epochs.
"""

import argparse
import json
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pro3d_sim import frame_paths


def retag(stem, tag):
    """AFC1_DELITPLAIN_20270205_000000 -> AFC1_<tag>_20270205_000000."""
    bits = stem.split("_")
    if len(bits) < 4:
        raise SystemExit("cannot retag %r" % stem)
    return "_".join([bits[0], tag] + bits[-2:])


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--series", required=True)
    ap.add_argument("--map", action="append", required=True, metavar="OLD=NEW",
                    help="variant folder OLD becomes part of folder NEW; repeatable")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()

    mapping = {}
    for m in a.map:
        old, _, new = m.partition("=")
        if not old or not new:
            raise SystemExit("--map takes OLD=NEW, got %r" % m)
        mapping[old] = new

    # plan first, so a collision is found before anything moves
    plan = []                        # (src png path, dst folder, dst stem)
    seen = {}                        # (newfolder, stem) -> source, to catch collisions
    for old, new in mapping.items():
        d = os.path.join(a.series, old)
        if not os.path.isdir(d):
            raise SystemExit("no such variant folder: %s" % d)
        for stem, full in sorted(frame_paths(d).items()):
            day = os.path.basename(os.path.dirname(full))
            tag = new.upper()
            dst_stem = retag(stem, tag)
            key = (new, day, dst_stem)
            if key in seen:
                raise SystemExit(
                    "collision: %s and %s both want %s/%s/%s -- nothing moved"
                    % (seen[key], full, new, day, dst_stem))
            seen[key] = full
            plan.append((full, os.path.join(a.series, new, day), dst_stem, stem))

    by_dst = {}
    for _, dstdir, _, _ in plan:
        by_dst[os.path.basename(os.path.dirname(dstdir))] = 1
    counts = {}
    for _, dstdir, _, _ in plan:
        v = os.path.relpath(dstdir, a.series).split(os.sep)[0]
        counts[v] = counts.get(v, 0) + 1
    for v in sorted(counts):
        print("%-10s <- %d frames" % (v, counts[v]))
    if a.dry_run:
        print("\ndry run: nothing moved")
        return 0

    # Stage through a temp root: a destination folder can share its name with a source
    # (micro -> micro), and moving into a folder that is still being read from is how a
    # rename loses files.
    tmp = os.path.join(a.series, ".relabel-tmp")
    if os.path.exists(tmp):
        shutil.rmtree(tmp)
    moved = 0
    for src, dstdir, dst_stem, src_stem in plan:
        rel = os.path.relpath(dstdir, a.series)
        stage = os.path.join(tmp, rel)
        os.makedirs(stage, exist_ok=True)
        for ext in (".png", ".mbi.json", ".png.json"):
            s = src[:-4] + ext if ext != ".png.json" else src + ".json"
            if os.path.exists(s):
                shutil.move(s, os.path.join(stage, dst_stem + ext))
        # the sidecar names the image; a rename that leaves it stale breaks projection
        mbi = os.path.join(stage, dst_stem + ".mbi.json")
        if os.path.exists(mbi):
            d = json.load(open(mbi, encoding="utf-8"))
            for blk in d.get("fits_hdu_headers", []):
                if "FILENAME" in blk:
                    blk["FILENAME"]["value"] = dst_stem + ".png"
            with open(mbi, "w", encoding="utf-8") as f:
                json.dump(d, f, indent=2)
        moved += 1

    for old in mapping:
        p = os.path.join(a.series, old)
        if os.path.isdir(p):
            shutil.rmtree(p)
    for v in sorted(os.listdir(tmp)):
        shutil.move(os.path.join(tmp, v), os.path.join(a.series, v))
    shutil.rmtree(tmp)
    print("\nrelabelled %d frames" % moved)

    # `stack/` is a flat copy of one variant's frames, thinned for PRo3D's projection
    # stack, and it carries that variant's tag in its filenames too. Leaving it behind
    # would put a DELITPLAIN frame beside a series that no longer has that word in it.
    js0 = os.path.join(a.series, "series.json")
    stack_src = None
    if os.path.exists(js0):
        stack_src = (json.load(open(js0, encoding="utf-8")).get("stack") or {}).get("variant")
    stack_dir = os.path.join(a.series, "stack")
    if stack_src in mapping and os.path.isdir(stack_dir):
        tag = mapping[stack_src].upper()
        n = 0
        for fn in sorted(os.listdir(stack_dir)):
            for ext in (".mbi.json", ".png.json", ".png"):
                if not fn.endswith(ext):
                    continue
                dst = retag(fn[:-len(ext)], tag) + ext
                if dst != fn:
                    os.replace(os.path.join(stack_dir, fn), os.path.join(stack_dir, dst))
                if ext == ".mbi.json":
                    mp = os.path.join(stack_dir, dst)
                    dd = json.load(open(mp, encoding="utf-8"))
                    for blk in dd.get("fits_hdu_headers", []):
                        if "FILENAME" in blk:
                            blk["FILENAME"]["value"] = dst[:-len(ext)] + ".png"
                    with open(mp, "w", encoding="utf-8") as f:
                        json.dump(dd, f, indent=2)
                if ext == ".png":
                    n += 1
                break
        print("relabelled %d stack frames (%s -> %s)" % (n, stack_src, mapping[stack_src]))

    # rebuild the manifest against what is now on disk, keeping the per-epoch target
    js = os.path.join(a.series, "series.json")
    d = json.load(open(js, encoding="utf-8"))
    target = {e["time"]: e.get("target") for e in d["epochs"]}
    files = {}
    for v in sorted(set(mapping.values())):
        for stem, full in frame_paths(os.path.join(a.series, v)).items():
            bits = stem.split("_")
            day, hms = bits[-2], bits[-1]
            t = "%s-%s-%sT%s:%s:%sZ" % (day[0:4], day[4:6], day[6:8],
                                        hms[0:2], hms[2:4], hms[4:6])
            files.setdefault(t, {})[v] = "%s/%s/%s.png" % (v, t[:10], stem)
    d["epochs"] = [{"time": t, "target": target.get(t), "files": files[t]}
                   for t in sorted(files)]
    d["variants"] = {v: {"dir": v, "frames": sum(1 for e in d["epochs"] if v in e["files"])}
                     for v in sorted(set(mapping.values()))}
    d["series"]["epochs"] = len(d["epochs"])
    if isinstance(d.get("stack"), dict) and d["stack"].get("variant") in mapping:
        v = mapping[d["stack"]["variant"]]
        d["stack"]["variant"] = v
        d["stack"]["note"] = ("copies of the %s frames, thinned for PRo3D's 32-layer "
                              "projection stack" % v)
    with open(js, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2)
    print("wrote %s (%d epochs, variants %s)"
          % (js, len(d["epochs"]), ",".join(sorted(d["variants"]))))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
