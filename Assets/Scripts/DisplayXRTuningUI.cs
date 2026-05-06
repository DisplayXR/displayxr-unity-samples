// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: BSL-1.0

using System.Runtime.InteropServices;
using DisplayXR;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds a runtime UI panel inside a DisplayXR window-space layer:
/// IPD slider, virtual-display-height slider, and a render-mode cycle button.
///
/// Drop this on a single empty GameObject in the scene (no Canvas needed
/// in the inspector — this script creates the Canvas + DisplayXRWindowSpaceUI
/// + all controls programmatically). Reference a DisplayXRDisplay rig in the
/// inspector or leave empty to auto-find.
/// </summary>
public class DisplayXRTuningUI : MonoBehaviour
{
    [Header("Target rig")]
    [Tooltip("DisplayXR rig to drive. Auto-found in scene if left null.")]
    public DisplayXRDisplay displayRig;

    [Header("Layer placement (fractional window coords)")]
    [Range(0f, 1f)] public float panelX = 0.02f;
    [Range(0f, 1f)] public float panelY = 0.65f;
    [Range(0f, 1f)] public float panelWidth = 0.30f;
    [Range(0f, 1f)] public float panelHeight = 0.32f;
    [Range(-0.05f, 0.05f)] public float disparity;

    [Header("Tunable ranges")]
    public float ipdMin = 0.0f;
    public float ipdMax = 2.0f;
    [Tooltip("0 = use display's physical height. Slider lets you override.")]
    public float vHeightMin = 0.05f;
    public float vHeightMax = 1.5f;

    private const int kRTWidth = 1024;
    private const int kRTHeight = 1024;

    private Slider m_IpdSlider;
    private Slider m_VHeightSlider;
    private Text m_IpdValueText;
    private Text m_VHeightValueText;
    private Text m_ModeButtonLabel;
    private Button m_ModeButton;
    private Font m_Font;

