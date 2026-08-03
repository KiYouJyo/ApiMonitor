@echo off
setlocal
rem ApiMonitor 00.4.0 one-click sideload installer.
rem Always run from this script's own directory; the .ps1 also resol0es paths itself.

set "EXITCODE=0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
  echo.
  echo ============================================================
  echo  Installation failed with exit code %EXITCODE%.
  echo  See the install log in %%TEMP%%\ApiMonitor-Install-*.log.
  echo ============================================================
  echo.
  pause
)

exit /b %EXITCODE%
