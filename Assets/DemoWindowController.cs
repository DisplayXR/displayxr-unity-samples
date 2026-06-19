// Demo-owned window-chrome UI for the transparent overlay.
//
// ARCHITECTURE NOTE: all window-chrome POLICY lives here, in the app — NOT in
// the plugin. The plugin exposes only mechanism (primitives):
//   displayxr_resize_overlay
//   displayxr_get_overlay_size
//   displayxr_consume_overlay_close_request
//   (also displayxr_toggle_window_decoration, intentionally NOT bound here —
//    see "no decoration" note below)
// This component binds whatever UI it wants on top of those. Edit freely — the
// plugin imposes no keys or gestures.
//
// Bindings:
//   Ctrl + arrows               keyboard resize (the reliable path)
//   Esc                         quit
//   overlay X button / Alt+F4   quit (via the plugin close-request flag)
//   (move = right-drag, handled by the plugin's overlay; nothing to do here)
//
// WHY KEYBOARD RESIZE, NOT MOUSE: the Leia SR weaver installs a WndProc subclass
// on the overlay HWND and SWALLOWS mouse button-downs near the window FRAME
// (hardware-traced) because the overlay is a non-activating satellite of cloaked
// Unity (it must stay non-activating so Unity keeps input focus). Every
// mouse-drag resize approach we tried (OS sizing border, client-edge grip,
// right-button edge, interior inset handle) dies on that. displayxr_resize_
// overlay is a direct SetWindowPos call (the same call the working right-drag
// MOVE uses) and is never intercepted, so Ctrl+arrows always work.
//
// WHY NO DECORATION TOGGLE: on this overlay an OS title bar earns its keep
// nowhere — caption move duplicates the plugin's right-drag, the sizing border
// is dead (SR subclass), and the X is flaky (NC press sometimes eaten). So we
// stay borderless. The plugin still exposes displayxr_toggle_window_decoration
// for other contexts (e.g. a normal non-SR monitor); the demo just doesn't bind
// it.

using UnityEngine;
using UnityEngine.InputSystem;
using DisplayXR;

public class DemoWindowController : MonoBehaviour
{
    [Tooltip("Pixels added/removed per Ctrl+arrow keyboard-resize press.")]
    public int keyboardResizeStepPx = 80;

    [Tooltip("Smallest window size the demo will request.")]
    public int minWindowPx = 200;

    [Tooltip("Key that toggles 3D <-> 2D display render mode. Only fires while " +
             "this app is the focused/foreground window (i.e. the tiger is in " +
             "focus, not clicked-through to the desktop).")]
    public Key renderModeToggleKey = Key.V;

    [Tooltip("FRAMES over which to ramp the rig's IPD factor 1<->0 around a 2D/3D " +
             "switch (parallax stays at 1 so head-tracked perspective is kept). " +
             "Frame-based, NOT time-based: a frame hitch during the heavy HW " +
             "switch can't skip the ramp into a snap. 3D->2D ramps down THEN " +
             "switches; 2D->3D switches THEN ramps up.")]
    public int modeSwitchRampFrames = 24;

    [Tooltip("Frames to hold mono (IPD 0) AFTER engaging 3D before ramping the " +
             "disparity up, so the HW mode-switch transition and the disparity " +
             "growth don't overlap.")]
    public int modeSwitchSettleFrames = 4;

    // Tracked render mode (the display starts in 3D for a stereo app). There is
    // no getter, so we toggle from this assumed initial state.
    private bool m_Mode3D = true;
    private Coroutine m_ModeSwitch;   // non-null while a ramped switch is running

    // Window-size persistence: remember the overlay size across launches so the
    // app starts at the size the user last set (the layout/split persists in
    // TigerSpeechBubble). These keys are shared with TigerSpeechBubble's launch
    // seed, which reads them so the zone-sized eye RT matches the restored size.
    public const string kWinWPref = "dxr_winW";
    public const string kWinHPref = "dxr_winH";
    public const string kWinXPref = "dxr_winX";
    public const string kWinYPref = "dxr_winY";
    // Shipped default layout = the tuned layout saved on the dev machine, so a
    // fresh install (cleared PlayerPrefs / partner machine) starts here. Per-user
    // changes still persist on top. NOTE position is monitor-relative — on a
    // different display setup it may land off-screen until the user moves it
    // (which then persists).
    private const int kDefaultW = 840;
    private const int kDefaultH = 1448;
    private const int kDefaultX = 2876;
    private const int kDefaultY = 673;
    private int m_LastW = -1, m_LastH = -1, m_LastX = int.MinValue, m_LastY = int.MinValue;
    private bool m_Restored;

