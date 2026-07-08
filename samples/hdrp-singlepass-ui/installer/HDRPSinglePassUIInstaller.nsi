; DisplayXR Unity Sample — HDRP Single-Pass + 2D UI — installer stub.
; All logic lives in the shared installer/common/SampleInstaller.nsh; this file
; only parameterizes it. See CLAUDE.md. ASCII only (compiles ANSI).

!define SAMPLE_KEY          "HDRPSinglePassUI"
!define SAMPLE_PRODUCT_EXE  "DisplayXR-HDRP-SinglePass-UI"
!define SAMPLE_DISPLAY_NAME "DisplayXR Unity Sample - HDRP Single-Pass + 2D UI"
!define SAMPLE_SLUG         "unity_hdrp_singlepass_ui"
!define SAMPLE_DESCRIPTION  "HDRP single-pass instanced stereo with a 2D UI overlay - the HDRP counterpart of the URP sample."

!ifndef VERSION
    !define VERSION "1.0.0"
!endif

!include "${__FILEDIR__}\..\..\..\installer\common\SampleInstaller.nsh"
