@echo off
REM Downloads the HERA SPICE kernels the test suite needs (see spice-kernels.pins).
REM
REM   fetch-spice-kernels.cmd [dest]        default dest: .\spice
REM
REM Then point the tests at it:  set PRO3D_SPICE_KERNELS=<dest>
REM
REM Needs bash and curl -- both ship with Git for Windows. The work is in the .sh; this
REM is only here so the script is reachable the same way as its siblings.

bash "%~dp0fetch-spice-kernels.sh" %*
