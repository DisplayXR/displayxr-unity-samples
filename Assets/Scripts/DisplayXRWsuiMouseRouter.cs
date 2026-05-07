// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: BSL-1.0
//
// Sample input router for DisplayXRWindowSpaceUI — NOT part of the plugin.
//
// `XrCompositionLayerWindowSpaceEXT` submits pixels and lets the runtime
// composite them; it doesn't carry input. Unity's GraphicRaycaster works on
// screen-space mouse coords against canvases that live in the screen's
// coordinate space — but our wsui canvas is a private WorldSpace canvas
// parked at world (0, 100000, 0) on a hidden layer, so EventSystem can't
// see clicks on it.
//
// This script bridges the two:
//   1. Reads the cursor position from the runtime preview window (editor)
//      or from Unity's Input.mousePosition (built apps), in fractional
//      window-coords.
//   2. Hit-tests against the wsui layer's fractional rect.
//   3. Maps the hit point to canvas-pixel coords inside the OverlayTexture.
//   4. Synthesizes PointerEventData with that canvas-pixel position and
//      dispatches click / drag events to UI Selectables (sliders, buttons,
//      toggles, etc.) on the wsui's Canvas.
//
// Drop it on the same GameObject as DisplayXRTuningUI. Fork freely if your
// input model isn't mouse-based.

using System.Collections.Generic;
using DisplayXR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(DisplayXRTuningUI))]
public class DisplayXRWsuiMouseRouter : MonoBehaviour
{
    private DisplayXRTuningUI m_Tuning;
    private DisplayXRWindowSpaceUI m_Wsui;
    private GraphicRaycaster m_Raycaster;
    private EventSystem m_EventSystem;

    private GameObject m_PressTarget;
    private PointerEventData m_PointerData;
    private Vector2 m_LastCanvasPos;
    private bool m_LeftDown;

    void OnEnable()
    {
        m_Tuning = GetComponent<DisplayXRTuningUI>();
        m_EventSystem = EventSystem.current;
        if (m_EventSystem == null)
        {
            // Most scenes already have one; create a minimal fallback so the
            // sample works in stripped-down test scenes.
            var es = new GameObject("DisplayXR_EventSystem", typeof(EventSystem),
                typeof(StandaloneInputModule));
            m_EventSystem = es.GetComponent<EventSystem>();
        }
        m_PointerData = new PointerEventData(m_EventSystem);
    }

