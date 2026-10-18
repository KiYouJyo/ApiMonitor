@echo off
setlocal DisableDelayedExpansion
set "ENTRY=%~dp0payload\Bootstrap.ps1"
if not exist "%ENTRY%" (
  echo ERROR: This installer is incomplete. Extract the entire ZIP first.
  pause
  exit /b 2
)
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%ENTRY%" -Action Install
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" (
  echo.
  echo ApiMonitor installation failed, error code %RESULT%.
  echo See the installation log in %LOCALAPPDATA%\ApiMonitor\Logs.
  pause
  exit /b %RESULT%
)
echo.
echo ApiMonitor installation is complete.
if not defined APIMONITOR_ONECLICK_NO_PAUSE pause
exit /b 0
