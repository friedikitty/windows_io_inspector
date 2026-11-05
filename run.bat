@echo off
cd /d "%~dp0"
if not exist bin\IoInspector.exe (call build.bat >nul || (echo BUILD FAILED & exit /b 1))
net session >nul 2>&1
if errorlevel 1 (
  echo elevating...
  powershell -NoProfile -Command "Start-Process -FilePath '%~dp0bin\IoInspector.exe' -ArgumentList '%*' -Verb RunAs"
  exit /b 0
)
bin\IoInspector.exe %*
