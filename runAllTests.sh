#!/usr/bin/env bash
# Runs ALL tests, including the HERA SPICE-kernel tests. These require the
# (non-public) HERA kernels under spice/kernels (e.g. spice/kernels/mk/hera_ops.tm);
# without them the heraSpice tests will fail. For the kernel-independent subset
# use runTests.sh instead. Extra args are passed through to the Expecto runner.
set -e
dotnet run --project src/Tests/Tests.fsproj -c Release -- "$@"
