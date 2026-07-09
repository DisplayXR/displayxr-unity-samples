@echo off
REM ============================================================
REM  build-sample.bat - build any of the 4 DisplayXR samples to
REM  a caller-chosen output folder.
REM
REM  Usage:  build-sample.bat <sample> [outputDir]
REM
REM    sample     One of: birp | urp | hdrp | avatar
REM               (or the exact folder name under samples\).
REM    outputDir  Optional folder to place <productName>.exe in.
REM               Default: samples\<folder>\Builds\Win64\<productName>
REM
REM  Builds the scenes in Build Settings headlessly (-batchmode),
REM  the equivalent of File > Build Settings > Build. A per-build
REM  log is written next to the output as build.log.
REM
REM  Override the editor path:  set UNITY_PATH=C:\path\to\Unity.exe
REM ============================================================
setlocal

set "UNITY_VERSION=6000.4.0f1"
if "%UNITY_PATH%"=="" set "UNITY_PATH=C:\Program Files\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

REM --- Map the sample key -> folder + productName --------------
set "KEY=%~1"
if "%KEY%"=="" goto :usage

set "FOLDER="
set "PRODUCT="
if /i "%KEY%"=="birp"              ( set "FOLDER=birp-multipass"     & set "PRODUCT=DisplayXR-BiRP-MultiPass" )
if /i "%KEY%"=="birp-multipass"    ( set "FOLDER=birp-multipass"     & set "PRODUCT=DisplayXR-BiRP-MultiPass" )
if /i "%KEY%"=="urp"               ( set "FOLDER=urp-singlepass-ui"  & set "PRODUCT=DisplayXR-URP-SinglePass-UI" )
if /i "%KEY%"=="urp-singlepass-ui" ( set "FOLDER=urp-singlepass-ui"  & set "PRODUCT=DisplayXR-URP-SinglePass-UI" )
if /i "%KEY%"=="hdrp"              ( set "FOLDER=hdrp-singlepass-ui" & set "PRODUCT=DisplayXR-HDRP-SinglePass-UI" )
if /i "%KEY%"=="hdrp-singlepass-ui"( set "FOLDER=hdrp-singlepass-ui" & set "PRODUCT=DisplayXR-HDRP-SinglePass-UI" )
if /i "%KEY%"=="avatar"            ( set "FOLDER=desktop-avatar"     & set "PRODUCT=DisplayXR-DesktopAvatar" )
if /i "%KEY%"=="desktop-avatar"    ( set "FOLDER=desktop-avatar"     & set "PRODUCT=DisplayXR-DesktopAvatar" )

if "%FOLDER%"=="" (
    echo ERROR: unknown sample "%KEY%".
    goto :usage
)

set "PROJECT_PATH=%ROOT%\samples\%FOLDER%"
if not exist "%PROJECT_PATH%\ProjectSettings\ProjectSettings.asset" (
    echo ERROR: sample project not found at "%PROJECT_PATH%".
    exit /b 1
)

REM --- Output folder (arg 2, else default under the project) ---
set "OUT_DIR=%~2"
if "%OUT_DIR%"=="" set "OUT_DIR=%PROJECT_PATH%\Builds\Win64\%PRODUCT%"

set "OUT_EXE=%OUT_DIR%\%PRODUCT%.exe"
set "LOG=%OUT_DIR%\build.log"

if not exist "%UNITY_PATH%" (
    echo ERROR: Unity not found at "%UNITY_PATH%".
    echo Set UNITY_PATH to your Unity %UNITY_VERSION% Editor\Unity.exe and retry.
    exit /b 1
)

if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

echo Unity   : %UNITY_PATH%
echo Sample  : %FOLDER%
echo Project : %PROJECT_PATH%
echo Output  : %OUT_EXE%
echo Log     : %LOG%
echo Building...

"%UNITY_PATH%" -batchmode -quit -projectPath "%PROJECT_PATH%" -buildWindows64Player "%OUT_EXE%" -logFile "%LOG%"

if %ERRORLEVEL% NEQ 0 (
    echo BUILD FAILED ^(exit %ERRORLEVEL%^). See "%LOG%".
    exit /b %ERRORLEVEL%
)
echo BUILD OK: %OUT_EXE%
endlocal
goto :eof

:usage
echo Usage: build-sample.bat ^<sample^> [outputDir]
echo   sample: birp ^| urp ^| hdrp ^| avatar
exit /b 1
