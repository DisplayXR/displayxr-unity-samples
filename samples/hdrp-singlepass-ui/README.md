# HDRP Single-Pass + 2D UI

The **HDRP** counterpart of the URP Single-Pass + 2D UI sample for the
[DisplayXR Unity plugin](https://github.com/DisplayXR/displayxr-unity). Same
window-space 2D UI overlay (`DisplayXRWindowSpaceUI` via
`XrCompositionLayerWindowSpaceDXR`) and single-pass instanced stereo, rendered
through the High Definition Render Pipeline — used to catch HDRP-specific
off-axis / projection regressions.

Part of the [displayxr-unity-samples](https://github.com/DisplayXR/displayxr-unity-samples)
monorepo — see the [root README](../../README.md) for prerequisites and the
shared build/install flow.

- **Pipeline:** High Definition (HDRP), single-pass instanced
- **Scene:** `Assets/CubeTest.unity` — cube + window-space UI panel
- **Product / exe:** `DisplayXR-HDRP-SinglePass-UI`
- **Installs to:** `C:\Program Files\DisplayXR\Unity\HDRPSinglePassUI\`

## Build

1. Open `samples/hdrp-singlepass-ui/` in Unity and **File ▸ Build** to
   `Builds/Win64/DisplayXR-HDRP-SinglePass-UI/`.
2. `installer\build-installer.bat [VERSION]` → produces
   `installer\DisplayXR-Unity-HDRPSinglePassUI-Setup-<ver>.exe`.

## Issues / License

Plugin bugs → [displayxr-unity](https://github.com/DisplayXR/displayxr-unity/issues);
runtime bugs → [displayxr-runtime](https://github.com/DisplayXR/displayxr-runtime/issues).
ISC — see [LICENSE](../../LICENSE).
