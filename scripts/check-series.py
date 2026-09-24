#!/usr/bin/env python3
"""Re-check an existing series against its own SPICE reference.

    python scripts/check-series.py --series <folder>

`make-image-time-series.py` already runs this check as a stage of producing a series and
records the verdict in `series.json`, so a folder arrives validated. This re-runs it --
on a folder someone sent you, on one whose frames have been touched since, or with
different thresholds than the run used.

The check itself lives in `pro3d_sim.validate_series`, not here: one implementation, so
the verdict recorded at generation time and the verdict you get now cannot disagree for
reasons other than the data.

Needs numpy and pillow.
"""

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pro3d_sim import print_validation, validate_series


NOTE = ("how far another transform must beat identity before it counts as a failure (default 0.05). Calibrated, not guessed: across both series a CORRECT frame's identity beats the runner-up by 0.12 to 0.73, while the near-symmetric views where flipUD edges ahead do so by at most 0.023. 0.05 sits in the empty band between those two populations")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--series", required=True, help="a series folder holding series.json")
    ap.add_argument("--size", type=int, default=340, help="crop size in px (default 340)")
    ap.add_argument("--threshold", type=int, default=25,
                    help="DN above which a pixel counts as body, for both sides (default 25)")
    ap.add_argument("--min-correlation", type=float, default=0.5,
                    help="below this a pair is reported as weak, never as failed (default 0.5)")
    ap.add_argument("--margin", type=float, default=0.05, help="%s" % NOTE)
    ap.add_argument("--update", action="store_true",
                    help="write the result back into series.json, replacing the verdict "
                         "recorded when the series was generated. For re-validating a "
                         "folder after the check itself changed, without re-rendering it.")
    ap.add_argument("--json", action="store_true",
                    help="print the result as JSON -- the same object series.json carries")
    a = ap.parse_args()

    path = os.path.join(a.series, "series.json")
    if not os.path.isfile(path):
        print("no series.json in %s" % a.series)
        return 2
    manifest = json.load(open(path, encoding="utf-8"))

    result = validate_series(a.series, manifest, size=a.size, threshold=a.threshold,
                             min_correlation=a.min_correlation, margin=a.margin)

    if a.update:
        manifest["validation"] = result
        with open(path, "w", encoding="utf-8") as f:
            json.dump(manifest, f, indent=2)
        print("updated %s" % path)

    if a.json:
        print(json.dumps(result, indent=2))
    else:
        print_validation(result)
        recorded = (manifest.get("validation") or {}).get("verdict")
        if recorded and recorded != result["verdict"] and not a.update:
            print("\nNOTE: series.json records '%s' but this run says '%s'. The frames or "
                  "the thresholds have changed since the series was generated."
                  % (recorded, result["verdict"]))

    return {"pass": 0, "fail": 1, "skipped": 2}[result["verdict"]]


if __name__ == "__main__":
    raise SystemExit(main())
