// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: Apache-2.0

// Optional head-tracked billboard for the tiger + A/B probe for the two ways an
// app can ask "where is the viewer?" (plugin issue #236).
//
// Why this exists: the plugin's Face Viewer (Billboard) sample shipped in v2.8.3
// reading the head from Camera.GetStereoViewMatrix(Left/Right). That cache is
// only written by Camera.SetStereoViewMatrix(), which the plugin does NOT call in
// provider mode — the per-eye poses reach Unity through the native frame desc
// (deviceAnchorToEyePose) and are consumed inside Unity's render loop, never
// round-tripped back into the C# camera. So both eyes read back the same (mono)
// matrix and the billboard never moves. The fix is
// DisplayXRProvider.TryGetViewerHead(), which reads the provider's own
// render-ready per-eye positions.
//
// desktop-avatar is the sample that can actually prove this: it runs on a real
// tracking display with a transparent overlay, so a head-coupled turn is visible
// at arm's length. Press F to toggle billboarding; the once-per-second log prints
// BOTH sources side by side so the broken-vs-fixed delta is captured in the log
// even without hardware eyes on the tiger.
//
// Turning is expressed as a *delta from the tiger's authored rotation*, derived
// from the yaw between (tiger -> rig camera) and (tiger -> viewer head), so it
// makes no assumption about which local axis is the tiger's face. `gain`
// exaggerates the turn — real head travel in front of a desktop panel subtends a
// small angle, and 2-3x makes the effect unmistakable in a demo.
//
// Auto-installs via RuntimeInitializeOnLoadMethod (the KooimaProbe pattern), so
// it needs no scene edit. Off by default — press F.

using DisplayXR;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class TigerFaceViewer : MonoBehaviour
{
    [Tooltip("Toggles head-tracked billboarding on/off.")]
    public Key toggleKey = Key.F;

    [Tooltip("Multiplies the viewer's off-axis angle. 1 = physically faithful; " +
             "2-3 exaggerates the turn so it reads clearly in a demo.")]
    public float gain = 2.5f;

    [Tooltip("Turn rate in degrees per second. 0 = snap instantly.")]
    public float turnSpeed = 180f;

    [Tooltip("Clamp on the applied yaw delta (degrees), so a bad head reading " +
             "can't spin the tiger backwards.")]
    public float maxYawDegrees = 60f;

    [Tooltip("Seconds between A/B probe log lines. 0 = don't log.")]
    public float logInterval = 1f;

    private Transform m_Tiger;
    private Quaternion m_BaseRotation;
    private Vector3 m_RefToCam;          // tiger -> camera at enable time, yaw plane
    private bool m_Enabled;
    private float m_Timer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall()
    {
        if (FindAnyObjectByType<TigerFaceViewer>() != null) return;
        var go = new GameObject("TigerFaceViewer");
        DontDestroyOnLoad(go);
        go.AddComponent<TigerFaceViewer>();
        Debug.Log("[TigerFaceViewer] installed (press F to toggle head-tracked billboard).");
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && toggleKey != Key.None && kb[toggleKey].wasPressedThisFrame)
            Toggle();

        if (logInterval > 0f && m_Enabled)
        {
            m_Timer += Time.deltaTime;
            if (m_Timer >= logInterval) { m_Timer = 0f; LogBothSources(); }
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
        var cam = ActiveCamera();
        if (m_Tiger == null || cam == null)
        {
            Debug.LogWarning("[TigerFaceViewer] no tiger (DragRotateCube) or no active " +
                             "rig camera found — staying off.");
            return;
        }

        m_BaseRotation = m_Tiger.rotation;
        m_RefToCam = YawPlane(cam.transform.position - m_Tiger.position);
        if (m_RefToCam.sqrMagnitude < 1e-6f)
        {
            Debug.LogWarning("[TigerFaceViewer] tiger sits on the camera axis — " +
                             "no yaw reference, staying off.");
            return;
        }
        m_Enabled = true;
        m_Timer = logInterval; // log immediately
        Debug.Log($"[TigerFaceViewer] ON  gain={gain:F1} tiger={m_Tiger.name} " +
                  $"cam={cam.name} eyeTracked={DisplayXRProvider.IsEyeTracked}");
    }

    void LateUpdate()
    {
        // LateUpdate so we win over the Animator (which runs earlier in the frame)
        // and over DragRotateCube's drag rotation while this mode is on.
        if (!m_Enabled || m_Tiger == null) return;
        if (!DisplayXRProvider.TryGetViewerHead(out Vector3 head)) return;

        Vector3 toHead = YawPlane(head - m_Tiger.position);
        if (toHead.sqrMagnitude < 1e-6f) return;

        float yaw = Vector3.SignedAngle(m_RefToCam, toHead, Vector3.up) * gain;
        yaw = Mathf.Clamp(yaw, -maxYawDegrees, maxYawDegrees);

        Quaternion target = Quaternion.AngleAxis(yaw, Vector3.up) * m_BaseRotation;
        m_Tiger.rotation = turnSpeed <= 0f
            ? target
            : Quaternion.RotateTowards(m_Tiger.rotation, target, turnSpeed * Time.deltaTime);
    }

    // Print the fixed source and the broken one together, so a single log line
    // shows why #236 happened and that the fix is live.
    void LogBothSources()
    {
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

        Debug.Log($"[TigerFaceViewer] provider ok={okProv} " +
                  $"L={Fmt(pl)} R={Fmt(pr)} head={Fmt(pHead)} ipd={(pr - pl).magnitude:F4} " +
                  $"| Camera.GetStereoViewMatrix stereo={stereo} " +
                  $"L={Fmt(sl)} R={Fmt(sr)} eyesEqual={camEyesEqual} " +
                  $"| eyeTracked={DisplayXRProvider.IsEyeTracked}");
    }

    static string Fmt(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

    static Vector3 YawPlane(Vector3 v) { v.y = 0f; return v; }

    static Camera ActiveCamera()
    {
        var cam = DisplayXRRigManager.ActiveCamera;
        return cam != null ? cam : Camera.main;
    }

    // The tiger is whatever DragRotateCube is attached to — that component owns
    // the tiger's rotation for mouse drag, so it is the authoritative handle
    // (avoids hard-coding a scene object name).
    static Transform FindTiger()
    {
        var drag = FindAnyObjectByType<DragRotateCube>();
        return drag != null ? drag.transform : null;
    }
}
