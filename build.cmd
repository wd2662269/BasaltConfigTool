@echo off
rem ASCII-only launcher: the real build script carries the Chinese messages.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
exit /b %ERRORLEVEL%