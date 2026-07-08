# DisplayXR Unity Samples

Sample Unity projects for the [DisplayXR Unity plugin](https://github.com/DisplayXR/displayxr-unity)
(`com.displayxr.unity`) — one repo, one shared installer, one place to keep them
building against new plugin/runtime releases.

Three of the samples are **render-pipeline conformance tests** (a matrix of
render pipeline × stereo mode × 2D-UI overlay); the fourth is the **Desktop
Avatar** feature showcase.

| Sample | Folder | Render pipeline / mode | Highlights |
|---|---|---|---|
| **BiRP Multi-Pass** | `samples/birp-multipass` | Built-in RP, multi-pass stereo | Baseline cube + rig-switching demo |
| **URP Single-Pass + 2D UI** | `samples/urp-singlepass-ui` | URP, single-pass instanced | `XrCompositionLayerWindowSpaceEXT` 2D UI overlay |
| **HDRP Single-Pass + 2D UI** | `samples/hdrp-singlepass-ui` | HDRP, single-pass instanced | Same UI overlay as URP, on HDRP |
| **Desktop Avatar** | `samples/desktop-avatar` | URP | Alpha-native transparency, click-through, per-eye foreground clip, `XR_EXT_display_zones` (3D zone + Local2D) |

## Prerequisites

- The **DisplayXR runtime** installed (the installers hard-require it; floor 1.26.1).
- **Unity** matching each sample's `ProjectSettings/ProjectVersion.txt`.
- **NSIS** (for building installers) at `C:\Program Files (x86)\NSIS\`.

## Building a sample

1. Build the Unity Player to `Builds/Win64/<productName>/`. Either:
   - **Headless (canonical):** `samples\<sample>\unity_build.bat` — batchmode
     build that honors the project's committed graphics API. Override the editor
     with `set UNITY_PATH=...`.
   - **Editor:** open `samples/<sample>/` in Unity and **File ▸ Build**. (The
     BiRP sample additionally has `BuildScript.BuildWindows64` in
     `Assets/Editor/DXRBuildScript.cs`, which force-sets D3D12.)
2. Build its installer:
   ```bat
   samples\<sample>\installer\build-installer.bat [VERSION]
   ```
   The Setup `.exe` is written next to the stub under `samples\<sample>\installer\`.

## Plugin pin

Each sample pins the plugin via `Packages/manifest.json`:

```json
"com.displayxr.unity": "https://github.com/DisplayXR/displayxr-unity.git#upm/vX.Y.Z"
```

For local plugin development, override with a `file:` path to your
`displayxr-unity` checkout (don't commit that override).

## One shared installer — the important bit

**All installer/uninstaller logic lives in
[`installer/common/SampleInstaller.nsh`](installer/common/SampleInstaller.nsh).**
Each sample's `installer/*.nsi` is a thin stub that sets five `SAMPLE_*`
`!define`s and `!include`s the shared file. Install dir, registry keys, the
Add/Remove-Programs entry, the app-manifest slug, icon filenames, and the
uninstall sweep are all **derived** from one `SAMPLE_KEY` — so they can never
drift between samples. (This is what previously caused the HDRP installer to
collide with the 2D-UI one in `%ProgramData%\DisplayXR\apps\`.)

Adding a sample = drop a Unity project under `samples/<name>/`, add a 6-line
stub under its `installer/`, done. See [CLAUDE.md](CLAUDE.md) for the exact
stub template and conventions.

## History

These samples were consolidated from the former `displayxr-unity-test`,
`displayxr-unity-test-2d-ui`, `displayxr-unity-test-hdrp`, and
`displayxr-unity-test-transparent` repos (full history preserved via
`git subtree`).
