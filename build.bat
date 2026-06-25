@echo off
cd /d "%~dp0"

rem Explorer-launched cmd may have stale PATH after a fresh dotnet install
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%ProgramFiles%\dotnet;%PATH%"
if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"

echo Building Task-Split (Release)...

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

call dotnet build -c Release
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)

echo.
echo Build succeeded.
echo Output: %~dp0BuiltExe\TaskSplit.exe
echo.
pause