    // Native rendering-mode access. We mirror the DllImports here so the
    // test app can call them without depending on internals of the
    // displayxr-unity package's DisplayXRNative class.
    private const string kNativeLib = "displayxr_unity";
    [DllImport(kNativeLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int displayxr_standalone_enumerate_rendering_modes(
        uint capacity, out uint count,
        [MarshalAs(UnmanagedType.LPArray)] uint[] modeIndices,
        System.IntPtr modeNames, // not used (would need fixed-byte buffer)
        [MarshalAs(UnmanagedType.LPArray)] uint[] viewCounts,
        [MarshalAs(UnmanagedType.LPArray)] uint[] tileColumns,
        [MarshalAs(UnmanagedType.LPArray)] uint[] tileRows,
        [MarshalAs(UnmanagedType.LPArray)] uint[] viewWidthPixels,
        [MarshalAs(UnmanagedType.LPArray)] uint[] viewHeightPixels,
        [MarshalAs(UnmanagedType.LPArray)] float[] viewScaleX,
        [MarshalAs(UnmanagedType.LPArray)] float[] viewScaleY,
        [MarshalAs(UnmanagedType.LPArray)] int[] hardwareDisplay3D);

    [DllImport(kNativeLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int displayxr_standalone_request_rendering_mode(uint modeIndex);

    private uint[] m_ModeIndices;
    private string[] m_ModeNames;
    private bool[] m_ModeIs3D;
    private int m_CurrentModeArrayIdx = -1;

    void Start()
    {
        if (displayRig == null) displayRig = Object.FindFirstObjectByType<DisplayXRDisplay>();

        // Pick a built-in font that ships with Unity. LegacyRuntime.ttf is
        // the modern default; Arial fallback for older Unity versions.
        m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (m_Font == null) m_Font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        BuildPanel();
    }

    void BuildPanel()
    {
        // ---- root canvas (child of this gameobject so it's tied to scene lifecycle) ----
        var canvasGO = new GameObject("DisplayXR_Tuning_Canvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        canvasGO.layer = LayerMask.NameToLayer("UI");

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; // wsui will switch this

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(kRTWidth, kRTHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // Attach DisplayXRWindowSpaceUI — submits Canvas RT as a window-space layer.
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
        panelImg.color = new Color(0.06f, 0.07f, 0.10f, 0.92f);

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
        layout.padding = new RectOffset(40, 32, 32, 32);
        layout.spacing = 24;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // ---- title ----
        var title = MakeText(panelGO.transform, "Title", "DisplayXR Tuning", 36, FontStyle.Bold);
        title.color = Color.white;
        var titleLE = title.gameObject.AddComponent<LayoutElement>();
        titleLE.preferredHeight = 50;

        // ---- IPD ----
        BuildSliderRow(panelGO.transform, "IPD",
            ipdMin, ipdMax,
            displayRig != null ? displayRig.ipdFactor : 1.0f,
            v =>
            {
                if (displayRig != null) displayRig.ipdFactor = v;
                if (m_IpdValueText != null) m_IpdValueText.text = v.ToString("0.00");
            },
            out m_IpdSlider, out m_IpdValueText);

        // ---- vHeight ----
        // 0 means "use physical display height" — show "auto" rather than a number.
        BuildSliderRow(panelGO.transform, "Display Height",
            vHeightMin, vHeightMax,
            (displayRig != null && displayRig.virtualDisplayHeight > 0f)
                ? displayRig.virtualDisplayHeight : 0.30f,
            v =>
            {
                if (displayRig != null) displayRig.virtualDisplayHeight = v;
                if (m_VHeightValueText != null) m_VHeightValueText.text = $"{v:0.00} m";
            },
            out m_VHeightSlider, out m_VHeightValueText);

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
        btnLE.preferredHeight = 70;

        m_ModeButtonLabel = MakeText(btnGO.transform, "Label", "Render Mode", 28, FontStyle.Bold);
        m_ModeButtonLabel.color = Color.white;
        m_ModeButtonLabel.alignment = TextAnchor.MiddleCenter;
        var labelRT = m_ModeButtonLabel.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        // Try to enumerate modes now; if the standalone session isn't ready
        // yet, retry from Update().
        TryEnumerateModes();
    }

    void Update()
    {
        // Push wsui placement changes from inspector edits.
        var wsui = GetComponentInChildren<DisplayXRWindowSpaceUI>();
        if (wsui != null)
        {
            wsui.positionX = panelX;
            wsui.positionY = panelY;
            wsui.width = panelWidth;
            wsui.height = panelHeight;
            wsui.disparity = disparity;
        }

        // Mode list may not be ready until the standalone session has begun.
        if (m_ModeNames == null) TryEnumerateModes();
    }

    void TryEnumerateModes()
    {
        const uint kCapacity = 16;
        try
        {
            uint[] indices = new uint[kCapacity];
            uint[] viewCounts = new uint[kCapacity];
            uint[] tileC = new uint[kCapacity];
            uint[] tileR = new uint[kCapacity];
            uint[] vw = new uint[kCapacity];
            uint[] vh = new uint[kCapacity];
            float[] sx = new float[kCapacity];
            float[] sy = new float[kCapacity];
            int[] hw3d = new int[kCapacity];
            uint count = 0;
            int ok = displayxr_standalone_enumerate_rendering_modes(
                kCapacity, out count, indices, System.IntPtr.Zero,
                viewCounts, tileC, tileR, vw, vh, sx, sy, hw3d);
            if (ok == 0 || count == 0)
                return;

            m_ModeIndices = new uint[count];
            m_ModeNames = new string[count];
            m_ModeIs3D = new bool[count];
            for (int i = 0; i < count; i++)
            {
                m_ModeIndices[i] = indices[i];
                m_ModeIs3D[i] = hw3d[i] != 0;
                // We don't have the real mode-name strings without a fixed
                // marshalled buffer; synthesize a friendly label. Mode 0 is
                // typically 2D, others are 3D variants.
                m_ModeNames[i] = SynthesizeModeName(indices[i], hw3d[i] != 0,
                    tileC[i], tileR[i], viewCounts[i]);
            }

            // Default selection: first hw3d mode if any, else first.
            m_CurrentModeArrayIdx = 0;
            for (int i = 0; i < count; i++)
            {
                if (m_ModeIs3D[i]) { m_CurrentModeArrayIdx = i; break; }
            }
            UpdateModeLabel();
        }
        catch (System.EntryPointNotFoundException) { /* old plugin — ignore */ }
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
        if (m_ModeIndices == null || m_ModeIndices.Length == 0) return;
        m_CurrentModeArrayIdx = (m_CurrentModeArrayIdx + 1) % m_ModeIndices.Length;
        try
        {
            displayxr_standalone_request_rendering_mode(m_ModeIndices[m_CurrentModeArrayIdx]);
        }
        catch (System.EntryPointNotFoundException) { }
        UpdateModeLabel();
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
        rowLE.preferredHeight = 80;

        var labelText = MakeText(rowGO.transform, "Label", label, 22, FontStyle.Normal);
        labelText.color = new Color(0.75f, 0.78f, 0.85f, 1f);
        var labelRT = labelText.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 0.55f);
        labelRT.anchorMax = new Vector2(0.7f, 1f);
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        valueText = MakeText(rowGO.transform, "Value", initial.ToString("0.00"), 22, FontStyle.Bold);
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
        bgRT.sizeDelta = new Vector2(0, 6);
        bgGO.AddComponent<Image>().color = new Color(0.18f, 0.20f, 0.25f, 1f);

        var fillAreaGO = MakeUIObject("Fill Area", sliderGO.transform);
        var fillAreaRT = fillAreaGO.GetComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0, 0.5f);
        fillAreaRT.anchorMax = new Vector2(1, 0.5f);
        fillAreaRT.pivot = new Vector2(0.5f, 0.5f);
        fillAreaRT.offsetMin = new Vector2(0, -3);
        fillAreaRT.offsetMax = new Vector2(0, 3);

        var fillGO = MakeUIObject("Fill", fillAreaGO.transform);
        var fillRT = fillGO.GetComponent<RectTransform>();
        fillRT.anchorMin = new Vector2(0, 0);
        fillRT.anchorMax = new Vector2(1, 1);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        fillGO.AddComponent<Image>().color = new Color(0.29f, 0.62f, 1.0f, 1f);

        var handleAreaGO = MakeUIObject("Handle Slide Area", sliderGO.transform);
        var handleAreaRT = handleAreaGO.GetComponent<RectTransform>();
        handleAreaRT.anchorMin = new Vector2(0, 0);
        handleAreaRT.anchorMax = new Vector2(1, 1);
        handleAreaRT.offsetMin = new Vector2(10, 0);
        handleAreaRT.offsetMax = new Vector2(-10, 0);

        var handleGO = MakeUIObject("Handle", handleAreaGO.transform);
        var handleRT = handleGO.GetComponent<RectTransform>();
        handleRT.sizeDelta = new Vector2(20, 20);
        var handleImg = handleGO.AddComponent<Image>();
        handleImg.color = Color.white;

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
