# BiRP Multi-Pass

Baseline sample for the [DisplayXR Unity plugin](https://github.com/DisplayXR/displayxr-unity)
on the **Built-in Render Pipeline** (multi-pass stereo). Exercises the basic
stereo pipeline plus **live rig switching** between a display-centric
(`DisplayXRDisplay`) and a camera-centric (`DisplayXRCamera`) rig — press **Tab**
to cycle.

Part of the [displayxr-unity-samples](https://github.com/DisplayXR/displayxr-unity-samples)
monorepo — see the [root README](../../README.md) for prerequisites and the
shared build/install flow.

- **Pipeline:** Built-in (BiRP), multi-pass
- **Scene:** `Assets/CubeTest.unity` — a rotating cube on a tracked 3D display
- **Product / exe:** `DisplayXR-BiRP-MultiPass`
- **Installs to:** `C:\Program Files\DisplayXR\Unity\BiRPMultiPass\`

## Build

1. Open `samples/birp-multipass/` in Unity and **File ▸ Build** to
   `Builds/Win64/DisplayXR-BiRP-MultiPass/` (batchmode:
   `BuildScript.BuildWindows64`).
2. `installer\build-installer.bat [VERSION]` → produces
   `installer\DisplayXR-Unity-BiRPMultiPass-Setup-<ver>.exe`.

## Running

- With a DisplayXR display connected: press Play — stereo 3D + head tracking.
- Without hardware: the runtime's `sim_display` driver activates; WASD + mouse
  simulate eye movement.
- **Tab** switches display-centric ↔ camera-centric rigs.

## Issues / License

Plugin bugs → [displayxr-unity](https://github.com/DisplayXR/displayxr-unity/issues);
runtime bugs → [displayxr-runtime](https://github.com/DisplayXR/displayxr-runtime/issues).
ISC — see [LICENSE](../../LICENSE).
