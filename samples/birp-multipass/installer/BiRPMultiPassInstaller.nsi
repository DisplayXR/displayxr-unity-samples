; DisplayXR Unity Sample — BiRP Multi-Pass — installer stub.
; All logic lives in the shared installer/common/SampleInstaller.nsh; this file
; only parameterizes it. See CLAUDE.md. ASCII only (compiles ANSI).

!define SAMPLE_KEY          "BiRPMultiPass"
!define SAMPLE_PRODUCT_EXE  "DisplayXR-BiRP-MultiPass"
!define SAMPLE_DISPLAY_NAME "DisplayXR Unity Sample - BiRP Multi-Pass"
!define SAMPLE_SLUG         "unity_birp_multipass"
!define SAMPLE_DESCRIPTION  "Built-in Render Pipeline multi-pass stereo test - textured cube + rig-switching demo through the DisplayXR provider."

!ifndef VERSION
    !define VERSION "1.0.0"
!endif

!include "${__FILEDIR__}\..\..\..\installer\common\SampleInstaller.nsh"
