; DisplayXR Unity Sample — URP Single-Pass + 2D UI — installer stub.
; All logic lives in the shared installer/common/SampleInstaller.nsh; this file
; only parameterizes it. See CLAUDE.md. ASCII only (compiles ANSI).

!define SAMPLE_KEY          "URPSinglePassUI"
!define SAMPLE_PRODUCT_EXE  "DisplayXR-URP-SinglePass-UI"
!define SAMPLE_DISPLAY_NAME "DisplayXR Unity Sample - URP Single-Pass + 2D UI"
!define SAMPLE_SLUG         "unity_urp_singlepass_ui"
!define SAMPLE_DESCRIPTION  "URP single-pass instanced stereo with an XrCompositionLayerWindowSpaceEXT 2D UI overlay - DisplayXR provider test."

!ifndef VERSION
    !define VERSION "1.0.0"
!endif

!include "${__FILEDIR__}\..\..\..\installer\common\SampleInstaller.nsh"
