@echo off
cd /d "%~dp0"
if not exist bin\IoInspector.exe (call build.bat >nul || (echo BUILD FAILED & exit /b 1))
rem GUI mode self-elevates via UAC; --cli runs as-is (counters-only without admin)
bin\IoInspector.exe %*
