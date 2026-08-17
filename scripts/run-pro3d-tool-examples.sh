#!/bin/sh
# Runnable usage examples for pro3d-tool.
#
#   run-pro3d-tool-examples.sh <test-data-dir> [spice-kernels-dir]
#
# <test-data-dir>     clone of https://github.com/pro3d-space/PRo3D.Resources.TestData
# [spice-kernels-dir] <clone>/kernels from https://spiftp.esac.esa.int/git/hera.git,
#                     needed by SPICE verbs (sun-angles, not implemented yet)
#
# Every example here is read-only. Options that modify data -- notably
# `kdtree --forcekdtreerebuild` -- are documented in docs/Pro3DTool.md but not run.

set -e

if [ -z "$1" ]; then
    echo "usage: $(basename "$0") <test-data-dir> [spice-kernels-dir]" >&2
    exit 2
fi

TESTDATA="$1"
PROJ="$(dirname "$0")/../src/PRo3D.Tool/PRo3D.Tool.fsproj"
OPC="$TESTDATA/1087_004779_MSLMST_0011"

if [ ! -d "$OPC" ]; then
    echo "not a PRo3D.Resources.TestData clone: $TESTDATA" >&2
    exit 1
fi

echo
echo "=== kdtree: validate an OPC and report its kd-trees ==="
dotnet run --project "$PROJ" -- kdtree "$OPC"

echo
if [ -z "$2" ]; then
    echo "=== sun-angles: SKIPPED, no SPICE kernels given ==="
    echo "  clone them with: git clone https://spiftp.esac.esa.int/git/hera.git"
    echo "  then re-run: $(basename "$0") \"$TESTDATA\" <clone>/kernels"
else
    echo "=== sun-angles: illumination geometry for the ASPECT image ==="
    dotnet run --project "$PROJ" -- sun-angles \
        --opc "$TESTDATA/HERA/Didymos_ASPECT" \
        --images "$TESTDATA/HERA/Instrument Data" \
        --kernel-root "$2" \
        --out "$PWD/sun-angles"
fi

echo
echo "done."
