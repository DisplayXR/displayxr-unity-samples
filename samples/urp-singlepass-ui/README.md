# DisplayXR Unity Test Project — 2D UI Overlay Variant

A URP test project that exercises the **window-space 2D UI overlay** feature
of the [DisplayXR Unity plugin](https://github.com/DisplayXR/displayxr-unity)
(`DisplayXRWindowSpaceUI`, plumbed through `XrCompositionLayerWindowSpaceEXT`
in the OpenXR runtime).

The scene renders a textured cube on a tracked 3D display and overlays a
runtime-built UI panel (IPD slider, virtual-display-height slider,
render-mode cycle button) submitted as a window-space composition layer.

**Render pipeline:** Universal (URP).

**Sibling test projects** — each repo focuses on one feature so a regression
in one demo doesn't mask the others:

| Repo | What it demonstrates | Pipeline |
|---|---|---|
| [displayxr-unity-test](https://github.com/DisplayXR/displayxr-unity-test) | Display-centric vs camera-centric rigs, live rig switching | BiRP |
| [displayxr-unity-test-2d-ui](https://github.com/DisplayXR/displayxr-unity-test-2d-ui) (you are here) | `XrCompositionLayerWindowSpaceEXT` 2D UI overlay (`DisplayXRWindowSpaceUI`) | URP |
| [displayxr-unity-test-transparent](https://github.com/DisplayXR/displayxr-unity-test-transparent) | Chroma-key transparent overlay (`DisplayXRTransparentOverlay`, Windows-only) | BiRP |

## Requirements

- **Unity 6000.3 LTS** (Unity 6) or newer
- A spatial display supported by [DisplayXR](https://github.com/DisplayXR/displayxr-runtime), or use the built-in `sim_display` driver for development without hardware
- The DisplayXR runtime installed (via the [installer](https://github.com/DisplayXR/displayxr-shell-releases/releases))

## Opening the Project

1. Clone this repo:
   ```bash
   git clone https://github.com/DisplayXR/displayxr-unity-test-2d-ui.git
   ```
2. Open the project in Unity Hub (`File → Open Project`)
3. Unity will fetch dependencies — this may take a few minutes on first open
4. Open `Assets/CubeTest.unity` to load the test scene

### URP setup

The project ships with the Universal Render Pipeline package in its manifest.
On first import, `Assets/Editor/URPSetupBootstrap.cs` automatically creates an
XR-friendly URP pipeline asset (`Assets/Settings/URP-Pipeline.asset` with
`UpscalingFilter=Auto`, MSAA off — both required to keep the OpenXR project
validator happy) and assigns it to Project Settings → Graphics + Quality.

If the cube renders magenta on first open, the wood-crate material is still
referencing the Built-in `Standard` shader. Run the URP converter once to
upgrade materials:

1. `Window → Rendering → Render Pipeline Converter`
2. Choose **Built-in to URP**
3. Tick *Material and Material Reference Upgrade*, then *Initialize Converters*
   and *Convert Assets*

## Plugin Reference

The project depends on the DisplayXR Unity plugin via Unity Package Manager. The dependency is declared in `Packages/manifest.json` and tracks the latest released plugin version (the `upm` branch is force-pushed by the plugin's CI on every `v*` tag, with the prebuilt native binary):

```json
"com.displayxr.unity": "https://github.com/DisplayXR/displayxr-unity.git#upm"
```

After editing, run `Window → Package Manager → Refresh`.

To test against a local development build of the plugin, change the dependency to:
```json
"com.displayxr.unity": "file:/absolute/path/to/displayxr-unity"
```

## Test Scene

| Scene | Description |
|-------|-------------|
| `Assets/CubeTest.unity` | Rotating textured cube on a tracked 3D display + a runtime-built window-space UI panel: IPD slider, virtual-display-height slider, render-mode cycle button. Verifies basic rendering AND the `XrCompositionLayerWindowSpaceEXT` overlay path. |

The window-space UI is constructed at runtime by
`Assets/Scripts/DisplayXRTuningUI.cs` (programmatic Canvas + sliders +
button — no hand-authored UI prefab). Adjust
`panelX/panelY/panelWidth/panelHeight` on the `DisplayXR_TuningUI` GameObject
to reposition the panel inside the runtime window.

### Making the UI interactive — sample input router

`XrCompositionLayerWindowSpaceEXT` submits pixels; it doesn't carry input.
Unity's `GraphicRaycaster` works on screen-space mouse coords against
canvases that live in screen space — but `DisplayXRWindowSpaceUI` renders
into a private WorldSpace canvas, so without help the layer is read-only.

The plugin (v1.2.13+) exposes the primitive needed:
`DisplayXR.DisplayXRPreviewInput.TryGetPreviewMousePosition()` returns the
runtime preview window's cursor in fractional (0..1, top-left) coords.
Routing on top of that primitive — hit-testing the wsui layer rect, mapping
to canvas-pixel coords, and dispatching synthetic `PointerEventData` —
stays app-side because each consumer can choose their own input model
(mouse / touch / hand-tracking).

`Assets/Scripts/DisplayXRWsuiMouseRouter.cs` is the **sample router**
included here. It does the canonical mouse → fractional → canvas-local →
`EventSystem.RaycastAll` flow with drag-state tracking, so sliders and
buttons in this scene respond to clicks. Fork it for your own input model.

The router is attached to the `DisplayXR_TuningUI` GameObject in the scene
alongside `DisplayXRTuningUI`. Removing it makes the panel read-only again
(visual-only HUD use case).

## Running the Project

1. With a spatial display connected: Press Play in the Unity Editor — the scene will render with stereo 3D and head tracking
2. Without hardware: The DisplayXR runtime's `sim_display` driver activates automatically — use WASD + mouse to simulate eye movement
3. To build a standalone player: `File → Build Settings → Build` (target `Builds/Win64/DisplayXR-test/`)

## Installing the prebuilt app

End-users typically don't build from source. The [latest release](https://github.com/DisplayXR/displayxr-unity-test-2d-ui/releases/latest) ships a Windows installer (`DisplayXR-Unity-Test2DUI-Setup-X.Y.Z.exe`) that:

- Hard-prereqs the DisplayXR runtime (aborts gracefully if missing).
- Installs the Player to `C:\Program Files\DisplayXR\Unity\Test2DUI\`.
- Registers the app with the DisplayXR Shell launcher (drops a `.displayxr.json` manifest + icons under `%ProgramData%\DisplayXR\apps\`) so it appears as a tile.

After installing, launch via the DisplayXR Shell tile or directly from the install dir.

### Building the installer yourself

Requires [NSIS](https://nsis.sourceforge.io/) installed at `C:\Program Files (x86)\NSIS\`.

1. Build the Unity Player (step 3 above) — output must land at `Builds/Win64/DisplayXR-test/`.
2. From a Developer Command Prompt: `cd installer && build-installer.bat`.
3. Output: `installer/DisplayXR-Unity-Test2DUI-Setup-X.Y.Z.exe`. Override the version with `set VERSION=1.x.y` before invoking.

## Reporting Issues

For plugin bugs, file issues on the [DisplayXR Unity plugin repo](https://github.com/DisplayXR/displayxr-unity/issues).
For runtime bugs, file issues on the [DisplayXR Shell releases repo](https://github.com/DisplayXR/displayxr-shell-releases/issues).

## License

ISC. See [LICENSE](LICENSE).
