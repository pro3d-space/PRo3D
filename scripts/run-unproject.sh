#!/bin/sh
# Image pixel coordinates to body-fixed surface coordinates on Didymos.
#
#   run-unproject.sh <path-to-PRo3D.Resources.TestData>
#
# Test data:  git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git
#
# SPICE kernels are NOT in the test data -- ESA publishes them separately:
#   git clone https://spiftp.esac.esa.int/git/hera.git
# Point PRO3D_SPICE_KERNELS at that clone (or at its 'kernels' subdirectory; either
# works):
#   export PRO3D_SPICE_KERNELS=/path/to/hera
# To use a different tree for one run, pass --kernel-root <dir>.
#
# The OPC needs kd-trees. Build them once with:
#   pro3d-tool kdtree "$1/HERA/Didymos_ASPECT"
#
# Writes ./unproject.csv, one row per input row.

dotnet run --project "$(dirname "$0")/../src/PRo3D.Tool/PRo3D.Tool.fsproj" -- unproject --opc "$1/HERA/Didymos_ASPECT" --images "$1/HERA/Instrument Data" --input "$(dirname "$0")/example-centroids.csv" --out "./unproject.csv"
