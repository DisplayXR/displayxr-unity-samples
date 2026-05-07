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

The project depends on the DisplayXR Unity plugin via Unity Package Manager. The dependency is declared in `Packages/manifest.json`:

```json
"com.displayxr.unity": "https://github.com/DisplayXR/displayxr-unity.git#upm/v1.2.9"
```

To test against a different plugin version, edit the URL fragment (`#upm/v1.2.9`) to point at the desired tag, then run `Window → Package Manager → Refresh`.

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
3. To build a standalone player: `File → Build Settings → Build`

## Reporting Issues

For plugin bugs, file issues on the [DisplayXR Unity plugin repo](https://github.com/DisplayXR/displayxr-unity/issues).
For runtime bugs, file issues on the [DisplayXR Shell releases repo](https://github.com/DisplayXR/displayxr-shell-releases/issues).

## License

ISC. See [LICENSE](LICENSE).
