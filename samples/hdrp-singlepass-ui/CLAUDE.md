# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project Overview

This is the **HDRP (High Definition Render Pipeline) test project** for the displayxr-unity
plugin — the validation vehicle for epic #166 milestone M3 / issue #22 (HDRP off-axis
projection correctness). Sibling Unity project that consumes the `com.displayxr.unity` UPM
package (pinned `#upm/v2.1.0`). Converted from `displayxr-unity-test-2d-ui`.

The scene (`Assets/CubeTest.unity`) renders an animated cube on a tracked 3D display through
the DisplayXR `IUnityXRDisplay` provider, with both a display-centric (`DisplayXRDisplay`) and
camera-centric (`DisplayXRCamera`) rig. The purpose is to verify the **off-center (Kooima)
stereo frustum renders correctly under HDRP** — under URP that needs the
`KooimaProjectionFixFeature` RendererFeature; HDRP has no RendererFeature equivalent, so this
repo is where the HDRP off-axis fix is developed and validated. (The `DisplayXRTuningUI` /
wsui components carried over from the -2d-ui scene but are incidental here.)

**Render pipeline:** High Definition Render Pipeline (HDRP). **HDRP requires Editor setup** —
after cloning, open once in Unity 6000.4.0f1 and run **Window ▸ Rendering ▸ HDRP Wizard ▸ "Fix
All"** (generates the `HDRenderPipelineAsset` + global settings + default volume/diffusion
profiles), convert the cube material to HDRP/Lit, and confirm Graphics/Quality settings point
at the HDRP asset. Color space is already set to **Linear** (mandatory for HDRP). The URP
pipeline assets + `URPSetupBootstrap.cs` were removed during conversion.

## Repository structure

```
displayxr-unity-test-2d-ui/
├── Assets/
│   ├── CubeTest.unity                       # the scene
│   ├── Cube.controller                      # Animator Controller for the cube
│   ├── CubeRotate.anim                      # rotation clip
│   ├── Scripts/
│   │   ├── DisplayXRTuningUI.cs             # builds the wsui canvas + controls at runtime
│   │   └── DisplayXRWsuiMouseRouter.cs      # bridges cursor → wsui RT-pixel pointer events
│   ├── Editor/
│   │   └── URPSetupBootstrap.cs             # idempotent URP pipeline asset creation
│   ├── Settings/                            # URP-Pipeline.asset + URP-Renderer.asset (auto-generated on first open)
│   └── XR/                                  # OpenXR settings asset
├── Packages/
│   ├── manifest.json                        # pins com.displayxr.unity (see below)
│   └── packages-lock.json
└── ProjectSettings/
```

## Component reference

Two test-project scripts plus an editor bootstrap. The plugin contributes the rigs, input, and the `DisplayXRWindowSpaceUI` MonoBehaviour itself.

| Component | File | Purpose |
|-----------|------|---------|
| `DisplayXRTuningUI` | `Assets/Scripts/DisplayXRTuningUI.cs` | Sits on the `DisplayXR_TuningUI` GameObject. Builds a `Canvas` + `DisplayXRWindowSpaceUI` + sliders/button **programmatically** in `OnEnable` — no Canvas wired in the inspector. `[ExecuteAlways]` so the panel exists during editor-only standalone preview (no Play Mode). Idempotent: tears down the previous canvas on each domain reload before rebuilding. |
| `DisplayXRWsuiMouseRouter` | `Assets/Scripts/DisplayXRWsuiMouseRouter.cs` | Companion to `DisplayXRTuningUI`. The wsui canvas is a private WorldSpace canvas parked at `(0, 100000, 0)` on a hidden layer, so Unity's `EventSystem` can't reach it. This script reads the cursor from `DisplayXRPreviewInput.TryGetPreviewMousePosition` (editor preview window) or `Input.mousePosition` (built apps), hit-tests against the wsui's fractional layer rect, maps the hit point to canvas-pixel coords, and synthesizes `PointerEventData` against the wsui's `GraphicRaycaster`. Also sets `DisplayXRWindowSpaceUI.IsCursorOverInteractive` so the plugin's scene input controller pauses (otherwise wheel scrolls would zoom the rig at the same time as dragging a slider). |
| `URPSetupBootstrap` (editor) | `Assets/Editor/URPSetupBootstrap.cs` | `[InitializeOnLoad]` that creates `Assets/Settings/URP-Pipeline.asset` + `URP-Renderer.asset` on first load if none is assigned to `GraphicsSettings.defaultRenderPipeline`. Pins `UpscalingFilter = Auto` (FSR/STP trips the OpenXR project validator) and disables MSAA. Self-healing: if a URP asset exists but the upscaling filter drifted, patches it back to Auto. No EditorPrefs sentinel — gates purely on observable state so the bootstrap re-runs after Library wipes or fresh clones. |

## Scene contents (`Assets/CubeTest.unity`)

| GameObject | Components | Purpose |
|------------|-----------|---------|
| `Main Camera` | `DisplayXRDisplay`, `DisplayXRInputController`, `DisplayXRGameViewOverlay` (plugin) | Display-centric stereo rig — the target the tuning panel drives. |
| `Cam Centric` | `DisplayXRCamera`, `DisplayXRInputController` (plugin) | Camera-centric stereo rig. Tab-cycle target (plain Tab is plugin-bound). |
| `Cube` | Animator with `Cube.controller` | The fixture. |
| `Directional Light` | — | Default scene lighting. |
| `DisplayXR_TuningUI` | `DisplayXRTuningUI`, `DisplayXRWsuiMouseRouter` (test) | The tuning panel root. Children (canvas, sliders, button) are created at runtime. |

