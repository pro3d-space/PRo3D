#!/usr/bin/env python3
"""Render a time series of simulated HERA/AFC-1 frames over one Dimorphos rotation.

    python scripts/make-image-time-series.py --out <folder>

Drives `pro3d-tool simulate-image` once per epoch on a fixed cadence, writing lit frames
with their `.mbi.json` sidecars, a coarsely sampled subset ready to import into the
viewer, and a scene set up to project them.

    <out>/micro/           every epoch WITH micro-structure, PNG + .mbi.json + .png.json
    <out>/smooth/          every epoch WITHOUT it, same cameras
    <out>/stack/           the --stack-count subset, evenly spaced over the series
    <out>/ImageSeries.pro3d  scene with body, frame, kernel, epoch and focal length set
    <out>/series.json      what was rendered, with what kernels, at what gain
    <out>/README.md        the same, for whoever receives the folder

Two lit variants per epoch, same camera and same --gain, so a pair differs in exactly one
thing:

    AFC1_MICRO_<stamp>     micro-structure on (--micro-amplitude 0.3) -- the realistic one
    AFC1_SMOOTH_<stamp>    micro-structure off -- the bare shape under the same light

Both are lit (Lommel-Seeliger, cast shadows, constant albedo); neither is texture-only.
Across the series the illumination and the visible face change while the exposure does
not, which is what makes it a series rather than a set of unrelated renders -- pass
--gain 0 to auto-expose each frame separately and that property is gone.

Why a rotation: Dimorphos turns in about 11.9 h, so the default span shows every face
once. A 15 min cadence over that is ~48 epochs; the projection stack caps at 32
(ProjectedImages.maxCount), and 48 layers would be unreadable anyway, so <out>/stack/
holds an evenly spaced subset -- 15 by default, i.e. one every ~48 min, ~24 deg of
rotation apart.

    test data   the OPC itself; the frames are only meaningful on the shape model they
                were rendered against, so keep them together. Defaults to
                $PRO3D_TEST_DATA/HERA/Dimorphos_opc/Dimorphos.
    kernels     $PRO3D_SPICE_KERNELS, or --kernel-root; --kernel picks the metakernel
                (the tool's default is <root>/mk/hera_plan.tm)
    python      numpy (for the sidecar check); no plotting

Re-runnable: a frame whose PNG is already there is skipped, so an interrupted run
continues where it stopped and a changed --stack-count costs nothing. Pass --force to
re-render. Changing --interval, --start or --kernel produces different frames, so use a
different --out (or --force) -- the stamps of a finer cadence otherwise interleave with a
coarser earlier run and series.json will describe only the last one.

THE EPOCH AND THE KERNEL SET GO TOGETHER. ESA regenerates the HERA plan kernels, and they
move the spacecraft: the default start lies inside hera_plan.tm's close-orbit coverage for
the tree this was written against (v182_20260820), where ranges are 6.7-8.4 km and the lit
frames show the whole disk. Against another tree the same epoch can put the body outside
the AFC frame, and the render fails with SPICE saying so. series.json records the
metakernel's MK_IDENTIFIER for exactly this reason.
"""

import argparse
import datetime
import json
import os
import shutil
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pro3d_sim import (check_sidecars, list_layers, run_tool, texture_index,
                       tool_error, write_scene)

# Dimorphos' rotation period. Tidally locked to Didymos, 11.92 h after the DART impact
# (Naidu et al. 2024) -- the pre-impact 11.92 h orbital period was shortened to ~11.37 h,
# so this is the post-impact value and only the default span, not a constant to rely on.
ROTATION_HOURS = 11.92

# 13:00, not 14:00: the camera follows HERA's CK, so an epoch is only renderable while the
# instrument is actually pointed at Dimorphos. Against hera_plan_v182_20260820_001 that
# window runs 2027-03-20T19:15 .. 2027-03-22T01:25, and a rotation started at 14:00 runs
# 20 min past its end -- the last two epochs render nothing and the verb says so. Started
# at 13:00 the whole 11.92 h fits inside the window with ~40 min to spare.
DEFAULT_START = "2027-03-21T13:00:00Z"
# ProjectedImages.maxCount in src/PRo3D.Core/ProjectedImageList-Model.fs -- the shader's
# uniform arrays are sized for it, so the viewer silently refuses layer 33.
MAX_STACK = 32


