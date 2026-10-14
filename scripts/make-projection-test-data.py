#!/usr/bin/env python3
"""Generate the AFC-1 projection test data set, and a PRo3D scene set up to use it.

    python scripts/make-projection-test-data.py --opc <Dimorphos OPC> --out <folder>

Renders simulated HERA/AFC-1 frames of Dimorphos with their `.mbi.json` sidecars, and
writes a `ProjectionTest.pro3d` alongside them with everything preset that projection
needs. The frames are self-referential on purpose: each is rendered *from* the shape
model and its sidecar describes *the camera that render actually used*, so projecting one
back onto the same body from its own viewpoint must reproduce it. There is no metadata to
doubt -- if it does not line up, the fault is in the projection.

    test data   the OPC itself; the frames are only meaningful on the shape model they
                were rendered against, so keep them together
    kernels     PRO3D_SPICE_KERNELS, or pass --kernel-root
                git clone https://spiftp.esac.esa.int/git/hera.git
    python      numpy (for the sidecar check); no plotting

Two frame kinds, because no single one serves every check:

    AFC1_<layer>_*   the texture as the camera sees it, unlit. Carries the surface
                     detail that registration and orientation checks need.
    AFC1_LIT_*       lit render -- Lommel-Seeliger, micro-structure, cast shadows,
                     constant albedo. Fills the disk, so its outline is the body's
                     outline and the silhouette check means something.

Both kinds are simulated, so neither is called SIM: the names say what differs, which is
the lighting.

--texture-layer is not cosmetic. The tool otherwise draws the patch's DEFAULT layer,
which is not necessarily the one a PRo3D scene displays, and on Dimorphos the two DRACO
layers are different DART passes over different hemispheres: pick the wrong one and the
frame is nearly black, which any correlation will happily score well. Run with
--list-layers to see what an OPC declares.

See docs/ProjectionValidation.md for what the resulting data is used to prove. For a
*series* of lit frames over a whole rotation rather than these four, see
scripts/make-image-time-series.py and docs/ImageTimeSeries.md; both drive the same tool
through the shared helpers in pro3d_sim.py.
"""

import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pro3d_sim import (check_sidecars, list_layers, run_tool, texture_index,
                       tool_error, write_scene)

# HARD-CODED DEFAULTS, not discovered values. The script does not ask the kernels what
# they cover: doing that properly needs spiceypy (see src/Tests/spice_coverage_tests.py)
# and is not worth the dependency here. So the epoch is a plain --date/--epochs argument
# with a default that is known to work against hera_plan.tm. Point it at a different
# kernel set without changing the date and the renders will simply fail, with SPICE
# saying so.
#
# Why this one: 2027-03-21 lies inside hera_plan.tm's coverage with HERA in close orbit at
# Dimorphos, which puts the ranges at 6.7-8.4 km and the phase angle low enough that the
# lit frames show the whole disk. Dimorphos rotates in about 11.9 h, so epochs 3 h apart
# are roughly a quarter turn each: the four frames see genuinely different faces, which is
# what makes the set useful as a stack and as a test that flying between them carries the
# scene clock along.
#
# Change --date/--epochs for a different mission phase or kernel set. The sidecar check at
# the end prints each frame's range, which is the quickest way to see whether the geometry
# the new epoch produced is sensible.
DEFAULT_EPOCHS = ["14:00:00", "17:00:00", "20:00:00", "23:00:00"]
DEFAULT_DATE = "2027-03-21"


