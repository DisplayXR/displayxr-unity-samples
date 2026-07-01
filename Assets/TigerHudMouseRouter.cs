// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: Apache-2.0
//
// Input router for TigerTuningHUD's window-space-layer canvas.
//
// XrCompositionLayerWindowSpaceEXT submits pixels; it doesn't carry input.
// The wsui canvas is a private WorldSpace canvas on a hidden layer at
// (0, 100000, 0), so Unity's EventSystem can't naturally see clicks on it.
//
// This bridges the cursor → fractional window coords → hit-test the wsui
// layer rect → map to RT-pixel coords → synthesize PointerEventData →
// dispatch click/drag via GraphicRaycaster. Adapted from
// displayxr-unity-test-2d-ui/DisplayXRWsuiMouseRouter (built-app path only).

using System.Collections.Generic;
using DisplayXR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Run BEFORE scene-input scripts (DragRotateCube, WheelZoomVHeight) so the
// IsCursorOverInteractive flag is up-to-date when they read it later in the
// frame. Without this, the flag lags by one frame and the first click in/out
// of the panel "leaks" into scene rotation.
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(TigerTuningHUD))]
public class TigerHudMouseRouter : MonoBehaviour
{
    private DisplayXRWindowSpaceUI m_Wsui;
    private GraphicRaycaster m_Raycaster;
    private EventSystem m_EventSystem;
    private DisplayXRTransparentOverlay m_Overlay;

    private GameObject m_PressTarget;
    private PointerEventData m_PointerData;
    private RaycastResult m_PressRaycast;
    private Vector2 m_LastCanvasPos;
    private Vector2 m_LastRaycastPos;
    private bool m_LeftDown;
    private bool m_WasInside;
    // Reuse one allocation; GraphicRaycaster.Raycast clears+fills.
    private readonly List<RaycastResult> m_Hits = new List<RaycastResult>(8);

    void OnEnable()
    {
        m_EventSystem = EventSystem.current;
        if (m_EventSystem == null)
        {
            var es = new GameObject("DisplayXR_EventSystem", typeof(EventSystem));
            m_EventSystem = es.GetComponent<EventSystem>();
        }
        m_PointerData = new PointerEventData(m_EventSystem);
    }

    private float m_NextDiagLog;

