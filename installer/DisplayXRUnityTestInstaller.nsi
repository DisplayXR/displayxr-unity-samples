; DisplayXR Unity Test — Windows Installer
; Copyright 2026, DisplayXR
; SPDX-License-Identifier: Apache-2.0
;
; Build: makensis /DVERSION=1.7.0 /DBIN_DIR=<unity-build-dir> /DSOURCE_DIR=<repo-root> /DOUTPUT_DIR=<output-dir> DisplayXRUnityTestInstaller.nsi
;
; Hard-prereqs the DisplayXR runtime (HKLM\Software\DisplayXR\Runtime\InstallPath).
; Installs the Unity Player tree to Program Files\DisplayXR\Unity\Test\.
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
    !define BIN_DIR "${__FILEDIR__}\..\Builds\Win64\DisplayXR-test"
!endif
!ifndef SOURCE_DIR
    !define SOURCE_DIR "${__FILEDIR__}\.."
!endif
!ifndef OUTPUT_DIR
    !define OUTPUT_DIR "${__FILEDIR__}"
!endif

;--------------------------------
; General

Name "DisplayXR Unity Test ${VERSION}"
OutFile "${OUTPUT_DIR}\DisplayXR-Unity-Test-Setup-${VERSION}.exe"
InstallDir "$PROGRAMFILES64\DisplayXR\Unity\Test"
InstallDirRegKey HKLM "Software\DisplayXR\Unity\Test" "InstallPath"
RequestExecutionLevel admin
ShowInstDetails show
ShowUninstDetails show

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"
!include "WordFunc.nsh"
!insertmacro VersionCompare

; Minimum runtime version. Cube-and-rig-switching demo only depends on the
; baseline stereo pipeline, so the same floor as the gaussiansplat reference
; installer (1.3.0) is fine — that's the runtime build where the VK→D3D11
; KMT-shared-texture / DComp bridge stabilized.
!define MIN_RUNTIME_VERSION "1.3.0"

;--------------------------------
; UI

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "DisplayXR Unity Test Setup"
!define MUI_WELCOMEPAGE_TEXT "This will install the DisplayXR Unity plugin test app (cube + rig-switching demo).$\r$\n$\r$\nThe DisplayXR runtime must be installed first."

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

    ; HKLM\Software\DisplayXR\Runtime\InstallPath is set by the runtime
    ; installer. NSIS is a 32-bit executable so HKLM access is silently
    ; redirected through WOW6432Node by default; the runtime writes to the
    ; 64-bit view. Switch to 64-bit view to match.
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

