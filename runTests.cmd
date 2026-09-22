@echo off
REM Runs the kernel-independent tests only. Passes --skip-hera so the HERA tests
REM (which need the large, non-public kernels under spice\kernels) skip themselves
REM deterministically, even on a machine that happens to have those kernels.
REM Extra args are passed through to the Expecto runner.
call "%~dp0adapt.cmd" || exit /b 1
dotnet run --project src/Tests/Tests.fsproj -c Release -- --skip-hera %*