    // Self-bootstrap: no scene wiring needed (mirrors the demo's other
    // RuntimeInitializeOnLoadMethod helpers). Lives for the whole session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("DemoWindowController");
        go.AddComponent<DemoWindowController>();
        DontDestroyOnLoad(go);
    }

    void Update()
    {
#if UNITY_STANDALONE_WIN
        if (Application.isEditor) return;

        // Remember / restore window size AND position across launches (the split
        // persists separately in TigerSpeechBubble). On the first valid frame,
        // restore the last-saved size + position; thereafter, track changes in
        // memory (PlayerPrefs is flushed in OnApplicationQuit, so no per-frame
        // disk writes during a resize/move). The launch seed reads the size keys
        // so the eye RT matches the restored window.
        DisplayXRNative.displayxr_get_overlay_size(out int curW, out int curH);
        DisplayXRNative.displayxr_get_overlay_position(out int curX, out int curY);
        if (curW > 0 && curH > 0)
        {
            if (!m_Restored)
            {
                m_Restored = true;
                // Fall back to the shipped default layout (not the born size/spot)
                // so a fresh install starts at the tuned layout.
                int wantW = PlayerPrefs.GetInt(kWinWPref, kDefaultW);
                int wantH = PlayerPrefs.GetInt(kWinHPref, kDefaultH);
                if (wantW != curW || wantH != curH)
                    DisplayXRNative.displayxr_resize_overlay(
                        Mathf.Max(minWindowPx, wantW), Mathf.Max(minWindowPx, wantH));
                int wantX = PlayerPrefs.GetInt(kWinXPref, kDefaultX);
                int wantY = PlayerPrefs.GetInt(kWinYPref, kDefaultY);
                if (wantX != curX || wantY != curY)
                    DisplayXRNative.displayxr_set_overlay_position(wantX, wantY);
            }
            else
            {
                if (curW != m_LastW || curH != m_LastH)
                {
                    m_LastW = curW; m_LastH = curH;
                    PlayerPrefs.SetInt(kWinWPref, curW);
                    PlayerPrefs.SetInt(kWinHPref, curH);
                }
                if (curX != m_LastX || curY != m_LastY)
                {
                    m_LastX = curX; m_LastY = curY;
                    PlayerPrefs.SetInt(kWinXPref, curX);
                    PlayerPrefs.SetInt(kWinYPref, curY);
                }
            }
        }

        // Quit: Esc, or the plugin's close-request flag (overlay X / Alt+F4).
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) { Quit(); return; }
        if (DisplayXRNative.displayxr_consume_overlay_close_request() != 0) { Quit(); return; }

        if (kb != null)
        {
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;

            // Ctrl + arrows -> keyboard resize (one step per press; discrete to
            // avoid per-frame swapchain churn).
            if (ctrl)
            {
                int dw = 0, dh = 0;
                if (kb.rightArrowKey.wasPressedThisFrame) dw += keyboardResizeStepPx;
                if (kb.leftArrowKey.wasPressedThisFrame)  dw -= keyboardResizeStepPx;
                if (kb.upArrowKey.wasPressedThisFrame)    dh += keyboardResizeStepPx;
                if (kb.downArrowKey.wasPressedThisFrame)  dh -= keyboardResizeStepPx;
                if (dw != 0 || dh != 0)
                {
                    DisplayXRNative.displayxr_get_overlay_size(out int w, out int h);
                    if (w > 0 && h > 0)
                        DisplayXRNative.displayxr_resize_overlay(
                            Mathf.Max(minWindowPx, w + dw), Mathf.Max(minWindowPx, h + dh));
                }
            }

            // V -> toggle 3D <-> 2D render mode, only while this app is focused
            // ("tiger in focus") and not mid-switch. Suppressed while Ctrl held.
            if (!ctrl && renderModeToggleKey != Key.None
                && kb[renderModeToggleKey].wasPressedThisFrame
                && DisplayXRNative.displayxr_is_our_process_foreground() != 0
                && m_ModeSwitch == null)
            {
                m_ModeSwitch = StartCoroutine(SwitchRenderMode(!m_Mode3D));
            }
        }