def parse_iso(s):
    t = s.strip().replace("Z", "+00:00")
    d = datetime.datetime.fromisoformat(t)
    return d.replace(tzinfo=None) if d.tzinfo is None else d.astimezone(datetime.timezone.utc).replace(tzinfo=None)


def iso(d):
    return d.strftime("%Y-%m-%dT%H:%M:%SZ")


def stamp(d):
    return d.strftime("%Y%m%d_%H%M%S")


def resolve_kernel(kernel, kernel_root):
    """The metakernel the tool will actually use, for the record in series.json."""
    if kernel:
        return os.path.abspath(kernel)
    root = kernel_root or os.environ.get("PRO3D_SPICE_KERNELS")
    if not root:
        return None
    if not os.path.isdir(os.path.join(root, "mk")):
        root = os.path.join(root, "kernels")
    return os.path.join(root, "mk", "hera_plan.tm")


def mk_identifier(path):
    """MK_IDENTIFIER out of a metakernel: the version string to quote when a render is
    questioned. The kernels move between releases and the images move with them."""
    if not path or not os.path.exists(path):
        return None
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            for line in f:
                if "MK_IDENTIFIER" in line:
                    _, _, rest = line.partition("=")
                    return rest.strip().strip("(){}").strip().strip("'\"") or None
    except OSError:
        return None
    return None


def subset(items, n):
    """n items spread evenly over the list, first and last included.

    Not items[::step]: a step that does not divide the length drops the end of the series,
    and the last frame is the one that closes the rotation.
    """
    if n >= len(items):
        return list(items)
    if n <= 1:
        return [items[0]]
    last = len(items) - 1
    idx = sorted({int(round(i * last / (n - 1.0))) for i in range(n)})
    return [items[i] for i in idx]


def preflight(epochs, kernel, instrument, target, observer):
    """Which epochs actually have the instrument pointed at the target.

    The camera follows the CK, so an epoch where the spacecraft was observing something
    else renders nothing -- correctly, but only after the OPC load and the shadow map. Over
    a rotation that is minutes of wasted work at the end of a run, and the reason is not
    obvious from a missing file. Checking first costs milliseconds.

    Needs spiceypy. Without it the check is skipped and the tool still reports each miss
    per frame, so this is an optimisation and a clearer report, never a correctness
    requirement.

    Returns (angles, halffov, None) or (None, None, reason-it-was-skipped).
    """
    try:
        import spiceypy as sp
    except ImportError:
        return None, None, "spiceypy not installed"
    if not kernel or not os.path.exists(kernel):
        return None, None, "metakernel not found (%s)" % kernel
    cwd = os.getcwd()
    try:
        sp.kclear()
        os.chdir(os.path.dirname(kernel))
        sp.furnsh(os.path.basename(kernel))
        try:
            # the FOV's own bounds, rather than a number repeated from the docs
            code = sp.bodn2c(instrument)
            _, _, bore, _, bounds = sp.getfov(code, 8)
            half = max(np.degrees(np.arccos(np.clip(np.dot(b / np.linalg.norm(b),
                                                           bore / np.linalg.norm(bore)), -1, 1)))
                       for b in bounds)
        except Exception:
            return None, None, "could not read %s's FOV from the kernels" % instrument
        angles = {}
        for e in epochs:
            try:
                et = sp.str2et(iso(e).replace("Z", ""))
                pos, _ = sp.spkpos(target, et, "J2000", "NONE", observer)
                d = pos / np.linalg.norm(pos)
                b = sp.pxform(instrument, "J2000", et) @ np.array([0.0, 0.0, 1.0])
                angles[iso(e)] = float(np.degrees(np.arccos(np.clip(np.dot(b, d), -1, 1))))
            except Exception:
                angles[iso(e)] = None      # no attitude or no ephemeris: let the tool say so
        return angles, float(half), None
    except Exception as ex:
        return None, None, str(ex)
    finally:
        os.chdir(cwd)
        try:
            import spiceypy as sp
            sp.kclear()
        except Exception:
            pass


