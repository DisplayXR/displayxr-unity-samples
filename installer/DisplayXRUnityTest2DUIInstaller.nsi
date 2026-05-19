; DisplayXR Unity Test (2D UI) — Windows Installer
; Copyright 2026, DisplayXR
; SPDX-License-Identifier: BSL-1.0
;
; Build: makensis /DVERSION=1.7.0 /DBIN_DIR=<unity-build-dir> /DSOURCE_DIR=<repo-root> /DOUTPUT_DIR=<output-dir> DisplayXRUnityTest2DUIInstaller.nsi
;
; Hard-prereqs the DisplayXR runtime (HKLM\Software\DisplayXR\Runtime\InstallPath).
; Installs the Unity Player tree to Program Files\DisplayXR\Unity\Test2DUI\.
; Drops a registered-mode app manifest + icons under %ProgramData%\DisplayXR\apps\
; so the DisplayXR Shell launcher discovers the tile (system-wide, since the
; installer runs elevated). See displayxr-runtime/docs/specs/runtime/displayxr-app-manifest.md.

!ifndef VERSION
    !define VERSION "1.0.0"
!endif
!ifndef VERSION_MAJOR
    !define VERSION_MAJOR "1"
!endif
!ifndef VERSION_MINOR
    !define VERSION_MINOR "0"
!endif
!ifndef VERSION_PATCH
    !define VERSION_PATCH "0"
!endif

!ifndef BIN_DIR
    !define BIN_DIR "${__FILEDIR__}\..\Builds\Win64\DisplayXR-test-2d-ui"
!endif
!ifndef SOURCE_DIR
    !define SOURCE_DIR "${__FILEDIR__}\.."
!endif
!ifndef OUTPUT_DIR
    !define OUTPUT_DIR "${__FILEDIR__}"
!endif

;--------------------------------
; General

Name "DisplayXR Unity Test (2D UI) ${VERSION}"
OutFile "${OUTPUT_DIR}\DisplayXR-Unity-Test2DUI-Setup-${VERSION}.exe"
InstallDir "$PROGRAMFILES64\DisplayXR\Unity\Test2DUI"
InstallDirRegKey HKLM "Software\DisplayXR\Unity\Test2DUI" "InstallPath"
RequestExecutionLevel admin
ShowInstDetails show
ShowUninstDetails show

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"
!include "WordFunc.nsh"
!insertmacro VersionCompare

; Minimum runtime version. Matches the gaussiansplat reference installer
; floor (1.3.0). Window-space composition layer support has been in the
; runtime since this stamp.
!define MIN_RUNTIME_VERSION "1.3.0"

;--------------------------------
; UI

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "DisplayXR Unity Test (2D UI) Setup"
!define MUI_WELCOMEPAGE_TEXT "This will install the DisplayXR Unity plugin window-space 2D UI overlay test app (URP).$\r$\n$\r$\nThe DisplayXR runtime must be installed first."

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

;--------------------------------
; Pre-flight: hard-prereq the runtime

Function .onInit
    ${IfNot} ${RunningX64}
        MessageBox MB_ICONSTOP "DisplayXR requires 64-bit Windows."
        Abort
    ${EndIf}

    SetRegView 64
    ReadRegStr $0 HKLM "Software\DisplayXR\Runtime" "InstallPath"
    ReadRegStr $1 HKLM "Software\DisplayXR\Runtime" "Version"
    SetRegView 32
    ${If} $0 == ""
        MessageBox MB_ICONSTOP "DisplayXR runtime is not installed.$\r$\n$\r$\nInstall the DisplayXR runtime first, then re-run this installer.$\r$\n$\r$\nGet it from:$\r$\nhttps://github.com/DisplayXR/displayxr-runtime/releases"
        Abort
    ${EndIf}

    ${VersionCompare} "$1" "${MIN_RUNTIME_VERSION}" $2
    ${If} $2 == 2
        MessageBox MB_ICONSTOP "DisplayXR runtime $1 is too old.$\r$\n$\r$\nThis test app requires runtime ${MIN_RUNTIME_VERSION} or later.$\r$\n$\r$\nUpdate from:$\r$\nhttps://github.com/DisplayXR/displayxr-runtime/releases"
        Abort
    ${EndIf}
FunctionEnd

;--------------------------------
; Install

