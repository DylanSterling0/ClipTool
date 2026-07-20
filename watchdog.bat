@echo off
title ClipTool Watchdog
cd /d "%~dp0"

:loop
tasklist /FI "IMAGENAME eq ClipTool.exe" 2>nul | find /I "ClipTool.exe" >nul
if errorlevel 1 (
    echo [%date% %time%] ClipTool not running, starting...
    start "" /B /D "%~dp0bin\Debug\net7.0-windows" ClipTool.exe
) else (
    REM Already running, nothing to do
)
timeout /t 10 /nobreak >nul
goto loop
