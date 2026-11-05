@echo off
cd /d "%~dp0"
dotnet build -c Release -o bin %*
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo BUILD OK: %~dp0bin\IoInspector.exe
