@echo off
rem Simulador do harness: NAO executa o dbMonitor real.
set "HARNESS_FERRAMENTA=dbMonitor"
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0..\simulador\ferramenta-falsa.ps1" %*
exit /b %ERRORLEVEL%
