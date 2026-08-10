@echo off
setlocal
set "LAUNCHER=%~dp0tools\EPA-Launcher\EPA-Launcher.ps1"
if not exist "%LAUNCHER%" (
  echo EPA Launcher could not find:
  echo %LAUNCHER%
  pause
  exit /b 1
)
start "EPA Launcher" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -STA -File "%LAUNCHER%"
exit /b 0