def main():
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--opc", required=True, help="Dimorphos OPC directory")
    ap.add_argument("--out", required=True, help="where to write the data set")
    ap.add_argument("--texture-layer", default="DRACO_2",
                    help="texture layer for the unlit frames (default DRACO_2)")
    ap.add_argument("--kernel-root", default=None,
                    help="SPICE kernel tree; defaults to $PRO3D_SPICE_KERNELS")
    ap.add_argument("--gain", default="4.492",
                    help="fixed gain for the lit frames, so a series is comparable")
    ap.add_argument("--scene-template", default=None,
                    help="a .pro3d to derive ProjectionTest.pro3d from; skipped if absent")
    ap.add_argument("--date", default=DEFAULT_DATE,
                    help="UTC date for the frames (default %s); must lie inside the "
                         "loaded kernels' coverage" % DEFAULT_DATE)
    ap.add_argument("--epochs", default=",".join(DEFAULT_EPOCHS),
                    help="comma-separated UTC times on that date (default %s)"
                         % ",".join(DEFAULT_EPOCHS))
    ap.add_argument("--list-layers", action="store_true",
                    help="print the OPC's texture layers and exit")
    a = ap.parse_args()
    date = a.date
    epochs = [e.strip() for e in a.epochs.split(",") if e.strip()]
    if not epochs:
        print("--epochs is empty")
        return 2
    # the scene's camera goes on one frame's axis; the third by default, which in the
    # shipped set is the closest pass (6.7 km) and so the most detailed frame
    best_epoch = epochs[2] if len(epochs) > 2 else epochs[-1]

    common = ["--opc", a.opc, "--body", "DIMORPHOS", "--frame", "DIMORPHOS_FIXED",
              "--observer", "HERA", "--instrument", "HERA_AFC-1"]
    if a.kernel_root:
        common += ["--kernel-root", a.kernel_root]

    if a.list_layers:
        print(list_layers(repo, common, "%sT%sZ" % (date, epochs[0])))
        return 0

    os.makedirs(a.out, exist_ok=True)
    print("rendering into %s" % a.out)

    # underscores out of the layer name, so DRACO_2 gives AFC1_DRACO2_... and the
    # epoch stays the only underscore-separated field in the stem
    tag = a.texture_layer.replace("_", "")

    failed = []
    for t in epochs:
        stamp = "%s_%s" % (date.replace("-", ""), t.replace(":", ""))
        iso = "%sT%sZ" % (date, t)
        print(" %s  unlit (%s)" % (iso, a.texture_layer))
        p1 = run_tool(repo, ["simulate-image"] + common +
                      ["--time", iso, "--texture-only", "--texture-layer", a.texture_layer,
                       "--write-mbi", "--out",
                       os.path.join(a.out, "AFC1_%s_%s.png" % (tag, stamp))])
        print(" %s  lit" % iso)
        p2 = run_tool(repo, ["simulate-image"] + common +
                      ["--time", iso, "--gain", a.gain, "--write-mbi", "--out",
                       os.path.join(a.out, "AFC1_LIT_%s.png" % stamp)])
        # A failed render leaves no files behind, and the sidecar check below only looks
        # at what IS there -- so without this a half-empty data set reports "all sidecars
        # pass". The usual cause is an epoch where the body is not in the instrument's
        # field of view, or one outside the kernels' coverage; the tool says which.
        for kind, pr in (("unlit", p1), ("lit", p2)):
            if pr.returncode != 0:
                failed.append((iso, kind, tool_error(pr)))

    print("\nsidecar check -- A^T*TRG_POS must be close to (0, 0, +1):")
    bad, _ = check_sidecars(a.out)

    # the scene's camera comes from a frame's sidecar, so there is nothing to write it
    # from when the renders failed -- report those below instead of a FileNotFoundError
    if a.scene_template and os.path.exists(a.scene_template) and not failed:
        idx = texture_index(repo, common, "%sT%sZ" % (date, epochs[0]), a.texture_layer)
        if idx is None:
            print("could not resolve '%s' to an index; scene not written" % a.texture_layer)
        else:
            best = "%s_%s" % (date.replace("-", ""), best_epoch.replace(":", ""))
            out = os.path.join(a.out, "ProjectionTest.pro3d")
            write_scene(a.scene_template, out, a.opc, a.texture_layer, idx,
                        os.path.join(a.out, "AFC1_%s_%s.mbi.json" % (tag, best)),
                        "%sT%s.0000000Z" % (date, best_epoch),
                        a.kernel_root or os.environ.get("PRO3D_SPICE_KERNELS"))
            print("\nwrote %s (texture %s index %d, camera on the %s frame's axis)"
                  % (out, a.texture_layer, idx, best_epoch))

    if failed:
        print("\n%d of %d renders FAILED:" % (len(failed), 2 * len(epochs)))
        for iso, kind, why in failed:
            print("   %s %-5s  %s" % (iso, kind, why))
    if bad:
        print("\n%d sidecar(s) FAILED the boresight invariant" % bad)
    if failed or bad:
        return 1
    print("\nall %d renders succeeded and every sidecar passes the boresight invariant"
          % (2 * len(epochs)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
