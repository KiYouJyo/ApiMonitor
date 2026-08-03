@echo off
setlocal
rem ApiMonitor v0.4.0 one-click uninstaller.

set "EXITCODE=0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1"
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
  echo.
  echo ============================================================
  echo  Uninstall finished with exit code %EXITCODE%.
  echo  See the log file in %%TEMP%%\ApiMonitor-Uninstall-*.log.
  echo ============================================================
  echo.
  pause
)

exit /b %EXITCODE%
