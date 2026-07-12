; DisplayXR Unity Sample — Desktop Avatar — installer stub.
; All logic lives in the shared installer/common/SampleInstaller.nsh; this file
; only parameterizes it. See CLAUDE.md. ASCII only (compiles ANSI).

!define SAMPLE_KEY          "DesktopAvatar"
!define SAMPLE_PRODUCT_EXE  "DisplayXR-DesktopAvatar"
!define SAMPLE_DISPLAY_NAME "DisplayXR Unity Sample - Desktop Avatar"
!define SAMPLE_SLUG         "unity_desktop_avatar"
!define SAMPLE_DESCRIPTION  "Desktop avatar showcase - alpha-native transparency, click-through, per-eye foreground clip, and XR_DXR_display_zones (3D zone + Local2D speech bubble)."

!ifndef VERSION
    !define VERSION "1.0.0"
!endif

!include "${__FILEDIR__}\..\..\..\installer\common\SampleInstaller.nsh"
