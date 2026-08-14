#!/usr/bin/env bash
# Runs the kernel-independent tests only. Passes --skip-hera so the HERA tests
# (which need the large, non-public kernels under spice/kernels) skip themselves
# deterministically, even on a machine that happens to have those kernels.
# Extra args are passed through to the Expecto runner.
set -e
dotnet run --project src/Tests/Tests.fsproj -c Release -- --skip-hera "$@"
