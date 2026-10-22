@echo off
rem Windows-side build. Usage: build.cmd [build|test|run|publish]   (default: build)
setlocal
cd /d "%~dp0"
dotnet --list-sdks 2>nul | findstr /B "8." >nul
if errorlevel 1 (
  echo No .NET 8 SDK found on Windows - only runtimes are installed.
  echo Install it with:  winget install Microsoft.DotNet.SDK.8
  exit /b 1
)
set CMD=%1
if "%CMD%"=="" set CMD=build
set APP=src\PracticeTimer\PracticeTimer.csproj
if /I "%CMD%"=="build"   dotnet build %APP% -c Release & goto :done
if /I "%CMD%"=="test"    dotnet test tests\PracticeTimer.Core.Tests & goto :done
if /I "%CMD%"=="run"     dotnet run --project %APP% -c Release & goto :done
if /I "%CMD%"=="publish" dotnet publish %APP% -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish & goto :done
echo Unknown command "%CMD%". Use build, test, run or publish.
exit /b 2
:done
exit /b %ERRORLEVEL%
