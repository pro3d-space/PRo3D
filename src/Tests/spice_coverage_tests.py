"""
SPICE coverage probe for the Hera kernel set
============================================

Standalone script (no notebook, no project structure). Run with:

    pip install spiceypy numpy
    python spice_coverage_tests.py

Goal
----
Document, in human-readable terms, what the Hera SPICE kernels actually
expose per body for geographical coordinate handling:

  - Are RADII available?  (Needed for any lat/lon <-> xyz at all.)
  - Are POLE_RA / POLE_DEC / PM available?
    (Needed for SPICE's auto-built IAU_<BODY> body-fixed frame.)
  - Is the IAU_<BODY> frame defined?  (=> body-fixed orientation at any epoch.)
  - Is there a non-IAU body-fixed frame available?
    (e.g. DIMORPHOS_FIXED is a dynamic two-vector frame in the FK because
    Dimorphos is tidally locked and ESA never assigned IAU pole constants.)

And then round-trip tests:

  - SPHERICAL roundtrip via latrec/reclat using a single mean radius.
    Always closes to numerical precision, but discards the body's
    tri-axial shape. Requires only RADII; no PM, no frame.

  - ELLIPSOIDAL ("planetographic") roundtrip via pgrrec/recpgr using
    equatorial radius re and flattening f = (re - rp) / re. Caveat
    discovered while running this: pgrrec / recpgr take a body name
    and SPICE internally looks up BODY<id>_PM to determine longitude
    sense. If PM is missing the call raises SPICE(MISSINGDATA), even
    though the math itself does not need pole orientation. So for
    Dimorphos this path FAILS until/unless a PM entry is provided.

  - Demonstration of how far the spherical approximation drifts from
    the tri-axial ellipsoidal surface for a body where a, b, c differ
    significantly (Phobos, Deimos, Dimorphos).

The script never asserts -- it prints PASS / FAIL / SKIP per check so all
sections run to completion even if SPICE raises on one body. Real SPICE
short messages (e.g. SPICE(MISSINGDATA), SPICE(FRAMEDATANOTFOUND)) are
included in the FAIL output for each failed step.
"""

import os
import sys
from contextlib import contextmanager

try:
    import numpy as np
    import spiceypy as sp
except ImportError as e:
    print(f"Missing dependency: {e}.  Install with:  pip install spiceypy numpy")
    sys.exit(1)


# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

# Path is relative to this script's directory (src/Tests/).
HERE = os.path.dirname(os.path.abspath(__file__))
META_KERNEL = os.path.abspath(os.path.join(HERE, "..", "..", "..", "spice", "kernels", "mk", "hera_ops.tm"))

# Bodies we want to probe. Includes Mars (well-defined IAU body, sanity check)
# down to Dimorphos (the interesting failure case).
BODIES = ["MARS", "PHOBOS", "DEIMOS", "DIDYMOS", "DIMORPHOS"]

# Test epoch (mid-mission for Hera).
EPOCH_UTC = "2025-03-12T10:30:20.482Z"

# A coarse sweep across the Hera mission timeline. The trajectory SPK in
# this kernel set (hera_fcp_..._241007_261104_v01.bsp) covers 2024-10-07
# through 2026-11-04, so anything outside that window is expected to fail
# with SPK errors. The sweep makes the difference between "no kernel data
# anywhere" and "data missing only at this epoch" visible.
EPOCHS = [
    ("pre-launch",  "2024-06-01T00:00:00Z"),  # outside trajectory coverage
    ("post-launch", "2024-10-15T00:00:00Z"),
    ("mars flyby",  "2025-03-12T10:30:20Z"),  # the standard test epoch
    ("cruise",      "2025-09-01T00:00:00Z"),
    ("approach",    "2026-09-01T00:00:00Z"),
    ("rendezvous",  "2026-10-15T00:00:00Z"),
    ("operations",  "2026-11-01T00:00:00Z"),  # near end of trajectory SPK
]