Section "DisplayXR Unity Test (2D UI)" SecApp
    SectionIn RO

    SetRegView 64
    SetShellVarContext all

    nsExec::ExecToLog 'taskkill /f /im DisplayXR-test.exe'
    Pop $0

    SetOutPath "$INSTDIR"
    File /r "${BIN_DIR}\*.*"

    CreateDirectory "$APPDATA\DisplayXR\apps"
    SetOutPath "$APPDATA\DisplayXR\apps"

    ; Source icons from BIN_DIR (Unity bundles icon.png + icon_sbs.png next
    ; to the exe) — single source of truth, no duplication in the repo.
    ; Rename on install so this variant's art doesn't collide with the cube
    ; or transparent installers in %ProgramData%\DisplayXR\apps\.
    File /oname=icon_unity_test_2d_ui.png "${BIN_DIR}\icon.png"
    File /oname=icon_sbs_unity_test_2d_ui.png "${BIN_DIR}\icon_sbs.png"

    FileOpen $0 "$APPDATA\DisplayXR\apps\unity_test_2d_ui.displayxr.json" w
    FileWrite $0 '{$\r$\n'
    FileWrite $0 '  "schema_version": 1,$\r$\n'
    FileWrite $0 '  "name": "DisplayXR-test (2D UI)",$\r$\n'
    FileWrite $0 '  "type": "3d",$\r$\n'
    FileWrite $0 '  "category": "test",$\r$\n'
    FileWrite $0 '  "display_mode": "auto",$\r$\n'
    FileWrite $0 '  "description": "Window-space 2D UI overlay test (URP) — textured cube with a runtime-built tuning panel via XrCompositionLayerWindowSpaceEXT.",$\r$\n'
    FileWrite $0 '  "icon": "icon_unity_test_2d_ui.png",$\r$\n'
    FileWrite $0 '  "icon_3d": "icon_sbs_unity_test_2d_ui.png",$\r$\n'
    FileWrite $0 '  "icon_3d_layout": "sbs-lr",$\r$\n'
    ${WordReplace} "$INSTDIR" "\" "/" "+" $1
    FileWrite $0 '  "exe_path": "$1/DisplayXR-test.exe"$\r$\n'
    FileWrite $0 '}$\r$\n'
    FileClose $0

    SetRegView 64
    WriteRegStr HKLM "Software\DisplayXR\Unity\Test2DUI" "InstallPath" "$INSTDIR"
    WriteRegStr HKLM "Software\DisplayXR\Unity\Test2DUI" "Version" "${VERSION}"

    WriteUninstaller "$INSTDIR\Uninstall.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest2DUI" \
        "DisplayName" "DisplayXR Unity Test (2D UI)"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest2DUI" \
        "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest2DUI" \
        "QuietUninstallString" "$\"$INSTDIR\Uninstall.exe$\" /S"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest2DUI" \
        "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest2DUI" \
        "DisplayIcon" "$INSTDIR\DisplayXR-test.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest2DUI" \
        "Publisher" "DisplayXR"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest2DUI" \
        "DisplayVersion" "${VERSION}"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest2DUI" \
        "VersionMajor" ${VERSION_MAJOR}
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest2DUI" \
        "VersionMinor" ${VERSION_MINOR}
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest2DUI" \
        "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest2DUI" \
        "NoRepair" 1
    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    IntFmt $0 "0x%08X" $0
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest2DUI" \
        "EstimatedSize" "$0"
SectionEnd

Section "Start Menu Shortcut" SecShortcut
    SetShellVarContext all
    CreateDirectory "$SMPROGRAMS\DisplayXR"
    CreateShortCut "$SMPROGRAMS\DisplayXR\DisplayXR Unity Test (2D UI).lnk" \
        "$INSTDIR\DisplayXR-test.exe" "" \
        "$INSTDIR\DisplayXR-test.exe" 0
SectionEnd

;--------------------------------
; Uninstall

Section "Uninstall"
    SetRegView 64
    SetShellVarContext all

    nsExec::ExecToLog 'taskkill /f /im DisplayXR-test.exe'
    Pop $0

    Delete "$APPDATA\DisplayXR\apps\unity_test_2d_ui.displayxr.json"
    Delete "$APPDATA\DisplayXR\apps\icon_unity_test_2d_ui.png"
    Delete "$APPDATA\DisplayXR\apps\icon_sbs_unity_test_2d_ui.png"
    RMDir "$APPDATA\DisplayXR\apps"

    Delete "$INSTDIR\Uninstall.exe"
    RMDir /r "$INSTDIR"
    RMDir "$PROGRAMFILES64\DisplayXR\Unity"

    Delete "$SMPROGRAMS\DisplayXR\DisplayXR Unity Test (2D UI).lnk"

    DeleteRegKey HKLM "Software\DisplayXR\Unity\Test2DUI"
    DeleteRegKey /ifempty HKLM "Software\DisplayXR\Unity"
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest2DUI"
SectionEnd

;--------------------------------
; Version metadata

VIProductVersion "${VERSION_MAJOR}.${VERSION_MINOR}.${VERSION_PATCH}.0"
VIAddVersionKey "ProductName" "DisplayXR Unity Test (2D UI)"
VIAddVersionKey "CompanyName" "DisplayXR"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 DisplayXR"
VIAddVersionKey "FileDescription" "DisplayXR Unity Test (2D UI) Installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
