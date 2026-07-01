// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: Apache-2.0

using DisplayXR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Window-space tuning HUD for the transparent tiger app.
///
/// Two sliders:
///   - Focus       : drives the active camera's world-Z position (same as W/S keys).
///   - Stereo      : single knob driving DisplayXRDisplay.ipdFactor AND
///                   DisplayXRDisplay.parallaxFactor together — one perceived-3D
///                   strength control (same as +/- keys).
///
/// Cosmetic spec:
///   - Black panel at 20% opacity (80% transparent).
///   - Rounded corners (procedural 9-slice sprite).
///   - Circular slider knobs (procedural antialiased disc).
///
/// SHIFT+TAB toggles visibility (plain Tab is reserved by
/// DisplayXRRigManager.CycleNext for camera cycling). Default visible.
///
/// Auto-installs into any loaded scene via RuntimeInitializeOnLoadMethod so
/// the test app doesn't need scene-level wiring — drops a fresh GameObject
/// at scene root and attaches both this HUD and the wsui mouse router.
/// </summary>
[ExecuteAlways]
public class TigerTuningHUD : MonoBehaviour
{
    [Header("Target rig (auto-found if null)")]
    public DisplayXRDisplay displayRig;

    [Header("Layer placement (fractional window coords)")]
    // Narrow + slim panel, roughly centered horizontally, lower-third
    // vertically. Tweakable via inspector (changes propagate via Update).
    // Panel is positioned so its bottom edge stays at ~0.81 (just below the
    // tiger's lower body). Height is the canvas RT height (650) projected
    // back to fractional window units at the same per-canvas-unit screen
    // scale we used before (0.146 ≈ 0.18 × 650/800), so slider knobs and
    // text render at the same visible size as the original 0.18 panel.
    [Range(0f, 1f)] public float panelX = 0.43f;
    [Range(0f, 1f)] public float panelY = 0.66f;
    [Range(0f, 1f)] public float panelWidth = 0.14f;
    [Range(0f, 1f)] public float panelHeight = 0.146f;
    [Range(-0.05f, 0.05f)] public float disparity;

    // 3D Focus drives camera.transform.position.z relative to the rig's
    // startup Z. ±0.2 m gives fine-grained focus pull without flying the
    // camera through the tiger.
    private const float kFocusRange = 0.2f;

    // 3D Depth: 0 (mono / no parallax) .. 1.0 (nominal stereo). Drives BOTH
    // ipdFactor and parallaxFactor to the same value so the single knob
    // captures perceived-3D strength coherently. 1.0 = default 3D feel.
    private const float kDepthMin = 0.0f;
    private const float kDepthMax = 1.0f;
    private const float kDepthDefault = 1.0f;
    // +/- keyboard ramp rate for 3D Depth, in factor-units per second.
    // Mirrors W/S → focus (held-key continuous). 0.5 = full 0..1 sweep in 2 s.
    private const float kDepthRampPerSec = 0.5f;

    // RT height matches the content stack so there's no dead space below
    // the bottom slider: title (90) + spacing (30) + 2× row (200) + spacing
    // (30) + top/bottom padding (50+50) = 650. Width unchanged.
    private const int kRTWidth = 1024;
    private const int kRTHeight = 650;

    private Camera m_Cam;
    private float m_InitialCamZ;

    private Slider m_FocusSlider;
    private Slider m_StereoSlider;
    private Text m_FocusValueText;
    private Text m_StereoValueText;
    private Font m_Font;

    private GameObject m_CanvasGO;
    // Start hidden — user reveals with SHIFT+TAB. Tuning is a power-user
    // feature; the default first-launch view is the tiger sans UI.
    private bool m_UIVisible = false;

    // -----------------------------------------------------------------------
    // Auto-install: drop a fresh HUD GameObject into every loaded scene so
    // the test app doesn't need to wire it up via the editor.
    // -----------------------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInstall()
    {
        // Idempotent — if a HUD already exists in the scene (e.g. user
        // dropped one into CubeTest.unity), don't double-install.
        if (Object.FindAnyObjectByType<TigerTuningHUD>() != null) return;
        var go = new GameObject("TigerTuningHUD");
        go.AddComponent<TigerTuningHUD>();
        go.AddComponent<TigerHudMouseRouter>();
        Debug.Log("[TigerTuningHUD] Auto-installed HUD into scene.");
    }

