// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: BSL-1.0

using DisplayXR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Avatar-style speech-bubble demo (issue #439/#491 Local2D over #131 zones),
/// rebuilt around the simple-window model — no in-app region editor, no virtual
/// window. The app is a plain movable OS window (DisplayXRTransparentOverlay's
/// simple-window mode); press B to toggle its decoration.
///
/// Layout keys off the REAL window's client rect (Screen.width/height in
/// simple-window mode), split into two horizontal bands by
/// <see cref="bubbleBandFraction"/> (default 0.25 = avatar's top-25% bubble /
/// bottom-75% tiger):
///   - 3D tiger zone = the bottom band → pushed as the XR_EXT_display_zones
///     3D-zone rect (and the legacy canvas sub-rect, for older runtimes /
///     the silhouette mapping). The runtime Kooima-frames the tiger into it.
///   - 2D bubble band = the top band → a DisplayXRLocal2D layer composited
///     "glass over 3D". Its rect is also pushed as the overlay surround rect so
///     the click-through region (SetWindowRgn) unions the bubble in (it sits
///     outside the tiger silhouette and would otherwise be clipped away).
///
/// Auto-installs via RuntimeInitializeOnLoadMethod. Transparent output renders
/// only in a built player (Editor Preview is black, but geometry validates).
/// </summary>
public class TigerSpeechBubble : MonoBehaviour
{
    [Header("Bubble text")]
    [TextArea] public string title = "Hi, I'm Leo";
    [TextArea] public string body =
        "your on-screen assistant. I can open apps, find your files, " +
        "and keep your windows tidy. Just ask!\n\nSo, how can I help you today?";

    [Header("Layout")]
    [Tooltip("Height of the top 2D bubble band as a fraction of the window " +
             "client height. The tiger 3D zone fills the rest (the bottom). " +
             "0.25 matches the avatar's top-25% bubble / bottom-75% tiger.")]
    [Range(0.1f, 0.5f)] public float bubbleBandFraction = kDefaultBubbleBandFraction;

    [Header("Layout editor (trimmed Ctrl+Shift+L)")]
    [Tooltip("Hold Ctrl+Shift and press this key to toggle Layout mode, then " +
             "drag (left button) to set the 2D/3D split line. This is the " +
             "surviving, SR-safe half of the old Ctrl+Shift+L: the split sits " +
             "deep-interior, away from the window frame the weaver subclass " +
             "claims, so its mouse-downs reach us. (Window move = right-drag, " +
             "resize = Ctrl+arrows — both elsewhere.)")]
    public Key layoutModeKey = Key.L;

    // PlayerPrefs key for the persisted split. Read by BOTH the launch seed (so
    // the zone-sized eye RT matches the chosen split at startup) and OnEnable.
    private const string kBandPrefKey = "dxr_bubbleBandFraction";

    private bool m_LayoutMode;
    private DragRotateCube[] m_SuspendedDrag;
    private Text m_TitleText, m_BodyText;   // swapped to a banner in Layout mode

    // Canonical avatar split (top-25% bubble / bottom-75% tiger). Also used by the
    // early SubsystemRegistration seed (no scene instance exists that early, so the
    // launch zone must be derived from a constant).
    // Shipped default layout = the tuned layout saved on the dev machine (so a
    // fresh install / partner starts here, not at the avatar 0.25 / 1000x1500).
    // Per-user changes still persist on top via PlayerPrefs.
    private const float kDefaultBubbleBandFraction = 0.33f;
    // ProjectSettings born-windowed default (840x1448) — fallback when Screen.* is
    // not yet populated at SubsystemRegistration time.
    private const int kDefaultPanelW = 840;
    private const int kDefaultPanelH = 1448;

    private Font m_Font;
    private int m_PanelW, m_PanelH;     // real window client size (px)

    // Bubble — its own Local2D layer (#439/#491), composited "glass over 3D".
    private GameObject m_BubbleL2DGO;
    private DisplayXRLocal2D m_BubbleL2D;
    private RectTransform m_BubbleRT;
    private RectTransform m_TailRT;
    private VerticalLayoutGroup m_BubbleLayout;

    private bool m_CanvasRectPushed;
    private bool m_SurroundRectPushed;
    private RectInt m_LastZoneRect = new RectInt(-1, -1, -1, -1);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInstall()
    {
        if (Object.FindAnyObjectByType<TigerSpeechBubble>() != null) return;
        var go = new GameObject("TigerSpeechBubble");
        go.AddComponent<TigerSpeechBubble>();
        Debug.Log("[TigerSpeechBubble] Auto-installed into scene.");
    }

    // Seed the 3D-zone rect BEFORE the XR loader enumerates view-configuration
    // views (which happens during XR init, before any MonoBehaviour Awake). The
    // plugin's xrEnumerateViewConfigurationViews hook reads this rect to size
    // Unity's per-eye swapchain to the zone extent, so the tiger renders at the
    // zone's recommended view size (== zone rect extent) and the runtime composite
    // is a clean 1:1-aspect blit into the zone — avatar-faithful, zone-confined.
    // The per-frame push in ApplyLayout keeps the rect live afterwards; for the
    // born-windowed fixed-size demo they coincide. (A later resize would need a
    // session restart to re-size the eye RT — currently moot: resize is blocked.)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void SeedLaunchZone()
    {
        // Use the persisted window size (remembered by DemoWindowController) so
        // the zone-sized eye RT matches the restored window; fall back to Screen.*
        // then the born-windowed default.
        int panelW = PlayerPrefs.GetInt("dxr_winW", Screen.width  > 0 ? Screen.width  : kDefaultPanelW);
        int panelH = PlayerPrefs.GetInt("dxr_winH", Screen.height > 0 ? Screen.height : kDefaultPanelH);
        // Use the persisted split (set in Layout mode) so the zone-sized eye RT
        // matches the chosen split at launch; fall back to the avatar default.
        float frac = PlayerPrefs.GetFloat(kBandPrefKey, kDefaultBubbleBandFraction);
        int splitY = Mathf.RoundToInt(panelH * frac);
        splitY = Mathf.Clamp(splitY, 1, panelH - 1);
        try
        {
            DisplayXRNative.displayxr_set_3d_zone_rect(0, splitY, panelW, panelH - splitY);
            Debug.Log($"[TigerSpeechBubble] Seeded launch 3D-zone rect (0,{splitY},{panelW},{panelH - splitY}) " +
                      $"before XR init (eye RT will be sized to the zone extent).");
        }
        catch (System.EntryPointNotFoundException) { }
    }

    void OnEnable()
    {
        m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (m_Font == null) m_Font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        // Restore the persisted split (matches the launch seed above).
        bubbleBandFraction = Mathf.Clamp(
            PlayerPrefs.GetFloat(kBandPrefKey, bubbleBandFraction), 0.1f, 0.5f);
    }

    void OnDisable()
    {
        ClearSurroundRect();
        ClearCanvasRect();
        if (m_BubbleL2DGO != null)
        {
            // DisplayXRLocal2D.OnDisable clears the native layer + RT.
            if (Application.isPlaying) Destroy(m_BubbleL2DGO);
            else DestroyImmediate(m_BubbleL2DGO);
            m_BubbleL2DGO = null;
        }
        m_BubbleRT = null; m_TailRT = null; m_BubbleL2D = null;
    }

    void Update()
    {
        // Window client size. The transparent OVERLAY (not Unity's window) is
        // what the runtime renders into — Unity is cloaked off-screen, so
        // Screen.* is frozen/wrong here. Use the overlay's live client size; it
        // also tracks moves/resizes from the B-decoration toggle.
        if (!TryGetWindowSize(out int w, out int h)) return;
        m_PanelW = w; m_PanelH = h;

        if (m_BubbleL2DGO == null)
            BuildUI();

#if UNITY_STANDALONE_WIN
        PollLayoutEditor();   // may update bubbleBandFraction before reflow
#endif
        ApplyLayout();
    }

#if UNITY_STANDALONE_WIN
    // Trimmed Ctrl+Shift+L: toggle Layout mode, then left-drag to set the 2D/3D
    // split. The split line sits deep in the interior (≈25% down), far from the
    // window frame the SR weaver subclass claims, so its mouse-downs reach us via
    // the plugin's polled pointer (Unity input is frozen on the cloaked HWND).
    // Reflow is live — the bubble band and tiger zone resize as you drag, which
    // IS the visual feedback (no separate handle needed). The change persists so
    // the next launch's zone-sized eye RT matches the chosen split.
    //
    // Caveat (same as window resize): the per-eye RT is sized to the zone at
    // LAUNCH, so a live split change stretches the tiger until the next restart,
    // when the persisted split is applied crisply.
    private void PollLayoutEditor()
    {
        if (Application.isEditor) return;
        var kb = Keyboard.current;
        if (kb != null)
        {
            bool ctrl  = kb.leftCtrlKey.isPressed  || kb.rightCtrlKey.isPressed;
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (ctrl && shift && layoutModeKey != Key.None
                && kb[layoutModeKey].wasPressedThisFrame)
                SetLayoutMode(!m_LayoutMode);
        }
        if (!m_LayoutMode || m_PanelH <= 0) return;

        DisplayXRNative.displayxr_get_overlay_pointer(out _, out int cy, out int buttons);
        if ((buttons & 1) != 0 && cy >= 0)
            bubbleBandFraction = Mathf.Clamp((float)cy / m_PanelH, 0.1f, 0.5f);
    }

    private void SetLayoutMode(bool on)
    {
        if (on == m_LayoutMode) return;
        m_LayoutMode = on;
        if (on)
        {
            // Suspend scene rotation so the split drag doesn't also spin the tiger.
            m_SuspendedDrag = FindObjectsByType<DragRotateCube>(FindObjectsSortMode.None);
            foreach (var d in m_SuspendedDrag) if (d != null) d.enabled = false;
            // Banner in the bubble so it's obvious we're editing (the bubble +
            // tiger also reflow live as you drag, which shows the split moving).
            if (m_TitleText != null) m_TitleText.text = "LAYOUT MODE";
            if (m_BodyText != null)  m_BodyText.text  =
                "Drag up / down to set the 2D / 3D split.\nCtrl+Shift+L to exit.";
        }
        else
        {
            if (m_SuspendedDrag != null)
                foreach (var d in m_SuspendedDrag) if (d != null) d.enabled = true;
            m_SuspendedDrag = null;
            if (m_TitleText != null) m_TitleText.text = title;
            if (m_BodyText != null)  m_BodyText.text  = body;
            // Persist the chosen split (read back by the launch seed + OnEnable).
            PlayerPrefs.SetFloat(kBandPrefKey, bubbleBandFraction);
            PlayerPrefs.Save();
        }
        Debug.Log($"[TigerSpeechBubble] Layout mode {(on ? "ON — drag to set the 2D/3D split" : "OFF")} " +
                  $"(split={bubbleBandFraction:P0}; restart for crisp rendering at the new split)");
    }
#endif

    // Overlay client size (built transparent app), falling back to the runtime
    // render-target size, then Screen.* (editor / no overlay).
    private bool TryGetWindowSize(out int w, out int h)
    {
        w = 0; h = 0;
        try
        {
            DisplayXRNative.displayxr_get_overlay_size(out int ow, out int oh);
            if (ow > 0 && oh > 0) { w = ow; h = oh; return true; }
        }
        catch (System.EntryPointNotFoundException) { }
        try
        {
            DisplayXRNative.displayxr_get_render_target_size(out uint rw, out uint rh);
            if (rw > 0 && rh > 0) { w = (int)rw; h = (int)rh; return true; }
        }
        catch (System.EntryPointNotFoundException) { }
        if (Screen.width > 0 && Screen.height > 0)
        {
            w = Screen.width; h = Screen.height; return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- build ---

    void BuildUI()
    {
        // The speech bubble is its own Local2D layer (#439/#491) — composited
        // "glass over 3D" at the 2D-zone rect. This is the VK-avatar design.
        var l2dGO = new GameObject("TigerBubble_Local2D",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        l2dGO.SetActive(false);
        l2dGO.transform.SetParent(transform, false);
        l2dGO.layer = LayerMask.NameToLayer("UI");
        m_BubbleL2D = l2dGO.AddComponent<DisplayXRLocal2D>();
        m_BubbleL2D.resolution = new Vector2Int(1024, 640);
        m_BubbleL2D.useExplicitRect = true; // we supply the exact panel-px rect
        BuildBubble(l2dGO.transform);
        m_BubbleL2DGO = l2dGO;
        l2dGO.SetActive(true);

        Debug.Log($"[TigerSpeechBubble] BUILD v11 (simple-window): client={m_PanelW}x{m_PanelH}; " +
                  $"bubbleBand={bubbleBandFraction:P0} (top); press B to toggle window decoration.");
    }

    void BuildBubble(Transform parent)
    {
        Color bubbleColor = new Color(0.06f, 0.07f, 0.11f, 0.86f);

        var panelGO = MakeUI("Bubble", parent);
        m_BubbleRT = panelGO.GetComponent<RectTransform>();
        // Panel fills the Local2D canvas, leaving a bottom strip for the tail.
        m_BubbleRT.anchorMin = new Vector2(0f, 0.16f);
        m_BubbleRT.anchorMax = new Vector2(1f, 1f);
        m_BubbleRT.offsetMin = Vector2.zero;
        m_BubbleRT.offsetMax = Vector2.zero;
        var img = panelGO.AddComponent<Image>();
        img.sprite = RoundedRect();
        img.type = Image.Type.Sliced;
        img.color = bubbleColor;

        var tailGO = MakeUI("Tail", panelGO.transform);
        tailGO.AddComponent<LayoutElement>().ignoreLayout = true;
        m_TailRT = tailGO.GetComponent<RectTransform>();
        m_TailRT.anchorMin = new Vector2(0.5f, 0f);
        m_TailRT.anchorMax = new Vector2(0.5f, 0f);
        m_TailRT.pivot = new Vector2(0.5f, 1f); // hang downward from the panel bottom
        m_TailRT.sizeDelta = new Vector2(90f, 80f);
        m_TailRT.anchoredPosition = new Vector2(0f, 4f);
        var tailImg = tailGO.AddComponent<Image>();
        tailImg.sprite = TriangleDown();
        tailImg.type = Image.Type.Simple;
        tailImg.color = bubbleColor;

        m_BubbleLayout = panelGO.AddComponent<VerticalLayoutGroup>();
        m_BubbleLayout.childAlignment = TextAnchor.MiddleCenter;
        m_BubbleLayout.childControlWidth = true;
        m_BubbleLayout.childControlHeight = true;
        m_BubbleLayout.childForceExpandWidth = true;
        m_BubbleLayout.padding = new RectOffset(48, 48, 40, 40);
        m_BubbleLayout.spacing = 10;

        m_TitleText = MakeText(panelGO.transform, "Title", title, 72, FontStyle.Bold);
        m_TitleText.alignment = TextAnchor.MiddleCenter;
        m_TitleText.color = Color.white;
        m_BodyText = MakeText(panelGO.transform, "Body", body, 48, FontStyle.Normal);
        m_BodyText.alignment = TextAnchor.MiddleCenter;
        m_BodyText.color = new Color(0.82f, 0.86f, 0.95f, 1f);
    }

    // ----------------------------------------------------------- layout/apply ---

    void ApplyLayout()
    {
        if (m_BubbleL2D == null) return;

        // Split the client rect: top band = bubble, bottom band = tiger zone.
        int splitY = Mathf.RoundToInt(m_PanelH * bubbleBandFraction);
        splitY = Mathf.Clamp(splitY, 1, m_PanelH - 1);

        // --- 2D bubble band (top): place the Local2D layer + union it into the
        // click-through region (it sits outside the tiger silhouette).
        m_BubbleL2D.SetExplicitRect(0, 0, m_PanelW, splitY);
        // In Layout mode, make the WHOLE window catch clicks (union a full-window
        // surround rect) so the split drag registers anywhere interior, not just
        // over the bubble/tiger. Normal mode unions only the bubble band.
        if (m_LayoutMode)
            PushSurroundRect(0, 0, m_PanelW, m_PanelH);
        else
            PushSurroundRect(0, 0, m_PanelW, splitY);

        // --- 3D tiger zone (bottom): the runtime Kooima-frames the tiger here.
        var zone = new RectInt(0, splitY, m_PanelW, m_PanelH - splitY);
        if (!zone.Equals(m_LastZoneRect))
        {
            PushCanvasRect(zone.x, zone.y, zone.width, zone.height);
            m_LastZoneRect = zone;
        }
    }

    // ------------------------------------------------------------ canvas rect ---

    private void PushCanvasRect(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        // XR_EXT_display_zones: push the 3D-zone rect so the runtime Kooima-frames
        // the tiger INTO this rect (the rect IS the canvas). The plugin gates the
        // legacy canvas-rect call off whenever the runtime supports zones; when it
        // does not (older runtime), the zone push is inert and the canvas-rect/
        // surround path still drives the 3D — a safe graceful fallback. The
        // silhouette/click-through still reads the canvas rect, so push both.
        try { DisplayXRNative.displayxr_set_3d_zone_rect(x, y, w, h); }
        catch (System.EntryPointNotFoundException) { }
        try { DisplayXRNative.displayxr_set_canvas_rect(x, y, (uint)w, (uint)h); }
        catch (System.EntryPointNotFoundException) { }
        m_CanvasRectPushed = true;
    }

    private void ClearCanvasRect()
    {
        if (!m_CanvasRectPushed) return;
        try { DisplayXRNative.displayxr_clear_3d_zone(); }
        catch (System.EntryPointNotFoundException) { }
        try { DisplayXRNative.displayxr_set_canvas_rect(0, 0, 0, 0); }
        catch (System.EntryPointNotFoundException) { }
        m_CanvasRectPushed = false;
    }

    // --------------------------------------------------------- click-through ---

    // Union the bubble band into the SetWindowRgn silhouette region (built by
    // DisplayXRTransparentOverlay's hit-mask each frame), so the bubble — which
    // lives outside the tiger silhouette — catches clicks and isn't clipped away.
    private void PushSurroundRect(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) { ClearSurroundRect(); return; }
        try
        {
            DisplayXRNative.displayxr_set_overlay_surround_rect(x, y, w, h);
            m_SurroundRectPushed = true;
        }
        catch (System.EntryPointNotFoundException) { }
    }

    private void ClearSurroundRect()
    {
        if (!m_SurroundRectPushed) return;
        try { DisplayXRNative.displayxr_set_overlay_surround_rect(0, 0, 0, 0); }
        catch (System.EntryPointNotFoundException) { }
        m_SurroundRectPushed = false;
    }

    // -------------------------------------------------------------- UI helpers ---

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

    // Procedural 9-slice rounded-rect sprite (white; tinted via Image.color).
    private static Sprite s_Rounded;
    private static Sprite RoundedRect()
    {
        if (s_Rounded != null) return s_Rounded;
        const int size = 128, radius = 52;
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

    // Procedural downward-pointing triangle (white; tinted via Image.color).
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
        for (int y = 0; y < size; y++)        // y=0 = bottom = apex
            for (int x = 0; x < size; x++)
            {
                float t = (float)y / (size - 1);
                float halfW = t * half;
                float d = halfW - Mathf.Abs((x + 0.5f) - half);
                float a = Mathf.Clamp01(d);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        s_Tail = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect);
        s_Tail.hideFlags = HideFlags.HideAndDontSave;
        return s_Tail;
    }
}
