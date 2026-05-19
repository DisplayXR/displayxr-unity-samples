# DisplayXR Unity Test Project

A minimal Unity test project for the [DisplayXR Unity plugin](https://github.com/DisplayXR/displayxr-unity). Use this project to validate the plugin against new releases, test scene setups, and try out the spatial display rendering on a tracked 3D display.

**Render pipeline:** Built-in (BiRP).

**Sibling test projects** — each repo focuses on one feature so a regression
in one demo doesn't mask the others:

| Repo | What it demonstrates | Pipeline |
|---|---|---|
| [displayxr-unity-test](https://github.com/DisplayXR/displayxr-unity-test) (you are here) | Display-centric vs camera-centric rigs, live rig switching | BiRP |
| [displayxr-unity-test-2d-ui](https://github.com/DisplayXR/displayxr-unity-test-2d-ui) | `XrCompositionLayerWindowSpaceEXT` 2D UI overlay (`DisplayXRWindowSpaceUI`) | URP |
| [displayxr-unity-test-transparent](https://github.com/DisplayXR/displayxr-unity-test-transparent) | Chroma-key transparent overlay (`DisplayXRTransparentOverlay`, Windows-only) | BiRP |

## Requirements

- **Unity 6000.3 LTS** (Unity 6) or newer
- A spatial display supported by [DisplayXR](https://github.com/DisplayXR/displayxr-runtime), or use the built-in `sim_display` driver for development without hardware
- The DisplayXR runtime installed (via the [installer](https://github.com/DisplayXR/displayxr-shell-releases/releases))

## Opening the Project

1. Clone this repo:
   ```bash
   git clone https://github.com/DisplayXR/displayxr-unity-test.git
   ```
2. Open the project in Unity Hub (`File → Open Project`)
3. Unity will fetch dependencies — this may take a few minutes on first open
4. Open `Assets/CubeTest.unity` to load the test scene

## Plugin Reference

The project depends on the DisplayXR Unity plugin via Unity Package Manager. The dependency is declared in `Packages/manifest.json` and tracks the latest released plugin version (the `upm` branch is force-pushed by CI on every plugin `v*` tag, with the prebuilt native binary):

```json
"com.displayxr.unity": "https://github.com/DisplayXR/displayxr-unity.git#upm"
```

To pin to a specific plugin version, edit the URL fragment to a `#upm/vX.Y.Z` tag (where one exists; the plugin repo's CI no longer creates new `upm/vX.Y.Z` tags after v1.2.9, so newer pins should use the regular `#vX.Y.Z` tag plus a local plugin build). After editing, run `Window → Package Manager → Refresh`.

To test against a local development build of the plugin, change the dependency to:
```json
"com.displayxr.unity": "file:/absolute/path/to/displayxr-unity"
```

## Test Scenes

| Scene | Description |
|-------|-------------|
| `Assets/CubeTest.unity` | Minimal rotating cube on a tracked 3D display — verifies the basic rendering pipeline |

## Running the Project

1. With a spatial display connected: Press Play in the Unity Editor — the scene will render with stereo 3D and head tracking
2. Without hardware: The DisplayXR runtime's `sim_display` driver activates automatically — use WASD + mouse to simulate eye movement
3. To build a standalone player: `File → Build Settings → Build` (target `Builds/Win64/DisplayXR-test/`)

## Installing the Prebuilt App

End-users typically don't build from source. The [latest release](https://github.com/DisplayXR/displayxr-unity-test/releases/latest) ships a Windows installer (`DisplayXR-Unity-Test-Setup-X.Y.Z.exe`) that:

- Hard-prereqs the DisplayXR runtime (aborts gracefully if missing).
- Installs the Player to `C:\Program Files\DisplayXR\Unity\Test\`.
- Registers the app with the DisplayXR Shell launcher (drops a `.displayxr.json` manifest + icons under `%ProgramData%\DisplayXR\apps\`) so it appears as a tile.

After installing, launch via the DisplayXR Shell tile or directly from the install dir.

### Building the installer yourself

Requires [NSIS](https://nsis.sourceforge.io/) installed at `C:\Program Files (x86)\NSIS\`.

1. Build the Unity Player (step 3 above) — output must land at `Builds/Win64/DisplayXR-test/`.
2. From a Developer Command Prompt: `cd installer && build-installer.bat`.
3. Output: `installer/DisplayXR-Unity-Test-Setup-X.Y.Z.exe`. Override the version with `set VERSION=1.x.y` before invoking.

## Reporting Issues

For plugin bugs, file issues on the [DisplayXR Unity plugin repo](https://github.com/DisplayXR/displayxr-unity/issues).
For runtime bugs, file issues on the [DisplayXR Shell releases repo](https://github.com/DisplayXR/displayxr-shell-releases/issues).

## License

ISC. See [LICENSE](LICENSE).
