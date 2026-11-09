@echo off
rem Simulador do harness: NAO executa o taskkill real.
set "HARNESS_FERRAMENTA=taskkill"
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0..\simulador\ferramenta-falsa.ps1" %*
exit /b %ERRORLEVEL%
