@echo off
REM Runs ALL tests, including the HERA SPICE-kernel tests. Those need ESA's HERA
REM mission kernels; without them they skip themselves. Get them with
REM   scripts\fetch-spice-kernels.cmd spice
REM   set PRO3D_SPICE_KERNELS=%CD%\spice
REM (or leave a full mirror in a 'spice' directory next to the PRo3D clone, which is
REM the fallback). See docs\tests\SpiceKernels.md. For the kernel-independent subset
REM use runTests.cmd instead. Extra args are passed through to the Expecto runner.
dotnet run --project src/Tests/Tests.fsproj -c Release -- %*
