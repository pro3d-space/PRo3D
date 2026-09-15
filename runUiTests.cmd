@echo off
REM Runs the Playwright UI tests in tests-ui\ against the real viewer (needs a GPU).
REM Data comes from PRO3D_TEST_DATA (a PRo3D.Resources.TestData checkout) and
REM PRO3D_SPICE_KERNELS (a HERA kernel tree matching the frames' epoch) -- see
REM tests-ui\README.md. Builds the viewer and pro3d-tool in Release first.
REM Extra args are passed through to Playwright, e.g. runUiTests.cmd projection-e2e
setlocal
if "%PRO3D_TEST_DATA%"=="" if not defined PRO3D_SCENE (
    echo PRO3D_TEST_DATA is not set: point it at a PRo3D.Resources.TestData checkout ^(see tests-ui\README.md^) 1>&2
    exit /b 1
)
dotnet build src\PRo3D.Viewer\PRo3D.Viewer.fsproj -c Release || exit /b 1
dotnet build src\PRo3D.Tool\PRo3D.Tool.fsproj -c Release || exit /b 1
pushd "%~dp0tests-ui"
if not exist node_modules (
    call npm install || (popd & exit /b 1)
)
call npx playwright install chromium || (popd & exit /b 1)
call npx playwright test %*
set rc=%ERRORLEVEL%
popd
exit /b %rc%
