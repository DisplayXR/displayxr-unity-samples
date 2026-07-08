// Right-click + drag the TIGER → moves the window.
// Mac-only smoke test for the displayxr_macos_*_window_drag primitives
// (plugin v1.5.7+). On Win32 the plugin handles right-drag inside the
// overlay HWND's WndProc, so this script is a no-op there.
//
// Gates on cursor-over-clickable using DisplayXRTransparentOverlay's
// onPointerEnter/Exit events (Mac hit-test ported in plugin v1.5.7).
// Once a drag is in flight, we keep updating even if the cursor leaves
// the tiger's silhouette — otherwise users would drop the drag the
// moment they cross a transparent pixel during the move.

using UnityEngine;
using System.Runtime.InteropServices;
using DisplayXR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MacRightDragMoveWindow : MonoBehaviour
{
#if UNITY_STANDALONE_OSX
    [DllImport("displayxr_unity", CallingConvention = CallingConvention.Cdecl)]
    private static extern void displayxr_macos_begin_window_drag();
    [DllImport("displayxr_unity", CallingConvention = CallingConvention.Cdecl)]
    private static extern void displayxr_macos_update_window_drag();
    [DllImport("displayxr_unity", CallingConvention = CallingConvention.Cdecl)]
    private static extern void displayxr_macos_end_window_drag();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInstall()
    {
        var go = new GameObject("MacRightDragMoveWindow");
        DontDestroyOnLoad(go);
        go.AddComponent<MacRightDragMoveWindow>();
        Debug.Log("[MacRightDrag] Installed");
    }

    private DisplayXRTransparentOverlay m_BoundOverlay;
    private bool m_HoveringClickable;
    private bool m_Dragging;

    void Update()
    {
        // Re-bind to the active rig's overlay (same pattern as DragRotateCube).
        var active = GetActiveOverlay();
        if (active != m_BoundOverlay)
        {
            if (m_BoundOverlay != null)
            {
                m_BoundOverlay.onPointerEnter.RemoveListener(OnHoverEnter);
                m_BoundOverlay.onPointerExit .RemoveListener(OnHoverExit);
            }
            if (active != null)
            {
                active.onPointerEnter.AddListener(OnHoverEnter);
                active.onPointerExit .AddListener(OnHoverExit);
            }
            m_BoundOverlay = active;
            m_HoveringClickable = false;
        }
        if (m_BoundOverlay == null) return;

        bool right = m_BoundOverlay.IsRightPressed;

        // Start drag only when the right-button press lands on a clickable
        // renderer. Once started, keep dragging until release regardless of
        // hover state (cursor may pass over transparent regions during drag).
        if (right && !m_Dragging && m_HoveringClickable)
        {
            m_Dragging = true;
            displayxr_macos_begin_window_drag();
        }
        else if (right && m_Dragging)
        {
            displayxr_macos_update_window_drag();
        }
        else if (!right && m_Dragging)
        {
            m_Dragging = false;
            displayxr_macos_end_window_drag();
        }
    }

    void OnHoverEnter(Renderer r) { m_HoveringClickable = true; }
    void OnHoverExit (Renderer r) { m_HoveringClickable = false; }

    static DisplayXRTransparentOverlay GetActiveOverlay()
    {
        var cam = DisplayXRRigManager.ActiveCamera;
        return cam != null ? cam.GetComponent<DisplayXRTransparentOverlay>() : null;
    }
#endif
}
