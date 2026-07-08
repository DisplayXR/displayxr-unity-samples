// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: Apache-2.0

using DisplayXR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Builds a runtime UI panel inside a DisplayXR window-space layer:
/// IPD slider, virtual-display-height slider, and a render-mode cycle button.
///
/// Drop this on a single empty GameObject in the scene (no Canvas needed
/// in the inspector — this script creates the Canvas + DisplayXRWindowSpaceUI
/// + all controls programmatically). Reference a DisplayXRDisplay rig in the
/// inspector or leave empty to auto-find.
///
/// [ExecuteAlways] is intentional: DisplayXR's standalone preview window
/// starts the runtime session WITHOUT entering Play Mode (it's an
/// EditorWindow + standalone OpenXR session). So MonoBehaviour.Start /
/// OnEnable normally wouldn't fire and the panel wouldn't be built. With
/// [ExecuteAlways] the panel is created on first scene load too, so
/// "Window → DisplayXR → Preview Window → Start" is enough — no Play
/// button required.
/// </summary>
[ExecuteAlways]
public class DisplayXRTuningUI : MonoBehaviour
{
    [Header("Target rig")]
    [Tooltip("DisplayXR rig to drive. Auto-found in scene if left null.")]
    public DisplayXRDisplay displayRig;

    [Header("Layer placement (fractional window coords)")]
    [Range(0f, 1f)] public float panelX = 0f;
    [Range(0f, 1f)] public float panelY = 0.65f;
    [Range(0f, 1f)] public float panelWidth = 0.20f;
    [Range(0f, 1f)] public float panelHeight = 0.32f;
    [Range(-0.05f, 0.05f)] public float disparity;

    [Header("2D/3D transition")]
    [Tooltip("Seconds to ease the stereo disparity across a 2D<->3D switch " +
             "(the shared DisplayXRModeSwitch ramp). 0 = instant. Provider mode only.")]
    public float modeTransitionSeconds = 0.18f;

    // Slider ranges/defaults are constants so scene-serialized values from
    // earlier versions can't override the canonical spec.
    private const float kIpdMin = 0.0f;
    private const float kIpdMax = 1.0f;
    private const float kIpdDefault = 1.0f;
    private const float kScaleMin = 0.5f;
    private const float kScaleMax = 1.5f;
    private const float kScaleDefault = 1.0f;

    private float m_InitialVHeight;

    private const int kRTWidth = 1024;
    private const int kRTHeight = 1024;

    private Slider m_IpdSlider;
    private Slider m_ScaleSlider;
    private Text m_IpdValueText;
    private Text m_ScaleValueText;
    private Text m_ModeButtonLabel;
    private Button m_ModeButton;
    private Font m_Font;

    // Rendering-mode enumeration/selection now goes exclusively through the
    // DisplayXRProvider facade (epic #166). The legacy standalone rendering-mode
    // P/Invokes were removed with the OpenXR hook + standalone preview.
    private uint[] m_ModeIndices;
    private string[] m_ModeNames;
    private bool[] m_ModeIs3D;
    private uint[] m_ModeViewCounts;
    private bool[] m_ModeRequestable;
    private int m_CurrentModeArrayIdx = -1;

    // Provider mode (built app, epic #166): the standalone rendering-mode API is
    // inert (no standalone session), so drive DisplayXRProvider + the shared smooth
    // 2D<->3D sequencer (ported from displayxr-common, matches the runtime test apps).
    private readonly DisplayXR.DisplayXRModeSwitch m_ModeSwitch = new DisplayXR.DisplayXRModeSwitch();
    private bool m_ProviderMode;

    void OnEnable()
    {
        if (displayRig == null) displayRig = Object.FindAnyObjectByType<DisplayXRDisplay>();

        // Pick a built-in font that ships with Unity. LegacyRuntime.ttf is
        // the modern default; Arial fallback for older Unity versions.
        m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (m_Font == null) m_Font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Idempotent: with [ExecuteAlways] this runs on every domain reload.
        // If we already built the panel last reload, drop it and build fresh —
        // simpler than trying to repair half-serialized component refs.
        var existing = transform.Find("DisplayXR_Tuning_Canvas");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }

        BuildPanel();
    }

    void BuildPanel()
    {
        // Capture the rig's initial vHeight before any slider drives it. The
        // Scale slider operates as a multiplier over this baseline so the
        // user's editor-time vHeight setting stays meaningful (slider=1 →
        // unchanged from editor; slider=0.5 → half; slider=1.5 → 1.5x).
        m_InitialVHeight = (displayRig != null && displayRig.virtualDisplayHeight > 0f)
            ? displayRig.virtualDisplayHeight : 0.30f;

        // ---- root canvas (child of this gameobject so it's tied to scene lifecycle) ----
        // Build inactive so DisplayXRWindowSpaceUI's OnEnable doesn't fire with
        // its default 512×256 resolution before we set it. Activate at the end.
        var canvasGO = new GameObject("DisplayXR_Tuning_Canvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.SetActive(false);
        canvasGO.transform.SetParent(transform, false);
        canvasGO.layer = LayerMask.NameToLayer("UI");

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; // wsui will switch this

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(kRTWidth, kRTHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // Attach DisplayXRWindowSpaceUI and configure BEFORE activation, so
        // OnEnable creates a kRTWidth × kRTHeight RT (matches CanvasScaler
        // reference resolution).
        var wsui = canvasGO.AddComponent<DisplayXRWindowSpaceUI>();
        wsui.positionX = panelX;
        wsui.positionY = panelY;
        wsui.width = panelWidth;
        wsui.height = panelHeight;
        wsui.disparity = disparity;
        wsui.resolution = new Vector2Int(kRTWidth, kRTHeight);

        // ---- panel background ----
        var panelGO = MakeUIObject("Panel", canvasGO.transform);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;
        var panelImg = panelGO.AddComponent<Image>();
        // 80% transparency = 20% opacity per the cosmetic spec. The dark
        // base lets light text/accents stay readable through the see-through.
        panelImg.color = new Color(0.06f, 0.07f, 0.10f, 0.20f);

        // Subtle accent strip on the left edge for a finished look.
        var accentGO = MakeUIObject("Accent", panelGO.transform);
        var accentRT = accentGO.GetComponent<RectTransform>();
        accentRT.anchorMin = new Vector2(0, 0);
        accentRT.anchorMax = new Vector2(0, 1);
        accentRT.pivot = new Vector2(0, 0.5f);
        accentRT.sizeDelta = new Vector2(6, 0);
        accentGO.AddComponent<Image>().color = new Color(0.29f, 0.62f, 1.0f, 1f); // #4A9EFF

        // Use a vertical layout to stack the rows nicely.
        var layout = panelGO.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(60, 50, 50, 50);
        layout.spacing = 40;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // ---- title ----
        var title = MakeText(panelGO.transform, "Title", "DisplayXR Tuning", 72, FontStyle.Bold);
        title.color = Color.white;
        var titleLE = title.gameObject.AddComponent<LayoutElement>();
        titleLE.preferredHeight = 100;

        // ---- IPD ----
        if (displayRig != null) displayRig.ipdFactor = kIpdDefault;
        BuildSliderRow(panelGO.transform, "IPD",
            kIpdMin, kIpdMax, kIpdDefault,
            v =>
            {
                if (displayRig != null) displayRig.ipdFactor = v;
                if (m_IpdValueText != null) m_IpdValueText.text = v.ToString("0.00");
            },
            out m_IpdSlider, out m_IpdValueText);

        // ---- Scale (magnification — slider value DIVIDES the rig's vHeight) ----
        // Smaller vHeight makes the scene appear larger (closer virtual
        // display), so dividing by the slider value gives a "bigger when
        // you slide right" interaction. Range stays 0.5..1.5 around 1.0:
        //   slider 0.5 → vHeight × 2  → content shrinks
        //   slider 1.0 → unchanged
        //   slider 1.5 → vHeight × 0.667 → content grows
        if (displayRig != null) displayRig.virtualDisplayHeight = m_InitialVHeight / kScaleDefault;
        BuildSliderRow(panelGO.transform, "Scale",
            kScaleMin, kScaleMax, kScaleDefault,
            v =>
            {
                if (displayRig != null) displayRig.virtualDisplayHeight = m_InitialVHeight / v;
                if (m_ScaleValueText != null) m_ScaleValueText.text = v.ToString("0.00") + "x";
            },
            out m_ScaleSlider, out m_ScaleValueText);

        // ---- render mode button ----
        var btnGO = MakeUIObject("ModeButton", panelGO.transform);
        var btnRT = btnGO.GetComponent<RectTransform>();
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.29f, 0.62f, 1.0f, 1f);
        m_ModeButton = btnGO.AddComponent<Button>();
        var colors = m_ModeButton.colors;
        colors.normalColor = new Color(0.29f, 0.62f, 1.0f, 1f);
        colors.highlightedColor = new Color(0.40f, 0.71f, 1.0f, 1f);
        colors.pressedColor = new Color(0.20f, 0.50f, 0.90f, 1f);
        colors.fadeDuration = 0.08f;
        m_ModeButton.colors = colors;
        m_ModeButton.targetGraphic = btnImg;
        m_ModeButton.onClick.AddListener(CycleRenderMode);
        var btnLE = btnGO.AddComponent<LayoutElement>();
        btnLE.preferredHeight = 140;

        m_ModeButtonLabel = MakeText(btnGO.transform, "Label", "Render Mode", 56, FontStyle.Bold);
        m_ModeButtonLabel.color = Color.white;
        m_ModeButtonLabel.alignment = TextAnchor.MiddleCenter;
        var labelRT = m_ModeButtonLabel.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        // Cache for visibility toggling (Shift+Tab) and inspector-edit
        // propagation. Looked up via stored reference so SetActive(false) +
        // GetComponentInChildren-without-include-inactive doesn't lose us.
        m_CanvasGO = canvasGO;

        // Activate now → DisplayXRWindowSpaceUI.OnEnable fires with the right
        // resolution, OverlayCamera spins up, and the RT-→-swapchain copy
        // path picks up the populated panel hierarchy on the very next frame.
        canvasGO.SetActive(m_UIVisible);

        // Try to enumerate modes now; if the standalone session isn't ready
        // yet, retry from Update().
        TryEnumerateModes();
    }

    private bool m_UIVisible = true;
    private GameObject m_CanvasGO;

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

        // Mode list may not be ready until the session has begun.
        if (m_ModeNames == null || (m_ProviderMode && m_ModeNames.Length == 0)) TryEnumerateModes();

        // Provider mode (built app): drive the smooth 2D<->3D sequencer every frame —
        // push its ramped ipdFactor onto the rig and fire the runtime mode request on
        // the frame it signals. Also keep the label synced to the active mode so it
        // reflects switches from any source (button, V key, shell).
        if (m_ProviderMode)
        {
            float ipd = m_ModeSwitch.Update(Time.deltaTime, out bool fire, out uint mode);
            if (m_ModeSwitch.Active && displayRig != null) displayRig.ipdFactor = ipd;
            if (fire && mode != DisplayXRProvider.ActiveModeIndex)
                DisplayXRProvider.RequestRenderingMode(mode);
            SyncProviderModeLabel();
        }

        // V key cycles render modes — same action as the on-screen button.
        // Read via the new Input System (reaches Unity through the provider focus
        // hook even while the woven output is in a separate window).
        // wasPressedThisFrame is already edge-detected so a held V doesn't spam-cycle.
#if ENABLE_INPUT_SYSTEM
        bool vNow = Keyboard.current != null && Keyboard.current[Key.V].wasPressedThisFrame;
#else
        bool vNow = Input.GetKeyDown(KeyCode.V);
#endif
        if (vNow) CycleRenderMode();

        // Shift+Tab toggles UI visibility. Read directly from Unity's new
        // Input System — this only fires when Unity's editor or game view
        // has focus, which is the typical case here (the user has clicked
        // back into the editor before pressing the shortcut). Plain Tab is
        // bound by the plugin's DisplayXRRigManager.CycleNext for camera
        // cycling, so Shift is the gate that keeps the two non-conflicting.
        var kb = Keyboard.current;
        if (kb != null && m_CanvasGO != null &&
            kb.tabKey.wasPressedThisFrame &&
            (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed))
        {
            m_UIVisible = !m_UIVisible;
            m_CanvasGO.SetActive(m_UIVisible);
        }
    }

    void TryEnumerateModes()
    {
        // Rendering modes come exclusively from the DisplayXRProvider facade
        // (epic #166) now that the OpenXR hook + standalone preview are gone.
        TryEnumerateModesProvider();
    }

    // Provider-mode enumeration: pull the mode list from DisplayXRProvider (the
    // native provider already enumerated them). Names come straight from the runtime.
    void TryEnumerateModesProvider()
    {
        var modes = DisplayXRProvider.Modes;
        if (modes == null || modes.Count == 0) { DisplayXRProvider.RefreshModes(); modes = DisplayXRProvider.Modes; }
        if (modes == null || modes.Count == 0) return; // session not ready yet — retry from Update

        int count = modes.Count;
        m_ModeIndices = new uint[count];
        m_ModeNames = new string[count];
        m_ModeIs3D = new bool[count];
        m_ModeViewCounts = new uint[count];
        m_ModeRequestable = new bool[count];
        for (int i = 0; i < count; i++)
        {
            var m = modes[i];
            m_ModeIndices[i] = m.modeIndex;
            m_ModeIs3D[i] = m.hardwareDisplay3D != 0;
            m_ModeViewCounts[i] = m.viewCount;
            m_ModeRequestable[i] = m.isRequestable != 0;
            m_ModeNames[i] = string.IsNullOrEmpty(m.name)
                ? SynthesizeModeName(m.modeIndex, m.hardwareDisplay3D != 0, m.tileColumns, m.tileRows, m.viewCount)
                : m.name;
        }
        m_ProviderMode = true;
        SyncProviderModeLabel();
        m_ModeSwitch.Configure(modeTransitionSeconds); // per-app ramp duration (default 0.18s)
    }

    // Map DisplayXRProvider.ActiveModeIndex → our array slot and update the label.
    // Runs every frame in provider mode so the label tracks external switches too
    // (e.g. a shell-initiated 2D/3D toggle, or the V key).
    void SyncProviderModeLabel()
    {
        if (m_ModeIndices == null) return;
        uint active = DisplayXRProvider.ActiveModeIndex;
        for (int i = 0; i < m_ModeIndices.Length; i++)
            if (m_ModeIndices[i] == active) { m_CurrentModeArrayIdx = i; break; }
        UpdateModeLabel();
    }

    // Provider-mode smooth cycle: pick the next REQUESTABLE mode and hand it to the
    // shared sequencer (ramps ipdFactor around the switch). The rig's steady IPD is
    // the tuning slider's value so a user-tuned disparity is preserved.
    void CycleRenderModeProvider()
    {
        if (m_ModeIndices == null || m_ModeIndices.Length == 0) return;
        int n = m_ModeIndices.Length;
        int next = m_CurrentModeArrayIdx;
        for (int step = 0; step < n; step++)
        {
            next = (next + 1) % n;
            if (m_ModeRequestable == null || m_ModeRequestable[next]) break;
        }
        uint curActive = DisplayXRProvider.ActiveModeIndex;
        uint curVC = 2;
        for (int i = 0; i < n; i++) if (m_ModeIndices[i] == curActive) { curVC = m_ModeViewCounts[i]; break; }
        float steady = m_IpdSlider != null ? m_IpdSlider.value : kIpdDefault;
        float curIpd = (displayRig != null) ? displayRig.ipdFactor : steady;
        m_ModeSwitch.Request(m_ModeIndices[next], m_ModeViewCounts[next],
                             curActive, curVC, curIpd, steady);
    }

    static string SynthesizeModeName(uint modeIndex, bool hw3d, uint cols, uint rows, uint viewCount)
    {
        if (!hw3d) return "2D Mono";
        if (cols == 2 && rows == 1) return viewCount == 2 ? "Side-by-Side" : "SBS";
        if (cols == 1 && rows == 2) return "Top-Bottom";
        if (cols == 2 && rows == 2) return "Quad (4-view)";
        if (cols == 1 && rows == 1 && viewCount > 1) return $"Lenticular ({viewCount})";
        return $"3D Mode {modeIndex}";
    }

    void CycleRenderMode()
    {
        // Provider-only: hand the cycle to the shared smooth 2D<->3D sequencer.
        CycleRenderModeProvider();
    }

    void UpdateModeLabel()
    {
        if (m_ModeButtonLabel == null) return;
        if (m_ModeNames == null || m_CurrentModeArrayIdx < 0 ||
            m_CurrentModeArrayIdx >= m_ModeNames.Length)
        {
            m_ModeButtonLabel.text = "Render Mode";
            return;
        }
        m_ModeButtonLabel.text = m_ModeNames[m_CurrentModeArrayIdx];
    }

    // ---- programmatic UI helpers ----

    // Procedural antialiased circle sprite for slider handles. Generated
    // once and cached statically so we don't pay the bake cost on every
    // panel rebuild — the texture lives for the editor lifetime.
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
                float a = Mathf.Clamp01(r - d + 0.5f); // 1 px antialias band at the edge
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
        rowLE.preferredHeight = 160;

        var labelText = MakeText(rowGO.transform, "Label", label, 44, FontStyle.Normal);
        labelText.color = new Color(0.75f, 0.78f, 0.85f, 1f);
        var labelRT = labelText.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 0.55f);
        labelRT.anchorMax = new Vector2(0.7f, 1f);
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        valueText = MakeText(rowGO.transform, "Value", initial.ToString("0.00"), 44, FontStyle.Bold);
        valueText.alignment = TextAnchor.MiddleRight;
        var valueRT = valueText.GetComponent<RectTransform>();
        valueRT.anchorMin = new Vector2(0.7f, 0.55f);
        valueRT.anchorMax = new Vector2(1f, 1f);
        valueRT.offsetMin = Vector2.zero;
        valueRT.offsetMax = Vector2.zero;

        // Slider — track + fill + handle.
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
        bgGO.AddComponent<Image>().color = new Color(0.18f, 0.20f, 0.25f, 1f);

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

        // Handle Slide Area is intentionally fixed-height + center-anchored
        // vertically. Unity's Slider sets handle.anchorMax.y = 1 for
        // LeftToRight sliders, making the handle fill the slide area's
        // height regardless of sizeDelta. So the slide area's height IS
        // the handle's rendered height — we want a circular handle, so
        // set both axes to the same size.
        const int kHandleSize = 32;
        var handleAreaGO = MakeUIObject("Handle Slide Area", sliderGO.transform);
        var handleAreaRT = handleAreaGO.GetComponent<RectTransform>();
        handleAreaRT.anchorMin = new Vector2(0, 0.5f);
        handleAreaRT.anchorMax = new Vector2(1, 0.5f);
        handleAreaRT.pivot = new Vector2(0.5f, 0.5f);
        handleAreaRT.sizeDelta = new Vector2(-20, kHandleSize);

        var handleGO = MakeUIObject("Handle", handleAreaGO.transform);
        var handleRT = handleGO.GetComponent<RectTransform>();
        // Slider.LeftToRight sets handle anchorMin.y=0 / anchorMax.y=1, so
        // handle height = parentHeight + sizeDelta.y. Slide area parent is
        // already kHandleSize tall, so sizeDelta.y must be 0 (not kHandleSize)
        // — otherwise the handle renders 2x its intended height.
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
