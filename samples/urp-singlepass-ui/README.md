# URP Single-Pass + 2D UI

Sample for the [DisplayXR Unity plugin](https://github.com/DisplayXR/displayxr-unity)
on **URP** (single-pass instanced stereo) that exercises the **window-space 2D UI
overlay** feature (`DisplayXRWindowSpaceUI`, plumbed through
`XrCompositionLayerWindowSpaceDXR` in the runtime). The scene renders a textured
cube on a tracked 3D display and overlays a runtime-built UI panel (IPD slider,
virtual-display-height slider, render-mode cycle button) as a window-space
composition layer.

Part of the [displayxr-unity-samples](https://github.com/DisplayXR/displayxr-unity-samples)
monorepo — see the [root README](../../README.md) for prerequisites and the
shared build/install flow.

- **Pipeline:** Universal (URP), single-pass instanced
- **Scene:** `Assets/CubeTest.unity` — cube + window-space UI panel
- **Product / exe:** `DisplayXR-URP-SinglePass-UI`
- **Installs to:** `C:\Program Files\DisplayXR\Unity\URPSinglePassUI\`

## URP setup

On first import, `Assets/Editor/URPSetupBootstrap.cs` creates an XR-friendly URP
pipeline asset (`UpscalingFilter=Auto`, MSAA off — both required to satisfy the
OpenXR project validator) and assigns it to Graphics + Quality. If the cube
renders magenta, run **Window ▸ Rendering ▸ Render Pipeline Converter ▸
Built-in to URP** once to upgrade materials.

## Build

1. Open `samples/urp-singlepass-ui/` in Unity and **File ▸ Build** to
   `Builds/Win64/DisplayXR-URP-SinglePass-UI/`.
2. `installer\build-installer.bat [VERSION]` → produces
   `installer\DisplayXR-Unity-URPSinglePassUI-Setup-<ver>.exe`.

## Issues / License

Plugin bugs → [displayxr-unity](https://github.com/DisplayXR/displayxr-unity/issues);
runtime bugs → [displayxr-runtime](https://github.com/DisplayXR/displayxr-runtime/issues).
ISC — see [LICENSE](../../LICENSE).
