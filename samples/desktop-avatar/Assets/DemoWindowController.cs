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

    [Tooltip("Seconds to ease the stereo disparity (ipdFactor) across a 2D<->3D " +
             "switch, via the shared DisplayXRModeSwitch (parallax stays at 1 so " +
             "head-tracked perspective is kept). 3D->2D ramps down THEN switches; " +
             "2D->3D switches THEN ramps up. 0 = instant. ~0.4s matches the demo's " +
             "former 24-frame ramp.")]
    public float modeTransitionSeconds = 0.4f;

    // Tracked render mode (the display starts in 3D for a stereo app). There is
    // no getter, so we toggle from this assumed initial state; updated when the
    // sequencer fires the switch.
    private bool m_Mode3D = true;
    // Shared smooth 2D<->3D sequencer (plugin: DisplayXR.DisplayXRModeSwitch) — the C#
    // port of displayxr-common's mode_switch the native test apps use. Replaces the
    // former hand-rolled coroutine ramp; owns the ramp-then-fire / fire-then-ramp
    // asymmetry. The runtime's #615 one-frame coherence guard covers the first 3D
    // frame (so no app-side settle-frames hold is needed).
    private readonly DisplayXR.DisplayXRModeSwitch m_ModeSeq = new DisplayXR.DisplayXRModeSwitch();
    private bool m_ModeSeqConfigured;
    private const float kSteadyIpd = 1.0f; // full-3D disparity to restore to

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

        // Workspace-tile mode (#225): the shell/runtime owns the window — it
        // syncs the hidden window to the tile's 3D-window pixel rect so Kooima
        // (from the tile pose) matches the render aspect (this is what every
        // other tile app gets for free). Restoring/persisting our own portrait
        // size here overrides that cell-resize and mismatches Kooima → the whole
        // scene stretches. So this window-chrome policy is inert as a tile.
        if (DisplayXRNative.displayxr_is_shell_mode() != 0) return;

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
                && DisplayXRNative.displayxr_is_our_process_foreground() != 0)
            {
                // Retargets cleanly if pressed mid-ramp (no "in flight" guard needed).
                RequestModeSwitch(!m_Mode3D);
            }
        }

        // Drive the shared smooth 2D<->3D sequencer every frame: push its ramped
        // ipdFactor onto the rig and fire the runtime mode request on the frame it
        // signals. (Built app only — Update returns early in the editor above.)
        if (!m_ModeSeqConfigured) { m_ModeSeq.Configure(modeTransitionSeconds); m_ModeSeqConfigured = true; }
        float seqIpd = m_ModeSeq.Update(Time.deltaTime, out bool seqFire, out uint seqMode);
        if (m_ModeSeq.Active) SetStereoAmount(seqIpd);
        if (seqFire)
        {
            // The provider owns the runtime mode request now (the hook / standalone
            // path is gone). RequestRenderingMode no-ops if the session isn't running.
            DisplayXR.DisplayXRProvider.RequestRenderingMode(seqMode);
            m_Mode3D = (seqMode == 1);
            Debug.Log($"[Demo] Render mode -> {(m_Mode3D ? "3D" : "2D")} (smooth sequencer)");
        }
#endif
    }

    // Hand the target mode to the shared sequencer. Mode index 0 = 2D (1 view),
    // 1 = 3D (2 views) — the runtime standard. currentIpd is the live ramp value when
    // mid-switch, else the steady-state for the current mode (so the first 3D->2D
    // press ramps down from full disparity, not from a stale 0).
    private void RequestModeSwitch(bool to3D)
    {
        uint targetMode = to3D ? 1u : 0u;
        uint targetVC   = to3D ? 2u : 1u;
        uint curMode    = m_Mode3D ? 1u : 0u;
        uint curVC      = m_Mode3D ? 2u : 1u;
        float curIpd    = m_ModeSeq.Active ? m_ModeSeq.Ipd : (m_Mode3D ? kSteadyIpd : 0f);
        m_ModeSeq.Request(targetMode, targetVC, curMode, curVC, curIpd, kSteadyIpd);
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

    // (SwitchRenderMode / RampStereo removed — the smooth ramp is now the shared
    // DisplayXR.DisplayXRModeSwitch sequencer, driven per-frame in Update. See
    // RequestModeSwitch + the sequencer-drive block in Update.)

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