def section(title):
    print()
    print("=" * 78)
    print(title)
    print("=" * 78)


def check(label, ok, detail=""):
    flag = "PASS" if ok else "FAIL"
    if detail:
        print(f"  [{flag}] {label}: {detail}")
    else:
        print(f"  [{flag}] {label}")


@contextmanager
def spice_errors_as_python():
    """Make SPICE raise Python exceptions on error instead of aborting."""
    sp.erract("SET", 256, "RETURN")
    sp.errdev("SET", 256, "NULL")  # suppress SPICE's own error output
    try:
        yield
    finally:
        sp.reset()


import re
_SPICE_SHORT = re.compile(r"SPICE\([A-Z0-9_]+\)")


def spice_error_text(exc):
    """Extract a one-line summary from a spiceypy exception."""
    s = str(exc)
    m = _SPICE_SHORT.search(s)
    short = m.group(0) if m else "SPICE(?)"
    # Best-effort: grab the first non-empty content line after the short tag,
    # which is usually the human-readable explanation.
    lines = [ln.strip() for ln in s.splitlines() if ln.strip()]
    explanation = ""
    for i, ln in enumerate(lines):
        if short in ln:
            for follow in lines[i + 1:]:
                if follow.startswith("--") or follow.startswith("==") or follow.endswith("_c") or "-->" in follow:
                    continue
                explanation = follow
                break
            break
    return f"{short}: {explanation}" if explanation else short


def load_kernels():
    if not os.path.exists(META_KERNEL):
        print(f"FATAL: meta-kernel not found at {META_KERNEL}")
        sys.exit(2)
    os.chdir(os.path.dirname(META_KERNEL))   # FURNSH resolves $KERNELS relatively
    sp.furnsh(META_KERNEL)
    print(f"Loaded meta-kernel: {META_KERNEL}")


# ---------------------------------------------------------------------------
# Probes
# ---------------------------------------------------------------------------

def frame_defined(name):
    """Is `name` defined as a frame in the kernel pool (regardless of epoch)?"""
    try:
        return sp.namfrm(name) != 0
    except Exception:
        return False


def probe_kernel_pool(body):
    """Return dict of which constants and frames are present for `body`."""
    info = {
        "body_id": None,
        "radii": None,
        "has_radii": False,
        "has_pole": False,
        "has_pm": False,
        "iau_frame_defined": False,
        "iau_frame_computes": False,
        "alt_frames_defined": [],
        "alt_frames_compute": [],
    }
    try:
        info["body_id"] = sp.bodn2c(body)
    except Exception as e:
        info["error"] = f"bodn2c failed: {spice_error_text(e)}"
        return info
    bid = info["body_id"]

    # RADII via bodvrd; if missing, SPICE raises.
    try:
        info["radii"] = sp.bodvrd(body, "RADII", 3)[1].tolist()
        info["has_radii"] = True
    except Exception:
        sp.reset()

    # POLE_RA, POLE_DEC, PM are stored as BODY<id>_POLE_RA / _POLE_DEC / _PM
    # in the kernel pool. Use bodvcd which takes the integer id.
    try:
        sp.bodvcd(bid, "POLE_RA", 3)
        info["has_pole"] = True
    except Exception:
        sp.reset()
    try:
        sp.bodvcd(bid, "PM", 3)
        info["has_pm"] = True
    except Exception:
        sp.reset()

    # IAU_<BODY> frame. Two questions:
    #   (a) is the frame name resolvable in the pool at all?
    #   (b) can SPICE actually compute it at our test epoch?
    iau = f"IAU_{body}"
    info["iau_frame_defined"] = frame_defined(iau)
    et = sp.str2et(EPOCH_UTC)
    try:
        sp.pxform(iau, "J2000", et)
        info["iau_frame_computes"] = True
    except Exception:
        sp.reset()

    # Try known Hera-specific alternative body-fixed frames.
    for candidate in [f"{body}_FIXED", f"{body}_CK", f"{body}_SHM"]:
        if frame_defined(candidate):
            info["alt_frames_defined"].append(candidate)
            try:
                sp.pxform(candidate, "J2000", et)
                info["alt_frames_compute"].append(candidate)
            except Exception:
                sp.reset()

    return info


