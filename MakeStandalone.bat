@echo off
rem Double-click this once to build a standalone, portable copy of the game.
rem The result needs no .NET install - copy the "Standalone" folder anywhere
rem and double-click DataCenterTycoon.exe inside it.
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET 8 SDK is needed to build the standalone copy.
  echo   https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

echo Building a standalone copy ^(this can take a minute^)...
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o "Standalone"
if errorlevel 1 (
  echo.
  echo Publish failed - see the messages above.
  pause
  exit /b 1
)

echo.
echo Done. Your standalone game is here:
echo   Standalone\DataCenterTycoon.exe
echo Copy the whole "Standalone" folder anywhere and double-click the exe.
pause
