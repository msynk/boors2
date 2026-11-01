@echo off
rem Builds the Boors desktop app (WinUI 3, Release) and starts it.
rem Arguments are passed to the app, e.g.:  run-desktop.bat --lang en --theme dark --page reports
rem Every switch is listed in winui\README.md (Desktop > Command line).
setlocal

set "PROJECT=%~dp0winui\src\Boors.Desktop"
set "EXE=%PROJECT%\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Boors.Desktop.exe"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo The .NET SDK was not found. Install .NET SDK 10.0.100 or later:
    echo https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

rem A running instance locks its binaries (the build would fail) and its DuckDB store (one process per file).
tasklist /nh /fi "imagename eq Boors.Desktop.exe" 2>nul | findstr /i /c:"Boors.Desktop.exe" >nul
if not errorlevel 1 (
    echo Boors is already running. Close it first, then run this file again.
    pause
    exit /b 1
)

echo Building Boors.Desktop (Release)...
dotnet build "%PROJECT%" -c Release -nologo
if errorlevel 1 (
    echo.
    echo Build failed; see the errors above.
    pause
    exit /b 1
)

start "" "%EXE%" %*
