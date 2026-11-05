@echo off
cd /d "%~dp0"
call build.bat >nul || (echo BUILD FAILED & exit /b 1)
bin\IoInspector.exe --selftest || (echo SELFTEST FAILED & exit /b 1)
bin\IoInspector.exe --detail 4 >nul || (echo DETAIL FAILED & exit /b 1)
bin\IoInspector.exe --cli --timeout 3 --top 5 --sort total || (echo CLI SMOKE FAILED & exit /b 1)
echo TEST OK
