// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: Apache-2.0
//
// Comprehensive runtime pose logger for diagnosing rig vs scene-content
// alignment.
//
// Drop on any GameObject (e.g. Main Camera). Populate `targets` with the
// GameObjects whose poses you want to inspect (typically the active rig
// + the visible scene content like Cube). The logger walks each target's
// parent chain and dumps world AND local positions/rotations + lossy
// scale, so a 1.5-1.6m Y shift hiding in a parent XR-Origin transform
// (typical of VR-floor tracking-origin setups) is visible.
//
// Output goes to:
//   - Editor: the Console window
//   - Built Player: <BuildDir>\<AppName>_Data\Player.log
//
// Suggested workflow to confirm or refute "something is shifting cube/rig
// by ~1.5m":
//   1. Attach this component to one GameObject in the scene
//   2. Drag Main Camera + Cube into `targets`
//   3. Build + run; tail Player.log
//   4. Check `world` vs `local` columns and the parent chain output

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class DisplayXRPoseLogger : MonoBehaviour
{
    [Tooltip("Transforms to log each interval (e.g. Main Camera, Cube).")]
    public Transform[] targets;

    [Tooltip("Log once every N frames. 60 ≈ once/second at 60fps.")]
    [Min(1)] public int logIntervalFrames = 60;

    [Tooltip("Also log the active rig as reported by DisplayXRRigManager.")]
    public bool logActiveRig = true;

    [Tooltip("Walk each target's parent chain (so an XR Origin / Camera Offset shift is visible).")]
    public bool logParentChain = true;

    [Tooltip("Also log XR Origin tracking-mode info (helps spot a floor-vs-device 1.6m offset).")]
    public bool logXrOriginInfo = true;

    private int m_FrameCount;

    void LateUpdate()
    {
        if (++m_FrameCount < logIntervalFrames) return;
        m_FrameCount = 0;

        if (targets != null)
        {
            foreach (var t in targets)
            {
                if (t == null) continue;
                LogTarget(t);
            }
        }

        if (logActiveRig)
        {
            var active = DisplayXR.DisplayXRRigManager.ActiveCamera;
            Debug.Log(active != null
                ? $"[PoseLog] activeRig={active.name} world=({active.transform.position.x:F3},{active.transform.position.y:F3},{active.transform.position.z:F3})"
                : "[PoseLog] activeRig=null");
        }

        if (logXrOriginInfo)
        {
            LogXrOriginInfo();
        }
    }

    private void LogTarget(Transform t)
    {
        var wp = t.position;
        var we = t.eulerAngles;
        var lp = t.localPosition;
        var le = t.localEulerAngles;
        var ls = t.lossyScale;
        Debug.Log($"[PoseLog] {t.name}: " +
                  $"world=({wp.x:F3},{wp.y:F3},{wp.z:F3}) " +
                  $"local=({lp.x:F3},{lp.y:F3},{lp.z:F3}) " +
                  $"lossyScale=({ls.x:F3},{ls.y:F3},{ls.z:F3}) " +
                  $"worldEul=({we.x:F1},{we.y:F1},{we.z:F1}) " +
                  $"localEul=({le.x:F1},{le.y:F1},{le.z:F1}) " +
                  $"parent={(t.parent ? t.parent.name : "<root>")}");

        if (logParentChain && t.parent != null)
        {
            var p = t.parent;
            int depth = 1;
            while (p != null)
            {
                var pwp = p.position;
                var plp = p.localPosition;
                Debug.Log($"[PoseLog]   ^ parent[{depth}]={p.name}: " +
                          $"world=({pwp.x:F3},{pwp.y:F3},{pwp.z:F3}) " +
                          $"local=({plp.x:F3},{plp.y:F3},{plp.z:F3})");
                p = p.parent;
                depth++;
                if (depth > 8) break; // safety
            }
        }
    }

    private void LogXrOriginInfo()
    {
        // Unity 6's XR Origin / tracking-origin mode is what introduces the
        // ~1.6m "floor reference space" Y shift on typical VR rigs. Log it
        // so we can see whether something has set up Unity-side floor mode.
        var subsystems = new System.Collections.Generic.List<UnityEngine.XR.XRInputSubsystem>();
        UnityEngine.SubsystemManager.GetSubsystems(subsystems);
        foreach (var sub in subsystems)
        {
            Debug.Log($"[PoseLog] XRInputSubsystem trackingOriginMode={sub.GetTrackingOriginMode()} " +
                      $"supportedModes={sub.GetSupportedTrackingOriginModes()}");
        }
        Debug.Log($"[PoseLog] XRSettings.enabled={UnityEngine.XR.XRSettings.enabled} " +
                  $"loadedDeviceName='{UnityEngine.XR.XRSettings.loadedDeviceName}'");
    }
}
