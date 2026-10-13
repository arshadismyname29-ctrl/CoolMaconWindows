@echo off
if not exist "%~dp0bin\CoolMacDock.exe" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
    if errorlevel 1 (
        echo The dock could not be built.
        pause
        exit /b 1
    )
)
start "" "%~dp0bin\CoolMacDock.exe"
