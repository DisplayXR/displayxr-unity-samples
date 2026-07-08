# Region Editor — Rearchitecture Handoff (#131)

Branch: `feat/2d-surround-demo` (test-transparent). Plugin: `DisplayXR/displayxr-unity`.
Written 2026-05-31 at the end of a long session. This doc is self-contained — read it
plus `memory/project_surround_2d_131.md` and you have everything to continue.

---

## TL;DR

The 2D-surround speech-bubble demo + an in-app **region editor** (adjust the 2D/3D
split + 2D-zone borders by mouse) is working. We then tried to add **window resize**
by resizing the actual OS HWND. That produced three quirks, all rooted in resizing the
real window. The fix — confirmed with the user — is to **stop resizing the OS window**:
make the overlay a **fixed full-screen surface** and treat the "window" as a **virtual
rect inside it**, with the 2D/3D areas defined by rects/lines (exactly like the internal
divider lines that already work smoothly). This is a rearchitecture; do it fresh.

---

## Current state

### Shipped / committed
- Plugin **v1.13.0** released (`#upm` @ `6a8c359`, `upm/v1.13.0`) — per-pixel surround
  click-through mask (`displayxr_set_overlay_surround_mask`). All good.
- `CLAUDE.md` "Known Issues" corrected: window-drag phase-snap is **resolved** (#61),
  not parked — commit `97508f1` direct to `main`.
- Demo branch `feat/2d-surround-demo` last *committed* at `deb9ee5` (bubble + cosmetic +
  per-pixel mask, pinned to `#upm` v1.13.0).

### Uncommitted working-tree state (to deal with before starting)
**Plugin `unity-3d-display` (on `main`, working tree):** the superseded OS-resize change.
`git checkout` these to revert — the full-screen approach does NOT resize the HWND:
- `native~/displayxr_win32.c` — `s_window_edit_mode` + `displayxr_set_overlay_window_edit`
  + capture-based edge-resize in `overlay_wnd_proc`.
- `native~/displayxr_hooks.h`, `Runtime/DisplayXRNative.cs` — its declaration/binding.
- `Runtime/Plugins/Windows/x64/displayxr_unity.dll` — rebuilt with it.
- Revert: `git checkout -- native~/displayxr_win32.c native~/displayxr_hooks.h Runtime/DisplayXRNative.cs Runtime/Plugins/Windows/x64/displayxr_unity.dll`
  (leaves `main` clean at `97508f1`).

**Demo `displayxr-unity-test-transparent` (`feat/2d-surround-demo`, working tree):**
- `Assets/TigerSpeechBubble.cs` — **BUILD v8**: region editor V1 (split + 2D borders +
  bubble rescale) **plus** the superseded OS-resize wiring (`displayxr_set_overlay_window_edit`,
  edge-frame gizmos, `DetectResizeSettled`). **Keep the region-editor parts as the base;
  replace the window/resize parts** with the virtual-window model below.
- `Packages/manifest.json` + `packages-lock.json` — on the local `file:` pin (for dev).
  Revert to `#upm` before any commit (see "Release flow").
- Do NOT commit `Assets/_Recovery/` (ghost); do NOT commit the `file:` pin.

---

## The three quirks and the one root cause

All three came from resizing the OS HWND:
1. **Resize outline only appeared around the 2D area.** Gizmos are drawn into the
   *surround*, which only exists in the **non-canvas (2D) region**. The 3D sub-rect shows
   the woven tiger (overwrites the surround), so an outline over the 3D area can't render.
2. **Resize shifted content, then snapped back.** `SetWindowPos` moved/resized the HWND
   immediately; the canvas-rect + content only caught up on the next frame/rebuild.
3. **Content disappeared mid-resize.** The surround RT no longer matched the new HWND
   client size, so the runtime skipped the surround blit until the debounced rebuild.

---

## Chosen architecture: fixed full-screen overlay + virtual window rect

Make the OS overlay a **fixed full-screen window at the display origin** and define the
"window" as a **virtual rect inside it**. The 2D/3D areas live inside the virtual rect.
Everything is **rectangle math** (per-frame), never an OS resize.

Why this fixes all three quirks **and** a fourth problem:
- **Move/resize = adjusting rects** (`displayxr_set_canvas_rect` for 3D, surround
  placement for 2D) → same smooth path as the internal lines → **no shift, no snap, no
  rebuild, no disappear** (quirks 2 & 3 gone).
- **The whole screen is surround now**, so the outline can be drawn anywhere, including
  around the 3D area — just **inset the 3D sub-rect** by the outline thickness so there's
  a surround border for it (quirk 1 gone).
- **Phase-snap disappears.** Window-drag needed the #61 snap because the weaver
  interlaces by *absolute* display pixel, so a moving window had to land on
  lenticular-aligned pixels. A **fixed** full-screen window sits at the aligned origin
  permanently → the weaver interlaces the whole screen correctly once → a 3D sub-rect
  placed *anywhere* within it is automatically correct. Slide/resize the 3D region freely,
  **zero ghosting, no snapping.** (Validate on hardware, but this is the expected win.)

Outside the virtual window → transparent + click-through to the desktop (as today).

---

## Interaction model (user-confirmed, incl. latest refinements)

Hotkey: **Ctrl+Shift+L** ("Layout") — NOT Ctrl+Shift+W (conflicts with the W/S
virtual-display-Z control).

**Normal mode:**
- **Right-drag** anywhere in the **2D or 3D zone** → **translate the virtual window**
  (`vx,vy`) across the screen. (The OS window is fixed, so right-drag now moves the
  *content rect*, not the HWND.)
- Clicks/hover outside the virtual window → desktop (click-through).

**Show Window / Layout mode (Ctrl+Shift+L toggles):**
- Show the **virtual window outline** encompassing the whole window (2D + 3D), with
  resize handles on edges/corners.
- **Left-drag an outline edge/corner** → **resize** the virtual window (`vw,vh`).
- **Left-drag the internal split line** → adjust the 2D/3D split (vertical).
- **Left-drag the internal 2D-zone left/right borders** → adjust 2D-zone width.
- **Right-drag** → still translates (move).
- Persist layout (`PlayerPrefs`).

All drag input via `displayxr_get_overlay_pointer` (Unity's mouse is frozen in overlay
mode). Hit-test outline edges / internal handles in screen px; right-button for translate.

---

## Native changes needed (plugin → v1.14.0)

After reverting the OS-resize change, add **one coherent mode**:

`displayxr_set_overlay_fullscreen(int enabled)`:
- **enabled:** resize the overlay HWND to its **monitor bounds** at the monitor origin
  (`MonitorFromWindow` → `GetMonitorInfo` → `rcMonitor`), and set an **app-managed flag**
  so the native wndproc **stops** doing its own right-drag MOVE (the existing
  `WM_RBUTTONDOWN`/`WM_MOUSEMOVE` move at `displayxr_win32.c` ~line 474+). The app now owns
  ALL window interaction via virtual rects.
- **disabled:** clear the flag (restore native move); size restore optional.
- Add the `s_app_managed_window` guard to the `WM_RBUTTONDOWN`/move handlers so they
  early-out when managed.
- Header decl + C# binding (inside the `#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN`
  block, alongside the other overlay fns).
