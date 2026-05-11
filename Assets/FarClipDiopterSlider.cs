// Far-clip diopter HUD for the foreground-only render investigation
// (displayxr-unity-test-transparent#2).
//
// Drives the active rig camera's `Camera.farClipPlane` from the keyboard,
// in diopters (D = 1/Z, m^-1). On-screen feedback is a 3D bar + numeric
// TextMesh placed next to the tiger — both world-anchored, so as the
// user moves the camera the HUD moves with the scene.
//
// Why not a window-space UI slider: the wsui composition layer crashes
// the runtime in transparent-overlay mode (DisplayXR/displayxr-unity#82).
// Keyboard + 3D HUD sidesteps the wsui path entirely until that's fixed.
//
// Controls (UnityEngine.InputSystem):
//   [   →  D -= step (smaller D, larger far Z)
//   ]   →  D += step (larger D, smaller far Z — pulls far clip in)
//   0   →  reset D to startup value (1 / cam.farClipPlane at OnEnable)
//   Hold Shift with [ / ] for 5x step
//
// Diopter mapping:
//   D ∈ [0.001, 2.0]  →  Z = 1/D clamped to [0.5, 1000] m.
//
// Visual:
//   - Track (dark gray cube) at fixed world position, fixed height
//     (`barMaxHeight`). Marks the diopter scale.
//   - Fill (orange cube) inside the track. Grows from the bottom upward;
//     height = barMaxHeight * (D / kDMax). Reads as a vertical thermometer.
//   - TextMesh above the bar shows `D=X.XXX  Z=Y.YYm`.
//
// Caveat (per plan-mode decision): hit-test in the plugin reads
// `Camera.farClipPlane`, so at small Z values tiger clicks may stop
// working. Acceptable for a diagnostic — observing render output, not
// interactivity.

using DisplayXR;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[ExecuteAlways]
public class FarClipDiopterSlider : MonoBehaviour
{
    [Header("Diopter step")]
    [Tooltip("Diopter delta applied per [ or ] key press. Hold Shift for 5x.")]
    public float dStep = 0.01f;

    [Header("3D HUD (world-anchored, next to tiger)")]
    [Tooltip("World position of the bar's BOTTOM (track grows upward from here). Tweak in inspector to place the HUD next to the tiger.")]
    public Vector3 hudWorldPosition = new Vector3(0.80f, -0.15f, 0f);

    [Tooltip("World height of the fully-filled bar (D = kDMax). Track is the same height.")]
    public float barMaxHeight = 0.30f;

    [Tooltip("World width / depth of the bar (square cross-section).")]
    public float barThickness = 0.04f;

    // Diopter range — narrowed from the original [0.001, 2.0] after the
    // first hardware run found the tiger disappears around D≈0.15 (Z≈6.6m).
    // Range now spans [0.001, 0.3] (Z ∈ [3.3m, 1000m]) so the disappearance
    // point sits roughly mid-bar, giving meaningful scrub resolution on
    // both sides of the clipping threshold.
    private const float kDMin = 0.001f;
    private const float kDMax = 0.3f;
    private const float kZMin = 3.0f;
    private const float kZMax = 1000f;

    private float m_D = kDMin;
    private float m_StartupD = kDMin;
    private Camera m_Cam;

    private GameObject m_HudRoot;
    private Transform m_BarFill;
    private TextMesh m_Text;

    void OnEnable()
    {
        // Idempotent — if HUD already exists from a prior OnEnable, reuse.
        // (Same destroy-deferred trap that bit the wsui canvas would apply
        // here for any native-side state, even though there isn't any
        // currently. Safer pattern by default.)
        if (m_HudRoot != null) return;
        var existing = transform.Find("FarClipHUD");
        if (existing != null)
        {
            m_HudRoot = existing.gameObject;
            // Re-bind cached refs from the existing hierarchy.
            m_BarFill = existing.Find("Fill");
            var labelTr = existing.Find("Label");
            if (labelTr != null) m_Text = labelTr.GetComponent<TextMesh>();
        }
        else
        {
            BuildHud();
        }

        m_Cam = ResolveCamera();
        // Seed D from the current camera far clip so the HUD reflects
        // reality on first frame (no surprise edits at scene start).
        if (m_Cam != null)
        {
            m_StartupD = Mathf.Clamp(1f / Mathf.Max(0.001f, m_Cam.farClipPlane), kDMin, kDMax);
            m_D = m_StartupD;
        }
        ApplyD(silentLog: true);
    }

