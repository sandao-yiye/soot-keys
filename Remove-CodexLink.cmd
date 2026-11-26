@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-CodexPet.ps1" -RemoveLink
set "result=%errorlevel%"
pause
exit /b %result%
