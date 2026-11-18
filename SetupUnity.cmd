@echo off
rem One-step Unity setup for THE LIFE WE LEFT BEHIND. Double-click, or run from a Command Prompt.
rem Details: tools\UnitySetup\Setup-UnityProject.ps1 and Docs\UnitySetup.md
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\UnitySetup\Setup-UnityProject.ps1" %*
echo.
pause
