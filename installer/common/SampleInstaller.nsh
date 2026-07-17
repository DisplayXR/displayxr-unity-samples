; DisplayXR Unity Sample — shared Windows installer
; Copyright 2026, DisplayXR
; SPDX-License-Identifier: Apache-2.0
;
; SINGLE SOURCE OF TRUTH for every sample's installer/uninstaller. A sample's
; own installer .nsi is a thin stub that !define's the five SAMPLE_* values
; below and then !include's this file — so install dir, registry keys, ARP
; entry, app-manifest slug, icon names, and the uninstall sweep are all
; DERIVED from one key and can never drift between samples (that copy-paste
; drift is exactly what created the HDRP/2D-UI manifest collision).
;
; Required !define's (set by the sample stub before !include):
;   SAMPLE_KEY          PascalCase id — install-dir leaf, reg-key leaf, ARP suffix
;                       e.g. "BiRPMultiPass"
;   SAMPLE_PRODUCT_EXE  built Unity exe basename (no .exe), == ProjectSettings
;                       productName, e.g. "DisplayXR-BiRP-MultiPass"
;   SAMPLE_DISPLAY_NAME human name (ASCII hyphen, not em-dash, for ANSI NSIS),
;                       e.g. "DisplayXR Unity Sample - BiRP Multi-Pass"
;   SAMPLE_SLUG         snake_case manifest/icon slug, e.g. "unity_birp_multipass"
;   SAMPLE_DESCRIPTION  one-line manifest + welcome description
;
; Passed by build-sample-installer.bat (with fallbacks):
;   VERSION / VERSION_MAJOR / VERSION_MINOR / VERSION_PATCH
;   BIN_DIR    folder containing <SAMPLE_PRODUCT_EXE>.exe + icon.png + icon_sbs.png
;   SOURCE_DIR repo-relative sample root
;   OUTPUT_DIR where the Setup .exe is written
;   MIN_RUNTIME_VERSION  runtime floor (default below)

!ifndef SAMPLE_KEY
    !error "SAMPLE_KEY not defined — include this from a sample stub that sets it"
!endif
!ifndef SAMPLE_PRODUCT_EXE
    !error "SAMPLE_PRODUCT_EXE not defined"
!endif
!ifndef SAMPLE_DISPLAY_NAME
    !error "SAMPLE_DISPLAY_NAME not defined"
!endif
!ifndef SAMPLE_SLUG
    !error "SAMPLE_SLUG not defined"
!endif
!ifndef SAMPLE_DESCRIPTION
    !error "SAMPLE_DESCRIPTION not defined"
!endif

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
    !define BIN_DIR "${__FILEDIR__}\..\Builds\Win64\${SAMPLE_PRODUCT_EXE}"
!endif
!ifndef SOURCE_DIR
    !define SOURCE_DIR "${__FILEDIR__}\.."
!endif
!ifndef OUTPUT_DIR
    !define OUTPUT_DIR "${__FILEDIR__}"
!endif

; Derived, single-source identifiers — everything keys off SAMPLE_KEY / SAMPLE_SLUG.
!define ARP_KEY  "Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayXRUnity${SAMPLE_KEY}"
!define COMP_KEY "Software\DisplayXR\Unity\${SAMPLE_KEY}"
!define APP_EXE  "${SAMPLE_PRODUCT_EXE}.exe"

;--------------------------------
; General

Name "${SAMPLE_DISPLAY_NAME} ${VERSION}"
OutFile "${OUTPUT_DIR}\DisplayXR-Unity-${SAMPLE_KEY}-Setup-${VERSION}.exe"
InstallDir "$PROGRAMFILES64\DisplayXR\Unity\${SAMPLE_KEY}"
InstallDirRegKey HKLM "${COMP_KEY}" "InstallPath"
RequestExecutionLevel admin
ShowInstDetails show
ShowUninstDetails show

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"
!include "WordFunc.nsh"
!insertmacro VersionCompare

; The DisplayXR runtime floor these samples need. 1.26.1 is where the
; VK->D3D11 KMT-shared-texture / DComp bridge stabilized; individual samples
; may raise it by passing /DMIN_RUNTIME_VERSION=x.y.z.
!ifndef MIN_RUNTIME_VERSION
    !define MIN_RUNTIME_VERSION "1.26.1"
!endif

;--------------------------------
; UI

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "${SAMPLE_DISPLAY_NAME} Setup"
!define MUI_WELCOMEPAGE_TEXT "${SAMPLE_DESCRIPTION}$\r$\n$\r$\nThe DisplayXR runtime must be installed first."

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
    ; installer in the 64-bit view; NSIS is 32-bit so switch views to match.
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
        MessageBox MB_ICONSTOP "DisplayXR runtime $1 is too old.$\r$\n$\r$\nThis sample requires runtime ${MIN_RUNTIME_VERSION} or later.$\r$\n$\r$\nUpdate from:$\r$\nhttps://github.com/DisplayXR/displayxr-runtime/releases"
        Abort
    ${EndIf}
FunctionEnd

;--------------------------------
; Install

