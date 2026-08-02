# CLAUDE.md

> **Part of the [displayxr-unity-samples](https://github.com/DisplayXR/displayxr-unity-samples) monorepo.**
> Folder `samples/desktop-avatar/` · product `DisplayXR-DesktopAvatar` · installs to `…\DisplayXR\Unity/DesktopAvatar`.
> Installer/build logic is **shared** — see the root [CLAUDE.md](../../CLAUDE.md) and
> `installer/common/SampleInstaller.nsh`; never fork installer logic into this sample.
> Some sections below predate the consolidation from the former `displayxr-unity-test*`
> repos — treat repo-structure/sibling references as mapping to `samples/*` here.

Guidance for Claude Code when working in this repository.

## Project Overview

This is the **transparent overlay test variant** of the displayxr-unity test suite — the working tree for `DisplayXR/displayxr-unity#57` (alpha-native transparent overlay with stereo content on Leia 3D displays; the chroma-color workaround that was the original v1.2.0 mechanism was removed in plugin v1.6.0 / `DisplayXR/displayxr-unity#103`). Sibling Unity project that consumes the `com.displayxr.unity` UPM package.

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
| `TransparentAutoSetup` | `Assets/TransparentAutoSetup.cs` | Two `[RuntimeInitializeOnLoadMethod]` entrypoints: `SubsystemRegistration` requests the alpha-native transparent session BEFORE the OpenXR session is created; `AfterSceneLoad` finds the tiger and wires the per-rig components. |
| `DragRotateCube` | `Assets/DragRotateCube.cs` | Left-click drag rotates the tiger root (yaw only — pitch intentionally removed). Tracks `DisplayXRRigManager.ActiveCamera` every frame and rebinds its `onPointerDown`/`Up` listeners on rig change. Pauses the Animator during drag so manual rotation isn't clobbered. Right-click ignored (the native overlay reserves it for window drag). |
| `WheelZoomVHeight` | `Assets/TransparentAutoSetup.cs` (nested) | Scroll-wheel → `DisplayXRDisplay.virtualDisplayHeight`. Display-centric only. Active-rig gated (only the focused rig drains the wheel accumulator). |
| `LockToForwardAxis` | `Assets/LockToForwardAxis.cs` | **Tiger-branch tweak.** Locks the rig camera's world X/Y to its startup values each `Update`, after the plugin's `DisplayXRInputController` has moved it. Net effect: AQDE keys become no-ops, only W/S still push the camera in/out (so only the in/out-of-display-plane axis is user-controllable). Uses `[DefaultExecutionOrder(int.MaxValue)]` to run after the plugin's input controller. |
| `ClipAtDisplayPlane` | `Assets/TransparentAutoSetup.cs` (nested) | **Work in progress.** Currently hooks `Camera.onPreCull` and rewrites the per-eye stereo projection's `m22`/`m23` (near/far elements) to clip at raw eye-Z. Tiger renders but the clip doesn't visibly take effect. See `docs~/handoff-foreground-clipping.md` and issue #2. |
| `TigerFaceViewer` | `Assets/TigerFaceViewer.cs` | **Head-tracked billboard (yaw-only), ON by default** — the tiger turns to face the tracked viewer; press **F** to turn it off and restore its authored rotation. Yaw math is ported 1:1 from the native reference (see below). The once-per-second log prints every viewer-position source side by side, which is also the A/B probe for plugin #236. Self-installs via `[RuntimeInitializeOnLoadMethod]` — no scene wiring. |
| `AutoBoxColliderFromRenderer` | `Assets/TransparentAutoSetup.cs` (nested) | Deferred BoxCollider sizing for **non-SMR** clickables (the cube fallback). Waits for `renderer.bounds` to be valid (a few frames), then sizes the box. Skipped for `SkinnedMeshRenderer` — the plugin handles those per-triangle. |

## How transparency + clickthrough work

The mechanism splits across plugin (most of it) and test-project (small bootstrap):

1. **Transparent session** — `TransparentAutoSetup.RequestTransparentSession()` (called from `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`) tells the plugin to ask for a transparent OpenXR session. The plugin sets `xsi.transparentBackgroundEnabled` on `XrWin32WindowBindingCreateInfoDXR` and opts the session into `XR_ENVIRONMENT_BLEND_MODE_ALPHA_BLEND` so Unity emits per-pixel alpha into the swapchain.
2. **Alpha-native camera clear** — the overlay's `OnEnable` flips the camera to `CameraClearFlags.SolidColor` with `backgroundColor = (0,0,0,0)` so transparent regions emit `alpha=0` directly. No chroma color is involved anywhere. (Older plugin versions painted a gray chroma color and relied on a runtime post-weave conversion pass; that workaround was removed once the runtime's compose-under-bg + alpha-gate DP path shipped — tracked in `DisplayXR/displayxr-unity#103`.)
3. **Per-pixel-alpha overlay HWND** — the plugin creates the overlay as a top-level `WS_POPUP` with `WS_EX_NOREDIRECTIONBITMAP`, so DWM has no opaque redirection surface and composites the HWND purely from the runtime's DComp visuals (real per-pixel alpha against the desktop). The runtime DP composes the captured desktop content under each tile pre-weave and alpha-gates post-weave, so anti-aliased silhouettes carry true soft alpha. The plugin explicitly strips `WS_EX_LAYERED` off Unity's HWND and does **not** call `SetLayeredWindowAttributes` / `LWA_COLORKEY`.
4. **Per-pixel click-through** — `WM_NCHITTEST` in the native overlay reads `s_hit_active` (set by the C# polling code each frame). When the cyclopean ray hits the tiger silhouette → `s_hit_active=1` → `HTCLIENT` → overlay captures. Otherwise `HTTRANSPARENT` → click forwards to the underlying app via `forward_click_to_underlying_window` (`SetForegroundWindow` + `PostMessage`).
5. **Hit testing** uses **per-triangle ray-tri** (Möller-Trumbore) against `SkinnedMeshRenderer.BakeMesh()` output, transformed via `Matrix4x4.TRS(smr.position, smr.rotation, Vector3.one)` — position + rotation only, no scale (BakeMesh's output is already in world units). 8-frame hysteresis smooths over silhouette-edge sub-pixel jitter. Active-rig gate prevents the two rigs from flapping `s_hit_active`. Implemented entirely in the plugin; the test project just sets `clickableRenderers` to the SMR.

## Head-coupled effects: work in PHYSICAL space, not world space

**Billboard yaw is computed in raw display space, not Unity world space.** This is
the single most important lesson from building `TigerFaceViewer`, and it mirrors the
viewer-confirmed native reference in
[`displayxr-demo-avatar`](https://github.com/DisplayXR/displayxr-demo-avatar)
(`windows/main.cpp`, "Face-the-viewer billboard"):

```csharp
// head centroid in metres, origin = physical panel centre, viewer at +Z
float hzAbs    = Mathf.Max(Mathf.Abs(hz), 1e-3f);
float targetYaw = FACE_YAW_SIGN * Mathf.Atan2(hx, hzAbs) * Mathf.Rad2Deg;  // FACE_YAW_SIGN = -1
```

Typical live values: `hz` ~0.5 m, `hx` ±0.12 m → ±14° of yaw.

Why not world space: on the **display-centric rig, scale-as-zoom inflates world
units**, so the same head reads as `ipd≈1.2` world units against a physical 0.06 m —
and any reference vector you build from scene objects inherits the scene's layout.
Two world-space attempts failed here: one keyed off `(camera − tiger)`, which
collapses because the tiger's root sits at the camera's x/z; the other picked its
target with `FindAnyObjectByType<DragRotateCube>()` and silently rotated the
**invisible fallback cube** (`TransparentAutoSetup` wires one onto every target
root — the tiger *and* `Cube`). Physical space has neither failure mode.

Smoothing is time-based exponential (`tau = 0.04 s`), and the heading only chases
while eye tracking is **locked** — warmup positions jitter it.

Viewer-position sources, all live per-frame:

| API | Space | Use for |
|-----|-------|---------|
| `DisplayXRNative.displayxr_get_eye_positions` | **physical metres**, panel-centre origin, viewer at +Z | **head-coupled effects — start here** (what the billboard uses) |
| `DisplayXRProvider.TryGetViewerHead` / `TryGetViewerEyes` | Unity **world** (v2.9.1+) | when the effect genuinely needs scene-space coordinates |
| `DisplayXRNative.displayxr_get_stereo_matrices` | per-eye view + proj | cyclopean hit-test, off-axis probes (`KooimaProbe`) |

### Rebase to the ZONE canvas, or dragging the avatar does nothing

The raw eyes are **panel-centre** relative, so on their own they carry no information
about where the avatar sits on screen: drag the window and the yaw doesn't budge. The
heading must be rebased to the **zone canvas** centre (the reference's zone-canvas
rebase):

```csharp
pxSizeX    = panelWidthM / panelWidthPx;
canvasCxPx = canvasRect.x + canvasRect.w / 2;
hx        -= (canvasCxPx - panelWidthPx / 2) * pxSizeX;
```

`DisplayXRNative.displayxr_get_kooima_canvas` is **the zone canvas**, not merely the
window rect — the provider chains `XrDisplayZoneDXR` onto the *primary* `xrLocateViews`
in front of the rig ("the rect IS the canvas", `displayxr_provider_session.cpp`), and
the `XrViewDisplayRawDXR` raw channel that fills `canvasRectPx` rides on that same
zone-scoped locate. So it already equals what the native reference obtains from its
separate zone-scoped locate. With no zone active it degrades to the full window, which
is the right reference then. `TigerFaceViewer` logs `geometry: canvas … | zone0 …` each
tick so this stays checkable — canvas extent should equal the primary zone's extent.

Measured on hardware, viewer stationary, window dragged left → centre → right:
`canvasDx` −0.127 → −0.001 → +0.126 m, yaw −12.4° → +4.2° → +20.4°.

### On `Camera.GetStereoViewMatrix` (plugin #236)

[Plugin #236](https://github.com/DisplayXR/displayxr-unity/issues/236) reports it
returning the **same matrix for both eyes** under the provider, which silently
freezes anything built on it. The plugin never writes that cache
(`Camera.SetStereoViewMatrix` went away with the #166 provider migration). But note:
**it does NOT reproduce in a built player here** — `TigerFaceViewer`'s log reads
`eyesEqual=False` on Windows/D3D12, with values matching the provider's exactly. So
the failure is configuration-specific (editor vs player, Unity version), not
universal. Use the physical or provider APIs above and the question doesn't arise.

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
| `ConsumeWheelDelta()` API | v1.2.2+ |
| Per-triangle SMR hit-test, `LateUpdate` timing, active-rig gate, hysteresis | v1.4.x+ |
| Alpha-native transparent overlay (no chroma-color camera-paint); requires runtime with compose-under-bg + alpha-gate DP path and Windows ALPHA_BLEND advertisement | **next plugin release** (clean-break removal of `chromaKeyColor` / `RequestChromaKey` API) |

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
9. **Head-tracked billboard is ON at startup** (`TigerFaceViewer`). Move your head
   left/right in front of the panel: the tiger turns to follow you. The
   once-per-second log should read like
   `rawHead (-0.115,0.111,0.509)m tracked=True -> targetYaw=12.4 yaw=11.8deg on
   cartoon tiger…` — i.e. `hz` ~0.5 m, `hx` ±0.12 m, yaw ±14°, on the **tiger**
   (if the target name is `Cube`, it grabbed the invisible fallback fixture).
   Press **F** to turn it off and restore the authored rotation.

## Open issues

- [#2 — Render only foreground content (in front of virtual display plane)](https://github.com/DisplayXR/displayxr-unity-test-transparent/issues/2). Detailed handoff at [`docs~/handoff-foreground-clipping.md`](./docs~/handoff-foreground-clipping.md).

## Cross-repo references

- Plugin: [`DisplayXR/displayxr-unity`](https://github.com/DisplayXR/displayxr-unity) — overlay implementation lives in `Runtime/DisplayXRTransparentOverlay.cs`.
- Use `DisplayXR/displayxr-unity#N` syntax to reference plugin issues.
