@echo off
REM Illumination geometry (incidence, emission, phase) for the ASPECT image on Didymos.
REM
REM   run-sun-angles.cmd <path-to-PRo3D.Resources.TestData>
REM
REM Test data:  git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git
REM
REM SPICE kernels are NOT in the test data -- ESA publishes them separately:
REM   git clone https://spiftp.esac.esa.int/git/hera.git
REM Point PRO3D_SPICE_KERNELS at that clone (or at its 'kernels' subdirectory; either
REM works) and the tool finds them on its own:
REM   setx PRO3D_SPICE_KERNELS C:\path\to\hera
REM To use a different tree for one run, call the tool directly with --kernel-root <dir>,
REM which overrides the variable.
REM
REM Writes float32 TIFFs in radians, an RGB preview, false-colour PNGs and a JSON
REM provenance sidecar to .\sun-angles.

dotnet run --project "%~dp0..\src\PRo3D.Tool\PRo3D.Tool.fsproj" -- sun-angles --opc "%~1\HERA\Didymos_ASPECT" --images "%~1\HERA\Instrument Data" --out ".\sun-angles" --false-color
