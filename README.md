# DisplayXR Unity Test Project — Transparent Overlay Variant

A test project that exercises the **chroma-key transparent overlay mode**
of the [DisplayXR Unity plugin](https://github.com/DisplayXR/displayxr-unity)
(added in [#57](https://github.com/DisplayXR/displayxr-unity/issues/57)).

The rotating cube renders above the Windows desktop with no rectangular
background — magenta is punched through by DWM. Clicks outside the cube's
bounding box fall through to whatever is behind the window.

**Render pipeline:** Built-in (BiRP).

**Sibling test projects** — each repo focuses on one feature so a regression
in one demo doesn't mask the others:

| Repo | What it demonstrates | Pipeline |
|---|---|---|
| [displayxr-unity-test](https://github.com/DisplayXR/displayxr-unity-test) | Display-centric vs camera-centric rigs, live rig switching | BiRP |
| [displayxr-unity-test-2d-ui](https://github.com/DisplayXR/displayxr-unity-test-2d-ui) | `XrCompositionLayerWindowSpaceEXT` 2D UI overlay (`DisplayXRWindowSpaceUI`) | URP |
| [displayxr-unity-test-transparent](https://github.com/DisplayXR/displayxr-unity-test-transparent) (you are here) | Chroma-key transparent overlay (`DisplayXRTransparentOverlay`, Windows-only) | BiRP |

## What's different from displayxr-unity-test

- `Assets/TransparentAutoSetup.cs` runs at scene load, attaches
  `DisplayXRTransparentOverlay` to `Camera.main`, and wires the rotating
  cube as the click-through hit region. No edits to `CubeTest.unity` needed.

## Requirements

- **Unity 6000.3 LTS** (Unity 6) or newer
- A **Leia SR Windows** machine for end-to-end verification (the layered-window
  path doesn't run in the editor preview — only in a Windows standalone build)
- The DisplayXR runtime installed (via the [installer](https://github.com/DisplayXR/displayxr-shell-releases/releases))

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

## Quick start

1. Open the project in Unity Hub. First import takes a few minutes.
2. Open `Assets/CubeTest.unity`.
3. **Build Windows standalone** (`File → Build Settings → Build`). Editor
   Play Mode shows the magenta clear color but **does not** apply the
   layered-window chroma key — that's a build-only path.
4. Run the resulting `.exe` on a Leia SR machine.

## Verification checklist

- Cube renders above the desktop with no rectangular background.
- Chroma-key regions punch through to the desktop (taskbar/browser visible).
- Clicks on the chroma-key region land on the underlying app.
- Clicks on the cube reach Unity (add a logging script to confirm).
- Cube pops convincingly in stereo. Transparent regions stay clean (no
  shimmer — `L == R` per sub-pixel).

## Limitations

On Leia hardware, antialiased cube edges become hard-mask alpha (alpha=0 or
alpha=1 with no in-between). This is a fundamental limitation of the
chroma-key trick used by the SR weaver — fully transparent regions are
punched through cleanly, but partial-transparency pixels on antialiased
edges either snap to opaque (with possible fringing toward the chroma key)
or to fully transparent. Apps that need soft alpha should choose a
content-safe `chromaKeyColor` to minimize fringing — the current setup
uses a near-mid-gray (`128, 127, 129`) for that reason.

## Compatibility

| Plugin version | Runtime version | Graphics APIs with desktop transparency |
|---|---|---|
| v1.2.x | runtime ≥ v25.6.x | D3D11, D3D12, Metal (macOS) |
| v1.3.0 | runtime ≥ v25.7.0 | D3D11, D3D12, Vulkan, OpenGL, Metal (macOS) |

Vulkan and OpenGL transparency landed in runtime PR #3b / PR #3c. On Vulkan,
most Win32 ICDs only expose `OPAQUE` compositeAlpha — in that case alpha is
dropped at WSI present and the cube renders opaque. On OpenGL, transparency
requires `WGL_NV_DX_interop2` (NVIDIA / AMD); Intel iGPUs fall back to
opaque presentation. D3D11 and D3D12 work on all GPUs.

## Reverting to opaque

Comment out the body of `TransparentAutoSetup.Install()` and rebuild — the
scene falls back to the default skybox.

## Reporting Issues

For plugin bugs, file issues on the [DisplayXR Unity plugin repo](https://github.com/DisplayXR/displayxr-unity/issues).
For runtime bugs, file issues on the [DisplayXR Shell releases repo](https://github.com/DisplayXR/displayxr-shell-releases/issues).

## License

ISC. See [LICENSE](LICENSE).
