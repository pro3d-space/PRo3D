@echo off
REM End-to-end check of `pro3d-tool unproject`: runs the verb over the example centroids and
REM verifies what came back. Unlike the Expecto suite this exercises the CLI itself -- argument
REM handling, the input table, SPICE, the kd-tree intersection and the output writer.
REM
REM   set PRO3D_TEST_DATA=C:\path\to\PRo3D.Resources.TestData
REM   set PRO3D_SPICE_KERNELS=C:\path\to\hera
REM   scripts\test-unproject.cmd
REM
REM Both may also be passed as arguments:
REM
REM   scripts\test-unproject.cmd <test-data> [<kernel-root>]
REM
REM Test data:  git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git
REM Kernels:    git clone https://spiftp.esac.esa.int/git/hera.git   (published by ESA, ~6.5 GB)

setlocal

set "TESTDATA=%~1"
if "%TESTDATA%"=="" set "TESTDATA=%PRO3D_TEST_DATA%"
set "KERNELS=%~2"
if "%KERNELS%"=="" set "KERNELS=%PRO3D_SPICE_KERNELS%"

if "%TESTDATA%"=="" (
    echo set PRO3D_TEST_DATA ^(or pass it^) to a clone of PRo3D.Resources.TestData 1>&2
    exit /b 2
)
if "%KERNELS%"=="" (
    echo set PRO3D_SPICE_KERNELS ^(or pass it^) to a clone of the ESA hera kernels 1>&2
    exit /b 2
)

set "OUT=%TEMP%\pro3d-unproject-test.csv"
if exist "%OUT%" del "%OUT%"

echo [test-unproject] running the verb
dotnet run --project "%~dp0..\src\PRo3D.Tool\PRo3D.Tool.fsproj" -- unproject --opc "%TESTDATA%\HERA\Didymos_ASPECT" --images "%TESTDATA%\HERA\Instrument Data" --input "%~dp0example-centroids.csv" --out "%OUT%" --kernel-root "%KERNELS%"
if errorlevel 1 goto :failed

if not exist "%OUT%" (
    echo [test-unproject] FAILED: no output written to %OUT% 1>&2
    exit /b 1
)

findstr /c:"status,x_m,y_m,z_m,lat_deg,lon_deg,alt_m,range_m" "%OUT%" >nul || goto :badheader

REM the on-body centroids resolve; the off-limb one is reported rather than silently dropped
findstr /c:"frame_centre,ok," "%OUT%" >nul || goto :nocentre
findstr /c:"sub_pixel_centroid,ok," "%OUT%" >nul || goto :nosubpixel
findstr /c:"off_the_limb,no-hit," "%OUT%" >nul || goto :nolimb

echo [test-unproject] OK -- %OUT%
exit /b 0

:failed
echo [test-unproject] FAILED: the verb exited non-zero 1>&2
exit /b 1
:badheader
echo [test-unproject] FAILED: the geometry columns are missing from the header 1>&2
exit /b 1
:nocentre
echo [test-unproject] FAILED: frame_centre should have hit the surface 1>&2
exit /b 1
:nosubpixel
echo [test-unproject] FAILED: a fractional centroid should have hit the surface 1>&2
exit /b 1
:nolimb
echo [test-unproject] FAILED: off_the_limb should be reported as no-hit 1>&2
exit /b 1
