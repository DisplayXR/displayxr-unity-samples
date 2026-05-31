// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: BSL-1.0

using DisplayXR;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Demo of the 2D surround feature (issue #131): a high-resolution 2D text
/// bubble composited POST-weave over the woven 3D tiger.
///
/// Unlike the window-space UI HUD (TigerTuningHUD, which composites pre-weave
/// and is only full-res at the display plane), the surround is written after
/// interlacing — so the text is always at full native panel resolution. The
/// tiger is shrunk into a centered canvas sub-rect; the bubble lives in the
/// surround region above it.
///
/// Auto-installs via RuntimeInitializeOnLoadMethod (no scene wiring). Coexists
/// with TigerTuningHUD (wsui) — different runtime mechanisms, no conflict.
/// SHIFT+B toggles visibility. Windows D3D12 built apps only.
/// </summary>
public class TigerSpeechBubble : MonoBehaviour
{
    // Canvas sub-rect for the 3D tiger, as fractions of the window. The bubble
    // sits in the surround region above this rect.
    [Range(0f, 1f)] public float tigerX = 0.20f;
    [Range(0f, 1f)] public float tigerY = 0.28f;
    [Range(0f, 1f)] public float tigerW = 0.60f;
    [Range(0f, 1f)] public float tigerH = 0.66f;

    [Header("Bubble")]
    [TextArea] public string title = "Hi, I'm Leo";
    [TextArea] public string body =
        "your on-screen assistant. I can open apps, find your files, " +
        "and keep your windows tidy. Just ask!\n\nSo, how can I help you today?";

    private GameObject m_CanvasGO;
    private DisplayXRSurround m_Surround;
    private Font m_Font;
    private int m_PanelW, m_PanelH;
    private bool m_Visible = true;

    // The bubble panel + its canvas, used to compute the bubble's window-pixel
    // rect each frame so the transparent overlay's click-through region catches
    // clicks on the bubble (it lives in the surround region, outside the 3D
    // silhouette). See PushBubbleRect / TryComputeBubbleWindowRect.
    private RectTransform m_CanvasRT;
    private RectTransform m_BubbleRT;
    private bool m_RectPushed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInstall()
    {
        if (Object.FindAnyObjectByType<TigerSpeechBubble>() != null) return;
        var go = new GameObject("TigerSpeechBubble");
        go.AddComponent<TigerSpeechBubble>();
        Debug.Log("[TigerSpeechBubble] Auto-installed into scene.");
    }

    void OnEnable()
    {
        m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (m_Font == null) m_Font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        // Defer BuildBubble until the runtime reports valid display dims — the
        // surround texture MUST match the window client area or the runtime
        // rejects it (XR_ERROR_VALIDATION_FAILURE). Update() polls and builds.
    }

    private bool TryResolvePanelDims()
    {
        // Use the runtime's actual weave-target size (the bound HWND client area),
        // NOT the display panel dims — the surround texture + canvas sub-rect must
        // match it or the runtime skips the surround blit (#131).
        if (DisplayXRSurround.TryGetTargetSize(Application.isEditor, out int w, out int h))
        {
            m_PanelW = w;
            m_PanelH = h;
            return true;
        }
        return false;
    }

    void OnDisable()
    {
        ClearBubbleRect();
        if (m_CanvasGO != null)
        {
            if (Application.isPlaying) Destroy(m_CanvasGO);
            else DestroyImmediate(m_CanvasGO);
            m_CanvasGO = null;
        }
        m_CanvasRT = null;
        m_BubbleRT = null;
    }

    void BuildBubble()
    {
        // Root canvas (the surround source). DisplayXRSurround takes it over as
        // a WorldSpace canvas and renders it into a panel-sized RGBA8 RT.
        var canvasGO = new GameObject("TigerBubble_Canvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.SetActive(false);
        canvasGO.transform.SetParent(transform, false);
        canvasGO.layer = LayerMask.NameToLayer("UI");
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; // surround switches this

        m_Surround = canvasGO.AddComponent<DisplayXRSurround>();
        m_Surround.resolution = new Vector2Int(m_PanelW, m_PanelH);
        m_Surround.setCanvasRect = true;
        m_Surround.canvasRect = new RectInt(
            Mathf.RoundToInt(tigerX * m_PanelW),
            Mathf.RoundToInt(tigerY * m_PanelH),
            Mathf.RoundToInt(tigerW * m_PanelW),
            Mathf.RoundToInt(tigerH * m_PanelH));

        // Soft-white comic speech bubble, top-center in the surround region above
        // the tiger, with a tail pointing down at it. Width ~40% of the window.
        Color bubbleColor = new Color(0.97f, 0.97f, 0.99f, 0.93f); // soft white
        m_CanvasRT = canvasGO.GetComponent<RectTransform>();
        var panelGO = MakeUI("Bubble", canvasGO.transform);
        var prt = panelGO.GetComponent<RectTransform>();
        m_BubbleRT = prt;
        prt.anchorMin = new Vector2(0.5f, 1f);
        prt.anchorMax = new Vector2(0.5f, 1f);
        prt.pivot = new Vector2(0.5f, 1f);
        prt.sizeDelta = new Vector2(m_PanelW * 0.40f, m_PanelH * 0.19f);
        prt.anchoredPosition = new Vector2(0f, -m_PanelH * 0.03f); // small top margin
        var img = panelGO.AddComponent<Image>();
        img.sprite = RoundedRect();
        img.type = Image.Type.Sliced;
        img.color = bubbleColor;

        // Comic tail: a triangle hanging off the bubble's bottom edge, pointing
        // down at the tiger (the canvas sub-rect sits just below the bubble).
        // ignoreLayout so the VerticalLayoutGroup doesn't reflow it.
        var tailGO = MakeUI("Tail", panelGO.transform);
        tailGO.AddComponent<LayoutElement>().ignoreLayout = true;
        var trt = tailGO.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0.5f, 0f);   // panel bottom-center
        trt.anchorMax = new Vector2(0.5f, 0f);
        trt.pivot = new Vector2(0.5f, 1f);        // hang downward from the top
        trt.sizeDelta = new Vector2(m_PanelW * 0.045f, m_PanelH * 0.05f);
        trt.anchoredPosition = new Vector2(-m_PanelW * 0.03f, m_PanelH * 0.004f); // slight left lean, tiny overlap
        var tailImg = tailGO.AddComponent<Image>();
        tailImg.sprite = TriangleDown();
        tailImg.color = bubbleColor;

        var layout = panelGO.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(40, 40, 30, 34);
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;

        var t = MakeText(panelGO.transform, "Title", title, 40, FontStyle.Bold);
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(0.11f, 0.13f, 0.18f, 1f);  // near-black ink
        var b = MakeText(panelGO.transform, "Body", body, 28, FontStyle.Normal);
        b.alignment = TextAnchor.MiddleCenter;
        b.color = new Color(0.24f, 0.27f, 0.34f, 1f);  // dark slate

        m_CanvasGO = canvasGO;
        // Keep the canvas (and its DisplayXRSurround) ALWAYS active so the canvas
        // sub-rect + surround registration persist; SHIFT+B toggles only the
        // bubble panel. Disabling the whole canvas would tear down the surround
        // and clear the sub-rect, snapping the tiger back to full-window.
        canvasGO.SetActive(true);
        m_BubbleRT.gameObject.SetActive(m_Visible);
    }

    void Update()
    {
        // Build once the runtime reports valid panel dims.
        if (m_CanvasGO == null)
        {
            if (TryResolvePanelDims()) BuildBubble();
            else return;
        }

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.bKey.wasPressedThisFrame &&
            (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) &&
            m_CanvasGO != null)
        {
            m_Visible = !m_Visible;
            if (m_BubbleRT != null) m_BubbleRT.gameObject.SetActive(m_Visible);
            Debug.Log($"[TigerSpeechBubble] toggle SHIFT+B → visible={m_Visible}");
        }

        PushBubbleRect();
    }

    // --- Click-through region for the bubble ---

    // The bubble sits in the 2D surround region, OUTSIDE the 3D tiger silhouette
    // that drives the transparent overlay's SetWindowRgn click-through. Without
    // help the OS would route clicks on the bubble straight to the desktop. Push
    // the bubble's window-pixel rect to the native overlay so it's UNION-ed into
    // the click-catching region (the empty surround around it still passes clicks
    // through). No-op outside the built transparent app (no overlay HWND).
    private void PushBubbleRect()
    {
        if (m_Visible && m_CanvasGO != null &&
            TryComputeBubbleWindowRect(out int x, out int y, out int w, out int h))
        {
            try { DisplayXRNative.displayxr_set_overlay_surround_rect(x, y, w, h); }
            catch (System.EntryPointNotFoundException) { }
            m_RectPushed = true;
        }
        else
        {
            ClearBubbleRect();
        }
    }

    private void ClearBubbleRect()
    {
        if (!m_RectPushed) return;
        try { DisplayXRNative.displayxr_set_overlay_surround_rect(0, 0, 0, 0); }
        catch (System.EntryPointNotFoundException) { }
        m_RectPushed = false;
    }

    // Bubble RectTransform → window-pixel rect (top-left origin), matching the
    // surround texture mapping (1 UI unit = 1 window pixel; flipY makes canvas-up
    // appear as window-up). Robust to the bubble's anchors/pivot: normalize the
    // panel's world corners against the canvas rect, then map to window pixels.
    private bool TryComputeBubbleWindowRect(out int x, out int y, out int w, out int h)
    {
        x = y = w = h = 0;
        if (m_BubbleRT == null || m_CanvasRT == null || m_Surround == null) return false;
        if (m_PanelW <= 0 || m_PanelH <= 0) return false;

        Vector3[] wc = new Vector3[4];
        m_BubbleRT.GetWorldCorners(wc); // 0=BL, 1=TL, 2=TR, 3=BR (world space)
        Rect cr = m_CanvasRT.rect;       // canvas-local rect (pivot-independent via xMin)
        if (cr.width <= 0f || cr.height <= 0f) return false;

        Vector3 bl = m_CanvasRT.InverseTransformPoint(wc[0]);
        Vector3 tr = m_CanvasRT.InverseTransformPoint(wc[2]);
        // Normalized 0..1 within the canvas, y-up (vMin = bottom, vMax = top).
        float uMin = Mathf.Clamp01((bl.x - cr.xMin) / cr.width);
        float uMax = Mathf.Clamp01((tr.x - cr.xMin) / cr.width);
        float vMin = Mathf.Clamp01((bl.y - cr.yMin) / cr.height);
        float vMax = Mathf.Clamp01((tr.y - cr.yMin) / cr.height);

        float left   = uMin * m_PanelW;
        float width  = (uMax - uMin) * m_PanelW;
        float height = (vMax - vMin) * m_PanelH;
        // Window y is top-left origin. flipY (default) maps canvas-top → window-top.
        float top = m_Surround.flipY ? (1f - vMax) * m_PanelH : vMin * m_PanelH;

        x = Mathf.RoundToInt(left);
        y = Mathf.RoundToInt(top);
        w = Mathf.RoundToInt(width);
        h = Mathf.RoundToInt(height);
        return w > 0 && h > 0;
    }

    // --- UI helpers ---

    GameObject MakeUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");
        return go;
    }

    Text MakeText(Transform parent, string name, string content, int size, FontStyle style)
    {
        var go = MakeUI(name, parent);
        var t = go.AddComponent<Text>();
        t.font = m_Font;
        t.fontSize = size;
        t.fontStyle = style;
        t.text = content;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    // Procedural 9-slice rounded-rect sprite (white, alpha inside; tinted via
    // Image.color). Same approach as TigerTuningHUD.
    private static Sprite s_Rounded;
    private static Sprite RoundedRect()
    {
        if (s_Rounded != null) return s_Rounded;
        const int size = 64, radius = 30;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int dx = (x < radius) ? (radius - 1 - x) : (x >= size - radius) ? (x - (size - radius)) : 0;
                int dy = (y < radius) ? (radius - 1 - y) : (y >= size - radius) ? (y - (size - radius)) : 0;
                float a = 1f;
                if (dx > 0 || dy > 0)
                    a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        s_Rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        s_Rounded.hideFlags = HideFlags.HideAndDontSave;
        return s_Rounded;
    }

    // Procedural downward-pointing triangle (white, alpha inside; tinted via
    // Image.color) for the comic speech-bubble tail. Apex at the bottom-center
    // (texture row 0), base across the top — so it hangs off the bubble's bottom
    // edge and points down at the tiger. ~1px edge antialiasing on the slants.
    private static Sprite s_Tail;
    private static Sprite TriangleDown()
    {
        if (s_Tail != null) return s_Tail;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)        // y=0 = bottom row = apex
            for (int x = 0; x < size; x++)
            {
                float t = (float)y / (size - 1);                 // 0 apex (bottom) → 1 base (top)
                float halfW = t * half;                          // triangle half-width at this row
                float d = halfW - Mathf.Abs((x + 0.5f) - half);  // >0 inside (≈px units)
                float a = Mathf.Clamp01(d);                      // 1px AA on the slanted edges
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        s_Tail = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        s_Tail.hideFlags = HideFlags.HideAndDontSave;
        return s_Tail;
    }
}