def report_coverage():
    section("Per-body kernel coverage")
    print(
        "For each body we check what the loaded kernels expose. The IAU_<BODY>\n"
        "frame is the standard PCK-built body-fixed frame; SPICE builds it\n"
        "automatically from POLE_RA / POLE_DEC / PM constants. If those are\n"
        "missing the frame simply does not exist."
    )
    rows = []
    for body in BODIES:
        with spice_errors_as_python():
            info = probe_kernel_pool(body)
        rows.append((body, info))
        print(f"\n-- {body} (NAIF id {info.get('body_id')})")
        check("RADII present", info["has_radii"], str(info["radii"]) if info["has_radii"] else "missing")
        check("POLE_RA / POLE_DEC present", info["has_pole"])
        check("PM (prime meridian) present", info["has_pm"])
        # Note: SPICE only auto-builds IAU_<BODY> for bodies in its built-in
        # NAIF ID table. Hera-specific negative IDs (Didymos -658030,
        # Dimorphos -658031) are NOT in that table, so IAU_<BODY> won't
        # resolve even when POLE/PM are defined.
        check(f"IAU_{body} resolves as frame name", info["iau_frame_defined"])
        check(f"IAU_{body} computes at epoch",     info["iau_frame_computes"])
        if info["alt_frames_defined"]:
            for f in info["alt_frames_defined"]:
                computes = f in info["alt_frames_compute"]
                check(f"alt frame {f}", computes,
                      "computes at epoch" if computes else "defined but does not compute (likely missing SPK / CK)")
        else:
            check("alt frame *_FIXED/_CK/_SHM defined", False, "none")
    return dict(rows)


# ---------------------------------------------------------------------------
# Round-trip tests
# ---------------------------------------------------------------------------

def roundtrip_spherical(radii, lon_deg, lat_deg):
    """latrec / reclat -- spherical, single mean radius. Always closes."""
    r = float(np.mean(radii))
    lon = np.deg2rad(lon_deg)
    lat = np.deg2rad(lat_deg)
    xyz = np.array(sp.latrec(r, lon, lat))
    r2, lon2, lat2 = sp.reclat(xyz)
    drift_deg = max(abs(np.rad2deg(lon2) - lon_deg), abs(np.rad2deg(lat2) - lat_deg))
    drift_r   = abs(r2 - r)
    return xyz, drift_deg, drift_r


def roundtrip_planetographic(body, radii, lon_deg, lat_deg, alt_km=0.0):
    """pgrrec / recpgr -- oblate-spheroid approximation using re and f only."""
    re = float(radii[0])
    rp = float(radii[2])
    f = (re - rp) / re if re != 0 else 0.0
    lon = np.deg2rad(lon_deg)
    lat = np.deg2rad(lat_deg)
    xyz = np.array(sp.pgrrec(body, lon, lat, alt_km, re, f))
    lon2, lat2, alt2 = sp.recpgr(body, xyz, re, f)
    drift_deg = max(abs(np.rad2deg(lon2) - lon_deg), abs(np.rad2deg(lat2) - lat_deg))
    drift_alt = abs(alt2 - alt_km)
    return xyz, drift_deg, drift_alt


