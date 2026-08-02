// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: Apache-2.0

// Optional head-tracked billboard for the tiger (yaw-only) + A/B probe for the
// ways an app can ask "where is the viewer?" (plugin issue #236).
//
// The yaw math mirrors the native reference implementation in
// displayxr-demo-avatar (windows/main.cpp, "Face-the-viewer billboard") 1:1,
// because that one is viewer-confirmed on hardware:
//
//     targetYaw = FACE_YAW_SIGN * atan2(hx, |hz|)
//
// with (hx, hz) = the head centroid in RAW DISPLAY SPACE — metres, origin at the
// physical panel centre, viewer at +Z — from displayxr_get_eye_positions. That is
// deliberately NOT the world-space render-ready eyes: physical space is
// independent of where the rig, camera and model sit in the scene, and immune to
// the display rig's scale-as-zoom (which inflates world-space distances, e.g. a
// 0.06 m IPD reads as ~1.2 world units here). Two earlier attempts to derive the
// angle in world space failed exactly there — one silently rotated the wrong
// object, the next found its reference vector degenerate because the tiger's root
// sits at the camera's x/z.
//
// Smoothing is the reference's time-based exponential (tau), and the heading only
// chases while eye tracking is LOCKED — warmup positions jitter the heading.
//
// The once-per-second log still prints the world-space provider API
// (DisplayXRProvider.TryGetViewerEyes, new in v2.9.1) next to the raw physical
// eyes and to Camera.GetStereoViewMatrix, so the sources stay comparable — that
// A/B is the point of the probe for issue #236.
//
// Auto-installs via RuntimeInitializeOnLoadMethod (the KooimaProbe pattern), so
// it needs no scene edit. Off by default — press F.
//
// Not ported from the reference: the zone-canvas rebase (it offsets hx by the
// window's centre on the panel for off-centre windows). Without it the heading is
// referenced to the PANEL centre rather than the window centre — a constant bias
// when the window is off-centre, not a tracking failure.

