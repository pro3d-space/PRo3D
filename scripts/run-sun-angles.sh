#!/bin/sh
# Illumination geometry (incidence, emission, phase) for the ASPECT image on Didymos.
#
#   run-sun-angles.sh <path-to-PRo3D.Resources.TestData>
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
# Writes float32 TIFFs in radians, an RGB preview, false-colour PNGs and a JSON
# provenance sidecar to ./sun-angles.

dotnet run --project "$(dirname "$0")/../src/PRo3D.Tool/PRo3D.Tool.fsproj" -- sun-angles --opc "$1/HERA/Didymos_ASPECT" --images "$1/HERA/Instrument Data" --out "./sun-angles" --false-color