Section "DisplayXR Unity Test" SecApp
    SectionIn RO

    ; Match the runtime installer's 64-bit registry view so HKLM keys land
    ; in the canonical (non-WOW6432Node) hive.
    SetRegView 64

    ; All-users context — $APPDATA -> %ProgramData%, $SMPROGRAMS -> All Users.
    SetShellVarContext all

    ; Kill any running instance so we can overwrite the exe.
    nsExec::ExecToLog 'taskkill /f /im DisplayXR-test.exe'
    Pop $0

    ; Copy the entire Unity Player tree: exe + DisplayXR-test_Data\ +
    ; UnityPlayer.dll + MonoBleedingEdge\ + plugins. BIN_DIR points at the
    ; folder *containing* DisplayXR-test.exe.
    SetOutPath "$INSTDIR"
    File /r "${BIN_DIR}\*.*"

    ; Drop the registered-mode app manifest + icons under %ProgramData%
    ; (system-wide, installer-elevated — matches §2.2 of the manifest spec).
    ; The shell launcher scans %ProgramData%\DisplayXR\apps\ on every workspace
    ; activate and picks up the tile.
    CreateDirectory "$APPDATA\DisplayXR\apps"
    SetOutPath "$APPDATA\DisplayXR\apps"

    ; Manifest + icons live together; icon paths inside the manifest are
    ; resolved relative to the manifest file (per spec §2.3). Source the
    ; icons from BIN_DIR (Unity bundles icon.png + icon_sbs.png next to
    ; the exe via its build pipeline) — single source of truth, no
    ; duplication in the repo. Rename on install so the cube-app art
    ; doesn't get clobbered by the transparent or 2D-UI installers,
    ; which still drop generic icon.png/icon_sbs.png. When those repos
    ; ship per-app art, they should also rename + source from BIN_DIR.
    File /oname=icon_unity_test.png "${BIN_DIR}\icon.png"
    File /oname=icon_sbs_unity_test.png "${BIN_DIR}\icon_sbs.png"

    ; Generate the manifest with an absolute exe_path. Forward slashes so
    ; the JSON parses with any strict library — the manifest spec accepts
    ; either separator and normalizes.
    FileOpen $0 "$APPDATA\DisplayXR\apps\unity_test.displayxr.json" w
    FileWrite $0 '{$\r$\n'
    FileWrite $0 '  "schema_version": 1,$\r$\n'
    FileWrite $0 '  "name": "DisplayXR-test",$\r$\n'
    FileWrite $0 '  "type": "3d",$\r$\n'
    FileWrite $0 '  "category": "test",$\r$\n'
    FileWrite $0 '  "display_mode": "auto",$\r$\n'
    FileWrite $0 '  "description": "Test App for DisplayXR Unity Plugin",$\r$\n'
    FileWrite $0 '  "icon": "icon_unity_test.png",$\r$\n'
    FileWrite $0 '  "icon_3d": "icon_sbs_unity_test.png",$\r$\n'
    FileWrite $0 '  "icon_3d_layout": "sbs-lr",$\r$\n'
    ${WordReplace} "$INSTDIR" "\" "/" "+" $1
    FileWrite $0 '  "exe_path": "$1/DisplayXR-test.exe"$\r$\n'
    FileWrite $0 '}$\r$\n'
    FileClose $0

    ; Registry breadcrumbs.
    SetRegView 64
    WriteRegStr HKLM "Software\DisplayXR\Unity\Test" "InstallPath" "$INSTDIR"
    WriteRegStr HKLM "Software\DisplayXR\Unity\Test" "Version" "${VERSION}"

    ; Add/Remove Programs entry.
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest" \
        "DisplayName" "DisplayXR Unity Test"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest" \
        "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest" \
        "QuietUninstallString" "$\"$INSTDIR\Uninstall.exe$\" /S"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest" \
        "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest" \
        "DisplayIcon" "$INSTDIR\DisplayXR-test.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest" \
        "Publisher" "DisplayXR"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest" \
        "DisplayVersion" "${VERSION}"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest" \
        "VersionMajor" ${VERSION_MAJOR}
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest" \
        "VersionMinor" ${VERSION_MINOR}
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest" \
        "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest" \
        "NoRepair" 1
    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    IntFmt $0 "0x%08X" $0
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest" \
        "EstimatedSize" "$0"
SectionEnd

Section "Start Menu Shortcut" SecShortcut
    SetShellVarContext all
    CreateDirectory "$SMPROGRAMS\DisplayXR"
    CreateShortCut "$SMPROGRAMS\DisplayXR\DisplayXR Unity Test.lnk" \
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

    ; Remove the registered-mode manifest + icons.
    Delete "$APPDATA\DisplayXR\apps\unity_test.displayxr.json"
    ; Only delete the cube-app's own icons — the transparent / 2D-UI
    ; installers still reference shared icon.png / icon_sbs.png, so we
    ; leave those alone.
    Delete "$APPDATA\DisplayXR\apps\icon_unity_test.png"
    Delete "$APPDATA\DisplayXR\apps\icon_sbs_unity_test.png"
    RMDir "$APPDATA\DisplayXR\apps"

    ; Remove install dir contents — Unity Player tree has many files
    ; under _Data\, MonoBleedingEdge\, etc., so blow the whole dir.
    Delete "$INSTDIR\Uninstall.exe"
    RMDir /r "$INSTDIR"
    ; Only remove the parent if no other Unity test apps remain.
    RMDir "$PROGRAMFILES64\DisplayXR\Unity"

    ; Start menu shortcut.
    Delete "$SMPROGRAMS\DisplayXR\DisplayXR Unity Test.lnk"
    ; Don't RMDir $SMPROGRAMS\DisplayXR — the runtime's own shortcuts may
    ; still live there.

    DeleteRegKey HKLM "Software\DisplayXR\Unity\Test"
    DeleteRegKey /ifempty HKLM "Software\DisplayXR\Unity"
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnityTest"
SectionEnd

;--------------------------------
; Version metadata

VIProductVersion "${VERSION_MAJOR}.${VERSION_MINOR}.${VERSION_PATCH}.0"
VIAddVersionKey "ProductName" "DisplayXR Unity Test"
VIAddVersionKey "CompanyName" "DisplayXR"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 DisplayXR"
VIAddVersionKey "FileDescription" "DisplayXR Unity Test Installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