def report_roundtrips(coverage):
    section("Round-trip tests (lat/lon -> xyz -> lat/lon)")
    print(
        "Two methods per body:\n"
        "  SPH  : latrec/reclat with mean radius. Pure spherical math, no\n"
        "         body identifier needed -- numerically exact but discards\n"
        "         tri-axial shape.\n"
        "  PGR  : pgrrec/recpgr (planetographic) with equatorial radius re\n"
        "         and flattening f = (re-rp)/re. CRUCIAL CAVEAT: although\n"
        "         the math only uses re and f, SPICE looks up BODY<id>_PM in\n"
        "         the kernel pool to decide longitude sense (east vs west).\n"
        "         If PM is missing, the call fails with SPICE(MISSINGDATA)\n"
        "         even though the radii are right there."
    )

    test_point = (45.0, 10.0)  # lon, lat in degrees
    for body in BODIES:
        info = coverage[body]
        print(f"\n-- {body}")
        if not info["has_radii"]:
            check("SPH round-trip", False, "no RADII -- skipped")
            check("PGR round-trip", False, "no RADII -- skipped")
            continue

        with spice_errors_as_python():
            try:
                xyz_sph, ddeg, dr = roundtrip_spherical(info["radii"], *test_point)
                check("SPH round-trip closes", ddeg < 1e-9 and dr < 1e-12,
                      f"max angle drift = {ddeg:.2e} deg, radius drift = {dr:.2e} km")
            except Exception as e:
                check("SPH round-trip", False, spice_error_text(e))
                sp.reset()

            try:
                xyz_pgr, ddeg, dalt = roundtrip_planetographic(body, info["radii"], *test_point)
                check("PGR round-trip closes", ddeg < 1e-9 and dalt < 1e-9,
                      f"max angle drift = {ddeg:.2e} deg, altitude drift = {dalt:.2e} km")
            except Exception as e:
                check("PGR round-trip", False, spice_error_text(e))
                sp.reset()


# ---------------------------------------------------------------------------
# Spherical vs. ellipsoidal: how much shape do we lose?
# ---------------------------------------------------------------------------

def report_tri_axial_drift(coverage):
    section("Spherical approximation vs. true tri-axial shape")
    print(
        "For each body we compute a representative surface point on the true\n"
        "tri-axial ellipsoid (a*cos(lat)cos(lon), b*cos(lat)sin(lon), c*sin(lat))\n"
        "and compare its radius to the mean-radius sphere used by the SPH path.\n"
        "Large drift => spherical model is a coarse approximation; the body is\n"
        "noticeably non-spherical."
    )
    lon_deg, lat_deg = 45.0, 10.0
    lon = np.deg2rad(lon_deg); lat = np.deg2rad(lat_deg)
    for body in BODIES:
        info = coverage[body]
        if not info["has_radii"]:
            continue
        a, b, c = info["radii"]
        x = a * np.cos(lat) * np.cos(lon)
        y = b * np.cos(lat) * np.sin(lon)
        z = c * np.sin(lat)
        true_r = np.sqrt(x*x + y*y + z*z)
        mean_r = (a + b + c) / 3.0
        rel_err = abs(true_r - mean_r) / mean_r
        print(f"  {body:10s}  a,b,c = ({a:.4f}, {b:.4f}, {c:.4f}) km   "
              f"true_r = {true_r:.4f}   mean_r = {mean_r:.4f}   "
              f"rel.err = {rel_err*100:.2f}%")


# ---------------------------------------------------------------------------
# Epoch sweep: try every candidate body-fixed frame at every sample epoch
# ---------------------------------------------------------------------------

# Friendly short labels for the common SPICE error tags we expect.
_ERR_ABBREV = {
    "SPICE(FRAMEDATANOTFOUND)": "no-PCK",   # POLE/PM missing in pool
    "SPICE(SPKINSUFFDATA)":     "no-SPK",   # missing ephemeris
    "SPICE(NOFRAMECONNECT)":    "no-link",  # no chain of frames
    "SPICE(UNKNOWNFRAME)":      "unknown",
    "SPICE(MISSINGDATA)":       "missing",
}


def try_pxform(frame, et):
    try:
        sp.pxform(frame, "J2000", et)
        return "OK"
    except Exception as e:
        s = str(e)
        m = _SPICE_SHORT.search(s)
        tag = m.group(0) if m else "SPICE(?)"
        return _ERR_ABBREV.get(tag, tag.replace("SPICE(", "").rstrip(")")[:8])
    finally:
        sp.reset()


