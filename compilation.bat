@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0compilation.ps1" %*
exit /b %errorlevel%
