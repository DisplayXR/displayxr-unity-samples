# Desktop Avatar

The flagship feature showcase for the
[DisplayXR Unity plugin](https://github.com/DisplayXR/displayxr-unity): a
see-through desktop avatar (cartoon tiger) rendered over the live screen on
**URP**, exercising the plugin's advanced surface features together:

- **Alpha-native transparency** (the avatar composites over whatever is behind it)
- **Click-through** — input passes to the apps beneath the transparent regions
- **Per-eye foreground clipping**
- **`XR_DXR_display_zones`** layout — the avatar is Kooima-projected into a 3D
  zone, with a **Local2D** speech-bubble zone alongside it

Part of the [displayxr-unity-samples](https://github.com/DisplayXR/displayxr-unity-samples)
monorepo — see the [root README](../../README.md) for prerequisites and the
shared build/install flow.

- **Pipeline:** Universal (URP)
- **Product / exe:** `DisplayXR-DesktopAvatar`
- **Installs to:** `C:\Program Files\DisplayXR\Unity\DesktopAvatar\`
- **Assets:** the tiger `.fbx` is tracked with **Git LFS** (see the sample's
  `.gitattributes`) — run `git lfs install` once before cloning/pulling.

## Build

1. Open `samples/desktop-avatar/` in Unity and **File ▸ Build** to
   `Builds/Win64/DisplayXR-DesktopAvatar/`.
2. `installer\build-installer.bat [VERSION]` → produces
   `installer\DisplayXR-Unity-DesktopAvatar-Setup-<ver>.exe`.

## Issues / License

Plugin bugs → [displayxr-unity](https://github.com/DisplayXR/displayxr-unity/issues);
runtime bugs → [displayxr-runtime](https://github.com/DisplayXR/displayxr-runtime/issues).
ISC — see [LICENSE](../../LICENSE).
