@echo off
rem Double-click this file to build and run DATA TYCOON.
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET 8 SDK is not installed.
  echo Get it here, then double-click this file again:
  echo   https://dotnet.microsoft.com/download/dotnet/8.0
  echo.
  pause
  exit /b 1
)

echo Building Data Tycoon...
dotnet build -c Release -v quiet
if errorlevel 1 (
  echo.
  echo Build failed - see the messages above.
  pause
  exit /b 1
)

echo Starting the game...
start "" "bin\Release\net8.0\DataCenterTycoon.exe"
