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

        // Quit: Esc, or the plugin's close-request flag (overlay X / Alt+F4).
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) { Quit(); return; }
        if (DisplayXRNative.displayxr_consume_overlay_close_request() != 0) { Quit(); return; }

        // Ctrl + arrows -> keyboard resize (one step per press; discrete to
        // avoid per-frame swapchain churn).
        if (kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed))
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
#endif
    }

#if UNITY_STANDALONE_WIN
    private void Quit()
    {
        Debug.Log("[Demo] Quit requested.");
        Application.Quit();
    }
#endif
}
