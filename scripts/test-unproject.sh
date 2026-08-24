#!/usr/bin/env bash
# End-to-end check of `pro3d-tool unproject`: runs the verb over the example centroids and
# verifies what came back. Unlike the Expecto suite this exercises the CLI itself -- argument
# handling, the input table, SPICE, the kd-tree intersection and the output writer.
#
#   PRO3D_TEST_DATA=/path/to/PRo3D.Resources.TestData \
#   PRO3D_SPICE_KERNELS=/path/to/hera \
#   scripts/test-unproject.sh
#
# Both may also be passed as arguments:
#
#   scripts/test-unproject.sh <test-data> [<kernel-root>]
#
# Test data:  git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git
# Kernels:    git clone https://spiftp.esac.esa.int/git/hera.git   (published by ESA, ~6.5 GB)

set -e

here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"

testdata="${1:-$PRO3D_TEST_DATA}"
kernels="${2:-$PRO3D_SPICE_KERNELS}"

if [ -z "$testdata" ]; then
    echo "set PRO3D_TEST_DATA (or pass it) to a clone of PRo3D.Resources.TestData" >&2
    exit 2
fi
if [ -z "$kernels" ]; then
    echo "set PRO3D_SPICE_KERNELS (or pass it) to a clone of the ESA hera kernels" >&2
    exit 2
fi

out="$(mktemp -d)/unproject.csv"

echo "[test-unproject] running the verb"
dotnet run --project "$root/src/PRo3D.Tool/PRo3D.Tool.fsproj" -- unproject \
    --opc         "$testdata/HERA/Didymos_ASPECT" \
    --images      "$testdata/HERA/Instrument Data" \
    --input       "$here/example-centroids.csv" \
    --out         "$out" \
    --kernel-root "$kernels"

fail() { echo "[test-unproject] FAILED: $1" >&2; exit 1; }

[ -f "$out" ] || fail "no output written to $out"

# one header plus one row per input row, in input order
lines=$(grep -c . "$out")
[ "$lines" -eq 5 ] || fail "expected 5 lines (header + 4 rows), got $lines"

head -1 "$out" | grep -q "status,x_m,y_m,z_m,lat_deg,lon_deg,alt_m,range_m" \
    || fail "the geometry columns are missing from the header"

# the three on-body centroids resolve, the off-limb one is reported as a miss rather than
# silently dropped
grep -q "frame_centre,ok,"       "$out" || fail "frame_centre should have hit the surface"
grep -q "sub_pixel_centroid,ok," "$out" || fail "a fractional centroid should have hit the surface"
grep -q "off_the_limb,no-hit,"   "$out" || fail "off_the_limb should be reported as no-hit"

# the surface point has to be on Didymos, whose radius is a few hundred metres
awk -F, '
    $5 == "ok" {
        r = sqrt($6*$6 + $7*$7 + $8*$8)
        if (r < 300 || r > 500) { print "radius out of range: " r; exit 1 }
        if ($9 < -90 || $9 > 90) { print "latitude out of range: " $9; exit 1 }
        n++
    }
    END { if (n < 3) { print "expected 3 hits, got " n; exit 1 } }
' "$out" || fail "the surface coordinates are not plausible for Didymos"

echo "[test-unproject] OK -- $out"
