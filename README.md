# DisplayXR Unity Test Project — Transparent Overlay Variant

A test project that exercises the **alpha-native transparent overlay mode**
of the [DisplayXR Unity plugin](https://github.com/DisplayXR/displayxr-unity)
(added in [#57](https://github.com/DisplayXR/displayxr-unity/issues/57); the
chroma-color workaround was removed in v1.6.0 — see
[`#103`](https://github.com/DisplayXR/displayxr-unity/issues/103)).

The Mixamo tiger (with a cube fallback) renders above the desktop with no
rectangular background — Unity emits per-pixel alpha into the OpenXR
swapchain via `XR_ENVIRONMENT_BLEND_MODE_ALPHA_BLEND` and the runtime DP
composes the captured desktop under each tile pre-weave so anti-aliased
silhouettes carry true soft alpha. Clicks outside the silhouette fall
through to whatever desktop window is behind.

**Render pipeline:** Built-in (BiRP).

**Sibling test projects** — each repo focuses on one feature so a regression
in one demo doesn't mask the others:

| Repo | What it demonstrates | Pipeline |
|---|---|---|
| [displayxr-unity-test](https://github.com/DisplayXR/displayxr-unity-test) | Display-centric vs camera-centric rigs, live rig switching | BiRP |
| [displayxr-unity-test-2d-ui](https://github.com/DisplayXR/displayxr-unity-test-2d-ui) | `XrCompositionLayerWindowSpaceEXT` 2D UI overlay (`DisplayXRWindowSpaceUI`) | URP |
| [displayxr-unity-test-transparent](https://github.com/DisplayXR/displayxr-unity-test-transparent) (you are here) | Alpha-native transparent overlay (`DisplayXRTransparentOverlay`) | BiRP |

## What's different from displayxr-unity-test

- `Assets/TransparentAutoSetup.cs` runs at scene load, attaches
  `DisplayXRTransparentOverlay` to the rig cameras, and wires the tiger
  (or cube fallback) as the click-through hit region. No edits to
  `CubeTest.unity` needed.

## Requirements

- **Unity 6000.3 LTS** (Unity 6) or newer
- A **Leia SR Windows** machine (or recent Mac) for end-to-end verification
  — the native window restyling path doesn't run in the editor preview,
  only in a standalone build
- The DisplayXR runtime installed (via the
  [installer](https://github.com/DisplayXR/displayxr-shell-releases/releases))
  — must be a build that advertises `XR_ENVIRONMENT_BLEND_MODE_ALPHA_BLEND`
  on the D3D11/D3D12 service compositor and has the compose-under-bg +
  alpha-gate DP path. Plugin v1.6.0+ requires this.

## Plugin Reference

The project depends on the DisplayXR Unity plugin via Unity Package Manager.
The dependency is declared in `Packages/manifest.json` and tracks the
latest released plugin version (the `upm` branch is force-pushed by the
plugin's CI on every `v*` tag, with the prebuilt native binary):

```json
"com.displayxr.unity": "https://github.com/DisplayXR/displayxr-unity.git#upm"
```

After editing, run `Window → Package Manager → Refresh`.

To test against a local development build of the plugin, change the
dependency to:
```json
"com.displayxr.unity": "file:/absolute/path/to/displayxr-unity"
```
and delete the `com.displayxr.unity` entry from
`Packages/packages-lock.json` so Unity re-resolves on next open. Revert
before committing.

## Quick start

1. Open the project in Unity Hub. First import takes a few minutes.
2. Open `Assets/CubeTest.unity`.
3. **Build a standalone** (`File → Build Settings → Build`, target `Builds/Win64/DisplayXR-test/`). Editor Play
   Mode shows the scene cleared to transparent but does **not** apply the
   native window restyling — that's a build-only path.
4. Run the resulting `.exe` (or `.app`) on a Leia SR machine.

## Installing the prebuilt app

End-users typically don't build from source. The [latest release](https://github.com/DisplayXR/displayxr-unity-test-transparent/releases/latest) ships a Windows installer (`DisplayXR-Unity-TestTransparent-Setup-X.Y.Z.exe`) that:

- Hard-prereqs the DisplayXR runtime (requires v1.7.0+ for the alpha-native path; aborts gracefully if older or missing).
- Installs the Player to `C:\Program Files\DisplayXR\Unity\TestTransparent\`.
- Registers the app with the DisplayXR Shell launcher (drops a `.displayxr.json` manifest + icons under `%ProgramData%\DisplayXR\apps\`) so it appears as a tile.

After installing, launch via the DisplayXR Shell tile or directly from the install dir.

### Building the installer yourself

Requires [NSIS](https://nsis.sourceforge.io/) installed at `C:\Program Files (x86)\NSIS\`.

1. Build the Unity Player (step 3 above) — output must land at `Builds/Win64/DisplayXR-test/`.
2. From a Developer Command Prompt: `cd installer && build-installer.bat`.
3. Output: `installer/DisplayXR-Unity-TestTransparent-Setup-X.Y.Z.exe`. Override the version with `set VERSION=1.x.y` before invoking.

## Verification checklist

- Tiger / cube renders above the desktop with no rectangular background.
- Anti-aliased silhouette edges blend cleanly into the desktop — no
  chroma fringe, no hard-mask jaggies.
- Clicks on the transparent region fall through to the underlying app
  (e.g. Notepad activates and accepts text).
- Clicks on the tiger reach Unity (console logs the `onPointerClick`
  payload).
- Tiger / cube pops convincingly in stereo. Transparent regions stay
  clean (no shimmer).
- Player.log shows `[DisplayXR] EnvironmentBlendMode = AlphaBlend
  (transparent session)` and **no** `XR_ERROR_VALIDATION_FAILURE` /
  `"is not supported for current Runtime"`.

## Compatibility

| Plugin version | Runtime version | Mechanism |
|---|---|---|
| v1.2.x – v1.5.13 | runtime ≥ v25.6.x | Chroma-key (camera paints a marker color, runtime DP converts to alpha=0 post-weave). Removed in v1.6.0. |
| **v1.6.0+** (current) | runtime advertising `ALPHA_BLEND` + compose-under-bg + alpha-gate DP path | Alpha-native end-to-end. Same path on Windows and macOS. |

A plugin / runtime version mismatch where the plugin is v1.6.0+ but the
runtime doesn't advertise `ALPHA_BLEND` fails the same way as the
v1.5.6 → v1.5.12 regression: every `xrEndFrame` returns
`XR_ERROR_VALIDATION_FAILURE` and Unity content never reaches the
swapchain. Cross-check Player.log when in doubt.

## Swapping the tiger for your own asset

The transparent overlay's click-through is wired through a single
`clickableRenderers` array in `Assets/TransparentAutoSetup.cs`, and
the asset is identified by name (`k_TargetName`) — so swapping in a
different model is mostly a one-line change. See
[`docs~/swap-asset.md`](docs~/swap-asset.md) for the full procedure,
import-settings checklist, multi-renderer extension, and a
troubleshooting section keyed to the specific symptoms users hit
historically.

## Reverting to opaque

Comment out the body of `TransparentAutoSetup.Install()` and rebuild — the
scene falls back to the default skybox.

## Reporting Issues

For plugin bugs, file issues on the [DisplayXR Unity plugin
repo](https://github.com/DisplayXR/displayxr-unity/issues).

## License

ISC. See [LICENSE](LICENSE).
