#!/bin/sh
# Validate the test OPC and report its kd-trees.
#
#   run-kdtree.sh <path-to-PRo3D.Resources.TestData>
#
# Test data: git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git
#
# Read-only. Add --forcekdtreerebuild to rewrite the .aakd files in place.

dotnet run --project "$(dirname "$0")/../src/PRo3D.Tool/PRo3D.Tool.fsproj" -- kdtree "$1/1087_004779_MSLMST_0011"
