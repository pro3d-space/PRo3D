#!/usr/bin/env python3
"""Write the top-level README for a folder holding several image series.

    python scripts/make-series-index.py --root <folder>

Each subfolder that has a `series.json` is one series. Everything printed here is read
back out of those manifests rather than restated, so the index cannot drift from the data
it describes -- the failure mode this whole delivery has already hit once, when a folder
ended up holding frames from two different renderer builds and nothing said so.
"""

import argparse
import json
import os

HEADER = """# Simulated HERA/AFC-1 image series -- Dimorphos

Simulated images, not observations. Rendered from a shape model with PRo3D's
`pro3d-tool simulate-image` using SPICE geometry, for testing reconstruction and
projection workflows before real images exist.

**{n} series**, each self-contained in its own folder with its own README and manifest:

{overview}
## Which one to use

{guidance}
## What every series folder contains

{layout}
## Shared facts

| | |
|---|---|
| body / frame | {body} / {frame} |
| observer / instrument | {observer} / {instrument} |
| image | {size} px, 8-bit greyscale PNG |
| SPICE kernel | `{mkid}` |
| shape model | `{opc}` |

**The kernel version is load-bearing.** ESA regenerates the HERA kernels and they move the
spacecraft: re-rendering these epochs against a different delivery gives different images,
and an epoch framed in one set can be off-target in another. Quote `{mkid}`.

**The frames belong to this shape model.** Each sidecar describes the camera that render
actually used, so projecting a frame back onto *this* OPC reproduces it. Against a
different shape model that guarantee is void.

**Every frame in a series comes from one renderer build.** The generator records a build
fingerprint in `series.json` and re-renders the whole series when it changes, because
mixing builds silently mixes geometry.
"""


def load(root):
    out = []
    for name in sorted(os.listdir(root)):
        p = os.path.join(root, name, "series.json")
        if os.path.isfile(p):
            try:
                out.append((name, json.load(open(p, encoding="utf-8"))))
            except Exception as e:
                print("  skipping %s: %s" % (name, e))
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", required=True, help="folder holding the series subfolders")
    a = ap.parse_args()

    series = load(a.root)
    if not series:
        print("no series.json found under %s" % a.root)
        return 2

    rows = ["| folder | epochs | cadence | span | range | apparent size |",
            "|---|---|---|---|---|---|"]
    for name, m in series:
        s, ep = m["series"], m.get("epochs", [])
        rng = [e["rangeMeters"] for e in ep if e.get("rangeMeters")]
        px = [176.9 / r / 0.0000937 for r in rng] if rng else []
        rows.append("| [`%s/`](%s/) | %d | %g min | %.2f h (%.2f rot) | %.1f-%.1f km | %d-%d px |"
                    % (name, name, s["epochs"], s["intervalMinutes"], s["spanHours"],
                       s["rotations"],
                       min(rng) / 1000.0 if rng else 0, max(rng) / 1000.0 if rng else 0,
                       min(px) if px else 0, max(px) if px else 0))

    # guidance is derived: whichever series shows the body largest is the detail one
    def med_px(m):
        rng = [e["rangeMeters"] for e in m.get("epochs", []) if e.get("rangeMeters")]
        return sorted(176.9 / r / 0.0000937 for r in rng)[len(rng) // 2] if rng else 0
    ranked = sorted(series, key=lambda kv: -med_px(kv[1]))
    guide = []
    if len(ranked) > 1:
        # "at the finer cadence" is only true if it IS the finer one -- derived, not
        # asserted, because an index that restates what it did not read is how a folder
        # ends up describing frames it does not hold
        finest = min(kv[1]["series"]["intervalMinutes"] for kv in ranked)
        cadence = (" and at the finest cadence here (%g min)" % finest
                   if ranked[0][1]["series"]["intervalMinutes"] == finest else "")
        guide.append("- **`%s/`** shows the body largest (median %d px across a 1020 px "
                     "frame)%s -- use it when surface detail or frame-to-frame continuity "
                     "matters." % (ranked[0][0], med_px(ranked[0][1]), cadence))
        guide.append("- **`%s/`** is the wider-range series (median %d px) -- use it for "
                     "whole-body geometry, coverage and projection-stack work."
                     % (ranked[-1][0], med_px(ranked[-1][1])))
    else:
        guide.append("- Only one series is present.")

    first = series[0][1]
    layout = ["```"]
    for v, info in sorted(first.get("variants", {}).items()):
        layout.append("%-14s %s" % (v + "/", info.get("description", "")))
    st = first.get("stack", {})
    layout.append("%-14s %s" % ("stack/", st.get("note", "subset for the projection stack")))
    layout.append("%-14s %s" % (first.get("scene") or "(scene)",
                                "PRo3D scene: body, frame, kernel, epoch, focal length"))
    layout.append("%-14s %s" % ("README.md", "that series in detail"))
    layout.append("%-14s %s" % ("series.json", "machine-readable, one entry per epoch"))
    layout.append("```")

    obs, prov = first["observation"], first["provenance"]
    text = HEADER.format(
        n=len(series),
        overview="\n".join(rows) + "\n",
        guidance="\n".join(guide) + "\n",
        layout="\n".join(layout) + "\n",
        body=obs["body"], frame=obs["frame"],
        observer=obs["observer"], instrument=obs["instrument"],
        size="x".join(str(x) for x in obs["imageSize"]),
        mkid=prov.get("mkIdentifier") or "UNRECORDED",
        opc=prov.get("opcProduct") or os.path.basename(prov.get("opc", "")))
    out = os.path.join(a.root, "README.md")
    with open(out, "w", encoding="utf-8") as f:
        f.write(text)
    print("wrote %s describing %d series" % (out, len(series)))
    for name, m in series:
        print("   %-32s %d epochs, %s variants"
              % (name, m["series"]["epochs"], ",".join(sorted(m.get("variants", {})))))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