#endif
    }

    // Flush remembered window size/position (tracked in-memory each frame) so it
    // persists to the next launch without per-frame disk writes during a drag.
    void OnApplicationQuit()
    {
        PlayerPrefs.Save();
    }

#if UNITY_STANDALONE_WIN
    private void Quit()
    {
        Debug.Log("[Demo] Quit requested.");
        Application.Quit();
    }

    // FULL render-mode switch with a smooth disparity ramp so the 2D/3D change
    // doesn't pop. 3D->2D: ramp stereo 1->0 (collapse to mono) THEN request 2D
    // (seamless, content is already flat). 2D->3D: request 3D THEN ramp 0->1
    // (disparity grows back in). Mode index 0 = 2D, 1 = 3D (runtime standard,
    // same as the Editor Preview). The standalone shim falls back to the hooked
    // backend in a built player.
    private System.Collections.IEnumerator SwitchRenderMode(bool to3D)
    {
        if (to3D)
        {
            // Force IPD to 0 and let the rig push it for a frame BEFORE engaging
            // 3D, so the runtime switches into stereo with the eyes already
            // coincident (mono) — otherwise 3D engages at full native disparity
            // for a frame and pops. Then ramp 0->1 to grow disparity smoothly.
            SetStereoAmount(0f);
            yield return null;
            m_Mode3D = true;
            int ok = DisplayXRNative.displayxr_standalone_request_rendering_mode(1);
            Debug.Log($"[Demo] Render mode -> 3D (returned {ok}); settling then ramping IPD 0->1");
            // Hold mono for a few frames so the HW/composition switch settles
            // before the disparity grows (the EXT request has no completion event
            // to wait on — see note to the user / runtime issue).
            for (int i = 0; i < modeSwitchSettleFrames; i++)
                yield return null;
            yield return RampStereo(0f, 1f);
        }
        else
        {
            yield return RampStereo(1f, 0f);
            m_Mode3D = false;
            int ok = DisplayXRNative.displayxr_standalone_request_rendering_mode(0);
            Debug.Log($"[Demo] Render mode -> 2D (returned {ok}) after parallax ramp 1->0");
        }
        m_ModeSwitch = null;
    }

    // Frame-based ramp: advances a fixed fraction per RENDERED frame, so even if
    // the HW mode switch causes a frame hitch the ramp still shows every step
    // (a time-based ramp would skip them on a big deltaTime → snap). NOTE: the
    // rig IPD ramp is verified smooth here; a residual 2D->3D output snap is
    // runtime-side (it doesn't honor the per-frame view-rig IPD across the mode
    // switch) — see DisplayXR/displayxr-runtime#615.
    private System.Collections.IEnumerator RampStereo(float from, float to)
    {
        int n = Mathf.Max(1, modeSwitchRampFrames);
        for (int i = 1; i <= n; i++)
        {
            SetStereoAmount(Mathf.Lerp(from, to, (float)i / n));
            yield return null;
        }
    }

    // Scale the active rig's IPD only: 1 = natural separation, 0 = mono (both
    // eyes coincide at the cyclopean center). parallaxFactor is intentionally
    // LEFT at its natural 1 so the 2D view keeps the head-tracked perspective —
    // only the stereo disparity is ramped away. The rig pushes the factor to the
    // runtime view-rig each LateUpdate (this runs in Update, so it lands the same
    // frame). No-op if no rig is present.
    private void SetStereoAmount(float a)
    {
        foreach (var d in FindObjectsByType<DisplayXRDisplay>(FindObjectsSortMode.None))
            if (d != null) d.ipdFactor = a;
        foreach (var c in FindObjectsByType<DisplayXRCamera>(FindObjectsSortMode.None))
            if (c != null) c.ipdFactor = a;
    }
#endif
}
