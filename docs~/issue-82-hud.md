# Work brief: tiger HUD + DisplayXR/displayxr-unity#82

Two coupled goals. The HUD is both the long-term tuning surface for the
tiger test app *and* the test surface for the #82 fix — once the wsui +
transparent-overlay crash is resolved, the HUD becomes the proof.

## Background

`DisplayXR/displayxr-unity#82` — transparent background (`DisplayXRTransparentOverlay`)
+ a `DisplayXRWindowSpaceUI` composition layer crashes the runtime in
`xrEndFrame` on the frame after the wsui swapchain is first created.
Reproduced reliably on D3D12 Windows in plugin v1.3.0 and confirmed still
present in v1.4.0 (the v1.4.0 fix was orthogonal — it added the
`clip_at_display_plane` tunable). Each feature works fine in isolation.

What we already know (from the v1.3.0 repro, see #82 for the full log tail):

- Native log dies *exactly* after `wsui_hooked: swapchain created 1024x256
  (3 images, fmt=28)` — the very next `xrEndFrame` SEGVs inside
  `displayxr_unity.dll` (no PDB shipped).
- The crash stack ends in `UnityOpenXR.DiagnosticReport_StartReport →
  displayxr_unity` (function-name unavailable). Unity's XR plugin detected
  a fatal condition somewhere in our hook chain and is collecting a
  diagnostic; the diagnostic callback itself is crashing.
- In transparent mode, the `FINALv2` projection log shows all-zero values
  (FOV=0°, pos=0,0,0). Possibly a symptom of the same broken composition
  path, possibly unrelated — worth checking either way.
- `xrCreateSession` injects `XrWin32WindowBindingCreateInfoEXT` with
  `transparentBackgroundEnabled=1` and a non-zero `chromaKeyColor`. That
  selects the runtime's BitBlt (D3D11) or DComp (D3D12) swapchain path.
- `wsui_hooked` separately calls `xrCreateSwapchain` for the overlay layer
  with format 28 (`DXGI_FORMAT_R8G8B8A8_UNORM`) and usage `COLOR_ATTACHMENT
  | SAMPLED`. That swapchain isn't part of the transparent BitBlt path.
- `hooked_xrEndFrame` extends the original layer array with the wsui layer
  via `XrCompositionLayerWindowSpaceEXT` and forwards to the runtime's
  real `xrEndFrame`. The first such forwarded call is what dies.

Probable causes (in rough likelihood order):

1. The wsui swapchain's image state when copied via D3D12
   `CopyTextureRegion` is incompatible with whatever resource state the
   transparent path leaves the main swapchain in. The `wsui_copy_to_swapchain_image`
   path in `native~/displayxr_d3d12_backend.cpp` relies on D3D12 common-state
   implicit promotion — may not hold under transparent compositor.
2. The runtime can't compose an `XrCompositionLayerWindowSpaceEXT` over a
   transparent-background main layer; the layer-mixer code path was only
   exercised against opaque sessions. This would be a runtime-side bug —
   investigation may need to touch `DisplayXR/displayxr-runtime`.
3. The chroma-key post-weave pass operates on the final composited image;
   it may misread alpha or stride from the wsui sublayer.

## Investigation plan

Order = priority. Stop at the first failing assertion that's specific
enough to fix.

1. **Reproduce on v1.4.0** with a minimal scene (one transparent overlay
   rig + one wsui canvas — no tiger, no slider, no other components). If
   minimal repro still crashes, the bug is purely in the composition path.
   If minimal repro doesn't crash, something tiger-specific is in the mix.

2. **Add native logging** inside `wsui_hooked_pre_end_frame`,
   `tick_session`, and `wsui_copy_to_swapchain_image` to identify the
   exact line that crashes. The crash trace tells us it's in
   `displayxr_unity`; an OutputDebugString *before* each significant call
   in the second xrEndFrame frame will localize it. Build, repro, read
   `displayxr.log` next to the .exe.

3. **Test in isolation** — try disabling the chroma key (`displayxr_set_transparent_chroma_key(0)`)
   but keep `transparentBackgroundEnabled=1`. Does the crash persist? If
   no, the chroma-key pass is the trigger and the fix is in the runtime's
   post-weave path or in how we set up the chroma key.

4. **Resource state forensics (D3D12)** — between the main swapchain
   image release and the wsui swapchain copy, log the D3D12 resource
   state of both. If D3D12 debug layer is on, it may emit a state
   transition error to OutputDebugString. Capture via DbgView.

5. **Runtime-side check** — if 1-4 don't isolate the cause, the issue may
   need a debug build of the runtime to step through layer composition.
   The runtime repo is `DisplayXR/displayxr-runtime`. See cross-repo refs
   in plugin CLAUDE.md.

Fix likely lands as a plugin change (D3D12 backend hardening) or a runtime
change (layer-mixer support for the transparent case). Both are
acceptable. If it turns out to be runtime-only, file a follow-up issue in
that repo and close #82 with a back-reference.

## HUD spec (tiger test repo)

Once #82 is fixed, build this in `displayxr-unity-test-transparent`. The
HUD doubles as the regression test surface: if it renders correctly on
top of the tiger with transparent overlay on, #82 is verified fixed.

### Visual

- Panel: black, **80% transparency** (alpha = 0.20 = mostly see-through),
  **rounded corners**. Use a 9-slice rounded-rect sprite generated
  procedurally, or a built-in Unity rounded sprite, or a UI Image with a
  custom material that does SDF-based rounded rectangles. Pick whichever
  is simplest — the DisplayXRTuningUI in test-2d-ui is the closest
  starting point and uses a flat solid color; you'll need to swap the
  background `Image` for a rounded one.
- Sliders: each row labeled, with a **rounded (circular) knob**. The
  existing slider code in `displayxr-unity-test-2d-ui/Assets/Scripts/DisplayXRTuningUI.cs`
  has a `GetCircleSprite()` helper that generates an antialiased circle —
  reuse that for the handle.
- Layout: position the panel in a sensible corner (top-left or bottom-left;
  match test-2d-ui's pattern). Use `DisplayXRWindowSpaceUI` so it lives
  as a composition layer.

### Controls

Two sliders:

1. **Display Z position** — drives camera world-Z translation, the same
   effect as W/S keys (push tiger in/out along the camera forward axis).
   Range: pick something reasonable like `[-2.0, +2.0]` Unity units around
   the camera's startup Z. Resets to startup value when slider is at
   center.
2. **Stereo intensity** — single slider that drives BOTH
   `DisplayXRDisplay.ipdFactor` AND `DisplayXRDisplay.parallaxFactor` to
   the same value. Range `[0.0, 1.5]` matches the existing tuning UI's
   stereo bounds. The two factors are coupled here for a single
   "perceived 3D strength" knob.

### Interaction

- **SHIFT+TAB toggles HUD show/hide** — read via `Keyboard.current` (new
  Input System; this project's `activeInputHandler == 1`).
- **Default to shown** — so the user sees the HUD on first launch and
  can confirm #82 is fixed visually.
- Plain Tab is bound by `DisplayXRRigManager.CycleNext` for camera
  cycling; SHIFT is the disambiguating modifier. test-2d-ui's
  `DisplayXRTuningUI.Update` has the exact pattern (`kb.tabKey.wasPressedThisFrame
  && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)`) — copy it.

### Implementation pattern (mirror test-2d-ui)

- Scene-attached GameObject (NOT runtime-created — that pattern proved
  fragile earlier in the foreground-only investigation; scene attachment
  is what test-2d-ui ships and what works reliably). Add to
  `Assets/CubeTest.unity` at scene root, similar to how
  `DisplayXR_FarClipSlider` was added in the tiger session.
- One MonoBehaviour that builds the Canvas + sliders in OnEnable,
  attaches `DisplayXRWindowSpaceUI` to the Canvas, with `[ExecuteAlways]`
  so editor preview works too.
- Sibling `DisplayXRWsuiMouseRouter`-style script for input routing
  (window-space layers don't carry pointer events; mouse coords need to
  be mapped to canvas-pixel coords and dispatched via `GraphicRaycaster`).
  Copy from test-2d-ui as a starting point — its existing version is
  already Input-System-friendly modulo the `Input.mousePosition` /
  `Input.GetMouseButton(0)` calls that need a `#if ENABLE_INPUT_SYSTEM`
  branch (see the keyboard-HUD's `DisplayXRWsuiMouseRouter.cs` lineage
  from the abandoned-wsui attempt for the exact fix).
- Once built, REMOVE the keyboard `FarClipDiopterSlider` and the 3D bar
  HUD — they were workarounds for #82, no longer needed. Or keep one of
  them as a diagnostic toggle.

## Repo touchpoints

- **Plugin** (`DisplayXR/displayxr-unity`): `native~/displayxr_window_space_ui.cpp`,
  `native~/displayxr_hooks.cpp` (the xrEndFrame hook), `native~/displayxr_d3d12_backend.cpp`.
  Possibly also runtime-side; if so, file a runtime issue.
- **Test repo** (`displayxr-unity-test-transparent`): new HUD script +
  `Assets/CubeTest.unity` scene edits.

## Acceptance

1. The minimal-repro scene from step 1 of the investigation no longer
   crashes.
2. The tiger HUD renders on the tiger scene with transparent overlay on,
   visibly composes over the tiger, sliders are interactive.
3. SHIFT+TAB toggles the HUD; Tab still cycles cameras.
4. #82 closed (in plugin repo) with a back-reference to the fixing PR /
   commit.

## Pickup state

- Plugin v1.4.0 is the latest release; tiger work has shipped.
- Test-transparent main is at the tiger version
  ([`e5297e5`](https://github.com/DisplayXR/displayxr-unity-test-transparent/commit/e5297e5)).
- Plugin pin in test-transparent is the floating `#upm` URL.
- The keyboard `FarClipDiopterSlider` HUD currently in the scene is the
  workaround for #82; replacing it with the wsui HUD is part of this
  work.

## Reference material in this repo

- `docs~/handoff-foreground-clipping.md` — the previous investigation
  log. Mostly resolved by v1.4.0, but the section on
  `Camera.SetStereoProjectionMatrix` not reaching the XR render path is
  still load-bearing context for future work.
- Plugin's `CLAUDE.md` "Known compatibility gap" section flags #82.
