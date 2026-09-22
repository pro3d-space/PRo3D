@echo off
REM Validate the test OPC and report its kd-trees.
REM
REM   run-kdtree.cmd <path-to-PRo3D.Resources.TestData>
REM
REM Test data: git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git
REM
REM Read-only. Add --forcekdtreerebuild to rewrite the .aakd files in place.

dotnet run --project "%~dp0..\src\PRo3D.Tool\PRo3D.Tool.fsproj" -- kdtree "%~1\1087_004779_MSLMST_0011"
