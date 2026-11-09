@echo off
rem Simulador do harness: NAO executa o gbak real.
set "HARNESS_FERRAMENTA=gbak"
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0..\simulador\ferramenta-falsa.ps1" %*
exit /b %ERRORLEVEL%
