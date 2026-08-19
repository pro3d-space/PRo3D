#!/bin/sh
# Simulated instrument image of Didymos as seen by Milani/ASPECT.
#
#   run-simulate-image.sh <path-to-PRo3D.Resources.TestData>
#
# Test data:  git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git
#
# SPICE kernels are NOT in the test data -- ESA publishes them separately:
#   git clone https://spiftp.esac.esa.int/git/hera.git
# Point PRO3D_SPICE_KERNELS at that clone (or at its 'kernels' subdirectory; either
# works) and the tool finds them on its own:
#   export PRO3D_SPICE_KERNELS=/path/to/hera
# To use a different tree for one run, call the tool directly with --kernel-root <dir>,
# which overrides the variable.
#
# Writes an 8-bit greyscale PNG to ./simulated.png. The epoch below lies inside the
# planning kernel's coverage; the test-data OPC has no DRACO layer, so the run reports
# a de-shading fallback to constant albedo -- that is expected.

dotnet run --project "$(dirname "$0")/../src/PRo3D.Tool/PRo3D.Tool.fsproj" -- simulate-image --opc "$1/HERA/Didymos_ASPECT" --time 2027-03-15T19:00:00Z --body DIDYMOS --frame DIDYMOS_FIXED --observer MILANI --instrument MILANI_ASPECT_NIR1 --out "./simulated.png"
