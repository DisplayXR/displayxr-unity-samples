# CLAUDE.md

Guidance for Claude Code when working in **displayxr-unity-samples**.

## What this repo is

A monorepo of sample Unity projects for the DisplayXR Unity plugin
(`com.displayxr.unity`, repo `displayxr-unity`). Each sample lives under
`samples/<name>/` and is a self-contained Unity project. Consolidated from four
former `displayxr-unity-test*` repos (history preserved via `git subtree`).

Samples:
- `samples/birp-multipass` — Built-in RP, multi-pass stereo. Key `BiRPMultiPass`.
- `samples/urp-singlepass-ui` — URP single-pass + `XrCompositionLayerWindowSpaceEXT` 2D UI. Key `URPSinglePassUI`.
- `samples/hdrp-singlepass-ui` — HDRP single-pass + 2D UI. Key `HDRPSinglePassUI`.
- `samples/desktop-avatar` — URP Desktop Avatar showcase (transparency, click-through, per-eye foreground clip, `XR_EXT_display_zones`). Key `DesktopAvatar`.

## The installer is shared — never fork it

**All installer/uninstaller logic lives in `installer/common/SampleInstaller.nsh`.**
A sample's `installer/<Key>Installer.nsi` is a *stub* that only sets five
`!define`s and includes the shared file:

```nsi
!define SAMPLE_KEY          "BiRPMultiPass"
!define SAMPLE_PRODUCT_EXE  "DisplayXR-BiRP-MultiPass"
!define SAMPLE_DISPLAY_NAME "DisplayXR Unity Sample - BiRP Multi-Pass"
!define SAMPLE_SLUG         "unity_birp_multipass"
!define SAMPLE_DESCRIPTION  "One-line description shown in the manifest + welcome page."
!ifndef VERSION
    !define VERSION "1.0.0"
!endif
!include "${__FILEDIR__}\..\..\..\installer\common\SampleInstaller.nsh"
```

The shared file DERIVES everything else from `SAMPLE_KEY` / `SAMPLE_SLUG`:
- install dir `…\DisplayXR\Unity\<Key>`
- component regkey `HKLM\Software\DisplayXR\Unity\<Key>`
- ARP uninstall key `DisplayXRUnity<Key>`
- manifest `%ProgramData%\DisplayXR\apps\<slug>.displayxr.json` + `icon_<slug>.png` / `icon_sbs_<slug>.png`
- Setup output `DisplayXR-Unity-<Key>-Setup-<ver>.exe`

**Fix installer behavior once, in the shared file** — do NOT copy logic into a
stub. (Per-repo copy-paste is exactly what caused the old HDRP↔2D-UI manifest
collision.)

### NSI conventions
- **ASCII only in `.nsi`/`.nsh` strings** — the scripts compile ANSI, so use a
  plain hyphen `-` in `SAMPLE_DISPLAY_NAME`, not an em-dash. (Em-dashes are fine
  in Markdown.)
- `SAMPLE_PRODUCT_EXE` MUST equal the Unity project's `productName` (the built
  exe is `<productName>.exe`). The build script reads `productName` from
  `ProjectSettings.asset`, so keep them in sync.
- Validate any installer change with `makensis` before pushing (a dummy
  `BIN_DIR` containing a stub exe + `icon.png` + `icon_sbs.png` is enough to
  compile).

## Building

Per sample: Unity **File ▸ Build** → `Builds/Win64/<productName>/`, then
`samples\<sample>\installer\build-installer.bat [VERSION]`, which delegates to
`installer\common\build-sample-installer.bat`. The BiRP sample has a batchmode
entry point `BuildScript.BuildWindows64` (class `BuildScript` in
`Assets/Editor/DXRBuildScript.cs` — note the class name differs from the file
name), output derived from `productName`; other samples build via the GUI.

Real Unity player builds are **not** run in CI (no license/runner) — CI is lint
only (see below). Build + installer + install/uninstall smoke tests are manual
on a Windows box with the runtime installed.

## Plugin pin

Standardize on the released `#upm/vX.Y.Z` tag in each `Packages/manifest.json`.
Use a local `file:` override only for plugin dev, and don't commit it.

## CI / lint

`.github/workflows/lint.yml` runs on PRs: a **vendor-name guard** (authored
files must not self-brand with the display vendor's company name — see the
allowlist in the workflow) and a workflow-YAML validity check. Keep the guard.

## Uninstall interplay with the bundle

The DisplayXR meta-bundle uninstaller removes these samples by **enumerating**
`HKLM\Software\DisplayXR\Unity\*` (not a hardcoded name list), so any sample
that registers a `<Key>` subkey is swept automatically — no bundle change is
needed when adding a sample here.