- Rebuild DLL (`native~/build-win.bat` via a Developer prompt; CMake VS generator finds
  MSVC). Verify the export with dumpbin.

Reference: the move handler + the (reverted) edge-resize show the capture/SetWindowPos +
`#61` WM_ENTERSIZEMOVE bracketing patterns — you will NOT need bracketing here (no OS
resize/move while managed), which is the whole point.

NOTE on the surround texture size: with a full-screen overlay the surround RT becomes the
full **monitor/render-target** size (`DisplayXRSurround` already sizes itself from
`displayxr_get_render_target_size` = HWND client = full screen). The runtime requires the
surround dims == render-target dims (HW TEST 1 in memory), which holds automatically here
since the HWND is fixed full-screen → no rebuild-on-resize needed anymore.

---

## Demo changes needed (`TigerSpeechBubble.cs`)

Keep the reusable parts of BUILD v8: the `DisplayXRSurround` setup, the bubble
(rounded panel + tail + text, rescale + wrap), `PlaceWindowRect`, `TryRectToWindow`,
`InRoundedRect`, the rounded-rect/triangle sprite gens, `displayxr_get_overlay_pointer`
polling, and the split/border drag math. **Replace** the OS-resize bits.

1. On enable, call `displayxr_set_overlay_fullscreen(1)`; on disable, `(0)`.
2. New state: virtual window rect `vx,vy,vw,vh` (fractions of the full screen, persisted)
   + `splitFrac`, `zoneLeft`, `zoneRight` (now *relative to the virtual window*).