    void Update()
    {
        // Lazy-bind to the wsui that TigerTuningHUD builds in OnEnable.
        // When the panel is toggled off (SHIFT+TAB), the canvas GameObject is
        // SetActive(false) and GetComponentInChildren returns null — clear
        // the gate flag so scene input handlers stop being suppressed.
        if (m_Wsui == null || !m_Wsui.isActiveAndEnabled)
        {
            // includeInactive=true so we find the wsui even when canvas is
            // hidden — but only treat it as "live" when active+enabled.
            var found = GetComponentInChildren<DisplayXRWindowSpaceUI>(true);
            if (found != null && found.isActiveAndEnabled)
            {
                m_Wsui = found;
            }
            else
            {
                DisplayXRWindowSpaceUI.IsCursorOverInteractive = false;
                ReleaseIfDown();
                if (Time.time >= m_NextDiagLog)
                {
                    m_NextDiagLog = Time.time + 2f;
                    Debug.Log($"[TigerHudRouter] inactive — wsui found={(found != null)} " +
                              $"active={(found != null && found.isActiveAndEnabled)}");
                }
                return;
            }
        }
        if (m_Raycaster == null)
        {
            m_Raycaster = m_Wsui.GetComponent<GraphicRaycaster>();
            if (m_Raycaster == null)
                m_Raycaster = m_Wsui.gameObject.AddComponent<GraphicRaycaster>();
            // OverlayCamera flips Y to match the runtime's top-left texture
            // origin, which makes Dot(camera.fwd, canvas.fwd) = -1.
            // GraphicRaycaster.ignoreReversedGraphics would skip every hit.
            m_Raycaster.ignoreReversedGraphics = false;
        }
        if (m_Overlay == null)
        {
            // Lazy + cached. The overlay is a scene-level MonoBehaviour
            // installed by the test app's bootstrap; it owns the native
            // cursor poll. If absent (e.g. running outside a transparent
            // build), IsLeftDown() falls back to Mouse.current.
            m_Overlay = Object.FindAnyObjectByType<DisplayXRTransparentOverlay>();
        }

        if (!TryGetWindowMouseFractional(out Vector2 windowFrac))
        {
            DisplayXRWindowSpaceUI.IsCursorOverInteractive = false;
            ReleaseIfDown();
            return;
        }

        // Map cursor → canvas position. Once a drag is in flight (left button
        // still held + we have a press target), keep dispatching drag events
        // even if the cursor walks off the panel — Unity's Slider clamps the
        // canvas position to its own rect internally, so the slider value
        // tracks the horizontal cursor motion all the way to the screen edge.
        // Drag ends only on left-up (handled below) or on mouse-release-off-
        // window (handled by TryGetWindowMouseFractional returning false).
        bool dragging = m_LeftDown && m_PressTarget != null;
        bool insidePanel =
            windowFrac.x >= m_Wsui.positionX && windowFrac.x <= m_Wsui.positionX + m_Wsui.width &&
            windowFrac.y >= m_Wsui.positionY && windowFrac.y <= m_Wsui.positionY + m_Wsui.height;

        if (Time.time >= m_NextDiagLog)
        {
            m_NextDiagLog = Time.time + 2f;
            Debug.Log($"[TigerHudRouter] frac=({windowFrac.x:F3},{windowFrac.y:F3}) " +
                      $"panel=({m_Wsui.positionX:F2},{m_Wsui.positionY:F2})..({m_Wsui.positionX + m_Wsui.width:F2},{m_Wsui.positionY + m_Wsui.height:F2}) " +
                      $"inside={insidePanel} dragging={dragging} screen={Screen.width}x{Screen.height}");
        }

        if (!insidePanel && !dragging)
        {
            // Outside the panel and not mid-drag — let scene input through.
            DisplayXRWindowSpaceUI.IsCursorOverInteractive = false;
            m_WasInside = false;
            return;
        }

        // Compute canvas position from the (possibly out-of-panel) cursor.
        // Slider.OnDrag clamps internally, so unclamped values here are fine.
        float panelFracX = (windowFrac.x - m_Wsui.positionX) / m_Wsui.width;
        float panelFracY = (windowFrac.y - m_Wsui.positionY) / m_Wsui.height;
        var canvasPos = new Vector2(
            panelFracX * m_Wsui.resolution.x,
            panelFracY * m_Wsui.resolution.y);

        bool nowDown = IsLeftDown();
        bool buttonChanged = nowDown != m_LeftDown;
        // Skip the raycast + dispatch work on idle in-panel frames. On the
        // cloaked DComp overlay HWND the per-frame GraphicRaycaster.Raycast
        // is heavy enough to make the Windows message pump fall behind,
        // which Windows surfaces as the blue "busy" spinner. Only raycast
        // when something interesting is happening: drag in flight, just
        // entered the panel, button-state edge, or cursor actually moved.
        bool needRaycast =
            dragging
            || (!m_WasInside && insidePanel)
            || buttonChanged
            || (canvasPos - m_LastRaycastPos).sqrMagnitude >= 1f;

        DisplayXRWindowSpaceUI.IsCursorOverInteractive = true;

        if (needRaycast)
        {
            // Cache pressPosition across Reset() — Reset wipes it. Slider
            // doesn't read pressPosition directly during drag, but other
            // IDragHandlers might, so keep it stable through the drag.
            Vector2 stickyPressPos = m_LeftDown ? m_PointerData.pressPosition : canvasPos;

            m_PointerData.Reset();
            m_PointerData.position = canvasPos;
            m_PointerData.delta = canvasPos - m_LastCanvasPos;
            m_PointerData.scrollDelta = Vector2.zero;
            m_PointerData.button = PointerEventData.InputButton.Left;
            m_PointerData.pressPosition = stickyPressPos;
            // CRITICAL: re-apply the cached press raycast so
            // PointerEventData.pressEventCamera resolves to the wsui's
            // OverlayCamera throughout the drag. Reset() wipes
            // pointerPressRaycast to default, which makes pressEventCamera
            // return null. Slider.OnDrag then calls ScreenPointToLocalPoint
            // with a null camera, which projects through the world-space
            // canvas (parked at world Y≈100000) wrongly and clamps the
            // slider value to 0 or 1. Symptom: drag value oscillates
            // between cursor and slider min every frame.
            if (m_LeftDown && m_PressRaycast.module != null)
                m_PointerData.pointerPressRaycast = m_PressRaycast;

            m_Hits.Clear();
            m_Raycaster.Raycast(m_PointerData, m_Hits);
            var hovered = m_Hits.Count > 0 ? m_Hits[0].gameObject : null;
            m_PointerData.pointerCurrentRaycast = m_Hits.Count > 0 ? m_Hits[0] : default(RaycastResult);
            m_LastRaycastPos = canvasPos;

            if (!m_LeftDown && nowDown && hovered != null)
            {
                m_PointerData.pointerPressRaycast = m_PointerData.pointerCurrentRaycast;
                m_PressRaycast = m_PointerData.pointerCurrentRaycast;
                m_PressTarget = ExecuteEvents.ExecuteHierarchy(
                    hovered, m_PointerData, ExecuteEvents.pointerDownHandler);
                if (m_PressTarget == null)
                    m_PressTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hovered);
                ExecuteEvents.Execute(m_PressTarget, m_PointerData, ExecuteEvents.beginDragHandler);
                m_PointerData.pressPosition = canvasPos;
            }
            else if (m_LeftDown && nowDown && m_PressTarget != null)
            {
                ExecuteEvents.Execute(m_PressTarget, m_PointerData, ExecuteEvents.dragHandler);
            }
            else if (m_LeftDown && !nowDown)
            {
                ExecuteEvents.Execute(m_PressTarget, m_PointerData, ExecuteEvents.endDragHandler);
                ExecuteEvents.Execute(m_PressTarget, m_PointerData, ExecuteEvents.pointerUpHandler);
                var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(m_PressTarget);
                bool overPressHierarchy = hovered != null &&
                    ExecuteEvents.GetEventHandler<IPointerClickHandler>(hovered) == clickHandler;
                if (clickHandler != null && (overPressHierarchy || m_Hits.Count > 0))
                    ExecuteEvents.Execute(clickHandler, m_PointerData, ExecuteEvents.pointerClickHandler);
                m_PressTarget = null;
                m_PressRaycast = default(RaycastResult);
                m_LeftDown = false;
            }

            // Only commit the down-state when we actually believe the
            // release (above branch already cleared m_LeftDown on real
            // release). On true→false transitions waiting for hysteresis,
            // m_LeftDown stays true so subsequent frames keep dispatching
            // dragHandler against m_PressTarget.
            if (nowDown) m_LeftDown = true;
        }

