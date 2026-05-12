# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project Overview

This is the **baseline test project** for the displayxr-unity plugin — a minimal scene used to validate the basic stereo rendering pipeline and to exercise **live rig switching between display-centric and camera-centric stereo rigs**. Sibling Unity project that consumes the `com.displayxr.unity` UPM package.

The scene (`Assets/CubeTest.unity`) renders a single animated cube and contains two cameras configured as different rig types. Tab cycles between them at runtime via the plugin's static `DisplayXRRigManager.CycleNext()`.

**Render pipeline:** Built-in Render Pipeline (BiRP). URP-related assets (`UniversalRenderPipelineGlobalSettings.asset`, `DefaultVolumeProfile.asset`) are Unity 6 auto-generated on first open and are unused — the project does **not** assign a URP asset to `GraphicsSettings.defaultRenderPipeline`.

## Repository structure

```
displayxr-unity-test/
├── Assets/
│   ├── CubeTest.unity                       # the scene (only scene in the project)
│   ├── Cube.controller                      # Animator Controller for the cube
│   ├── CubeRotate.anim                      # the rotation clip
│   └── XR/                                  # OpenXR settings asset
├── Packages/
│   ├── manifest.json                        # pins com.displayxr.unity (see below)
│   └── packages-lock.json
├── ProjectSettings/
└── README.md
```

There are **no scripts in `Assets/`**. The whole demo is scene-driven — every behavior comes from MonoBehaviours that the plugin ships in `com.displayxr.unity`.

## Scene contents (`Assets/CubeTest.unity`)

| GameObject | Components from the plugin | Purpose |
|------------|---------------------------|---------|
| `Main Camera` | `DisplayXRDisplay`, `DisplayXRInputController`, `DisplayXRGameViewOverlay` | Display-centric stereo rig (scale-as-zoom; FOV from physical display geometry + `virtualDisplayHeight`). `DisplayXRGameViewOverlay` draws the shared texture into the Game View during Play Mode. |
| `Cam Centric` | `DisplayXRCamera`, `DisplayXRInputController` | Camera-centric stereo rig (inherits camera FOV; convergence via `1/convergenceDistance`). Tab-cycle target. |
| `Cube` | (Animator with `Cube.controller`) | The fixture. Animator plays `CubeRotate.anim` for a continuous yaw. |
| `Directional Light` | — | Default scene lighting. |

**Rig coordination** is automatic: both rigs self-register with `DisplayXRRigManager` on `OnEnable`. The first one registered becomes `ActiveCamera`; `DisplayXRRigManager.CycleNext()` (bound to Tab inside `DisplayXRInputController`) advances. Only the active rig pushes Kooima tunables into the native hook chain — see "Multi-Camera Rig Management" in the plugin's `CLAUDE.md` for the gating rationale.

## How the demo runs

1. Plugin's `DisplayXRFeature` (OpenXR feature) hooks `xrCreateSession` and `xrLocateViews` at the native layer.
2. Either rig is "active" at a time; the active rig writes its tunables (eye-pose, view matrix, FOV) into native shared state.
3. The native hook chain rewrites `xrLocateViews` output with Kooima asymmetric frustum projection (display3d for `DisplayXRDisplay`, camera3d for `DisplayXRCamera`).
4. The runtime composes the stereo output and writes it to a shared texture (IOSurface on macOS, DXGI handle on Windows).
5. In Play Mode, `DisplayXRGameViewOverlay` blits that texture to the Game View. In standalone preview, the editor preview window does the same. Either way, no Unity camera output reaches the display directly — only the woven shared texture.

## Plugin dependency

The manifest pins `com.displayxr.unity` to `https://github.com/DisplayXR/displayxr-unity.git#upm` (floating; tracks the latest published release).

**During plugin development:** temporarily point `Packages/manifest.json` at a local checkout (`file:/absolute/path/to/displayxr-unity`) to pick up uncommitted plugin changes. Revert before committing, and delete the corresponding `com.displayxr.unity` entry from `Packages/packages-lock.json` so Unity re-resolves from the git URL on next open.

### Plugin features this test project exercises

| Feature | Plugin version |
|---------|---------------|
| `DisplayXRDisplay` (display-centric rig) | v1.0.0+ |
| `DisplayXRCamera` (camera-centric rig) | v1.0.0+ |
| `DisplayXRRigManager` + Tab cycling | v1.0.0+ |
| `DisplayXRInputController` (WASD/mouse/scroll) | v1.0.0+ |
| `DisplayXRGameViewOverlay` (Play Mode shared-texture display) | v1.0.0+ |

This project should keep working against any released plugin version since v1.0.0. Use it as a regression check before tagging a new plugin release.

## Verification flow

After opening the project and starting **Window → DisplayXR → Preview Window → Start** (preferred) or pressing Play:

1. Cube renders in stereo on the connected DisplayXR display (or in `sim_display` if no hardware).
2. Cube spins continuously from the Animator.
3. **W / S** push the camera forward/back; **A / Q / D / E** strafe (per `DisplayXRInputController`).
4. Mouse drag rotates (when input controller drag is enabled).
5. Scroll wheel adjusts `virtualDisplayHeight` (display-centric rig only).
6. **Tab** switches between `Main Camera` (display rig) and `Cam Centric` (camera rig). Projection math changes visibly — display-centric scales with vHeight; camera-centric inherits the Unity camera's FOV.
7. **Shift+Tab** is intentionally left free here (consumed by the 2D UI test project for panel visibility).

## Cross-repo references

- Plugin: [`DisplayXR/displayxr-unity`](https://github.com/DisplayXR/displayxr-unity) — both rig MonoBehaviours live in `Runtime/DisplayXRDisplay.cs` and `Runtime/DisplayXRCamera.cs`.
- Sibling test projects (focus-isolated):
  - [`DisplayXR/displayxr-unity-test-2d-ui`](https://github.com/DisplayXR/displayxr-unity-test-2d-ui) — window-space 2D UI overlay (URP).
  - [`DisplayXR/displayxr-unity-test-transparent`](https://github.com/DisplayXR/displayxr-unity-test-transparent) — transparent overlay + chroma key + click-through (Windows-only, BiRP).
- Use `DisplayXR/displayxr-unity#N` syntax to reference plugin issues.