3. Derive each frame (all in full-screen px):
   - 3D canvas sub-rect = the 3D area inside the virtual window, **inset** by the outline
     thickness; push via `displayxr_set_canvas_rect`.
   - 2D zone rect = the 2D area inside the virtual window; place the bubble in it.
   - Click mask (surround mask or rect) = the **virtual window rect** (outside →
     click-through). In Layout mode the whole virtual window catches clicks.
4. Gizmos (Layout mode): virtual-window **outline + edge/corner resize handles**
   (now renderable all around, since the full screen is surround and the 3D rect is
   inset), plus the existing internal split + 2D-border handles.
5. Interaction (via `displayxr_get_overlay_pointer`):
   - Right-drag (button bit 1) → translate `vx,vy`.
   - Left-drag outline edge/corner → resize `vw,vh` (min size; clamp to screen).
   - Left-drag internal split/borders → as today (now relative to the virtual window).
6. Remove `displayxr_set_overlay_window_edit`, the edge-frame `m_EdgeT/B/L/R` resize-band
   gizmos, and `DetectResizeSettled` (no OS resize → no rebuild). Bump the diag to v9.

`DisplayXRSurround` itself needs **no change** (it already self-sizes from the
render-target = full screen).

---

## Release flow (after hardware validation)

1. Plugin: branch off `main`, commit the `fullscreen` native + binding (subject ends
   with `(#131)` and the `Co-Authored-By: Claude Opus 4.8 (1M context)` trailer), PR,
   green CI (both platforms — the fn is Windows-only in `win32.c`, mac resolves via the
   C# EntryPointNotFound fallback), merge (`--rebase --admin --delete-branch`).
2. `/release v1.14.0` (skill spawns a general-purpose subagent; current is v1.13.0).
3. Demo: revert manifest `file:` pin → `#upm`; bump `packages-lock.json` hash to the new
   `#upm` tip (full SHA via `git ls-remote …displayxr-unity.git refs/heads/upm`); restore
   `ProjectSettings/ProjectSettings.asset` if Unity dropped `preloadedAssets`
   (`git checkout --`); commit `TigerSpeechBubble.cs` + lock on `feat/2d-surround-demo`.

Local dev/test before release: keep the `file:` pin at
`file:C:/Users/Sparks i7 3080/Documents/GitHub/unity-3d-display`.

---

## Build / test gotchas (from this session)

- Build the demo player (transparent surround only renders in a **built** player; Editor
  Preview is black). `git checkout -- ProjectSettings/ProjectSettings.asset` before/after
  building (Unity re-drops `preloadedAssets` → XR won't init).
- Player.log: `%USERPROFILE%\AppData\LocalLow\DisplayXR\DisplayXR-test\Player.log`.
  Native log (built app): `<ExeDir>\displayxr.log`.
- The bubble auto-installs via `RuntimeInitializeOnLoadMethod` (Play Mode / built only).
- DLL build: `& cmd.exe /c "<abs path>\native~\build-win.bat"` from PowerShell; ensure
  CMake is on PATH (`C:\Program Files\CMake\bin`). Verify exports with the MSVC `dumpbin`.
