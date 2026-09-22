@echo off
REM Simulated instrument image of Didymos as seen by Milani/ASPECT.
REM
REM   run-simulate-image.cmd <path-to-PRo3D.Resources.TestData>
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
REM Writes an 8-bit greyscale PNG to .\simulated.png. The epoch below lies inside the
REM planning kernel's coverage. De-shading is off by default (and the test-data OPC has
REM no DRACO layer to de-shade anyway); the surface is constant albedo + micro-structure.

dotnet run --project "%~dp0..\src\PRo3D.Tool\PRo3D.Tool.fsproj" -- simulate-image --opc "%~1\HERA\Didymos_ASPECT" --time 2027-03-15T19:00:00Z --body DIDYMOS --frame DIDYMOS_FIXED --observer MILANI --instrument MILANI_ASPECT_NIR1 --out ".\simulated.png"
