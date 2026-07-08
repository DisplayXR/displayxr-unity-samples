@echo off
REM =====================================================================
REM  Shared installer builder for every DisplayXR Unity sample.
REM
REM  Usage:
REM     build-sample-installer.bat <sample-root-dir> [VERSION]
REM
REM  Typically invoked by a sample's own installer\build-installer.bat:
REM     call "%~dp0..\..\..\installer\common\build-sample-installer.bat" "%~dp0.." %*
REM
REM  Derives the Unity product name from the sample's
REM  ProjectSettings\ProjectSettings.asset (single source of truth), so
REM  BIN_DIR / the built exe name can never drift from the installer.
REM  Finds the single stub .nsi under <sample>\installer\ and compiles it
REM  against installer\common\SampleInstaller.nsh.
REM =====================================================================
setlocal enabledelayedexpansion

set "COMMON_DIR=%~dp0"
set "SAMPLE_ROOT=%~1"
if "%SAMPLE_ROOT%"=="" (
    echo ERROR: sample root dir required.
    echo Usage: build-sample-installer.bat ^<sample-root-dir^> [VERSION]
    exit /b 1
)
REM Strip trailing backslash for clean joins.
if "%SAMPLE_ROOT:~-1%"=="\" set "SAMPLE_ROOT=%SAMPLE_ROOT:~0,-1%"

set "PS_ASSET=%SAMPLE_ROOT%\ProjectSettings\ProjectSettings.asset"
if not exist "%PS_ASSET%" (
    echo ERROR: %PS_ASSET% not found — is "%SAMPLE_ROOT%" a Unity project?
    exit /b 1
)

REM productName: <name>  (no spaces in our sample product names)
set "PRODUCT="
for /f "tokens=1,* delims=:" %%a in ('findstr /b /c:"  productName:" "%PS_ASSET%"') do set "PRODUCT=%%b"
set "PRODUCT=%PRODUCT: =%"
if "%PRODUCT%"=="" (
    echo ERROR: could not read productName from %PS_ASSET%.
    exit /b 1
)

set "BIN_DIR=%SAMPLE_ROOT%\Builds\Win64\%PRODUCT%"
set "OUT_DIR=%SAMPLE_ROOT%\installer"

if not exist "%BIN_DIR%\%PRODUCT%.exe" (
    echo ERROR: Unity Player build not found at %BIN_DIR%\%PRODUCT%.exe
    echo Open the project in Unity and Build to Builds\Win64\%PRODUCT%\, then re-run.
    exit /b 1
)

REM Locate the single stub .nsi under the sample's installer\ dir.
set "STUB="
for %%f in ("%SAMPLE_ROOT%\installer\*.nsi") do set "STUB=%%f"
if "%STUB%"=="" (
    echo ERROR: no stub .nsi under %SAMPLE_ROOT%\installer\.
    exit /b 1
)

REM Version: arg2 or VERSION env, default 1.0.0. Split into MAJOR.MINOR.PATCH.
if "%VERSION%"=="" set "VERSION=%~2"
if "%VERSION%"=="" set "VERSION=1.0.0"
for /f "tokens=1,2,3 delims=." %%a in ("%VERSION%") do (
    set "VMAJOR=%%a"
    set "VMINOR=%%b"
    set "VPATCH=%%c"
)
if "%VMINOR%"=="" set "VMINOR=0"
if "%VPATCH%"=="" set "VPATCH=0"

set "MAKENSIS=C:\Program Files (x86)\NSIS\makensis.exe"
if not exist "%MAKENSIS%" set "MAKENSIS=makensis"

echo === Building installer for %PRODUCT% (v%VERSION%) ===
"%MAKENSIS%" /DVERSION=%VERSION% /DVERSION_MAJOR=%VMAJOR% /DVERSION_MINOR=%VMINOR% /DVERSION_PATCH=%VPATCH% ^
    "/DBIN_DIR=%BIN_DIR%" "/DSOURCE_DIR=%SAMPLE_ROOT%" "/DOUTPUT_DIR=%OUT_DIR%" "%STUB%" || exit /b 1

echo === DONE ===
echo Installer written under: %OUT_DIR%
endlocal
