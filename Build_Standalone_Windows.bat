@echo off
setlocal
cd /d "%~dp0PhoenixLedger.CSharpLab"
where dotnet >nul 2>&1
if errorlevel 1 (
 echo Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
 pause
 exit /b 1
)
echo Publishing independent self-contained win-x64 PhoenixLedger C# Lab...
dotnet publish PhoenixLedger.CSharpLab.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "..\dist\win-x64"
if errorlevel 1 (
 echo Build failed; review the error messages above.
 pause
 exit /b 1
)
echo.
echo Build output: %~dp0dist\win-x64\PhoenixLedger.CSharpLab.exe
echo This EXE uses a SEPARATE data folder from Python PhoenixLedger.
pause
endlocal