    void OnEnable()
    {
        if (displayRig == null) displayRig = Object.FindAnyObjectByType<DisplayXRDisplay>();
        m_Cam = displayRig != null ? displayRig.GetComponent<Camera>() : Camera.main;
        m_InitialCamZ = m_Cam != null ? m_Cam.transform.position.z : 0f;

        m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (m_Font == null) m_Font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Idempotent rebuild on domain reloads.
        var existing = transform.Find("TigerTuning_Canvas");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }
        BuildPanel();
    }

    void BuildPanel()
    {
        // ---- root canvas (built inactive; activated at the end so
        // DisplayXRWindowSpaceUI.OnEnable picks up the configured resolution)
        var canvasGO = new GameObject("TigerTuning_Canvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.SetActive(false);
        canvasGO.transform.SetParent(transform, false);
        canvasGO.layer = LayerMask.NameToLayer("UI");

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; // wsui switches this

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(kRTWidth, kRTHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var wsui = canvasGO.AddComponent<DisplayXRWindowSpaceUI>();
        wsui.positionX = panelX;
        wsui.positionY = panelY;
        wsui.width = panelWidth;
        wsui.height = panelHeight;
        wsui.disparity = disparity;
        wsui.resolution = new Vector2Int(kRTWidth, kRTHeight);

        // ---- panel background: rounded-rect 9-slice, black @ 20% opacity ----
        var panelGO = MakeUIObject("Panel", canvasGO.transform);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;
        var panelImg = panelGO.AddComponent<Image>();
        panelImg.sprite = GetRoundedRectSprite();
        panelImg.type = Image.Type.Sliced;
        panelImg.color = new Color(0f, 0f, 0f, 0.20f); // 80% transparent black

        var layout = panelGO.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(50, 50, 50, 50);
        layout.spacing = 30;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        // Respect LayoutElement.preferredHeight on title + slider rows so
        // they take their declared sizes (90, 200, 200) and the panel hugs
        // the content tightly instead of leaving dead space at the bottom.
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // ---- title ----
        var title = MakeText(panelGO.transform, "Title", "Tuning", 56, FontStyle.Bold);
        title.color = Color.white;
        title.alignment = TextAnchor.MiddleCenter;
        var titleLE = title.gameObject.AddComponent<LayoutElement>();
        titleLE.preferredHeight = 90;

        // ---- 3D Focus (camera world-Z; same as W/S) ----
        BuildSliderRow(panelGO.transform, "3D Focus",
            -kFocusRange, kFocusRange, 0f,
            v =>
            {
                if (m_Cam != null)
                {
                    var p = m_Cam.transform.position;
                    p.z = m_InitialCamZ + v;
                    m_Cam.transform.position = p;
                }
                if (m_FocusValueText != null) m_FocusValueText.text = v.ToString("+0.00;-0.00;0.00") + " m";
            },
            out m_FocusSlider, out m_FocusValueText);

        // ---- 3D Depth (drives ipdFactor + parallaxFactor) ----
        if (displayRig != null)
        {
            displayRig.ipdFactor = kDepthDefault;
            displayRig.parallaxFactor = kDepthDefault;
        }
        BuildSliderRow(panelGO.transform, "3D Depth",
            kDepthMin, kDepthMax, kDepthDefault,
            v =>
            {
                if (displayRig != null)
                {
                    displayRig.ipdFactor = v;
                    displayRig.parallaxFactor = v;
                }
                if (m_StereoValueText != null) m_StereoValueText.text = v.ToString("0.00");
            },
            out m_StereoSlider, out m_StereoValueText);

        m_CanvasGO = canvasGO;
        canvasGO.SetActive(m_UIVisible);
    }

    void Update()
    {
        // Push wsui placement changes from inspector edits.
        var wsui = m_CanvasGO != null ? m_CanvasGO.GetComponent<DisplayXRWindowSpaceUI>() : null;
        if (wsui != null)
        {
            wsui.positionX = panelX;
            wsui.positionY = panelY;
            wsui.width = panelWidth;
            wsui.height = panelHeight;
            wsui.disparity = disparity;
        }

        // Mirror the camera's current Z back into the Focus slider so W/S
        // (driven by the plugin's DisplayXRInputController) and the slider
        // stay in agreement — both write m_Cam.transform.position.z, but the
        // slider was previously write-only, so W/S moves left the knob stale
        // and the next slider touch stomped the W/S offset. SetValueWithoutNotify
        // updates the knob + readout WITHOUT re-firing onValueChanged (which
        // would write the camera Z straight back and fight the input controller).
        if (m_Cam != null && m_FocusSlider != null)
        {
            float zOffset = m_Cam.transform.position.z - m_InitialCamZ;
            float clamped = Mathf.Clamp(zOffset, -kFocusRange, kFocusRange);
            if (!Mathf.Approximately(clamped, m_FocusSlider.value))
            {
                m_FocusSlider.SetValueWithoutNotify(clamped);
                if (m_FocusValueText != null)
                    m_FocusValueText.text = clamped.ToString("+0.00;-0.00;0.00") + " m";
            }
        }

        // Same read-back for the Depth slider: mirror the rig's current
        // ipdFactor back into the knob so any other input that writes
        // ipdFactor/parallaxFactor stays reflected here. ipdFactor is the
        // canonical value (the callback sets both factors to v).
        if (displayRig != null && m_StereoSlider != null)
        {
            float depth = Mathf.Clamp(displayRig.ipdFactor, kDepthMin, kDepthMax);
            if (!Mathf.Approximately(depth, m_StereoSlider.value))
            {
                m_StereoSlider.SetValueWithoutNotify(depth);
                if (m_StereoValueText != null)
                    m_StereoValueText.text = depth.ToString("0.00");
            }
        }

        // Keyboard handling (+/- ramps 3D Depth, SHIFT+TAB toggles the panel)
        // is gated on our process owning the foreground window: in
        // transparent-overlay builds runInBackground=true and the plugin
        // requests raw input via RIDEV_INPUTSINK so the cube/HUD stay
        // live when the window is defocused. Side-effect: Keyboard.current
        // sees every keystroke system-wide, including the user typing in
        // another app they clicked through to. Without this gate, +/- or
        // SHIFT+TAB while typing in Notepad would leak into the HUD.
        //
        // We can't compare GetForegroundWindow().class to "DisplayXROverlay"
        // because the overlay HWND has WS_EX_NOACTIVATE — it never claims
        // foreground itself. Instead the plugin exposes
        // displayxr_is_our_process_foreground() which checks every window
        // owned by our process (Unity's cloaked HWND + the overlay) against
        // the foreground HWND's PID. Same signal DisplayXRInputController
        // uses to gate WASD movement, so the HUD keys and W/S share the
        // same "our app is focused" definition.
        var kb = Keyboard.current;
        if (kb != null && IsOurProcessForeground())
        {
            // +/- ramps 3D Depth (ipdFactor + parallaxFactor together),
            // mirroring W/S → focus. Held-key continuous ramp at
            // kDepthRampPerSec. Accept the unshifted '=' as '+' and the numpad
            // keys so it works on any layout. The Depth read-back above mirrors
            // the new value into the slider (same 1-frame lag as W/S → Focus).
            if (displayRig != null)
            {
                float step = 0f;
                if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed)
                    step += kDepthRampPerSec * Time.deltaTime;
                if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed)
                    step -= kDepthRampPerSec * Time.deltaTime;
                if (step != 0f)
                {
                    float v = Mathf.Clamp(displayRig.ipdFactor + step, kDepthMin, kDepthMax);
                    displayRig.ipdFactor = v;
                    displayRig.parallaxFactor = v;
                }
            }

            // SHIFT+TAB toggles visibility. Plain Tab is bound by
            // DisplayXRRigManager.CycleNext for camera cycling, so SHIFT gates it.
            if (m_CanvasGO != null)
            {
                bool shiftTab = kb.tabKey.wasPressedThisFrame &&
                                (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
                if (shiftTab)
                {
                    m_UIVisible = !m_UIVisible;
                    m_CanvasGO.SetActive(m_UIVisible);
                    Debug.Log($"[TigerTuningHUD] toggle via SHIFT+TAB → visible={m_UIVisible}");
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Foreground gate for keyboard input (see Update for rationale).
    // displayxr_is_our_process_foreground() is exported from the plugin's
    // native DLL (DISPLAYXR_EXPORT, native~/displayxr_hooks.h:207). It
    // walks every window in our process and returns 1 if any of them is
    // the OS foreground window. Same signal DisplayXRInputController uses
    // for WASD gating.
    // -----------------------------------------------------------------------
    [System.Runtime.InteropServices.DllImport("displayxr_unity",
        CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int displayxr_is_our_process_foreground();

    private static bool IsOurProcessForeground()
    {
        // Fail-open if the symbol is missing (older plugin binary, editor
        // context where the native lib hasn't loaded yet) — never block
        // the user out of their own HUD because of a probe failure.
        try { return displayxr_is_our_process_foreground() != 0; }
        catch { return true; }
    }

    // ------------------------------------------------------------------------
    // Procedural sprites
    // ------------------------------------------------------------------------

    private static Sprite s_CircleSprite;
    private static Sprite GetCircleSprite()
    {
        if (s_CircleSprite != null) return s_CircleSprite;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[size * size];
        float cx = (size - 1) * 0.5f;
        float r = size * 0.5f - 1f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cx;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        s_CircleSprite = Sprite.Create(tex,
            new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        s_CircleSprite.hideFlags = HideFlags.HideAndDontSave;
        return s_CircleSprite;
    }

    // 9-slice rounded-rect sprite (white, alpha=1 inside the rounded shape,
    // alpha=0 outside). Caller tints via Image.color. Border is set so Unity's
    // sliced rendering preserves the corner radius at any size.
    private static Sprite s_RoundedRectSprite;
    private static Sprite GetRoundedRectSprite()
    {
        if (s_RoundedRectSprite != null) return s_RoundedRectSprite;
        const int size = 64;
        const int radius = 20; // ~31% of size — generous rounding
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float a = 1.0f;
                // Distance to the rounded corner: collapse into the inset
                // quadrant centers, then take Euclidean distance.
                int dx = (x < radius) ? (radius - 1 - x)
                       : (x >= size - radius) ? (x - (size - radius))
                       : 0;
                int dy = (y < radius) ? (radius - 1 - y)
                       : (y >= size - radius) ? (y - (size - radius))
                       : 0;
                if (dx > 0 || dy > 0)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    a = Mathf.Clamp01(radius - d);
                }
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        // Border = radius so the rounded corners are preserved during slicing.
        s_RoundedRectSprite = Sprite.Create(tex,
            new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
        s_RoundedRectSprite.hideFlags = HideFlags.HideAndDontSave;
        return s_RoundedRectSprite;
    }

    // ------------------------------------------------------------------------
    // UI building helpers
    // ------------------------------------------------------------------------

    GameObject MakeUIObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");
        return go;
    }

    Text MakeText(Transform parent, string name, string content, int size, FontStyle style)
    {
        var go = MakeUIObject(name, parent);
        var t = go.AddComponent<Text>();
        t.font = m_Font;
        t.fontSize = size;
        t.fontStyle = style;
        t.text = content;
        t.alignment = TextAnchor.MiddleLeft;
        t.color = new Color(0.92f, 0.93f, 0.95f, 1f);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    void BuildSliderRow(Transform parent, string label, float min, float max,
                        float initial, System.Action<float> onChanged,
                        out Slider slider, out Text valueText)
    {
        var rowGO = MakeUIObject(label + "Row", parent);
        var rowLE = rowGO.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 200;

        var labelText = MakeText(rowGO.transform, "Label", label, 44, FontStyle.Normal);
        labelText.color = new Color(0.75f, 0.78f, 0.85f, 1f);
        var labelRT = labelText.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 0.55f);
        labelRT.anchorMax = new Vector2(0.65f, 1f);
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        valueText = MakeText(rowGO.transform, "Value", initial.ToString("0.00"), 44, FontStyle.Bold);
        valueText.alignment = TextAnchor.MiddleRight;
        var valueRT = valueText.GetComponent<RectTransform>();
        valueRT.anchorMin = new Vector2(0.65f, 0.55f);
        valueRT.anchorMax = new Vector2(1f, 1f);
        valueRT.offsetMin = Vector2.zero;
        valueRT.offsetMax = Vector2.zero;

        var sliderGO = MakeUIObject("Slider", rowGO.transform);
        var sliderRT = sliderGO.GetComponent<RectTransform>();
        sliderRT.anchorMin = new Vector2(0, 0);
        sliderRT.anchorMax = new Vector2(1, 0.5f);
        sliderRT.offsetMin = Vector2.zero;
        sliderRT.offsetMax = Vector2.zero;

        var bgGO = MakeUIObject("Background", sliderGO.transform);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 0.5f);
        bgRT.anchorMax = new Vector2(1, 0.5f);
        bgRT.pivot = new Vector2(0.5f, 0.5f);
        bgRT.sizeDelta = new Vector2(0, 12);
        bgGO.AddComponent<Image>().color = new Color(0.30f, 0.30f, 0.35f, 0.7f);

        var fillAreaGO = MakeUIObject("Fill Area", sliderGO.transform);
        var fillAreaRT = fillAreaGO.GetComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0, 0.5f);
        fillAreaRT.anchorMax = new Vector2(1, 0.5f);
        fillAreaRT.pivot = new Vector2(0.5f, 0.5f);
        fillAreaRT.offsetMin = new Vector2(0, -6);
        fillAreaRT.offsetMax = new Vector2(0, 6);

        var fillGO = MakeUIObject("Fill", fillAreaGO.transform);
        var fillRT = fillGO.GetComponent<RectTransform>();
        fillRT.anchorMin = new Vector2(0, 0);
        fillRT.anchorMax = new Vector2(1, 1);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        fillGO.AddComponent<Image>().color = new Color(0.29f, 0.62f, 1.0f, 1f);

        // Circular knob — slide-area sets the handle's rendered height, so
        // its sizeDelta.y IS the handle diameter. At a 0.14-fractional panel
        // width on a 1920px window, 40px on a 1024px RT renders at ~10
        // screen px — too small to click reliably. 80 is roughly a 20-px
        // target, which is the smallest that's comfortable to grab.
        const int kHandleSize = 80;
        var handleAreaGO = MakeUIObject("Handle Slide Area", sliderGO.transform);
        var handleAreaRT = handleAreaGO.GetComponent<RectTransform>();
        handleAreaRT.anchorMin = new Vector2(0, 0.5f);
        handleAreaRT.anchorMax = new Vector2(1, 0.5f);
        handleAreaRT.pivot = new Vector2(0.5f, 0.5f);
        // X inset is -kHandleSize/2 so the knob's edge aligns with the
        // slider extremes — keeps the knob from over-traveling past the
        // fill rect at min/max value.
        handleAreaRT.sizeDelta = new Vector2(-kHandleSize / 2, kHandleSize);

        var handleGO = MakeUIObject("Handle", handleAreaGO.transform);
        var handleRT = handleGO.GetComponent<RectTransform>();
        handleRT.sizeDelta = new Vector2(kHandleSize, 0);
        var handleImg = handleGO.AddComponent<Image>();
        handleImg.color = Color.white;
        handleImg.sprite = GetCircleSprite();

        slider = sliderGO.AddComponent<Slider>();
        slider.fillRect = fillRT;
        slider.handleRect = handleRT;
        slider.targetGraphic = handleImg;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = Mathf.Clamp(initial, min, max);
        slider.wholeNumbers = false;

        var capturedOnChanged = onChanged;
        slider.onValueChanged.AddListener(v => capturedOnChanged(v));
    }
}