## How the window-space UI works

The mechanism is shared between plugin and test-project:

1. **Layer build (`DisplayXRTuningUI.OnEnable`):** creates a `Canvas` (`ScreenSpaceOverlay` initially), attaches `DisplayXRWindowSpaceUI` with fractional placement (`panelX`, `panelY`, `panelWidth`, `panelHeight`) and a fixed RT resolution of 1024 × 1024. The wsui flips the canvas to `WorldSpace`, parks it at `(0, 100000, 0)` on a hidden layer, and spins up an `OverlayCamera` that renders the canvas into its private RT.
2. **Composition layer submission (plugin native):** each frame the plugin grabs the RT, hands it to the runtime as an `XrCompositionLayerWindowSpaceEXT` payload, and the runtime composes it on top of the stereo scene with the configured per-eye `disparity`.
3. **Input bridge (`DisplayXRWsuiMouseRouter.Update`):** EventSystem can't see the wsui canvas because it's at world-space-far-away with no main-camera coverage. Router reads cursor coords from `DisplayXRPreviewInput` (editor) or `Input` (built), tests the wsui rect, maps to canvas-pixel coords, and dispatches `pointerDown` / `drag` / `pointerUp` / `pointerClick` directly via `ExecuteEvents`. It also strips any pre-existing `StandaloneInputModule` from `EventSystem.current` — that module throws every frame on the new Input System Package and corrupts EventSystem state for `GraphicRaycaster`.
4. **Render-mode enumeration:** `DisplayXRTuningUI.TryEnumerateModes` calls `displayxr_standalone_enumerate_rendering_modes` (DllImport on `displayxr_unity`) to populate the cycle button. Retried from `Update` until the standalone session is up. Names come from `displayxr_standalone_get_rendering_mode_name`; synthesized fallback for older runtimes.

## Controls

| Input | Effect |
|-------|--------|
| Drag IPD slider | `DisplayXRDisplay.ipdFactor` (0–1) |
| Drag Scale slider | `virtualDisplayHeight` ← `initialVHeight / sliderValue` (0.5x–1.5x; right = bigger) |
| Click Render Mode | Cycles to next runtime-reported rendering mode (2D, SBS, Quad, Lenticular, …) |
| `V` | Same as clicking Render Mode (edge-detected via `DisplayXRPreviewInput.IsKeyPressed`) |
| `Shift+Tab` | Toggle panel visibility (Unity Input System `Keyboard.current`) |
| `Tab` | Plugin-bound: cycle Main Camera ↔ Cam Centric |

## Plugin dependency

The manifest pins `com.displayxr.unity` to `https://github.com/DisplayXR/displayxr-unity.git#upm` (floating; tracks the latest published release).

**During plugin development:** temporarily point `Packages/manifest.json` at a local checkout (`file:/absolute/path/to/displayxr-unity`) to pick up uncommitted plugin changes. Revert before committing, and delete the corresponding `com.displayxr.unity` entry from `Packages/packages-lock.json` so Unity re-resolves from the git URL on next open.

### Plugin features this test project depends on

| Feature | Plugin version |
|---------|---------------|
| `DisplayXRWindowSpaceUI` MonoBehaviour | v1.2.0+ |
| `DisplayXRWindowSpaceUI.IsCursorOverInteractive` static gate | v1.2.0+ |
| URP RenderGraph compatibility for wsui | v1.2.8+ |
| `displayxr_standalone_enumerate_rendering_modes` DllImport | v1.2.0+ |
| `displayxr_standalone_get_rendering_mode_name` | v1.2.x+ (synthesized fallback if absent) |
| `DisplayXRPreviewInput.TryGetPreviewMousePosition` / `IsKeyPressed` | v1.2.0+ |

## Verification flow

After opening the project and starting **Window → DisplayXR → Preview Window → Start** (preferred) or pressing Play:

1. Cube renders in stereo; tuning panel appears in the upper-left region of the window with title "DisplayXR Tuning".
2. Drag the IPD slider → stereo separation changes in real time.
3. Drag the Scale slider → scene visibly magnifies (right) or shrinks (left).
4. Click Render Mode (or press `V`) → cycles 2D / SBS / Lenticular etc. The runtime caches the last mode across session restarts.
5. Press `Shift+Tab` → panel hides; press again → reappears.
6. Press `Tab` → camera switches (Main Camera ↔ Cam Centric); panel stays bound to the `DisplayXRDisplay` rig (auto-found via `FindAnyObjectByType` in `DisplayXRTuningUI.OnEnable`).
7. Cursor over the panel → the plugin's input controller stops draining mouse wheel for vHeight zoom (the router sets `IsCursorOverInteractive`).

## Cross-repo references

- Plugin: [`DisplayXR/displayxr-unity`](https://github.com/DisplayXR/displayxr-unity) — wsui implementation lives in `Runtime/DisplayXRWindowSpaceUI.cs`; the native submission path is in `native~/displayxr_wsui.*` and the `xrEndFrame` hook.
- Sibling test projects (focus-isolated):
  - [`DisplayXR/displayxr-unity-test`](https://github.com/DisplayXR/displayxr-unity-test) — baseline rendering + rig switching (BiRP).
  - [`DisplayXR/displayxr-unity-test-transparent`](https://github.com/DisplayXR/displayxr-unity-test-transparent) — transparent overlay + chroma key + click-through (Windows-only, BiRP).
- Use `DisplayXR/displayxr-unity#N` syntax to reference plugin issues.
