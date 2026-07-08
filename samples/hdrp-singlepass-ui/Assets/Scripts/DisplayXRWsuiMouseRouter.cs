// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: Apache-2.0
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
using UnityEngine.InputSystem;
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
            // Bare EventSystem with NO input module — projects on the new
            // Input System Package would throw InvalidOperationException
            // every frame from StandaloneInputModule.UpdateModule trying to
            // read legacy UnityEngine.Input. The router synthesizes events
            // via ExecuteEvents directly so it doesn't need a working input
            // module; it just needs EventSystem.current to be non-null for
            // PointerEventData construction.
            var es = new GameObject("DisplayXR_EventSystem", typeof(EventSystem));
            m_EventSystem = es.GetComponent<EventSystem>();
        }
        else
        {
            // If a pre-existing EventSystem has a StandaloneInputModule and
            // the project uses the new Input System Package, that module
            // throws every frame and may corrupt EventSystem state for
            // GraphicRaycaster. Strip it — we don't need an input module.
            var legacy = m_EventSystem.GetComponent<StandaloneInputModule>();
            if (legacy != null)
            {
                Debug.Log("[wsui-router] Removing StandaloneInputModule from existing EventSystem to silence Input-System exceptions.");
                if (Application.isPlaying) Destroy(legacy);
                else DestroyImmediate(legacy);
            }
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
            // The wsui's OverlayCamera has up=Vector3.down + looks toward -Z to
            // Y-flip the rendered RT, which makes Dot(camera.fwd, canvas.fwd)
            // = -1. GraphicRaycaster.ignoreReversedGraphics treats that as
            // "back of graphic facing camera" and skips every hit.
            m_Raycaster.ignoreReversedGraphics = false;
        }

        // ---- 1. Read cursor in fractional window-coords ----
        if (!TryGetWindowMouseFractional(out Vector2 windowFrac))
        {
            DisplayXR.DisplayXRWindowSpaceUI.IsCursorOverInteractive = false;
            ReleaseIfDown();
            return;
        }

        // ---- 2. Hit-test the wsui layer rect ----
        // wsui.position[XY] is also fractional, top-left origin → straightforward rect test.
        if (windowFrac.x < m_Wsui.positionX || windowFrac.x > m_Wsui.positionX + m_Wsui.width ||
            windowFrac.y < m_Wsui.positionY || windowFrac.y > m_Wsui.positionY + m_Wsui.height)
        {
            DisplayXR.DisplayXRWindowSpaceUI.IsCursorOverInteractive = m_PressTarget != null;
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
        // Cursor is over the wsui rect; tell the plugin we own input so the
        // scene input controller pauses cube rotation. Stays true even if we
        // happen to hover empty space inside the panel — that matches what
        // the user expects (cursor is "in the UI region").
        DisplayXR.DisplayXRWindowSpaceUI.IsCursorOverInteractive = true;
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
            // Fire click if the press target is itself a click handler AND
            // either (a) the cursor is still over a child of the press target
            // or (b) the cursor is still inside the wsui rect (lenient — a 1px
            // jitter at release shouldn't drop a button click). Stricter
            // Unity-style rule (require currentRaycast to resolve to the press
            // target) is too brittle for the runtime preview where alignment
            // is approximate.
            var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(m_PressTarget);
            bool overPressHierarchy = hovered != null &&
                ExecuteEvents.GetEventHandler<IPointerClickHandler>(hovered) == clickHandler;
            if (clickHandler != null && (overPressHierarchy || hits.Count > 0))
            {
                ExecuteEvents.Execute(clickHandler, m_PointerData,
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
        // Provider editor Play Mode (#173): the woven output is a dedicated window,
        // not Unity's Game View, so Unity's Input System doesn't track the cursor
        // over it. Read the cursor from the overlay window (client px, top-left) and
        // normalize by the overlay's client size to get fractional window coords.
        try
        {
            DisplayXRNative.displayxr_get_overlay_pointer(out int cx, out int cy, out int _);
            DisplayXRNative.displayxr_get_overlay_size(out int ow, out int oh);
            if (ow > 0 && oh > 0 && cx >= 0 && cy >= 0 && cx < ow && cy < oh)
            {
                frac = new Vector2((float)cx / ow, (float)cy / oh);
                return true;
            }
        }
        catch (System.EntryPointNotFoundException) { /* old binary */ }
        frac = Vector2.zero;
        return false;
#else
        // Built apps: the runtime composites into Unity's main window.
        // Read cursor via the new Input System (project's activeInputHandler
        // is 1 = Input System Package only, so legacy UnityEngine.Input
        // returns zeros). Mouse.current.position is bottom-left → flip to
        // top-left for fractional.
        var mouse = Mouse.current;
        if (mouse == null || Screen.width <= 0 || Screen.height <= 0)
        {
            frac = Vector2.zero;
            return false;
        }
        var pos = mouse.position.ReadValue();
        float mx = pos.x;
        float my = pos.y;
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
        // Provider editor Play Mode (#173): button state comes from the overlay
        // window's native WndProc tracker (Unity's Input System doesn't see clicks
        // over the separate weave window).
        try
        {
            DisplayXRNative.displayxr_get_overlay_pointer(out int _, out int _, out int buttons);
            return (buttons & 0x1) != 0;
        }
        catch (System.EntryPointNotFoundException) { return false; }
#else
        var mouse = Mouse.current;
        return mouse != null && mouse.leftButton.isPressed;
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
