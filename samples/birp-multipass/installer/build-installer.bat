@echo off
setlocal
set "REPO=%~dp0.."
set "BIN_DIR=%REPO%\Builds\Win64\DisplayXR-test"
set "OUT_DIR=%~dp0"
if "%OUT_DIR:~-1%"=="\" set "OUT_DIR=%OUT_DIR:~0,-1%"
if "%VERSION%"=="" set "VERSION=1.7.1"
if "%VERSION_MAJOR%"=="" set "VERSION_MAJOR=1"
if "%VERSION_MINOR%"=="" set "VERSION_MINOR=7"
if "%VERSION_PATCH%"=="" set "VERSION_PATCH=1"

if not exist "%BIN_DIR%\DisplayXR-test.exe" (
    echo ERROR: Unity Player build not found at %BIN_DIR%\DisplayXR-test.exe
    echo Open the project in Unity and run File ^> Build Settings ^> Build, targeting Builds\Win64\DisplayXR-test\.
    exit /b 1
)

"C:\Program Files (x86)\NSIS\makensis.exe" /DVERSION=%VERSION% /DVERSION_MAJOR=%VERSION_MAJOR% /DVERSION_MINOR=%VERSION_MINOR% /DVERSION_PATCH=%VERSION_PATCH% "/DBIN_DIR=%BIN_DIR%" "/DSOURCE_DIR=%REPO%" "/DOUTPUT_DIR=%OUT_DIR%" "%~dp0DisplayXRUnityTestInstaller.nsi" || exit /b 1

echo === DONE ===
echo Installer: %OUT_DIR%\DisplayXR-Unity-Test-Setup-%VERSION%.exe