        m_WasInside = insidePanel;
        m_LastCanvasPos = canvasPos;
    }

    private bool TryGetWindowMouseFractional(out Vector2 frac)
    {
        if (Screen.width <= 0 || Screen.height <= 0)
        {
            frac = Vector2.zero;
            return false;
        }
        // Prefer the overlay's synchronous native poll — Unity parks its
        // main HWND off-screen in transparent mode and the New InputSystem
        // independently polls system mouse on that cloaked HWND, producing
        // stale/wrong positions that get interleaved with the overlay's
        // QueueStateEvent injection. Result: Mouse.current.position
        // alternates between real cursor and stale values frame-to-frame,
        // which used to make slider drag oscillate between cursor and
        // slider-min. PointerPosition (window-client pixels, top-left) is
        // set straight from the overlay's native cursor poll each frame
        // and has no other source.
        if (m_Overlay != null)
        {
            Vector2 pp = m_Overlay.PointerPosition;
            if (pp.x < 0 || pp.x >= Screen.width || pp.y < 0 || pp.y >= Screen.height)
            {
                frac = Vector2.zero;
                return false;
            }
            // PointerPosition is already top-left origin — no Y inversion.
            frac = new Vector2(pp.x / Screen.width, pp.y / Screen.height);
            return true;
        }
        // Fallback for non-transparent builds: Mouse.current (bottom-left
        // origin, flip Y to top-left fractional).
        var mouse = Mouse.current;
        if (mouse == null)
        {
            frac = Vector2.zero;
            return false;
        }
        Vector2 pos = mouse.position.ReadValue();
        if (pos.x < 0 || pos.x >= Screen.width || pos.y < 0 || pos.y >= Screen.height)
        {
            frac = Vector2.zero;
            return false;
        }
        frac = new Vector2(pos.x / Screen.width, 1f - pos.y / Screen.height);
        return true;
    }

    private bool IsLeftDown()
    {
        // Prefer the overlay's same-frame native poll (set in its LateUpdate
        // from displayxr_get_overlay_pointer) — monotonic per button, no
        // queued transient false dips. Falls back to Mouse.current for
        // builds without the overlay (e.g. editor play mode).
        if (m_Overlay != null) return m_Overlay.IsLeftPressed;
        var mouse = Mouse.current;
        return mouse != null && mouse.leftButton.isPressed;
    }

    private void ReleaseIfDown()
    {
        if (m_LeftDown && m_PressTarget != null)
        {
            ExecuteEvents.Execute(m_PressTarget, m_PointerData, ExecuteEvents.endDragHandler);
            ExecuteEvents.Execute(m_PressTarget, m_PointerData, ExecuteEvents.pointerUpHandler);
            m_PressTarget = null;
        }
        m_PressRaycast = default(RaycastResult);
        m_LeftDown = false;
        m_WasInside = false;
    }
}
