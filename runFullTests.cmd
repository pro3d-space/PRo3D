@echo off
REM Runs everything: the whole Expecto suite (runAllTests.cmd, including the HERA
REM kernel tests) and then the Playwright UI tests (runUiTests.cmd). Both run even
REM if the first fails; the exit code is non-zero if either did.
setlocal
call "%~dp0runAllTests.cmd"
set dotnetrc=%ERRORLEVEL%
call "%~dp0runUiTests.cmd"
set uirc=%ERRORLEVEL%
echo.
echo Expecto exit code %dotnetrc%, UI tests exit code %uirc%
if not "%dotnetrc%"=="0" exit /b 1
if not "%uirc%"=="0" exit /b 1
exit /b 0