    void Update()
    {
        // Lazy-bind to the wsui that DisplayXRTuningUI builds in OnEnable.
        if (m_Wsui == null)
        {
            m_Wsui = GetComponentInChildren<DisplayXRWindowSpaceUI>();
            if (m_Wsui == null) return;
        }
        if (m_Raycaster == null)
        {
            m_Raycaster = m_Wsui.GetComponent<GraphicRaycaster>();
            if (m_Raycaster == null)
                m_Raycaster = m_Wsui.gameObject.AddComponent<GraphicRaycaster>();
        }

        // ---- 1. Read cursor in fractional window-coords ----
        if (!TryGetWindowMouseFractional(out Vector2 windowFrac))
        {
            ReleaseIfDown();
            return;
        }

        // ---- 2. Hit-test the wsui layer rect ----
        // wsui.position[XY] is also fractional, top-left origin → straightforward rect test.
        if (windowFrac.x < m_Wsui.positionX || windowFrac.x > m_Wsui.positionX + m_Wsui.width ||
            windowFrac.y < m_Wsui.positionY || windowFrac.y > m_Wsui.positionY + m_Wsui.height)
        {
            ReleaseIfDown();
            return;
        }

        // ---- 3. Map to RT-pixel coords (= screen coords for OverlayCamera) ----
        float panelFracX = (windowFrac.x - m_Wsui.positionX) / m_Wsui.width;
        float panelFracY = (windowFrac.y - m_Wsui.positionY) / m_Wsui.height;
        // The wsui's OverlayCamera is rotated with up = Vector3.down so the
        // RT comes out Y-flipped (matching the runtime's top-left texture
        // origin). When that camera serves as Canvas.worldCamera,
        // ScreenPointToRay's Y is inverted by the same flip — so a panelFracY
        // of 0 (top of layer) needs screenY = 0, and panelFracY of 1 (bottom)
        // needs screenY = resolution.y. No additional flip in the router.
        var canvasPos = new Vector2(
            panelFracX * m_Wsui.resolution.x,
            panelFracY * m_Wsui.resolution.y);

        // ---- 4. Synthesize PointerEventData and dispatch ----
        m_PointerData.Reset();
        m_PointerData.position = canvasPos;
        m_PointerData.delta = canvasPos - m_LastCanvasPos;
        m_PointerData.scrollDelta = Vector2.zero;
        m_PointerData.button = PointerEventData.InputButton.Left;
        m_PointerData.pressPosition = m_LeftDown ? m_PointerData.pressPosition : canvasPos;

        var hits = new List<RaycastResult>();
        m_Raycaster.Raycast(m_PointerData, hits);
        var hovered = hits.Count > 0 ? hits[0].gameObject : null;
        // PointerEventData.pressEventCamera / enterEventCamera are read-only
        // in Unity 6's UGUI — they're derived from
        // pointerCurrentRaycast.module / pointerPressRaycast.module. Wire the
        // raycast results so consumers (Slider.OnDrag's
        // ScreenPointToLocalPointInRectangle, etc.) see OverlayCamera as the
        // event camera and project canvasPos against the canvas correctly.
        m_PointerData.pointerCurrentRaycast = hits.Count > 0
            ? hits[0]
            : default(RaycastResult);

        bool nowDown = IsLeftDown();
        if (!m_LeftDown && nowDown && hovered != null)
        {
            // Snapshot the press raycast so PointerEventData.pressEventCamera
            // resolves to OverlayCamera throughout the drag (it reads
            // pointerPressRaycast.module).
            m_PointerData.pointerPressRaycast = m_PointerData.pointerCurrentRaycast;
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
            if (m_PressTarget != null && hovered != null &&
                ExecuteEvents.GetEventHandler<IPointerClickHandler>(hovered) == m_PressTarget)
            {
                ExecuteEvents.Execute(m_PressTarget, m_PointerData,
                    ExecuteEvents.pointerClickHandler);
            }
            m_PressTarget = null;
        }

        m_LeftDown = nowDown;
        m_LastCanvasPos = canvasPos;
    }

    private bool TryGetWindowMouseFractional(out Vector2 frac)
    {
#if UNITY_EDITOR
        // Editor preview: the runtime owns its own NSWindow / HWND. Read the
        // cursor from there.
        if (DisplayXRPreviewInput.TryGetPreviewMousePosition(out float fx, out float fy))
        {
            frac = new Vector2(fx, fy);
            return true;
        }
        frac = Vector2.zero;
        return false;
#else
        // Built apps: the runtime composites into Unity's main window.
        // Input.mousePosition is bottom-left → flip to top-left for fractional.
        if (Screen.width <= 0 || Screen.height <= 0)
        {
            frac = Vector2.zero;
            return false;
        }
        float mx = Input.mousePosition.x;
        float my = Input.mousePosition.y;
        if (mx < 0 || mx >= Screen.width || my < 0 || my >= Screen.height)
        {
            frac = Vector2.zero;
            return false;
        }
        frac = new Vector2(mx / Screen.width, 1f - my / Screen.height);
        return true;
#endif
    }

    private bool IsLeftDown()
    {
#if UNITY_EDITOR
        DisplayXRPreviewInput.GetPreviewMouseState(out int buttons, out int _);
        return (buttons & 0x1) != 0;
#else
        return Input.GetMouseButton(0);
#endif
    }

    private void ReleaseIfDown()
    {
        if (m_LeftDown && m_PressTarget != null)
        {
            ExecuteEvents.Execute(m_PressTarget, m_PointerData, ExecuteEvents.endDragHandler);
            ExecuteEvents.Execute(m_PressTarget, m_PointerData, ExecuteEvents.pointerUpHandler);
            m_PressTarget = null;
        }
        m_LeftDown = false;
    }
}
