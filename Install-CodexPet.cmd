@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-CodexPet.ps1"
set "result=%errorlevel%"
if "%result%"=="0" echo Open the Codex companion picker. If the cat is missing, reopen Codex.
pause
exit /b %result%