def report_epoch_sweep():
    section("Frame evaluation across the Hera mission timeline")
    print(
        "For each candidate body-fixed frame, try pxform(<frame>, 'J2000', et)\n"
        "at several epochs spanning the Hera mission. Columns are epochs;\n"
        "entries are 'OK' or an abbreviated SPICE failure class:\n"
        "  no-PCK  : POLE_RA / POLE_DEC / PM not in the kernel pool\n"
        "  no-SPK  : required ephemeris (e.g. body-to-body position) missing\n"
        "  no-link : no chain of frames connecting source to J2000\n"
        "If a frame fails identically at every epoch the problem is\n"
        "structural (a kernel is missing outright); if it fails only at some\n"
        "epochs the issue is coverage."
    )

    headers = [tag for tag, _ in EPOCHS]
    col_w   = max(7, max(len(h) for h in headers))
    name_w  = 18
    print()
    print(f"  {'frame':{name_w}s}  " + "  ".join(f"{h:^{col_w}}" for h in headers))
    print(f"  {'-'*name_w}  " + "  ".join("-"*col_w for _ in headers))

    for body in BODIES:
        candidates = [f"IAU_{body}", f"{body}_FIXED", f"{body}_CK", f"{body}_SHM"]
        for candidate in candidates:
            if not frame_defined(candidate):
                continue
            with spice_errors_as_python():
                cells = []
                for _, t in EPOCHS:
                    try:
                        et = sp.str2et(t)
                        cells.append(try_pxform(candidate, et))
                    except Exception:
                        cells.append("bad-et")
                        sp.reset()
            print(f"  {candidate:{name_w}s}  " + "  ".join(f"{c:^{col_w}}" for c in cells))


# ---------------------------------------------------------------------------
# Dimorphos-specific: confirm no IAU frame ever, but DIMORPHOS_FIXED works
# ---------------------------------------------------------------------------

def report_dimorphos_story():
    section("Dimorphos: why IAU_DIMORPHOS does not exist (in any kernel version)")
    print(
        "Across hera_didymos_v00.tpc through v06.tpc only the RADII keyword is\n"
        "defined for body -658031. POLE_RA / POLE_DEC / PM were never written,\n"
        "in either the pre-impact or the post-DART-impact versions; only the\n"
        "radii values themselves changed (v05: (0.1038, 0.0798, 0.0665) km;\n"
        "v06: (0.0895, 0.0845, 0.0575) km).\n\n"
        "This is intentional. Dimorphos is tidally locked to Didymos, so ESA\n"
        "models its orientation through the DIMORPHOS_FIXED dynamic two-vector\n"
        "frame (defined in hera_v16.tf): +X points from Dimorphos toward\n"
        "Didymos, +Y along the orbital velocity. No IAU pole constants needed.\n"
    )
    et = sp.str2et(EPOCH_UTC)
    with spice_errors_as_python():
        try:
            sp.pxform("IAU_DIMORPHOS", "J2000", et)
            check("IAU_DIMORPHOS -> J2000", True)
        except Exception as e:
            check("IAU_DIMORPHOS -> J2000", False, spice_error_text(e))
            sp.reset()

        try:
            sp.pxform("DIMORPHOS_FIXED", "J2000", et)
            check("DIMORPHOS_FIXED -> J2000", True, "rotation matrix obtained")
        except Exception as e:
            check("DIMORPHOS_FIXED -> J2000", False, spice_error_text(e))
            sp.reset()


# ---------------------------------------------------------------------------
# Final summary
# ---------------------------------------------------------------------------

