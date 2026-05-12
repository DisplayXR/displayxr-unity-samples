# Tiger HUD handoff — picking up from #82

The plugin-side crash for `DisplayXR/displayxr-unity#82` is fixed and shipped
in [plugin v1.4.1](https://github.com/DisplayXR/displayxr-unity/releases/tag/v1.4.1).
This document is a snapshot of where the tiger HUD work stands on the
`fix/issue-82-wsui-transparent` branch ([PR #3](https://github.com/DisplayXR/displayxr-unity-test-transparent/pull/3))
so a future session can continue cleanly.

## What works

- Transparent overlay + `DisplayXRWindowSpaceUI` composes cleanly. Confirmed
  by the user — no more `DEVICE_REMOVED` crash on first wsui frame.
- HUD renders: black panel, 80% transparency, rounded corners, two
  circular-knob sliders ("3D Focus" and "3D Depth").
- SHIFT+TAB (and `H` as an alternative key) toggles visibility.
- HUD defaults to hidden — user reveals it.
- Auto-installs into any loaded scene via
  `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` — scene file doesn't
  need editing.
- Slider value drives the right thing: Focus → camera world-Z (range
  ±0.2 m around startup Z); Depth → `ipdFactor` AND `parallaxFactor`
  (range 0..1, default 1).
- `DragRotateCube` and `WheelZoomVHeight` now gate on
  `DisplayXRWindowSpaceUI.IsCursorOverInteractive` — when cursor is over
  the HUD, scene drag/zoom is suppressed.

## What still needs work — user feedback as of `1e6ad56`

1. **Cursor shows the Windows "busy" spinner (blue ring) constantly.**
   Likely cause: the router's per-frame `GraphicRaycaster.Raycast` +
   `ExecuteEvents` dispatching is heavy enough that the overlay HWND's
   message pump isn't keeping up. Mitigations to try:
   - Only run the raycaster while `m_LeftDown` or just-down/just-up
     edges, not every frame. Hover state is currently re-queried each
     frame for no real benefit (there are no hover-only Unity events on
     the wsui controls that matter for tuning).
   - Avoid the `new List<RaycastResult>()` allocation every frame —
     reuse a member field.

2. **Knob pickup is unreliable.** Sometimes the click misses the handle.
   Probable causes:
   - The knob is 40 px in a 1024×800 RT; the panel is 0.14 × 0.18
     fractional which is small on screen — actual handle target may
     only be ~10–14 visible pixels wide depending on display
     resolution. Either bump the knob size or widen the panel.
   - `m_PointerData.pressPosition` is being set after the raycast, but
     `Slider.OnPointerDown` reads `pointerPressRaycast.module` to decide
     whether to drag-along — if the raycast hit a child of the slider
     other than the handle, the slider's "drag from anywhere on the bar"
     code path may not fire from a synthesized pointer event.

3. **Slider drag stops mid-way.** Drag visibly works for a bit, then the
   knob stops following the cursor. Probable causes:
   - The router clears `m_PressTarget` when `nowDown=false` (mouse-up).
     If `Mouse.current.leftButton.isPressed` flickers to false for a
     single frame mid-drag (which can happen with the overlay's polled
     state when the OS cursor moves fast), the drag ends prematurely.
   - The injected `Mouse.current` state from
     `DisplayXRTransparentOverlay` is queued via `InputSystem.QueueStateEvent`.
     Queue events are processed at frame boundaries, so cursor coords
     may lag the actual mouse by 1 frame — meaning `delta` is computed
     against a stale `m_LastCanvasPos` and Unity's `Slider.OnDrag`
     receives a delta that doesn't match the real cursor.
   - Race between the router (`Update`, order -100) and the overlay's
     state injection (`LateUpdate`). The router reads stale state on
     frame N, sees position unchanged, doesn't dispatch a drag event,
     slider sits.

Diagnostic logs are wired in `1e6ad56` — once every 2 s the router logs
`frac=(x,y) panel=(...) inside=… dragging=…` and `[TigerTuningHUD] toggle
via H/SHIFT+TAB → visible=…`. Useful for the next debugging round.

## Architecture pointers for the next session

- **Plugin's wsui router model** (`displayxr-unity-test-2d-ui/Assets/Scripts/DisplayXRWsuiMouseRouter.cs`)
  is the closest working reference. It works reliably in opaque mode.
  This repo's `TigerHudMouseRouter.cs` is adapted from it for the
  transparent (DComp) Unity HWND case. Diff carefully — the
  cloaked-window cursor injection path is what makes the transparent
  case fragile.

- **`DisplayXRWindowSpaceUI.IsCursorOverInteractive`** is the static
  gate between UI and scene. Set by the router each frame, read by
  `DragRotateCube` and `WheelZoomVHeight`. Don't break this contract.

- **Plugin v1.4.1 ships the format-match fix** for the actual `#82`
  crash. Don't try to fix HUD problems by patching the plugin — the
  fragility is on the input-routing side, not the wsui composition
  side.

- **Test cycle**: edit script → Unity recompiles → File > Build And Run
  (or use the existing build at `C:\Users\Sparks i7 3080\Documents\Unity\DisplayXR-Test-Transp+UI`).
  Player.log lives at `%LOCALAPPDATA%\..\LocalLow\DisplayXR\DisplayXR-test\Player.log`.
  The diagnostic logs from `1e6ad56` write there.
