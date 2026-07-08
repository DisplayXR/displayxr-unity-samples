@echo off
REM Convenience wrapper — delegates to the shared builder in installer\common.
REM Usage: build-installer.bat [VERSION]
call "%~dp0..\..\..\installer\common\build-sample-installer.bat" "%~dp0.." %*