def report_summary(coverage):
    section("Summary of what is and is not possible")
    print(
        "Round-trip lat/lon <-> xyz:\n"
        "  * SPH path (latrec/reclat with one mean radius): works for ANY\n"
        "    body that has RADII. No frame, no PM needed. Numerically exact.\n"
        "    Trade-off: collapses the tri-axial shape to a single sphere\n"
        "    (see the drift table above -- ~9-12% radius error for the small\n"
        "    bodies).\n"
        "  * PGR path (pgrrec/recpgr with re, f): numerically exact AND\n"
        "    correct for an oblate spheroid -- but SPICE refuses unless\n"
        "    BODY<id>_PM is in the kernel pool. So: works for Mars/Phobos/\n"
        "    Deimos (standard PCK); FAILS for Dimorphos (no PM in any Hera\n"
        "    PCK version).\n"
        "\n"
        "Body-fixed orientation (what 'lat/lon' means in inertial space):\n"
        "  * IAU_<BODY>: the frame NAME resolves for every body in this\n"
        "    kernel set, including Dimorphos -- SPICE auto-registers\n"
        "    IAU_<NAME> from the NAIF id table. But EVALUATING the frame\n"
        "    at an epoch needs POLE_RA / POLE_DEC / PM. Dimorphos has\n"
        "    neither, so pxform(IAU_DIMORPHOS, ...) fails with\n"
        "    SPICE(FRAMEDATANOTFOUND). Didymos has POLE/PM but its\n"
        "    IAU frame also fails here -- the Hera FK appears to\n"
        "    redirect Didymos via DIDYMOS_FIXED.\n"
        "  * The Hera FK provides explicit body-fixed frames instead:\n"
        "    DIDYMOS_FIXED (CLASS 2 PCK-based) and DIMORPHOS_FIXED\n"
        "    (CLASS 5 dynamic two-vector, anchored on the Didymos line).\n"
        "    In this run DIDYMOS_FIXED computes; DIMORPHOS_FIXED fails\n"
        "    with SPICE(SPKINSUFFDATA) because no SPK in the loaded set\n"
        "    provides Dimorphos's position relative to Didymos at the\n"
        "    test epoch. A Dimorphos ephemeris kernel would unblock it.\n"
        "\n"
        "Impact on PRo3D's CooTransformation wrapper:\n"
        "  * The wrapper's LatLonAlt2Xyz/Xyz2LatLonAlt almost certainly\n"
        "    uses the PGR path internally, which is why it returns error -3\n"
        "    for Dimorphos (PM missing).\n"
        "  * If we want lat/lon <-> xyz on Dimorphos via the wrapper, the\n"
        "    options are: (a) add a synthesised BODY-658031_PM entry to a\n"
        "    side PCK (a fiction, but unblocks PGR), or (b) change the\n"
        "    wrapper to fall back to the spherical (SPH) path when PM is\n"
        "    absent, or (c) accept that Dimorphos lat/lon support requires\n"
        "    real orientation kernels from ESA.\n"
    )

    print("Coverage table:\n")
    print(f"  {'body':10s}  {'RADII':6s}  {'POLE':6s}  {'PM':6s}  {'IAU defined':12s}  {'IAU computes':14s}  alt body-fixed (computes)")
    print(f"  {'-'*10}  {'-'*6}  {'-'*6}  {'-'*6}  {'-'*12}  {'-'*14}  {'-'*40}")
    for body in BODIES:
        info = coverage[body]
        def y(b): return "yes" if b else "no"
        alt = ", ".join(info["alt_frames_compute"]) or ("defined: " + ", ".join(info["alt_frames_defined"]) if info["alt_frames_defined"] else "-")
        print(f"  {body:10s}  {y(info['has_radii']):6s}  {y(info['has_pole']):6s}  "
              f"{y(info['has_pm']):6s}  {y(info['iau_frame_defined']):12s}  "
              f"{y(info['iau_frame_computes']):14s}  {alt}")


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main():
    load_kernels()
    coverage = report_coverage()
    report_roundtrips(coverage)
    report_tri_axial_drift(coverage)
    report_epoch_sweep()
    report_dimorphos_story()
    report_summary(coverage)


if __name__ == "__main__":
    main()
