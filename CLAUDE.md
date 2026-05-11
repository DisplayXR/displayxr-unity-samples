# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project Overview

This is the **transparent overlay test variant** of the displayxr-unity test suite — the working tree for `DisplayXR/displayxr-unity#57` (chroma-key transparent overlay with stereo content on Leia 3D displays). Sibling Unity project that consumes the `com.displayxr.unity` UPM package.

The fixture historically held a Cube; it now holds a Mixamo **"cartoon tiger in witches hat"** FBX. The scene (`Assets/CubeTest.unity`) keeps the cube around (disabled) for fallback testing.

## Repository structure

```
displayxr-unity-test-transparent/
├── Assets/
│   ├── CubeTest.unity                       # the scene
│   ├── TransparentAutoSetup.cs              # runtime bootstrap, see below
│   ├── DragRotateCube.cs                    # left-click drag → yaw rotate
│   ├── LockToForwardAxis.cs                 # tiger-branch: locks AQDE keys to no-op
│   ├── TigerAnim.controller                 # Animator Controller wrapping the Mixamo clip
│   └── cartoon-tiger-in-witches-hat/        # the FBX + textures + materials
├── Packages/
│   ├── manifest.json                        # pins com.displayxr.unity (see below)
│   └── packages-lock.json
└── docs~/                                   # ignored by Unity (~ suffix)
    └── handoff-foreground-clipping.md       # test-transparent#2 handoff
```

## Component reference

All test-project components are runtime-wired by `TransparentAutoSetup` — there's no manual setup in the scene.

| Component | File | Purpose |
|-----------|------|---------|
| `TransparentAutoSetup` | `Assets/TransparentAutoSetup.cs` | Two `[RuntimeInitializeOnLoadMethod]` entrypoints: `SubsystemRegistration` requests transparent session + chroma key BEFORE the OpenXR session is created; `AfterSceneLoad` finds the tiger and wires the per-rig components. |
| `DragRotateCube` | `Assets/DragRotateCube.cs` | Left-click drag rotates the tiger root (yaw only — pitch intentionally removed). Tracks `DisplayXRRigManager.ActiveCamera` every frame and rebinds its `onPointerDown`/`Up` listeners on rig change. Pauses the Animator during drag so manual rotation isn't clobbered. Right-click ignored (the native overlay reserves it for window drag). |
| `WheelZoomVHeight` | `Assets/TransparentAutoSetup.cs` (nested) | Scroll-wheel → `DisplayXRDisplay.virtualDisplayHeight`. Display-centric only. Active-rig gated (only the focused rig drains the wheel accumulator). |
| `LockToForwardAxis` | `Assets/LockToForwardAxis.cs` | **Tiger-branch tweak.** Locks the rig camera's world X/Y to its startup values each `Update`, after the plugin's `DisplayXRInputController` has moved it. Net effect: AQDE keys become no-ops, only W/S still push the camera in/out (so only the in/out-of-display-plane axis is user-controllable). Uses `[DefaultExecutionOrder(int.MaxValue)]` to run after the plugin's input controller. |
| `ClipAtDisplayPlane` | `Assets/TransparentAutoSetup.cs` (nested) | **Work in progress.** Currently hooks `Camera.onPreCull` and rewrites the per-eye stereo projection's `m22`/`m23` (near/far elements) to clip at raw eye-Z. Tiger renders but the clip doesn't visibly take effect. See `docs~/handoff-foreground-clipping.md` and issue #2. |
| `AutoBoxColliderFromRenderer` | `Assets/TransparentAutoSetup.cs` (nested) | Deferred BoxCollider sizing for **non-SMR** clickables (the cube fallback). Waits for `renderer.bounds` to be valid (a few frames), then sizes the box. Skipped for `SkinnedMeshRenderer` — the plugin handles those per-triangle. |

## How transparency + clickthrough work

The mechanism splits across plugin (most of it) and test-project (small bootstrap):

