// Off-axis projection probe (shared between displayxr-unity-test* repos to compare
// known-good 2d-ui vs the broken transparent overlay). Logs once/sec:
//   - headX: average eye world X from the rig's FlipViewZ'd view matrices (head side)
//   - L/R m00,m02,m03: the RUNTIME per-eye projection horizontal scale (m00) and the
//     two off-center terms — m02 (frustum shear, OpenGL clip) and m03 (post-divide
//     principal-point shift). One of these encodes window-relative off-center.
// Identical output format in both repos so the logs diff directly. Reads the per-eye
// view/proj from the plugin's native shared state via
// DisplayXRNative.displayxr_get_stereo_matrices — the same matrices the provider's
// KooimaProjectionFixFeature and the transparent overlay silhouette consume (the
// former DisplayXRFeature.GetStereoMatrices hook API is gone). URP-only; self-installs
// at scene load. Purely diagnostic.
using UnityEngine;
using UnityEngine.Rendering;
using DisplayXR;

[DisallowMultipleComponent]
public class KooimaProbe : MonoBehaviour
{
    float m_Timer;

    // Scratch buffers for the native getter (column-major float[16] per matrix).
    static readonly float[] s_LV = new float[16], s_LP = new float[16],
                            s_RV = new float[16], s_RP = new float[16];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall()
    {
        if (GraphicsSettings.currentRenderPipeline == null) return; // URP only
        if (FindAnyObjectByType<KooimaProbe>() != null) return;
        var go = new GameObject("Kooima Probe");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<KooimaProbe>();
        Debug.Log("[KooimaProbe] installed.");
    }

    void LateUpdate()
    {
        DisplayXRNative.displayxr_get_stereo_matrices(s_LV, s_LP, s_RV, s_RP, out int valid);
        if (valid == 0) return;

        Matrix4x4 lv = ToMat(s_LV), lp = ToMat(s_LP), rv = ToMat(s_RV), rp = ToMat(s_RP);

        m_Timer += Time.deltaTime;
        if (m_Timer < 1f) return;
        m_Timer = 0f;

        float headX = 0.5f * (FlipZ(lv).inverse.GetColumn(3).x +
                              FlipZ(rv).inverse.GetColumn(3).x);
        Debug.Log($"[KooimaProbe] headX={headX:F4} " +
                  $"L(m00={lp.m00:F4} m02={lp.m02:F4} m03={lp.m03:F4}) " +
                  $"R(m00={rp.m00:F4} m02={rp.m02:F4} m03={rp.m03:F4})");
    }

    // Column-major float[16] (OpenXR/OpenGL convention) -> Matrix4x4.
    static Matrix4x4 ToMat(float[] m)
    {
        var r = new Matrix4x4();
        r.m00 = m[0];  r.m10 = m[1];  r.m20 = m[2];  r.m30 = m[3];
        r.m01 = m[4];  r.m11 = m[5];  r.m21 = m[6];  r.m31 = m[7];
        r.m02 = m[8];  r.m12 = m[9];  r.m22 = m[10]; r.m32 = m[11];
        r.m03 = m[12]; r.m13 = m[13]; r.m23 = m[14]; r.m33 = m[15];
        return r;
    }

    // OpenXR -> Unity handedness flip the rig applies before SetStereoViewMatrix,
    // so the eye world positions match what the renderer/shader see.
    static Matrix4x4 FlipZ(Matrix4x4 m)
    {
        m.m02 = -m.m02; m.m12 = -m.m12; m.m22 = -m.m22; m.m32 = -m.m32;
        return m;
    }
}
