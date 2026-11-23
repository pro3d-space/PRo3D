@echo off
call "%~dp0adapt.cmd" || exit /b 1
dotnet run --project src\Tests\Tests.fsproj -- --testdatasource C:\pro3ddata\testdata %*
