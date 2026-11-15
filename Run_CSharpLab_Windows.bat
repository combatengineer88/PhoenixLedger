@echo off
setlocal
cd /d "%~dp0PhoenixLedger.CSharpLab"
where dotnet >nul 2>&1
if errorlevel 1 (
 echo.
 echo The .NET 10 SDK is required to build and run PhoenixLedger C# Lab.
 echo Download it from https://dotnet.microsoft.com/download/dotnet/10.0
 echo Install the Windows x64 SDK, then run this file again.
 echo.
 pause
 exit /b 1
)
echo Starting the SEPARATE experimental C# PhoenixLedger edition...
dotnet run --configuration Release
if errorlevel 1 (
 echo.
 echo The C# Lab could not start. Review the build error above.
 pause
 exit /b 1
)
endlocal
