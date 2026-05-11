// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: BSL-1.0
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

    private GameObject m_PressTarget;
    private PointerEventData m_PointerData;
    private Vector2 m_LastCanvasPos;
    private bool m_LeftDown;

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

    void Update()
    {
        // Lazy-bind to the wsui that TigerTuningHUD builds in OnEnable.
        // When the panel is toggled off (SHIFT+TAB), the canvas GameObject is
        // SetActive(false) and GetComponentInChildren returns null — clear
        // the gate flag so scene input handlers stop being suppressed.
        if (m_Wsui == null || !m_Wsui.isActiveAndEnabled)
        {
            m_Wsui = GetComponentInChildren<DisplayXRWindowSpaceUI>();
            if (m_Wsui == null || !m_Wsui.isActiveAndEnabled)
            {
                DisplayXRWindowSpaceUI.IsCursorOverInteractive = false;
                ReleaseIfDown();
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

        if (!TryGetWindowMouseFractional(out Vector2 windowFrac))
        {
            DisplayXRWindowSpaceUI.IsCursorOverInteractive = false;
            ReleaseIfDown();
            return;
        }

        // Hit-test wsui layer rect.
        if (windowFrac.x < m_Wsui.positionX || windowFrac.x > m_Wsui.positionX + m_Wsui.width ||
            windowFrac.y < m_Wsui.positionY || windowFrac.y > m_Wsui.positionY + m_Wsui.height)
        {
            DisplayXRWindowSpaceUI.IsCursorOverInteractive = m_PressTarget != null;
            ReleaseIfDown();
            return;
        }

        float panelFracX = (windowFrac.x - m_Wsui.positionX) / m_Wsui.width;
        float panelFracY = (windowFrac.y - m_Wsui.positionY) / m_Wsui.height;
        var canvasPos = new Vector2(
            panelFracX * m_Wsui.resolution.x,
            panelFracY * m_Wsui.resolution.y);

        m_PointerData.Reset();
        m_PointerData.position = canvasPos;
        m_PointerData.delta = canvasPos - m_LastCanvasPos;
        m_PointerData.scrollDelta = Vector2.zero;
        m_PointerData.button = PointerEventData.InputButton.Left;
        m_PointerData.pressPosition = m_LeftDown ? m_PointerData.pressPosition : canvasPos;

        var hits = new List<RaycastResult>();
        m_Raycaster.Raycast(m_PointerData, hits);
        var hovered = hits.Count > 0 ? hits[0].gameObject : null;
        DisplayXRWindowSpaceUI.IsCursorOverInteractive = true;
        m_PointerData.pointerCurrentRaycast = hits.Count > 0 ? hits[0] : default(RaycastResult);

        bool nowDown = IsLeftDown();
        if (!m_LeftDown && nowDown && hovered != null)
        {
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
            var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(m_PressTarget);
            bool overPressHierarchy = hovered != null &&
                ExecuteEvents.GetEventHandler<IPointerClickHandler>(hovered) == clickHandler;
            if (clickHandler != null && (overPressHierarchy || hits.Count > 0))
                ExecuteEvents.Execute(clickHandler, m_PointerData, ExecuteEvents.pointerClickHandler);
            m_PressTarget = null;
        }

        m_LeftDown = nowDown;
        m_LastCanvasPos = canvasPos;
    }

    private bool TryGetWindowMouseFractional(out Vector2 frac)
    {
        // Built app: read via the new Input System (project uses
        // activeInputHandlers=1). DisplayXRTransparentOverlay injects
        // synthesized mouse state into Mouse.current, so this works for
        // cloaked-Unity-window apps that route input through the overlay.
        var mouse = Mouse.current;
        if (mouse == null || Screen.width <= 0 || Screen.height <= 0)
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
        m_LeftDown = false;
    }
}
