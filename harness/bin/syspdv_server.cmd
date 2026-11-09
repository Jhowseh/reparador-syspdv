@echo off
rem Simulador do harness: NAO executa o syspdv_server real.
set "HARNESS_FERRAMENTA=syspdv_server"
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0..\simulador\ferramenta-falsa.ps1" %*
exit /b %ERRORLEVEL%
