@echo off
REM ============================================================
REM  unity_build.bat - build the Win64 Player for this test repo.
REM
REM  Opens the project with Unity in -batchmode and builds the
REM  scenes listed in Build Settings to Builds\Win64\<exe>, writing
REM  a log to Logs\unity_build.log. This is the headless equivalent
REM  of File > Build Settings > Build.
REM
REM  Override the editor path:  set UNITY_PATH=C:\path\to\Unity.exe
REM ============================================================
setlocal

set "UNITY_VERSION=6000.4.0f1"
if "%UNITY_PATH%"=="" set "UNITY_PATH=C:\Program Files\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"

set "PROJECT_PATH=%~dp0"
if "%PROJECT_PATH:~-1%"=="\" set "PROJECT_PATH=%PROJECT_PATH:~0,-1%"

REM Build into a named subfolder (NOT loose in Win64) so the player and its
REM _Data/dll sidecars stay self-contained — matches installer\build-installer.bat
REM (BIN_DIR = Builds\Win64\DisplayXR-test-hdrp, exe = DisplayXR-test-hdrp.exe).
set "OUT_DIR=%PROJECT_PATH%\Builds\Win64\DisplayXR-test-hdrp"
set "OUT_EXE=%OUT_DIR%\DisplayXR-test-hdrp.exe"
set "LOG=%PROJECT_PATH%\Logs\unity_build.log"

if not exist "%UNITY_PATH%" (
    echo ERROR: Unity not found at "%UNITY_PATH%".
    echo Set UNITY_PATH to your Unity %UNITY_VERSION% Editor\Unity.exe and retry.
    exit /b 1
)

if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"
if not exist "%PROJECT_PATH%\Logs" mkdir "%PROJECT_PATH%\Logs"

echo Unity   : %UNITY_PATH%
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
