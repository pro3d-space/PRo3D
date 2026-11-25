#!/usr/bin/env bash
# Runs the kernel-independent tests only. Passes --skip-hera so the HERA tests
# (which need the large, non-public kernels under spice/kernels) skip themselves
# deterministically, even on a machine that happens to have those kernels.
# Extra args are passed through to the Expecto runner.
set -e
bash "$(dirname "$0")/adapt.sh"
# adapt.sh ran the global paket restore, so skip the one msbuild forces per project (#891).
dotnet run --project src/Tests/Tests.fsproj -c Release -p:PaketDisableGlobalRestore=true -- --skip-hera "$@"
