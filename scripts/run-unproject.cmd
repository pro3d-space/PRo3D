@echo off
REM Image pixel coordinates to body-fixed surface coordinates on Didymos.
REM
REM   run-unproject.cmd <path-to-PRo3D.Resources.TestData>
REM
REM Test data:  git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git
REM
REM SPICE kernels are NOT in the test data -- ESA publishes them separately:
REM   git clone https://spiftp.esac.esa.int/git/hera.git
REM Point PRO3D_SPICE_KERNELS at that clone (or at its 'kernels' subdirectory), or pass
REM --kernel-root <dir> to override it for one run.
REM
REM The OPC needs kd-trees. Build them once with:
REM   pro3d-tool kdtree "%~1\HERA\Didymos_ASPECT"
REM
REM Writes .\unproject.csv, one row per input row.

dotnet run --project "%~dp0..\src\PRo3D.Tool\PRo3D.Tool.fsproj" -- unproject --opc "%~1\HERA\Didymos_ASPECT" --images "%~1\HERA\Instrument Data" --input "%~dp0example-centroids.csv" --out ".\unproject.csv"