README = """# Simulated {instrument} images -- {body}

Simulated images, not observations. Rendered from a shape model with PRo3D
`pro3d-tool simulate-image` using SPICE geometry.

| | |
|---|---|
| kernel | `{mkid}` |
| shape model | `{opcname}` |
| body / frame | {body} / {frame} |
| observer / instrument | {observer} / {instrument} |
| epochs | {start} .. {end} UTC |
| cadence | {interval:g} min, {count} epochs ({span:.2f} h = {rotations:.2f} rotations) |
| range | {rmin:.2f} .. {rmax:.2f} km |
| image | {width} px, 8-bit greyscale PNG |
| gain | {gain} (fixed, not auto-exposed) |
| generated | {generated} |

## Files

| | |
|---|---|
| `micro/` | all {count} epochs, micro-structure on |
| `smooth/` | all {count} epochs, micro-structure off, same cameras |
| `.png` | the image |
| `.mbi.json` | `SC_QUAT0..3` = spacecraft -> J2000 quaternion; `TRG_POSX/Y/Z` = target minus spacecraft, km, J2000 |
| `.png.json` | pixel size |
| `stack/` | {nstack} {stackvariant} frames, evenly spaced; PRo3D projection stack caps at 32 |
| `series.json` | the same data per frame, machine-readable |
| `{scene}` | PRo3D scene: body, frame, kernel, epoch, focal length preset |

In PRo3D: open the scene, GIS tab -> Projected Images -> Import Directory -> `stack/`,
`+` per row, Orientation Source MBI, Transfer Function off.

## Facts to keep with the data

- Re-rendering against a different kernel delivery gives different images. Quote `{mkid}`.
- The frames belong to this shape model; sidecars describe the camera each render used.
- Pointing comes from the CK. Only epochs with {instrument} on target exist here.
- No detector model (no PSF, noise, quantisation). No phase function.
- Geometric positions: no light-time or stellar aberration.
- Micro-structure is shading only, not topography.
- All sidecars pass the boresight invariant.
"""


def write_readme(path, **kw):
    with open(path, "w", encoding="utf-8") as f:
        f.write(README.format(**kw))