using System;
using DisplayXR;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class TigerFaceViewer : MonoBehaviour
{
    [Tooltip("Toggles head-tracked billboarding on/off. On by default, so F " +
             "turns it OFF and restores the tiger's authored rotation.")]
    public Key toggleKey = Key.F;

    [Tooltip("Billboard from startup. The tiger's DragRotateCube is wired by " +
             "TransparentAutoSetup at AfterSceneLoad, same phase as this " +
             "component, so enabling retries until the target exists.")]
    public bool startEnabled = true;

    [Tooltip("Yaw direction. -1 matches the viewer-confirmed native reference " +
             "(displayxr-demo-avatar FACE_YAW_SIGN); flip to +1 if the tiger " +
             "turns away from you instead of toward you.")]
    public float yawSign = -1f;

    [Tooltip("Multiplies the viewer's off-axis angle. 1 = physically faithful " +
             "(what the native reference does); >1 exaggerates for a demo.")]
    public float gain = 1f;

    [Tooltip("Smoothing time constant in seconds (settle ~= 3x tau). Matches the " +
             "reference's 0.04. Time-based, so it is frame-rate independent.")]
    public float tau = 0.04f;

    [Tooltip("Clamp on the applied yaw (degrees).")]
    public float maxYawDegrees = 60f;

    [Tooltip("Seconds between A/B probe log lines. 0 = don't log.")]
    public float logInterval = 1f;

    private Transform m_Tiger;
    private Quaternion m_BaseRotation;
    private bool m_Enabled;
    private float m_Timer;
    private float m_Yaw;             // smoothed, applied
    private float m_TargetYaw;       // pre-smoothing, for the log
    private bool m_UserTurnedOff;    // an explicit F press wins over startEnabled
    private float m_RetryTimer;

    private static bool s_RawMissing;
    private static bool s_CanvasMissing;
    private static float s_CanvasDx;   // last window-centre rebase, metres (log)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall()
    {
        if (FindAnyObjectByType<TigerFaceViewer>() != null) return;
        var go = new GameObject("TigerFaceViewer");
        DontDestroyOnLoad(go);
        go.AddComponent<TigerFaceViewer>();
        Debug.Log("[TigerFaceViewer] installed — head-tracked billboard ON by default (press F to turn it off).");
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && toggleKey != Key.None && kb[toggleKey].wasPressedThisFrame)
        {
            m_UserTurnedOff = m_Enabled;   // F while on = the user wants it off
            Toggle();
        }

        // Auto-enable at startup. TransparentAutoSetup adds the tiger's
        // DragRotateCube in the same AfterSceneLoad phase we install in, and the
        // order between them is undefined — so retry rather than assume the
        // target exists on frame one. Throttled: FindObjectsByType is not free.
        if (startEnabled && !m_Enabled && !m_UserTurnedOff)
        {
            m_RetryTimer -= Time.deltaTime;
            if (m_RetryTimer <= 0f)
            {
                m_RetryTimer = 0.25f;
                if (FindTiger() != null) Toggle();
            }
        }

        if (logInterval > 0f && m_Enabled)
        {
            m_Timer += Time.deltaTime;
            if (m_Timer >= logInterval) { m_Timer = 0f; LogSources(); }
        }
    }

    void Toggle()
    {
        if (m_Enabled)
        {
            m_Enabled = false;
            if (m_Tiger != null) m_Tiger.rotation = m_BaseRotation;
            Debug.Log("[TigerFaceViewer] OFF (rotation restored)");
            return;
        }

        m_Tiger = FindTiger();
        if (m_Tiger == null)
        {
            Debug.LogWarning("[TigerFaceViewer] no DragRotateCube target found — staying off.");
            return;
        }

        m_BaseRotation = m_Tiger.rotation;
        m_Yaw = 0f;
        m_Enabled = true;
        m_Timer = logInterval; // log immediately

        var rend = m_Tiger.GetComponentInChildren<Renderer>(true);
        bool raw = TryGetRawHead(out float hx, out float hy, out float hz, out bool tracked);
        Debug.Log($"[TigerFaceViewer] ON  target={m_Tiger.name} " +
                  $"renderer={(rend != null ? rend.GetType().Name + "/" + rend.name : "<none>")} " +
                  $"pos={Fmt(m_Tiger.position)} yawSign={yawSign} gain={gain:F1} " +
                  $"| rawHead ok={raw} ({hx:F3},{hy:F3},{hz:F3}) tracked={tracked}");
    }

    void LateUpdate()
    {
        // LateUpdate so we win over the Animator (which runs earlier in the frame)
        // and over DragRotateCube's drag rotation while this mode is on.
        if (!m_Enabled || m_Tiger == null) return;
        if (!TryGetRawHead(out float hx, out float _, out float hz, out bool tracked)) return;

        // Reference math, 1:1: yaw from the head's lateral offset over its
        // distance to the panel. |hz| guarded so a viewer in the panel plane
        // can't blow the angle up.
        float hzAbs = Mathf.Max(Mathf.Abs(hz), 1e-3f);
        m_TargetYaw = Mathf.Clamp(yawSign * Mathf.Atan2(hx, hzAbs) * Mathf.Rad2Deg * gain,
                                  -maxYawDegrees, maxYawDegrees);

        // Time-based exponential smoothing (frame-rate independent). Only chase
        // while tracking is locked — warmup positions jitter the heading.
        if (tracked)
        {
            float a = Mathf.Clamp01(1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(tau, 1e-4f)));
            m_Yaw = Mathf.LerpAngle(m_Yaw, m_TargetYaw, a);
        }

        m_Tiger.rotation = Quaternion.AngleAxis(m_Yaw, Vector3.up) * m_BaseRotation;
    }

    // Print every viewer-position source together: the raw physical eyes that
    // drive the billboard, the world-space provider API (new in v2.9.1), and the
    // Camera.GetStereoViewMatrix readback that issue #236 was filed about.
    void LogSources()
    {
        LogZoneGeometry();
        bool raw = TryGetRawHead(out float hx, out float hy, out float hz, out bool tracked);

        bool okProv = DisplayXRProvider.TryGetViewerEyes(out Vector3 pl, out Vector3 pr);
        Vector3 pHead = okProv ? (pl + pr) * 0.5f : Vector3.zero;

        var cam = ActiveCamera();
        bool stereo = cam != null && cam.stereoEnabled;
        Vector3 sl = Vector3.zero, sr = Vector3.zero;
        if (stereo)
        {
            sl = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
            sr = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);
        }
        bool camEyesEqual = stereo && (sl - sr).sqrMagnitude < 1e-10f;

        Debug.Log($"[TigerFaceViewer] rawHead ok={raw} ({hx:F3},{hy:F3},{hz:F3})m tracked={tracked} " +
                  $"canvasDx={s_CanvasDx:F3}m " +
                  $"-> targetYaw={m_TargetYaw:F1} yaw={m_Yaw:F1}deg on {(m_Tiger != null ? m_Tiger.name : "<none>")} " +
                  $"| provider(world) ok={okProv} head={Fmt(pHead)} ipd={(pr - pl).magnitude:F4} " +
                  $"| GetStereoViewMatrix stereo={stereo} eyesEqual={camEyesEqual} " +
                  $"| eyeTracked={DisplayXRProvider.IsEyeTracked}");
    }

    // Head centroid in RAW DISPLAY SPACE: metres, origin at the physical panel
    // centre, +X right, +Y up, viewer at +Z. Same channel the Scene-view eye
    // gizmo uses (DisplayXRGizmoHelpers.TryGetLiveRawEyes) and the same one the
    // native reference billboards from.
    //
    // hx is then REBASED from the panel centre to the ZONE canvas centre — the
    // reference's zone-canvas rebase. Without it the heading is referenced to the
    // panel centre, so dragging the avatar across the screen does not change the
    // yaw at all: it keeps facing wherever it would if the zone were centred,
    // instead of turning to keep looking at you.
    static bool TryGetRawHead(out float hx, out float hy, out float hz, out bool tracked)
    {
        hx = 0f; hy = 0f; hz = 0f; tracked = false;
        if (s_RawMissing) return false;
        try
        {
            DisplayXRNative.displayxr_get_eye_positions(
                out float lx, out float ly, out float lz,
                out float rx, out float ry, out float rz,
                out int isTracked);
            hx = (lx + rx) * 0.5f;
            hy = (ly + ry) * 0.5f;
            hz = (lz + rz) * 0.5f;
            tracked = isTracked != 0;
            // All-zero = the runtime hasn't filled the raw channel yet.
            if (hx == 0f && hy == 0f && hz == 0f) return false;
        }
        catch (DllNotFoundException) { s_RawMissing = true; return false; }
        catch (EntryPointNotFoundException) { s_RawMissing = true; return false; }

        if (TryGetCanvasOffsetX(out s_CanvasDx)) hx -= s_CanvasDx;
        return true;
    }

    // Signed distance in METRES from the panel centre to the ZONE CANVAS centre,
    // +X right, published every frame so it follows the avatar as it is dragged.
    //
    //     pxSizeX    = panel width (m) / panel width (px)
    //     canvasCxPx = canvasRect.x + canvasRect.w / 2
    //     dx         = (canvasCxPx - panelWidthPx / 2) * pxSizeX
    //
    // Centred -> dx = 0, a no-op. Dragged right -> dx > 0 -> the viewer sits
    // further LEFT relative to the zone, and the yaw follows.
    //
    // displayxr_get_kooima_canvas IS the zone canvas, not merely the window rect:
    // the provider chains XrDisplayZoneDXR onto the PRIMARY xrLocateViews in front
    // of the rig ("the rect IS the canvas", displayxr_provider_session.cpp), and
    // the XrViewDisplayRawDXR raw channel that fills canvasRectPx rides on that
    // same zone-scoped locate. So this is the exact rect the native reference gets
    // from its separate zone-scoped locate (zoneRaw.canvasRectPx). With no zone
    // active it degrades to the full window, which is the correct reference then.
    // LogZoneGeometry() prints canvas vs zone rect so this stays checkable.
    static bool TryGetCanvasOffsetX(out float dxMeters)
    {
        dxMeters = 0f;
        if (s_CanvasMissing) return false;
        try
        {
            if (DisplayXRNative.displayxr_get_kooima_canvas(
                    out int rectX, out int _, out int rectW, out int _,
                    out float sizeWMeters, out float _) == 0)
                return false;
            if (rectW <= 0) return false;

            // Panel geometry for the centre reference. Fall back to the canvas's
            // own metres-per-pixel if display info isn't valid yet.
            if (!DisplayXRProvider.TryGetDisplayInfo(out var info) ||
                info.pixelWidth == 0 || info.widthM <= 0f)
                return false;

            float pxSizeX = info.widthM > 0f && info.pixelWidth > 0
                ? info.widthM / info.pixelWidth
                : sizeWMeters / rectW;
            float canvasCxPx = rectX + rectW * 0.5f;
            dxMeters = (canvasCxPx - info.pixelWidth * 0.5f) * pxSizeX;
            return true;
        }
        catch (DllNotFoundException) { s_CanvasMissing = true; return false; }
        catch (EntryPointNotFoundException) { s_CanvasMissing = true; return false; }
    }

    // Prove (rather than assume) that the rect driving the rebase is the ZONE
    // canvas: prints the panel-space canvas the provider publishes next to the
    // app-authored zone rects in window-client pixels. If a layout ever makes the
    // canvas width stop matching the primary zone's width, the rebase is keying
    // off the wrong rect and this line is where it shows up.
    static void LogZoneGeometry()
    {
        try
        {
            int ok = DisplayXRNative.displayxr_get_kooima_canvas(
                out int cx, out int cy, out int cw, out int ch,
                out float cwM, out float chM);
            string zones = "";
            uint n = DisplayXRProviderNative.dxr_prov_get_zone_count();
            for (uint z = 0; z < n && z < 4; z++)
                if (DisplayXRProviderNative.dxr_prov_get_zone_rect_px(z, out int zx, out int zy,
                                                                      out int zw, out int zh) != 0)
                    zones += $" zone{z}=({zx},{zy} {zw}x{zh})px";
            string panel = DisplayXRProvider.TryGetDisplayInfo(out var info)
                ? $"panel={info.pixelWidth}x{info.pixelHeight}px {info.widthM:F4}x{info.heightM:F4}m"
                : "panel=<invalid>";
            Debug.Log($"[TigerFaceViewer] geometry: canvas ok={ok} ({cx},{cy} {cw}x{ch})panelPx " +
                      $"{cwM:F4}x{chM:F4}m | {panel} | zoneCount={n}{zones}");
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    static string Fmt(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

    static Camera ActiveCamera()
    {
        var cam = DisplayXRRigManager.ActiveCamera;
        return cam != null ? cam : Camera.main;
    }

    // DragRotateCube owns each target's drag rotation, so it is the handle we
    // want — but TransparentAutoSetup wires one onto EVERY target root, which is
    // the tiger AND the legacy fallback cube. FindAnyObjectByType returns an
    // arbitrary one, and picking the cube silently rotates an invisible fixture
    // while the tiger sits still (looks exactly like "head tracking is dead").
    // Prefer the skinned mesh — that's the tiger, and it needs no name match.
    static Transform FindTiger()
    {
        var drags = FindObjectsByType<DragRotateCube>();
        if (drags == null || drags.Length == 0) return null;
        foreach (var d in drags)
            if (d.target is SkinnedMeshRenderer) return d.transform;
        // No skinned mesh (cube-only fallback scene): take the first target that
        // is actually being drawn.
        foreach (var d in drags)
            if (d.target != null && d.target.enabled && d.target.gameObject.activeInHierarchy)
                return d.transform;
        return drags[0].transform;
    }
}
