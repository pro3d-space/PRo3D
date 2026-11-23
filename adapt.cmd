@echo off
REM Generates the Adaptify *.g.fs files, which are not checked in. By default only the missing
REM or stale ones; --all regenerates everything, --check only reports. build.cmd and the
REM runTests scripts run this too. See docs/ModelTypes.md.
setlocal
pushd "%~dp0"
dotnet fsi utilities\Adapt.fsx %*
set rc=%ERRORLEVEL%
popd
exit /b %rc%