def main():
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    testdata = os.environ.get("PRO3D_TEST_DATA")
    default_opc = os.path.join(testdata, "HERA", "Dimorphos_opc", "Dimorphos") if testdata else None
    default_template = (os.path.join(testdata, "HERA", "Dimorphos_opc", "AFC_2027-03-21",
                                     "ProjectionTest.pro3d") if testdata else None)

    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--opc", default=default_opc, required=default_opc is None,
                    help="Dimorphos OPC directory (default: $PRO3D_TEST_DATA/HERA/Dimorphos_opc/Dimorphos)")
    ap.add_argument("--out", required=True, help="where to write the series")
    ap.add_argument("--start", default=DEFAULT_START,
                    help="first epoch, ISO-8601 UTC (default %s); must lie inside the "
                         "loaded kernels' coverage" % DEFAULT_START)
    ap.add_argument("--interval", type=float, default=15.0,
                    help="cadence in minutes (default 15)")
    ap.add_argument("--duration", type=float, default=ROTATION_HOURS,
                    help="span in hours (default %.2f, one Dimorphos rotation); ignored "
                         "when --count is given" % ROTATION_HOURS)
    ap.add_argument("--count", type=int, default=None,
                    help="number of epochs; overrides --duration")
    ap.add_argument("--kernel", default=None,
                    help="explicit SPICE metakernel (default: <kernel-root>/mk/hera_plan.tm)")
    ap.add_argument("--kernel-root", default=None,
                    help="SPICE kernel tree; defaults to $PRO3D_SPICE_KERNELS")
    ap.add_argument("--gain", default="4.492",
                    help="fixed I/F->DN gain, so the series is radiometrically comparable "
                         "(default 4.492); 0 auto-exposes every frame on its own")
    ap.add_argument("--albedo", default=None, help="normal reflectance (tool default 0.16)")
    ap.add_argument("--micro-scale", default="3.0",
                    help="micro-structure feature size in metres (default 3.0). Bigger "
                         "than the tool's 0.5 on purpose: at 7-8 km AFC sees ~0.7 m/px, "
                         "and structure below the pixel averages out to nothing")
    ap.add_argument("--micro-amplitude", default="0.3",
                    help="normal perturbation strength of the MICRO variant (default 0.3); "
                         "the SMOOTH variant is always 0")
    ap.add_argument("--distance", default=None,
                    help="camera range override in metres; default is the spacecraft's real range")
    ap.add_argument("--variants", default="micro,smooth",
                    help="which lit variants to render, comma-separated (default micro,smooth)")
    ap.add_argument("--stack-count", type=int, default=15,
                    help="how many frames to copy into <out>/stack/ (default 15, cap %d)" % MAX_STACK)
    ap.add_argument("--stack-variant", default="micro", choices=["micro", "smooth"],
                    help="which variant the stack subset takes (default micro)")
    ap.add_argument("--texture-layer", default="DRACO_2",
                    help="texture layer the scene displays under the projection (default DRACO_2)")
    ap.add_argument("--scene-template", default=default_template,
                    help="a .pro3d to derive ImageSeries.pro3d from; skipped if absent")
    ap.add_argument("--force", action="store_true", help="re-render frames that already exist")
    ap.add_argument("--list-layers", action="store_true",
                    help="print the OPC's texture layers and exit")
    a = ap.parse_args()

    try:
        start = parse_iso(a.start)
    except ValueError:
        print("--start is not an ISO-8601 time: %s" % a.start)
        return 2
    if a.interval <= 0:
        print("--interval must be positive")
        return 2

    count = a.count if a.count else max(1, int(round(a.duration * 60.0 / a.interval)))
    epochs = [start + datetime.timedelta(minutes=a.interval * i) for i in range(count)]
    span = (epochs[-1] - epochs[0]).total_seconds() / 3600.0

    variants = [v.strip().lower() for v in a.variants.split(",") if v.strip()]
    unknown = [v for v in variants if v not in ("micro", "smooth")]
    if unknown or not variants:
        print("--variants takes micro and/or smooth, got %s" % a.variants)
        return 2
    if a.stack_variant not in variants:
        print("--stack-variant %s is not among --variants %s" % (a.stack_variant, ",".join(variants)))
        return 2
    if a.stack_count > MAX_STACK:
        print("note: --stack-count %d exceeds the viewer's cap of %d; clamping"
              % (a.stack_count, MAX_STACK))
    stack_count = max(0, min(a.stack_count, MAX_STACK))

    common = ["--opc", a.opc, "--body", "DIMORPHOS", "--frame", "DIMORPHOS_FIXED",
              "--observer", "HERA", "--instrument", "HERA_AFC-1"]
    if a.kernel:
        common += ["--kernel", a.kernel]
    if a.kernel_root:
        common += ["--kernel-root", a.kernel_root]

    if a.list_layers:
        print(list_layers(repo, common, iso(epochs[0])))
        return 0

    kernel = resolve_kernel(a.kernel, a.kernel_root)
    mkid = mk_identifier(kernel)
    # One folder per variant, so each is directly importable: PRo3D's Import Directory
    # takes a folder, and a folder holding both variants would load two layers per epoch.
    # It also keeps the README, the scene and the stack out of the data.
    vdir = {v: os.path.join(a.out, v) for v in variants}
    for d in vdir.values():
        os.makedirs(d, exist_ok=True)

    print("%d epochs, %s .. %s, every %g min (%.2f h, %.2f rotations)"
          % (count, iso(epochs[0]), iso(epochs[-1]), a.interval, span, span / ROTATION_HOURS))
    print("kernel %s%s" % (kernel or "(tool default)", "  [%s]" % mkid if mkid else ""))

    # Pointing pre-flight. The camera is aimed by the kernels, so epochs where the
    # instrument was observing something else cannot produce a frame -- drop them here,
    # visibly, instead of rendering into a guaranteed failure at the end of a long run.
    offtarget = []
    angles, halffov, why = preflight(epochs, kernel, "HERA_AFC-1", "DIMORPHOS", "HERA")
    if angles is None:
        print("pointing pre-flight skipped (%s); the tool still checks each frame" % why)
    else:
        keep = []
        for e in epochs:
            ang = angles.get(iso(e))
            if ang is not None and ang > halffov:
                offtarget.append((iso(e), ang))
            else:
                keep.append(e)
        if offtarget:
            print("pointing: %d of %d epochs are OFF TARGET (%s half-FOV %.3f deg) and are excluded:"
                  % (len(offtarget), len(epochs), "HERA_AFC-1", halffov))
            for t, ang in offtarget[:6]:
                print("   %s  boresight %.3f deg off DIMORPHOS" % (t, ang))
            if len(offtarget) > 6:
                print("   ... and %d more" % (len(offtarget) - 6))
            print("   (the instrument was pointed elsewhere; these epochs cannot be rendered)")
        if not keep:
            print("\nno epoch in this range has HERA_AFC-1 pointed at DIMORPHOS -- nothing to render.")
            return 2
        epochs = keep
        count = len(epochs)
        span = (epochs[-1] - epochs[0]).total_seconds() / 3600.0
        print("pointing: %d epochs on target (max %.3f deg off, half-FOV %.3f deg)"
              % (count, max((angles[iso(e)] or 0.0) for e in epochs), halffov))
    print("rendering %d frame(s) per epoch into %s" % (len(variants), ", ".join(sorted(vdir))))

    # micro-structure is the only difference between the two, so everything else that
    # touches brightness is shared -- an unequal albedo or gain would make the pair
    # incomparable and the comparison is the reason both exist
    shared = ["--gain", a.gain, "--micro-scale", a.micro_scale, "--write-mbi"]
    if a.albedo:
        shared += ["--albedo", a.albedo]
    if a.distance:
        shared += ["--distance", a.distance]
    flags = {"micro": ["--micro-amplitude", a.micro_amplitude],
             "smooth": ["--micro-amplitude", "0"]}

    failed, rendered, skipped = [], [], 0
    t0 = time.time()
    for i, e in enumerate(epochs):
        for v in variants:
            stem = "AFC1_%s_%s" % (v.upper(), stamp(e))
            png = os.path.join(vdir[v], stem + ".png")
            if os.path.exists(png) and not a.force:
                skipped += 1
                rendered.append((stem, iso(e), v))
                continue
            done = len(rendered) - skipped
            eta = ""
            if done > 0:
                per = (time.time() - t0) / done
                left = (count - i) * len(variants) - variants.index(v)
                eta = "  ~%d min left" % max(0, int(per * left / 60.0))
            print(" [%d/%d] %s  %s%s" % (i + 1, count, iso(e), v, eta))
            p = run_tool(repo, ["simulate-image"] + common + shared + flags[v] +
                         ["--time", iso(e), "--out", png])
            # A failed render leaves no files behind, and the sidecar check below only
            # looks at what IS there -- so without this a half-empty series reports "all
            # sidecars pass". The usual cause is an epoch where the body is not in the
            # instrument's field of view, or one outside the kernels' coverage.
            if p.returncode != 0:
                failed.append((iso(e), v, tool_error(p)))
            else:
                rendered.append((stem, iso(e), v))

    if skipped:
        print("\n%d frame(s) already present, skipped (--force re-renders)" % skipped)

    print("\nsidecar check -- A^T*TRG_POS must be close to (0, 0, +1):")
    bad, ranges = 0, {}
    for v in sorted(vdir):
        b, r = check_sidecars(vdir[v])
        bad += b
        ranges.update(r)

    # the subset comes from the epochs that actually produced a frame, so a failed render
    # in the middle shifts the sampling instead of putting a hole in the stack
    ok = [(s, t) for (s, t, v) in rendered if v == a.stack_variant and s in ranges]
    ok.sort(key=lambda x: x[1])
    chosen = subset(ok, stack_count) if stack_count else []
    stack_dir = os.path.join(a.out, "stack")
    if chosen:
        if os.path.isdir(stack_dir):
            shutil.rmtree(stack_dir)
        os.makedirs(stack_dir)
        for stem, _ in chosen:
            # .png.json, not .json: --write-mbi names the statistics sidecar after the
            # image file, extension included. Copying "<stem>.json" silently copies
            # nothing and the subset arrives without the pixel size unproject needs.
            for ext in (".png", ".mbi.json", ".png.json"):
                src = os.path.join(vdir[a.stack_variant], stem + ext)
                if os.path.exists(src):
                    shutil.copy2(src, os.path.join(stack_dir, stem + ext))
        first, last = chosen[0][1], chosen[-1][1]
        step = (parse_iso(last) - parse_iso(first)).total_seconds() / 60.0 / max(1, len(chosen) - 1)
        print("\nwrote %s: %d %s frames, %s .. %s, ~%.0f min apart"
              % (stack_dir, len(chosen), a.stack_variant, first, last, step))
        print("   import this folder in the GIS tab (Projected Images -> Import Directory)")

    scene = None
    if a.scene_template and os.path.exists(a.scene_template) and chosen:
        idx = texture_index(repo, common, iso(epochs[0]), a.texture_layer)
        if idx is None:
            print("could not resolve '%s' to an index; scene not written" % a.texture_layer)
        else:
            # the camera goes on the middle subset frame's axis: the series' own midpoint,
            # and the scene clock with it, so the sun matches what that frame saw
            mid_stem, mid_iso = chosen[len(chosen) // 2]
            scene = os.path.join(a.out, "ImageSeries.pro3d")
            write_scene(a.scene_template, scene, a.opc, a.texture_layer, idx,
                        os.path.join(vdir[a.stack_variant], mid_stem + ".mbi.json"),
                        mid_iso.replace("Z", ".0000000Z"),
                        a.kernel_root or os.environ.get("PRO3D_SPICE_KERNELS"), a.kernel)
            print("wrote %s (texture %s index %d, camera on %s)"
                  % (scene, a.texture_layer, idx, mid_stem))

    manifest = {
        "generated": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "opc": os.path.abspath(a.opc),
        "body": "DIMORPHOS", "frame": "DIMORPHOS_FIXED",
        "observer": "HERA", "instrument": "HERA_AFC-1",
        "kernel": kernel, "mkIdentifier": mkid,
        "start": iso(epochs[0]), "end": iso(epochs[-1]),
        "intervalMinutes": a.interval, "count": count,
        "spanHours": round(span, 3), "rotations": round(span / ROTATION_HOURS, 3),
        "gain": a.gain, "microScale": a.micro_scale, "microAmplitude": a.micro_amplitude,
        "variants": variants,
        "frames": [{"stem": s, "time": t, "variant": v, "rangeMeters": ranges.get(s)}
                   for (s, t, v) in sorted(rendered, key=lambda r: (r[1], r[2]))],
        "stack": [s for s, _ in chosen],
        "stackVariant": a.stack_variant,
        "scene": scene,
        "failed": [{"time": t, "variant": v, "error": w} for (t, v, w) in failed],
        "skippedOffTarget": [{"time": t, "boresightOffDeg": round(ang, 4)} for (t, ang) in offtarget],
    }
    with open(os.path.join(a.out, "series.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
    print("wrote %s" % os.path.join(a.out, "series.json"))

    # A README travels with the data: whoever receives this folder has neither this
    # terminal nor the repo, and the kernel version is the one thing they cannot recover
    # from the files themselves.
    rs = [v for v in ranges.values() if v]
    readme = os.path.join(a.out, "README.md")
    write_readme(readme,
                 instrument="HERA_AFC-1", body="DIMORPHOS", frame="DIMORPHOS_FIXED",
                 observer="HERA", generated=manifest["generated"],
                 kernel=kernel or "(tool default)", mkid=mkid or "UNKNOWN -- record it by hand",
                 opc=os.path.abspath(a.opc),
                 # the .opcx basename is the product id (GSD, source, version); the path
                 # above is this machine's and means nothing to whoever receives the data
                 opcname=next((os.path.splitext(f)[0] for f in sorted(os.listdir(a.opc))
                               if f.endswith(".opcx")), os.path.basename(os.path.abspath(a.opc))),
                 start=iso(epochs[0]), end=iso(epochs[-1]),
                 interval=a.interval, count=count, span=span,
                 rotations=span / ROTATION_HOURS,
                 rmin=(min(rs) / 1000.0 if rs else 0.0), rmax=(max(rs) / 1000.0 if rs else 0.0),
                 width="1020x1020", gain=a.gain,
                 nstack=len(chosen), stackvariant=a.stack_variant,
                 scene=os.path.basename(scene) if scene else "ImageSeries.pro3d (not written)")
    print("wrote %s" % readme)

    if failed:
        print("\n%d of %d renders FAILED:" % (len(failed), count * len(variants)))
        for t, v, w in failed:
            print("   %s %-6s  %s" % (t, v, w))
    if bad:
        print("\n%d sidecar(s) FAILED the boresight invariant" % bad)
    if failed or bad:
        return 1
    print("\nall %d frames present and every sidecar passes the boresight invariant"
          % len(rendered))
    return 0


if __name__ == "__main__":
    sys.exit(main())