Section "${SAMPLE_DISPLAY_NAME}" SecApp
    SectionIn RO

    ; Match the runtime installer's 64-bit registry view.
    SetRegView 64
    ; All-users context — $APPDATA -> %ProgramData%, $SMPROGRAMS -> All Users.
    SetShellVarContext all

    ; Kill any running instance so we can overwrite the exe.
    nsExec::ExecToLog 'taskkill /f /im ${APP_EXE}'
    Pop $0

    ; Copy the entire Unity Player tree (exe + _Data\ + UnityPlayer.dll +
    ; MonoBleedingEdge\ + plugins). BIN_DIR is the folder containing the exe.
    SetOutPath "$INSTDIR"
    File /r "${BIN_DIR}\*.*"

    ; Registered-mode app manifest + icons under %ProgramData% (system-wide,
    ; installer-elevated). Slug-scoped filenames so no two samples collide in
    ; %ProgramData%\DisplayXR\apps\ (the old copy-paste collision is now
    ; structurally impossible — the names derive from SAMPLE_SLUG).
    CreateDirectory "$APPDATA\DisplayXR\apps"
    SetOutPath "$APPDATA\DisplayXR\apps"
    File /oname=icon_${SAMPLE_SLUG}.png "${BIN_DIR}\icon.png"
    File /oname=icon_sbs_${SAMPLE_SLUG}.png "${BIN_DIR}\icon_sbs.png"

    FileOpen $0 "$APPDATA\DisplayXR\apps\${SAMPLE_SLUG}.displayxr.json" w
    FileWrite $0 '{$\r$\n'
    FileWrite $0 '  "schema_version": 1,$\r$\n'
    FileWrite $0 '  "name": "${SAMPLE_DISPLAY_NAME}",$\r$\n'
    FileWrite $0 '  "type": "3d",$\r$\n'
    FileWrite $0 '  "category": "sample",$\r$\n'
    FileWrite $0 '  "display_mode": "auto",$\r$\n'
    FileWrite $0 '  "min_runtime": "${MIN_RUNTIME_VERSION}",$\r$\n'
    FileWrite $0 '  "description": "${SAMPLE_DESCRIPTION}",$\r$\n'
    FileWrite $0 '  "icon": "icon_${SAMPLE_SLUG}.png",$\r$\n'
    FileWrite $0 '  "icon_3d": "icon_sbs_${SAMPLE_SLUG}.png",$\r$\n'
    FileWrite $0 '  "icon_3d_layout": "sbs-lr",$\r$\n'
    ${WordReplace} "$INSTDIR" "\" "/" "+" $1
    FileWrite $0 '  "exe_path": "$1/${APP_EXE}"$\r$\n'
    FileWrite $0 '}$\r$\n'
    FileClose $0

    ; Registry breadcrumbs.
    WriteRegStr HKLM "${COMP_KEY}" "InstallPath" "$INSTDIR"
    WriteRegStr HKLM "${COMP_KEY}" "Version" "${VERSION}"

    ; Add/Remove Programs entry.
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    WriteRegStr HKLM "${ARP_KEY}" "DisplayName" "${SAMPLE_DISPLAY_NAME}"
    WriteRegStr HKLM "${ARP_KEY}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
    WriteRegStr HKLM "${ARP_KEY}" "QuietUninstallString" "$\"$INSTDIR\Uninstall.exe$\" /S"
    WriteRegStr HKLM "${ARP_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "${ARP_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
    WriteRegStr HKLM "${ARP_KEY}" "Publisher" "DisplayXR"
    WriteRegStr HKLM "${ARP_KEY}" "DisplayVersion" "${VERSION}"
    WriteRegDWORD HKLM "${ARP_KEY}" "VersionMajor" ${VERSION_MAJOR}
    WriteRegDWORD HKLM "${ARP_KEY}" "VersionMinor" ${VERSION_MINOR}
    WriteRegDWORD HKLM "${ARP_KEY}" "NoModify" 1
    WriteRegDWORD HKLM "${ARP_KEY}" "NoRepair" 1
    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    IntFmt $0 "0x%08X" $0
    WriteRegDWORD HKLM "${ARP_KEY}" "EstimatedSize" "$0"
SectionEnd

Section "Start Menu Shortcut" SecShortcut
    SetShellVarContext all
    CreateDirectory "$SMPROGRAMS\DisplayXR"
    CreateShortCut "$SMPROGRAMS\DisplayXR\${SAMPLE_DISPLAY_NAME}.lnk" \
        "$INSTDIR\${APP_EXE}" "" \
        "$INSTDIR\${APP_EXE}" 0
SectionEnd

;--------------------------------
; Uninstall

Section "Uninstall"
    SetRegView 64
    SetShellVarContext all

    nsExec::ExecToLog 'taskkill /f /im ${APP_EXE}'
    Pop $0

    ; Registered-mode manifest + icons (slug-scoped, so only this sample's).
    Delete "$APPDATA\DisplayXR\apps\${SAMPLE_SLUG}.displayxr.json"
    Delete "$APPDATA\DisplayXR\apps\icon_${SAMPLE_SLUG}.png"
    Delete "$APPDATA\DisplayXR\apps\icon_sbs_${SAMPLE_SLUG}.png"
    RMDir "$APPDATA\DisplayXR\apps"

    ; Unity Player tree — blow the whole install dir.
    Delete "$INSTDIR\Uninstall.exe"
    RMDir /r "$INSTDIR"
    ; Only remove the umbrella if no other Unity samples remain.
    RMDir "$PROGRAMFILES64\DisplayXR\Unity"

    Delete "$SMPROGRAMS\DisplayXR\${SAMPLE_DISPLAY_NAME}.lnk"
    ; Leave $SMPROGRAMS\DisplayXR — the runtime's own shortcuts may live there.

    DeleteRegKey HKLM "${COMP_KEY}"
    DeleteRegKey /ifempty HKLM "Software\DisplayXR\Unity"
    DeleteRegKey HKLM "${ARP_KEY}"
SectionEnd

;--------------------------------
; Version metadata

VIProductVersion "${VERSION_MAJOR}.${VERSION_MINOR}.${VERSION_PATCH}.0"
VIAddVersionKey "ProductName" "${SAMPLE_DISPLAY_NAME}"
VIAddVersionKey "CompanyName" "DisplayXR"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 DisplayXR"
VIAddVersionKey "FileDescription" "${SAMPLE_DISPLAY_NAME} Installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
