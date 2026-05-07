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
    [Range(0f, 1f)] public float panelX = 0.02f;
    [Range(0f, 1f)] public float panelY = 0.65f;
    [Range(0f, 1f)] public float panelWidth = 0.20f;
    [Range(0f, 1f)] public float panelHeight = 0.32f;
    [Range(-0.05f, 0.05f)] public float disparity;

    [Header("Tunable ranges")]
    public float ipdMin = 0.0f;
    public float ipdMax = 1.0f;
    public float ipdDefault = 1.0f;
    [Tooltip("Multiplier over the rig's initial virtualDisplayHeight (set in editor).")]
    public float scaleMin = 0.5f;
    public float scaleMax = 1.5f;
    public float scaleDefault = 1.0f;

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

    // Native rendering-mode access. We mirror the DllImports here so the
    // test app can call them without depending on internals of the
    // displayxr-unity package's DisplayXRNative class.
    private const string kNativeLib = "displayxr_unity";
    [DllImport(kNativeLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int displayxr_standalone_enumerate_rendering_modes(
        uint capacity, out uint count,
        [Out, MarshalAs(UnmanagedType.LPArray)] uint[] modeIndices,
        System.IntPtr modeNames, // not used (would need fixed-byte buffer)
        [Out, MarshalAs(UnmanagedType.LPArray)] uint[] viewCounts,
        [Out, MarshalAs(UnmanagedType.LPArray)] uint[] tileColumns,
        [Out, MarshalAs(UnmanagedType.LPArray)] uint[] tileRows,
        [Out, MarshalAs(UnmanagedType.LPArray)] uint[] viewWidthPixels,
        [Out, MarshalAs(UnmanagedType.LPArray)] uint[] viewHeightPixels,
        [Out, MarshalAs(UnmanagedType.LPArray)] float[] viewScaleX,
        [Out, MarshalAs(UnmanagedType.LPArray)] float[] viewScaleY,
        [Out, MarshalAs(UnmanagedType.LPArray)] int[] hardwareDisplay3D);

    [DllImport(kNativeLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int displayxr_standalone_request_rendering_mode(uint modeIndex);

    [DllImport(kNativeLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int displayxr_standalone_get_rendering_mode_name(
        uint arraySlot,
        [MarshalAs(UnmanagedType.LPArray)] byte[] buffer,
        uint bufferSize);

    private uint[] m_ModeIndices;
    private string[] m_ModeNames;
    private bool[] m_ModeIs3D;
    private int m_CurrentModeArrayIdx = -1;

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
        if (displayRig != null) displayRig.ipdFactor = ipdDefault;
        BuildSliderRow(panelGO.transform, "IPD",
            ipdMin, ipdMax, ipdDefault,
            v =>
            {
                if (displayRig != null) displayRig.ipdFactor = v;
                if (m_IpdValueText != null) m_IpdValueText.text = v.ToString("0.00");
            },
            out m_IpdSlider, out m_IpdValueText);

        // ---- Scale (multiplier over initial vHeight) ----
        if (displayRig != null) displayRig.virtualDisplayHeight = m_InitialVHeight * scaleDefault;
        BuildSliderRow(panelGO.transform, "Scale",
            scaleMin, scaleMax, scaleDefault,
            v =>
            {
                if (displayRig != null) displayRig.virtualDisplayHeight = m_InitialVHeight * v;
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
        btnLE.preferredHeight = 70;

        m_ModeButtonLabel = MakeText(btnGO.transform, "Label", "Render Mode", 28, FontStyle.Bold);
        m_ModeButtonLabel.color = Color.white;
        m_ModeButtonLabel.alignment = TextAnchor.MiddleCenter;
        var labelRT = m_ModeButtonLabel.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        // Activate now → DisplayXRWindowSpaceUI.OnEnable fires with the right
        // resolution, OverlayCamera spins up, and the RT-→-swapchain copy
        // path picks up the populated panel hierarchy on the very next frame.
        canvasGO.SetActive(true);

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
            byte[] nameBuf = new byte[256];
            for (int i = 0; i < count; i++)
            {
                m_ModeIndices[i] = indices[i];
                m_ModeIs3D[i] = hw3d[i] != 0;

                // Fetch the runtime-reported display name for this slot.
                // Falls back to a synthesized label if the runtime returns
                // empty (older runtime, or modes without a name string).
                string name = null;
                System.Array.Clear(nameBuf, 0, nameBuf.Length);
                if (displayxr_standalone_get_rendering_mode_name((uint)i, nameBuf, (uint)nameBuf.Length) != 0)
                {
                    int len = 0;
                    while (len < nameBuf.Length && nameBuf[len] != 0) len++;
                    if (len > 0) name = System.Text.Encoding.UTF8.GetString(nameBuf, 0, len);
                }
                if (string.IsNullOrEmpty(name))
                    name = SynthesizeModeName(indices[i], hw3d[i] != 0,
                        tileC[i], tileR[i], viewCounts[i]);
                m_ModeNames[i] = name;
            }

            // Default selection: first hw3d mode if any, else first. The
            // runtime starts in its own default (typically mode 0, '2D'),
            // so push a request_rendering_mode for the chosen slot so the
            // visual output matches the label from the very first frame.
            // The runtime caches the last requested mode across session
            // restarts, so subsequent Play sessions in the same editor
            // launch already start in the right mode regardless of this.
            m_CurrentModeArrayIdx = 0;
            for (int i = 0; i < count; i++)
            {
                if (m_ModeIs3D[i]) { m_CurrentModeArrayIdx = i; break; }
            }
            try { displayxr_standalone_request_rendering_mode(m_ModeIndices[m_CurrentModeArrayIdx]); }
            catch (System.EntryPointNotFoundException) { }
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
        Debug.Log($"[TuningUI] CycleRenderMode invoked on '{name}'. m_ModeIndices len={(m_ModeIndices == null ? -1 : m_ModeIndices.Length)} curIdx={m_CurrentModeArrayIdx} indices=[{(m_ModeIndices == null ? "null" : string.Join(",", m_ModeIndices))}]");
        if (m_ModeIndices == null || m_ModeIndices.Length == 0) return;
        m_CurrentModeArrayIdx = (m_CurrentModeArrayIdx + 1) % m_ModeIndices.Length;
        try
        {
            uint mode = m_ModeIndices[m_CurrentModeArrayIdx];
            Debug.Log($"[TuningUI] Requesting rendering mode index={mode} (array slot {m_CurrentModeArrayIdx})");
            displayxr_standalone_request_rendering_mode(mode);
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
