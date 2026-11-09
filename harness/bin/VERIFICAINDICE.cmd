@echo off
rem Simulador do harness: NAO executa o VERIFICAINDICE real.
set "HARNESS_FERRAMENTA=VERIFICAINDICE"
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0..\simulador\ferramenta-falsa.ps1" %*
exit /b %ERRORLEVEL%
