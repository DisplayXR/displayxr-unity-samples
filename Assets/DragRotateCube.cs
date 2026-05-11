// Demo handler for the issue #57 transparent overlay tests. Shows how an
// app-side script consumes DisplayXRTransparentOverlay's polled cursor /
// pointer events to drive a drag-to-rotate interaction. Standard Unity input
// (Input.mousePosition, Mouse.current.position, OnMouseDown) doesn't work in
// transparent mode because the cloaked Unity HWND is not OS-foreground —
// see DisplayXRTransparentOverlay's class doc for the full story.
//
// Press + drag with left button while over the target → rotate it based on
// per-frame cursor delta. While dragging we disable the Animator so the
// looping clip doesn't clobber our transform.Rotate calls each frame.
//
// Multi-rig: binds to whichever rig is currently active per
// DisplayXRRigManager.ActiveCamera, and re-binds when the user Tab-cycles.
// Only the active rig drives PointerDelta and onPointerXxx events under the
// active-rig gate, so listening to a fixed overlay would silently break
// after a cycle.

using UnityEngine;
using DisplayXR;

public class DragRotateCube : MonoBehaviour
{
    public Renderer target;
    public float degreesPerPixel = 1f;

    private bool m_Dragging;
    private Animator m_Animator;
    private bool m_AnimatorWasEnabled;
    private DisplayXRTransparentOverlay m_BoundOverlay;

    void Update()
    {
        // Re-bind to the currently active rig's overlay if it changed.
        var active = GetActiveOverlay();
        if (active != m_BoundOverlay)
        {
            if (m_BoundOverlay != null)
            {
                m_BoundOverlay.onPointerDown.RemoveListener(OnDown);
                m_BoundOverlay.onPointerUp  .RemoveListener(OnUp);
            }
            if (active != null && target != null)
            {
                active.onPointerDown.AddListener(OnDown);
                active.onPointerUp  .AddListener(OnUp);
                Debug.Log($"[DragRotateCube] Bound to active rig {active.gameObject.name}");
            }
            m_BoundOverlay = active;
        }

        if (!m_Dragging || m_BoundOverlay == null) return;
        Vector2 d = m_BoundOverlay.PointerDelta;
        if (d.x == 0f) return;
        // Yaw-only: drag-right → tiger turns left. Pitch (Y-axis drag) is
        // intentionally ignored so the character stays upright.
        transform.Rotate(Vector3.up, -d.x * degreesPerPixel, Space.World);
    }

    static DisplayXRTransparentOverlay GetActiveOverlay()
    {
        var cam = DisplayXRRigManager.ActiveCamera;
        return cam != null ? cam.GetComponent<DisplayXRTransparentOverlay>() : null;
    }

    void OnDestroy()
    {
        if (m_BoundOverlay == null) return;
        m_BoundOverlay.onPointerDown.RemoveListener(OnDown);
        m_BoundOverlay.onPointerUp  .RemoveListener(OnUp);
    }

    void OnDown(Renderer r)
    {
        if (r != target) return;
        // onPointerDown fires for both left and right buttons. We only want
        // left to start a rotate-drag; right is reserved by the native
        // overlay for window drag (capture-based SetWindowPos), and letting
        // both fire causes the tiger to rotate-then-reset as the window
        // chases the cursor during right-drag.
        if (m_BoundOverlay == null || !m_BoundOverlay.IsLeftPressed) return;
        m_Dragging = true;
        if (m_Animator == null) m_Animator = GetComponent<Animator>();
        if (m_Animator != null && m_Animator.enabled)
        {
            m_AnimatorWasEnabled = true;
            m_Animator.enabled = false;
        }
        Debug.Log("[DragRotateCube] drag started");
    }

    void OnUp(Renderer r)
    {
        if (!m_Dragging) return;
        m_Dragging = false;
        if (m_Animator != null && m_AnimatorWasEnabled)
        {
            m_Animator.enabled = true;
            m_AnimatorWasEnabled = false;
        }
        Debug.Log("[DragRotateCube] drag ended");
    }
}
