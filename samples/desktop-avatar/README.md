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

**Render pipeline:** `main` is the **URP consolidated + display-zones variant**
(plugin **v1.21.0+**) — URP off-axis projection fix
([#127](https://github.com/DisplayXR/displayxr-unity/issues/127)/[#129](https://github.com/DisplayXR/displayxr-unity/issues/129))
+ alpha-native transparency + per-eye foreground clip + a **`XR_EXT_display_zones`
layout**: the tiger is Kooima-projected into a 3D zone and a **Local2D speech
bubble** ([#439](https://github.com/DisplayXR/displayxr-unity/issues/439)/[#491](https://github.com/DisplayXR/displayxr-unity/issues/491))
sits in the adjacent 2D band, in a **real floating window** you can move, resize,
and flip 2D⇄3D from the keyboard. See [**Controls**](#controls) for the full key
map. The earlier **Built-in (BiRP)** transparent-overlay variant lives on the
**`legacy-birp`** branch (the older 2D-surround / in-app region-editor build is
also retrievable at tag `archive/urp-transparent-clip-pre-local2d`). If you're a
partner setting this up, jump to [**Partner setup**](#partner-setup).

**Sibling test projects** — each repo focuses on one feature so a regression
in one demo doesn't mask the others:

| Repo | What it demonstrates | Pipeline |
|---|---|---|
| [displayxr-unity-test](https://github.com/DisplayXR/displayxr-unity-test) | Display-centric vs camera-centric rigs, live rig switching | BiRP |
| [displayxr-unity-test-2d-ui](https://github.com/DisplayXR/displayxr-unity-test-2d-ui) | `XrCompositionLayerWindowSpaceEXT` 2D UI overlay (`DisplayXRWindowSpaceUI`) | URP |
| [displayxr-unity-test-transparent](https://github.com/DisplayXR/displayxr-unity-test-transparent) (you are here) | Alpha-native transparent overlay (`DisplayXRTransparentOverlay`) + display zones / Local2D bubble | URP (`main`); BiRP on `legacy-birp` |

## Get started

**Prerequisite for everyone:** install the **DisplayXR runtime bundle** first —
the [`displayxr-installer`](https://github.com/DisplayXR/displayxr-installer/releases/latest)
one-click installer (`DisplayXRBundle-X.Y.Z.exe`), which sets up the runtime,
Shell, and SR-display plug-in at matched versions. This app needs **runtime ≥ 1.22.0**;
for the smooth **V** 2D⇄3D transition use a bundle with **runtime ≥ 1.23.0**
(current bundle is fine). You also need an **eye-tracked 3D (light-field) display**
supported by the DisplayXR runtime.

### Just want to try it (prebuilt)

1. Install the DisplayXR bundle (above).
2. Download the latest app installer from
   [**Releases**](https://github.com/DisplayXR/displayxr-unity-test-transparent/releases/latest)
   — `DisplayXR-Unity-TestTransparent-Setup-X.Y.Z.exe` — and run it (it hard-prereqs
   the runtime and aborts gracefully if it's missing/too old).
3. Launch from the DisplayXR Shell tile (or the install dir). See [**Controls**](#controls).

→ More detail in [Installing the prebuilt app](#installing-the-prebuilt-app).

### Want to build it (developers)

1. Install the DisplayXR bundle (above).
2. Clone this repo — the default **`main`** branch is the URP + display-zones build.
   (The old Built-in/BiRP variant is on the **`legacy-birp`** branch.)
3. Open in **Unity 6000.4.0f1**; Package Manager resolves the plugin via `#upm`
   (v1.21.0+). Confirm Graphics API = **Direct3D12**.
4. Build via `unity_build.bat` (or *File → Build Settings → Build*), or run the
   in-editor **Window → DisplayXR → Preview Window → Start**.

→ Full developer walkthrough in [**Partner setup**](#partner-setup).

## Partner setup

`main` is the **URP consolidated + display-zones variant**: the URP off-axis
projection fix (plugin **v1.21.0+**) + alpha-native transparency + per-eye
foreground clip + the multi-object scene (tiger **and** cube) + the
`XR_EXT_display_zones` 3D-zone / Local2D-bubble layout in a real floating window.
Everything URP-side is already committed — **no manual renderer wiring, Player
Settings, or material conversion is needed.**

### Prerequisites

1. **Latest DisplayXR bundle installed.** Plugin **v1.21.0** hard-requires a
   runtime that advertises `XR_EXT_view_rig` SPEC_VERSION 2, `XR_EXT_display_zones`,
   `XR_EXT_local_3d_zone`, and `XR_EXT_display_info` *and* the alpha-native
   `ALPHA_BLEND` + compose-under-background DP path — i.e. runtime **v1.22.0+**
   (hardware-verified on runtime v1.22.0 / leia-sr 1.8.3). Without these the plugin
   logs a one-shot WARN and passes raw views through → **no stereo / no zones**.
   *(Note: the smooth V-key 2D⇄3D switch needs the runtime fix for
   [#615](https://github.com/DisplayXR/displayxr-runtime/issues/615), which is on
   runtime `main` but not yet in a release as of v1.22.0 — until then the 2D→3D
   transition snaps; everything else works on v1.22.0.)*
2. **Unity 6 (`6000.4.0f1`).** The project is URP 17.0.4 and the off-axis fix uses
   URP 17 RenderGraph — it will not compile/run on older Unity/URP.
3. **A DisplayXR-supported eye-tracked 3D display, with a tracked face** (sit in front of
   the eye tracker). The per-eye foreground clip degenerates without a real face.

### Steps

1. Install/update the **latest DisplayXR bundle** (registers the OpenXR runtime).
2. Clone this repo (the default `main` branch is the URP/zones build).
3. Open in **Unity 6000.4.0f1**. Let Package Manager resolve
   `com.displayxr.unity#upm` → it should pull **v1.21.0+**. If it sticks on an
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

### Already committed on `main` (do **not** redo)

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

## Controls

The app runs as a **real floating window** (born windowed; size/position/split
persist across launches). All bindings below are **app policy** — they live in
the demo's `Assets/` scripts, not the plugin (the plugin only exposes window
*primitives*; the demo decides the keys). Edit them freely.

> **Changed from the older build.** The archived `archive/urp-transparent-clip-pre-local2d`
> tag used a *fixed-fullscreen overlay with an in-app virtual-window region
> editor* (the full Ctrl+Shift+L dragged virtual window edges to resize/move
> *inside* a fullscreen overlay, with a 2D surround). This build is a genuine
> OS floating window instead, so **move/resize moved to direct window controls**
> and Ctrl+Shift+L is trimmed to only the 2D/3D split. The table below is the
> current, authoritative map.

### Window management

| Action | Control | Notes |
|---|---|---|
| **Move window** | **Right-mouse drag** (anywhere on the tiger / interior) | Capture-based `SetWindowPos`; the only drag the display's weaving compositor doesn't eat. |
| **Resize window** | **Ctrl + arrow keys** | →/← = width, ↑/↓ = height, 80 px per press. |
| **Quit** | **Esc** (also the window **X** button / **Alt+F4**) | — |

> **Why the overlay, and why keyboard resize instead of mouse-drag edges?**
> Per-pixel-alpha transparent output needs a layered DirectComposition window,
> which Unity's own player window can't be — Unity creates and owns its standalone
> window handle. So the plugin renders into a **separate transparent overlay
> window** (with Unity's own window cloaked off-screen). That overlay has to stay
> **non-activating** so Unity keeps keyboard/input focus, and the 3D display's
> weaving compositor installs a window-procedure subclass on it that **swallows
> mouse button-downs near the window frame** — so OS edge-resize and title-bar
> drag never fire. The workaround: **move** is a capture-based right-drag and
> **resize** is keyboard, both issued via a direct `SetWindowPos` the subclass
> doesn't intercept. There is no OS title bar by design.

### Display / render mode

| Action | Control | Notes |
|---|---|---|
| **Toggle 3D ⇄ 2D** | **V** | Only while the app is focused ("tiger in focus", not clicked-through). Ramps the rig **IPD** smoothly (parallax stays 1, so the head-tracked perspective is kept). The 2D→3D direction still snaps until the runtime ships the [#615](https://github.com/DisplayXR/displayxr-runtime/issues/615) fix. |

### Scene navigation

| Action | Control |
|---|---|
| Move horizontally | **W / A / S / D** |
| Move down / up | **Q / E** |
| Cycle cameras / rigs | **Tab** |

*(Mouse-look and scroll-zoom on the rig controller are disabled in this demo —
left-drag and the wheel are reserved for the tiger, below.)*

### Tiger interaction

| Action | Control | Notes |
|---|---|---|
| **Rotate the tiger** | **Left-mouse drag** on the tiger | Yaw only (stays upright); pauses the idle animation while dragging. |
| **Zoom** | **Mouse wheel** | Scales the rig's virtual display height ("zoom in window" — the window itself stays put). |
| **Reset** | **Space** | Restores the tiger's orientation **and** the zoom (vHeight) to startup. |
| Click the tiger | **Left click** on the silhouette | Reaches Unity (logs `onPointerClick`). Clicks on the transparent area fall through to the desktop window behind. |

### Layout (2D / 3D split)

| Action | Control | Notes |
|---|---|---|
| **Toggle Layout mode** | **Ctrl + Shift + L** | The bubble shows a "LAYOUT MODE" banner and tiger rotation is suspended. |
| **Set the split** | **Left-drag up / down** (in Layout mode) | Moves the 2D-band / 3D-zone divider; press **Ctrl+Shift+L** again to exit. The split persists across launches. |

### Persistence & defaults

Window **size**, **position**, and the **2D/3D split** are saved to PlayerPrefs
and restored on the next launch. A fresh install (cleared PlayerPrefs) starts at
the tuned shipped default: **840 × 1448** at screen **(2876, 673)**, split at
**0.33**. (Position is monitor-relative — on a different display layout it may
land off-screen until you move it once, which then persists.)

## What's different from displayxr-unity-test

- `Assets/TransparentAutoSetup.cs` runs at scene load, attaches
  `DisplayXRTransparentOverlay` to the rig cameras, and wires the tiger
  (or cube fallback) as the click-through hit region. No edits to
  `CubeTest.unity` needed.

## Requirements

- **Unity 6000.3 LTS** (Unity 6) or newer
- An **eye-tracked 3D display on Windows** (or recent Mac) for end-to-end verification
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
3. **Build a standalone** — run `unity_build.bat` (one-command headless build,
   output lands at `Builds/Win64/DisplayXR-test-transparent/`), or *File → Build
   Settings → Build* targeting that same folder. Editor Play Mode shows the scene
   cleared to transparent but does **not** apply the native window restyling —
   that's a build-only path.
4. Run the resulting `.exe` on a machine with an eye-tracked 3D display.

## Installing the prebuilt app

End-users typically don't build from source. The [latest release](https://github.com/DisplayXR/displayxr-unity-test-transparent/releases/latest) ships a Windows installer (`DisplayXR-Unity-TestTransparent-Setup-X.Y.Z.exe`) that:

- Hard-prereqs the DisplayXR runtime (this build requires **v1.22.0+** for the display-zones / Local2D path — **v1.23.0+** for the smooth V 2D⇄3D switch; aborts gracefully if older or missing). Install it via the [DisplayXR bundle](https://github.com/DisplayXR/displayxr-installer/releases/latest).
- Installs the Player to `C:\Program Files\DisplayXR\Unity\TestTransparent\`.
- Registers the app with the DisplayXR Shell launcher (drops a `.displayxr.json` manifest + icons under `%ProgramData%\DisplayXR\apps\`) so it appears as a tile.

After installing, launch via the DisplayXR Shell tile or directly from the install dir.

### Building the installer yourself

Requires [NSIS](https://nsis.sourceforge.io/) installed at `C:\Program Files (x86)\NSIS\`.

1. Build the Unity Player (step 3 above) — output must land at `Builds/Win64/DisplayXR-test-transparent/`.
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
