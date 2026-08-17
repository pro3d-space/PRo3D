@echo off
REM Runnable usage examples for pro3d-tool.
REM
REM   run-pro3d-tool-examples.cmd <test-data-dir> [spice-kernels-dir]
REM
REM <test-data-dir>     clone of https://github.com/pro3d-space/PRo3D.Resources.TestData
REM [spice-kernels-dir] <clone>\kernels from https://spiftp.esac.esa.int/git/hera.git,
REM                     needed by SPICE verbs (sun-angles, not implemented yet)
REM
REM Every example here is read-only. Options that modify data -- notably
REM `kdtree --forcekdtreerebuild` -- are documented in docs/Pro3DTool.md but not run.

setlocal
if "%~1"=="" (
    echo usage: %~nx0 ^<test-data-dir^> [spice-kernels-dir]
    exit /b 2
)

set "TESTDATA=%~1"
set "PROJ=%~dp0..\src\PRo3D.Tool\PRo3D.Tool.fsproj"
set "OPC=%TESTDATA%\1087_004779_MSLMST_0011"

if not exist "%OPC%" (
    echo not a PRo3D.Resources.TestData clone: %TESTDATA%
    exit /b 1
)

echo.
echo === kdtree: validate an OPC and report its kd-trees ===
dotnet run --project "%PROJ%" -- kdtree "%OPC%"
if errorlevel 1 exit /b %errorlevel%

echo.
echo === sun-angles: not implemented yet, skipping ===

echo.
echo done.
endlocal
