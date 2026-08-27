#!/usr/bin/env bash
# Runs ALL tests, including the HERA SPICE-kernel tests. Those need ESA's HERA mission
# kernels; without them they skip themselves. Get them with
#   scripts/fetch-spice-kernels.sh spice
#   export PRO3D_SPICE_KERNELS=$PWD/spice
# (or leave a full mirror in a 'spice' directory next to the PRo3D clone, which is the
# fallback). See docs/tests/SpiceKernels.md. For the kernel-independent subset use
# runTests.sh instead. Extra args are passed through to the Expecto runner.
set -e
dotnet run --project src/Tests/Tests.fsproj -c Release -- "$@"
