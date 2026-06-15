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

**Render pipeline:** `main` is Built-in (BiRP). **This branch
(`feat/urp-transparent-clip`) is the URP consolidated variant** — URP off-axis
projection fix (plugin v1.20.0, [#127](https://github.com/DisplayXR/displayxr-unity/issues/127)/[#129](https://github.com/DisplayXR/displayxr-unity/issues/129))
+ alpha-native transparency + per-eye foreground clip + multi-object scene
(tiger **and** cube). If you're a partner trying this branch, jump to
[**Partner setup (URP branch)**](#partner-setup-urp-branch) below; the BiRP
notes elsewhere in this README describe `main`.

**Sibling test projects** — each repo focuses on one feature so a regression
in one demo doesn't mask the others:

| Repo | What it demonstrates | Pipeline |
|---|---|---|
| [displayxr-unity-test](https://github.com/DisplayXR/displayxr-unity-test) | Display-centric vs camera-centric rigs, live rig switching | BiRP |
| [displayxr-unity-test-2d-ui](https://github.com/DisplayXR/displayxr-unity-test-2d-ui) | `XrCompositionLayerWindowSpaceEXT` 2D UI overlay (`DisplayXRWindowSpaceUI`) | URP |
| [displayxr-unity-test-transparent](https://github.com/DisplayXR/displayxr-unity-test-transparent) (you are here) | Alpha-native transparent overlay (`DisplayXRTransparentOverlay`) | BiRP |

## Partner setup (URP branch)

This branch (`feat/urp-transparent-clip`) is the **URP consolidated variant**:
the URP off-axis projection fix (plugin v1.20.0) + alpha-native transparency +
per-eye foreground clip + the multi-object scene (tiger **and** cube). Everything
URP-side is already committed — **no manual renderer wiring, Player Settings, or
material conversion is needed.**

### Prerequisites

1. **Latest DisplayXR bundle installed.** Plugin v1.20.0 hard-requires a runtime
   that advertises `XR_EXT_view_rig` SPEC_VERSION 2 (runtime **v1.16.0+ / bundle
   0.17.0+**) *and* the alpha-native `ALPHA_BLEND` + compose-under-background DP
   path. Without `XR_EXT_view_rig` the plugin logs a one-shot WARN and passes raw
   views through → **no stereo**.
2. **Unity 6 (`6000.4.0f1`).** The project is URP 17.0.4 and the off-axis fix uses
   URP 17 RenderGraph — it will not compile/run on older Unity/URP.
3. **A DisplayXR / Leia eye-tracked display, with a tracked face** (sit in front of
   the eye tracker). The per-eye foreground clip degenerates without a real face.

### Steps

1. Install/update the **latest DisplayXR bundle** (registers the OpenXR runtime).
2. Clone this repo and `git checkout feat/urp-transparent-clip`.
3. Open in **Unity 6000.4.0f1**. Let Package Manager resolve
   `com.displayxr.unity#upm` → it should pull **v1.20.0+**. If it sticks on an
   older cached version, delete the `com.displayxr.unity` entry from
   `Packages/packages-lock.json` (or hit *Refresh* in Package Manager) so it
   re-resolves.
4. Confirm Graphics API is **Direct3D12** (committed) and the DisplayXR OpenXR
   runtime is active.
5. Run it: build via `unity_build.bat`, **or** *Window → DisplayXR → Preview
   Window → Start*, **or** Play Mode.
6. With a tracked face, move your head off-center to **both** sides (incl. far
   left) and confirm:
   - the image stays correct off-axis (the URP projection fix);
   - **tiger and cube both render**;
   - the foreground clip cuts the tiger's back half but keeps the (foreground) cube;
   - the background is transparent and clicks fall through outside the silhouettes.

### Already committed on this branch (do **not** redo)

Preserve Framebuffer Alpha = on; `KooimaProjectionFixFeature` wired into the URP
renderer; the foreground-clip Full Screen Pass feature + material; the cube's
URP/Lit material; Graphics API = D3D12.

### Troubleshooting

- **Flat / no stereo** → runtime too old. Grep `Player.log`
  (`~/AppData/LocalLow/DisplayXR/DisplayXR-test/Player.log`) for `XR_EXT_view_rig`;
  a WARN there means the runtime lacks it → update the bundle.
- **Magenta / missing objects** → a material didn't resolve to URP.
- **`XR_ERROR_VALIDATION_FAILURE`** in the log → runtime lacks `ALPHA_BLEND`
  (update the bundle).

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
