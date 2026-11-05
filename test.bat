@echo off
cd /d "%~dp0"
call build.bat >nul || (echo BUILD FAILED & exit /b 1)
bin\IoInspector.exe --selftest || (echo SELFTEST FAILED & exit /b 1)
bin\IoInspector.exe --cli --seconds 3 --top 5 || (echo CLI SMOKE FAILED & exit /b 1)
echo TEST OK