    void Update()
    {
        // Rebind on active-rig change (Tab cycle). Don't push our D to the
        // new camera — let it keep its own farClipPlane; reflect that in the
        // HUD instead.
        var active = ResolveCamera();
        if (active != m_Cam)
        {
            m_Cam = active;
            if (m_Cam != null)
            {
                m_D = Mathf.Clamp(1f / Mathf.Max(0.001f, m_Cam.farClipPlane), kDMin, kDMax);
                UpdateVisuals();
            }
        }

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;

        bool inc = kb.rightBracketKey.wasPressedThisFrame;
        bool dec = kb.leftBracketKey.wasPressedThisFrame;
        bool reset = kb.digit0Key.wasPressedThisFrame;
        bool fast = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

        if (reset)
        {
            m_D = m_StartupD;
            ApplyD();
        }
        else if (inc || dec)
        {
            float step = fast ? dStep * 5f : dStep;
            m_D = Mathf.Clamp(m_D + (inc ? step : -step), kDMin, kDMax);
            ApplyD();
        }
#endif
    }

    private Camera ResolveCamera()
    {
        var active = DisplayXRRigManager.ActiveCamera;
        if (active != null) return active;
        return Camera.main;
    }

    private void ApplyD(bool silentLog = false)
    {
        float z = Mathf.Clamp(1f / Mathf.Max(0.001f, m_D), kZMin, kZMax);
        if (m_Cam != null) m_Cam.farClipPlane = z;
        UpdateVisuals();
        if (!silentLog)
        {
            Debug.Log($"[FarClipDiopter] D={m_D:0.000}  Z={z:0.00}m  cam={m_Cam?.name}");
        }
    }

    private void UpdateVisuals()
    {
        float z = Mathf.Clamp(1f / Mathf.Max(0.001f, m_D), kZMin, kZMax);
        if (m_BarFill != null)
        {
            float h = barMaxHeight * Mathf.Clamp01(m_D / kDMax);
            // Pivot-at-bottom: scale Y to h, offset center to h/2 above the base.
            m_BarFill.localScale = new Vector3(barThickness * 0.85f, Mathf.Max(h, 0.001f), barThickness * 0.85f);
            m_BarFill.localPosition = new Vector3(0f, h * 0.5f, 0f);
        }
        if (m_Text != null)
        {
            m_Text.text = $"D = {m_D:0.000}\nZ = {z:0.00} m";
        }
    }

    private void BuildHud()
    {
        m_HudRoot = new GameObject("FarClipHUD");
        m_HudRoot.transform.SetParent(transform, false);
        m_HudRoot.transform.localPosition = hudWorldPosition;

        // ---- track (dark gray, full-height reference rectangle) ----
        var track = GameObject.CreatePrimitive(PrimitiveType.Cube);
        track.name = "Track";
        track.transform.SetParent(m_HudRoot.transform, false);
        track.transform.localPosition = new Vector3(0f, barMaxHeight * 0.5f, 0f);
        track.transform.localScale = new Vector3(barThickness, barMaxHeight, barThickness);
        StripCollider(track);
        SetSolidColor(track, new Color(0.12f, 0.13f, 0.18f, 1f));

        // ---- fill (orange, grows from bottom) ----
        var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fill.name = "Fill";
        fill.transform.SetParent(m_HudRoot.transform, false);
        // Initial position/scale set in UpdateVisuals(); start near-zero so
        // there's no flash at the wrong size on the first frame.
        fill.transform.localPosition = new Vector3(0f, 0.0005f, 0f);
        fill.transform.localScale = new Vector3(barThickness * 0.85f, 0.001f, barThickness * 0.85f);
        StripCollider(fill);
        SetSolidColor(fill, new Color(1f, 0.62f, 0.29f, 1f));
        m_BarFill = fill.transform;

        // ---- numeric label (TextMesh above the bar) ----
        var labelGO = new GameObject("Label", typeof(MeshRenderer), typeof(TextMesh));
        labelGO.transform.SetParent(m_HudRoot.transform, false);
        labelGO.transform.localPosition = new Vector3(0f, barMaxHeight + 0.05f, 0f);
        // TextMesh defaults: origin at left of baseline. Center the anchor
        // so we can position by midpoint above the bar.
        m_Text = labelGO.GetComponent<TextMesh>();
        m_Text.text = "D = 0.000\nZ = 0.00 m";
        m_Text.anchor = TextAnchor.LowerCenter;
        m_Text.alignment = TextAlignment.Center;
        m_Text.fontSize = 64;
        m_Text.characterSize = 0.0075f;  // ~3x the original 0.0025 — text block ~48cm wide
        m_Text.color = Color.white;
        // Default ARGB font material is fine; built-in fonts ship with it.
    }

    private static void StripCollider(GameObject go)
    {
        var col = go.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }
    }

    // Use an unlit material so the bar is readable regardless of scene
    // lighting (Directional Light direction, transparent overlay clear
    // color, etc.). Falls back to Standard if Unlit/Color isn't around.
    private static void SetSolidColor(GameObject go, Color color)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null) return;
        Shader sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh) { color = color };
        // Standard shader uses "_Color"; Unlit/Color uses "_Color" too;
        // setting via Material.color covers both.
        r.sharedMaterial = m;
    }
}
