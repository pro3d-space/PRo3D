@echo off
REM Runs ALL tests, including the HERA SPICE-kernel tests. These require the
REM (non-public) HERA kernels under spice\kernels (e.g. spice\kernels\mk\hera_ops.tm);
REM without them the heraSpice tests will fail. For the kernel-independent subset
REM use runTests.cmd instead. Extra args are passed through to the Expecto runner.
dotnet run --project src/Tests/Tests.fsproj -c Release -- %*