1. **Transparent session** — `TransparentAutoSetup.RequestTransparentSession()` (called from `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`) tells the plugin to ask for a transparent OpenXR session. The plugin sets `xsi.transparentBackgroundEnabled` on `XrWin32WindowBindingCreateInfoEXT` before `xrCreateSession`; the runtime selects the BitBlt swapchain path and the post-weave shader writes `alpha=0` for chroma-key pixels.
2. **Chroma key** — `TransparentAutoSetup.RequestChromaKey(s_ChromaKey)` passes the same color (gray `(128, 127, 129)`) to both the runtime (via `displayxr_set_transparent_chroma_key`) AND the camera clear (via `DisplayXRTransparentOverlay.chromaKeyColor`). Gray is used here instead of magenta so silhouette-edge halos blend invisibly with typical desktop backgrounds.
3. **Layered HWND** — at `OnEnable` the overlay flips the parent HWND to `WS_POPUP | WS_EX_LAYERED | LWA_COLORKEY` so the OS punches matching pixels through to the desktop.
4. **Per-pixel click-through** — `WM_NCHITTEST` in the native overlay reads `s_hit_active` (set by the C# polling code each frame). When the cyclopean ray hits the tiger silhouette → `s_hit_active=1` → `HTCLIENT` → overlay captures. Otherwise `HTTRANSPARENT` → click forwards to the underlying app via `forward_click_to_underlying_window` (`SetForegroundWindow` + `PostMessage`).
5. **Hit testing** uses **per-triangle ray-tri** (Möller-Trumbore) against `SkinnedMeshRenderer.BakeMesh()` output, transformed via `Matrix4x4.TRS(smr.position, smr.rotation, Vector3.one)` — position + rotation only, no scale (BakeMesh's output is already in world units). 8-frame hysteresis smooths over silhouette-edge sub-pixel jitter. Active-rig gate prevents the two rigs from flapping `s_hit_active`. **This path is implemented in the plugin** (uncommitted dev work in `unity-3d-display`); the test project just sets `clickableRenderers` to the SMR.

## Tiger asset facts

- **Rig type: Generic** (not Legacy). Required for `Animator` instead of the deprecated `Animation` component.
- **Loop Time** is on the `mixamo.com` clip — set in the FBX import **Animation** tab (the Animator Controller's "Loop" doesn't do this for legacy or Mixamo clips).
- Mixamo lossy-scale chain: SMR `lossyScale = 180` (rig scale 100 × prefab scale 1.8), rootBone `lossyScale = 1.8`. `BakeMesh` output already accounts for all of these, which is why the hit-test uses **position + rotation only, no scale** when building the world transform.
- `SkinnedMeshRenderer.rootBone = mixamorig:Hips`. Note: NOT a descendant of the SMR — they're siblings under the prefab root. The plugin's hit-test uses the SMR transform directly, not the rootBone.

## Plugin dependency

The manifest pins `com.displayxr.unity` to `https://github.com/DisplayXR/displayxr-unity.git#upm`.

**During plugin development:** `Packages/manifest.json` may be temporarily pointed at the local path `file:C:/Users/Sparks i7 3080/Documents/GitHub/unity-3d-display` to pick up uncommitted plugin changes. Remember to **revert the manifest before committing**, and delete the corresponding `com.displayxr.unity` entry from `Packages/packages-lock.json` so Unity re-resolves from the git URL on next open.

### Plugin features this test project depends on

| Feature | Plugin version |
|---------|---------------|
| `DisplayXRTransparentOverlay` MonoBehaviour | v1.2.0+ |
| Chroma-key property setter (`chromaKeyColor`) | v1.2.1+ |
| `ConsumeWheelDelta()` API | v1.2.2+ |
| Per-triangle SMR hit-test, `LateUpdate` timing, active-rig gate, hysteresis | **uncommitted** dev work (will ship in next release) |
| Native `s_vkey_state` fix for forwarded button events (stuck-drag) | **uncommitted** dev work (will ship in next release) |

## Verification flow

After Build And Run for Windows:

1. Hover the tiger silhouette → console logs `[CubeTest] PointerEnter cartoon tiger...`.
2. Left-click on the tiger body → drag rotates the tiger (yaw only). Animator pauses during drag, resumes on release.
3. Click on a clearly transparent area of the window → click falls through to whatever desktop app is behind (e.g. Notepad activates).
4. Right-click + drag on the tiger silhouette → the application window moves with the cursor; the tiger does NOT rotate (right is reserved for window drag).
5. Scroll wheel over the tiger → `virtualDisplayHeight` zoom (display-rig only).
6. **W / S** keys → camera (and therefore the tiger relative to the display) push in/out of the display plane.
7. **A / Q / D / E** keys → no effect (locked by `LockToForwardAxis`).
8. Tab → cycles between Main Camera (display rig) and Cam Centric (camera rig); `DragRotateCube` rebinds its listeners automatically.

## Open issues

- [#2 — Render only foreground content (in front of virtual display plane)](https://github.com/DisplayXR/displayxr-unity-test-transparent/issues/2). Detailed handoff at [`docs~/handoff-foreground-clipping.md`](./docs~/handoff-foreground-clipping.md).

## Cross-repo references

- Plugin: [`DisplayXR/displayxr-unity`](https://github.com/DisplayXR/displayxr-unity) — overlay implementation lives in `Runtime/DisplayXRTransparentOverlay.cs`.
- Use `DisplayXR/displayxr-unity#N` syntax to reference plugin issues.
