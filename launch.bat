@echo off
cd /d "%~dp0"

rem Explorer-launched cmd may have stale PATH after a fresh dotnet install
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%ProgramFiles%\dotnet;%PATH%"
if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"

echo Starting Task-Split...

where dotnet >nul 2>&1
if errorlevel 1 (
    echo.
    echo .NET SDK not found. Install with:
    echo   winget install Microsoft.DotNet.SDK.9
    echo.
    echo If you already installed it, log out and back in, then try again.
    echo.
    pause
    exit /b 1
)

call dotnet run
if errorlevel 1 pause
