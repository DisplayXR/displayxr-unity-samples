// Auto-setup for the displayxr-unity-test-transparent project.
//
// At scene load: attaches DisplayXRTransparentOverlay to *every* DisplayXR rig
// camera in the scene (DisplayXRDisplay or DisplayXRCamera). CubeTest.unity
// ships with both rigs, and Unity may render either one depending on
// component init order — flipping clear flags on only Camera.main left the
// other rig's skybox showing in builds.

using UnityEngine;
using UnityEngine.XR;
using DisplayXR;

public static class TransparentAutoSetup
{
    // Near-mid-gray instead of magenta so silhouette-edge halos blend
    // invisibly into typical desktop backgrounds. Single source of truth —
    // both RequestChromaKey (runtime post-weave pass) and the component's
    // chromaKeyColor field (camera clear + LWA_COLORKEY) read it.
    // Trade-off: avatar/cube pixels that land exactly on (128,127,129) will
    // go transparent — keep materials clear of this color.
    static readonly Color s_ChromaKey = new Color(128f / 255f, 127f / 255f, 129f / 255f, 0f);

    // Must run BEFORE the OpenXR session is created — that's when the runtime
    // reads transparentBackgroundEnabled off XrWin32WindowBindingCreateInfoEXT
    // and decides whether to use the BitBlt swapchain path. AfterSceneLoad is
    // too late; SubsystemRegistration fires earlier than the XR loader init.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RequestTransparentSession()
    {
        DisplayXRTransparentOverlay.RequestTransparentSession();
        DisplayXRTransparentOverlay.RequestChromaKey(s_ChromaKey);
        Debug.Log("[TransparentAutoSetup] Requested runtime transparent-background mode + chroma key 0x00818081 (gray 128,127,129).");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        // Configurable target name — edit this constant when swapping the model.
        const string k_TargetName = "cartoon tiger in witches hat_ rigged and animated";

        var targetRoot = GameObject.Find(k_TargetName);
        Renderer targetRenderer = null;
        Renderer[] hit = null;
        if (targetRoot != null)
        {
            // SkinnedMeshRenderer sits on a child node for FBX models;
            // MeshRenderer on the root for the original cube. Either works.
            targetRenderer = targetRoot.GetComponentInChildren<Renderer>(true);
            if (targetRenderer != null)
            {
                hit = new[] { targetRenderer };

                // For SkinnedMeshRenderers the plugin manages a per-frame
                // BakeMesh + MeshCollider on the rootBone, so we add no
                // collider here — adding a BoxCollider on the SMR GO would
                // shadow the per-triangle collider on AABB-inside-but-off-
                // silhouette pixels. For plain MeshRenderers (e.g. the cube)
                // fall back to the deferred BoxCollider helper.
                if (!(targetRenderer is SkinnedMeshRenderer)
                    && targetRenderer.GetComponent<Collider>() == null
                    && targetRenderer.GetComponent<AutoBoxColliderFromRenderer>() == null)
                {
                    var auto = targetRenderer.gameObject.AddComponent<AutoBoxColliderFromRenderer>();
                    auto.source = targetRenderer;
                }
            }
        }

        int installed = 0;
        foreach (var cam in Camera.allCameras)
        {
            if (cam == null) continue;

            // Only target DisplayXR rig cameras — leave UI / overlay cameras alone.
            bool isRig = cam.GetComponent<DisplayXRDisplay>() != null
                      || cam.GetComponent<DisplayXRCamera>() != null;
            if (!isRig) continue;

            var overlay = cam.GetComponent<DisplayXRTransparentOverlay>();
            if (overlay == null)
                overlay = cam.gameObject.AddComponent<DisplayXRTransparentOverlay>();
            // The chromaKeyColor property setter re-pushes to camera clear +
            // native overlay, so this works even after OnEnable already ran
            // with the magenta default during AddComponent.
            overlay.chromaKeyColor = s_ChromaKey;
            if (hit != null)
                overlay.clickableRenderers = hit;

            // Wire up the pointer-event API for the click-through test. These
            // replace OnMouseDown for transparent-overlay mode (Unity's input
            // system Mouse.current.position is frozen for cloaked HWNDs).
            overlay.onPointerEnter.AddListener(r2 => Debug.Log($"[CubeTest] PointerEnter {r2?.name}"));
            overlay.onPointerExit .AddListener(r2 => Debug.Log($"[CubeTest] PointerExit  {r2?.name}"));
            overlay.onPointerDown .AddListener(r2 => Debug.Log($"[CubeTest] PointerDown  {r2?.name}"));
            overlay.onPointerUp   .AddListener(r2 => Debug.Log($"[CubeTest] PointerUp    {r2?.name}"));
            overlay.onPointerClick.AddListener(r2 => Debug.Log($"[CubeTest] PointerClick {r2?.name}"));

            // Drag-to-rotate the target. DragRotateCube goes on the ROOT so
            // transform.Rotate spins the whole model; the renderer is passed
            // as `target` for the click-comparison. The overlay reference is
            // resolved by DragRotateCube itself via DisplayXRRigManager.ActiveCamera
            // — necessary because the active-rig gate in the overlay means
            // only the currently-active rig fires events / updates PointerDelta.
            if (targetRoot != null && hit != null && hit.Length > 0)
            {
                var rotate = targetRoot.GetComponent<DragRotateCube>();
                if (rotate == null)
                    rotate = targetRoot.AddComponent<DragRotateCube>();
                rotate.target = hit[0];
            }

            // Mouse-wheel zoom — drives the display-centric rig's
            // virtualDisplayHeight (smaller = more zoom). The plugin v1.2.2+
            // no longer self-resizes the overlay; we read the raw wheel
            // delta via overlay.ConsumeWheelDelta() and apply it here.
            // Camera-centric rigs (DisplayXRCamera) are skipped — vHeight
            // is a display-rig concept.
            var displayRig = cam.GetComponent<DisplayXRDisplay>();
            if (displayRig != null)
            {
                var zoom = cam.GetComponent<WheelZoomVHeight>();
                if (zoom == null)
                    zoom = cam.gameObject.AddComponent<WheelZoomVHeight>();
                zoom.overlay = overlay;
                zoom.rig = displayRig;
            }

            // Foreground-only render (test-transparent#2): cull background
            // objects entirely past the display plane. Uses Camera.cullingMatrix
            // (not farClipPlane) so the overlay's Physics.Raycast hit detection
            // keeps working — see ClipAtDisplayPlane class doc for the why.
            if (cam.GetComponent<ClipAtDisplayPlane>() == null)
                cam.gameObject.AddComponent<ClipAtDisplayPlane>();

            // Tiger branch only: disable A/Q/D/E camera translation by
            // locking world X / Y on the rig each LateUpdate. W/S
            // (forward/back along world Z for a forward-facing rig)
            // still works → push tiger in / out of display plane.
            if (cam.GetComponent<LockToForwardAxis>() == null)
                cam.gameObject.AddComponent<LockToForwardAxis>();

            installed++;
        }

        if (installed == 0)
            Debug.LogWarning("[TransparentAutoSetup] No DisplayXR rig cameras found; transparent overlay not installed.");
        else
            Debug.Log($"[TransparentAutoSetup] Transparent overlay installed on {installed} rig camera(s)" +
                      (targetRoot != null ? $" ('{k_TargetName}' wired as hit region)" : $" (no '{k_TargetName}' found — whole window stays clickable)"));

        // Stop Unity from rendering the camera mirror view to the parent
        // window's backbuffer. Without this, the parent HWND fills with a
        // flat 2D version of Camera.main's output, and you see THAT through
        // the child overlay's transparent regions instead of the desktop.
        // The OpenXR eye-render path is unaffected; only the mirror blit
        // to the application's main window is suppressed.
        XRSettings.gameViewRenderMode = GameViewRenderMode.None;
        Debug.Log("[TransparentAutoSetup] XRSettings.gameViewRenderMode = None (suppress parent-window mirror).");
    }
}

/// <summary>
/// Drives a DisplayXRDisplay rig's virtualDisplayHeight from the
/// transparent overlay's mouse-wheel delta. Smaller vHeight = more zoom.
/// The window itself stays put — this is "zoom in window".
///
/// Multi-rig safe: only the active rig (per DisplayXRRigManager.ActiveCamera)
/// consumes the wheel delta. Inactive rigs see 0 because the active one
/// has already drained the native accumulator that frame.
/// </summary>
public class WheelZoomVHeight : MonoBehaviour
{
    public DisplayXRTransparentOverlay overlay;
    public DisplayXRDisplay rig;

    [Tooltip("Multiplicative scale per wheel notch (120 raw units). " +
             "0.05 = 5% per notch — wheel forward shrinks vHeight (zoom in).")]
    public float zoomPerNotch = 0.05f;

    [Tooltip("Clamp vHeight so the user can't zoom past sane limits.")]
    public float minVHeight = 0.05f;
    public float maxVHeight = 5.0f;

    void Update()
    {
        if (overlay == null || rig == null) return;

        // Multi-rig gate: only the active rig drains the wheel accumulator.
        // ActiveCamera comes from the DisplayXRRigManager static registry —
        // same source of truth used by the rigs themselves to gate Kooima
        // tunables. Without this, both rigs would race to ConsumeWheelDelta
        // and the result would depend on Update execution order.
        var cam = GetComponent<Camera>();
        if (cam != null && DisplayXRRigManager.ActiveCamera != cam) return;

        int delta = overlay.ConsumeWheelDelta();
        if (delta == 0) return;

        // Win32 WHEEL_DELTA = 120 per notch. Wheel forward (positive) →
        // factor < 1 → smaller vHeight → cube appears bigger.
        float notches = delta / 120f;
        float factor = Mathf.Pow(1f - zoomPerNotch, notches);
        rig.virtualDisplayHeight = Mathf.Clamp(
            rig.virtualDisplayHeight * factor, minVHeight, maxVHeight);
    }
}

/// <summary>
/// Adds a BoxCollider to its GameObject sized from the source Renderer's
/// world-space bounds, converted into local space. Waits a few frames so
/// SkinnedMeshRenderer.bounds is populated, then self-destructs.
///
/// Why not use Renderer.localBounds directly: for Mixamo / cm-source FBXs,
/// SkinnedMeshRenderer.localBounds is in the mesh's source units and ignores
/// the FBX import scale, producing a multi-hundred-meter box. renderer.bounds
/// is the live world-space AABB that respects all transforms.
/// </summary>
public class AutoBoxColliderFromRenderer : MonoBehaviour
{
    public Renderer source;
    int m_Frame;

    void LateUpdate()
    {
        if (source == null) { Destroy(this); return; }
        if (++m_Frame < 5) return;

        Bounds b = source.bounds;
        if (b.size.sqrMagnitude < 1e-6f) return;  // wait for valid bounds

        var box = GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();

        Vector3 ls = transform.lossyScale;
        box.center = transform.InverseTransformPoint(b.center);
        box.size = new Vector3(
            b.size.x / Mathf.Max(1e-4f, Mathf.Abs(ls.x)),
            b.size.y / Mathf.Max(1e-4f, Mathf.Abs(ls.y)),
            b.size.z / Mathf.Max(1e-4f, Mathf.Abs(ls.z)));

        Debug.Log($"[AutoBoxCollider] {gameObject.name} sized: " +
                  $"local center={box.center} size={box.size} | world bounds={box.bounds}");
        Destroy(this);
    }
}

/// <summary>
/// Foreground-only render switch: turns on the plugin's per-view
/// `clipAtDisplayPlane` tunable on the active rig. The plugin's native
/// Kooima then overrides each view's projection far_z with that view's
/// own |eye.z|*m2v (display-centric) or 1/invConvergenceDistance
/// (camera-centric). Resolves displayxr-unity-test-transparent#2.
///
/// Per-view, N-view safe: the native side iterates over all xrLocateViews
/// outputs (2, 4, 9, ... views depending on render mode) and computes a
/// fresh far per view — sub-mm eye-Z differences propagate to sub-mm
/// far differences automatically. C# only sets the flag.
///
/// Hit-test stays at full range: Camera.farClipPlane is untouched. The
/// transparent-overlay hit test (Physics.Raycast + per-triangle bestT
/// init) still uses farClipPlane in Unity units. Behind-display-plane
/// geometry: invisible (clipped) AND uninclickable past the bestT init —
/// "invisible = unclickable" alignment is intentional for foreground-only.
///
/// Slider interaction: FarClipDiopterSlider writes Camera.farClipPlane.
/// With the clip-at-display-plane flag active, the rig still uses
/// Camera.farClipPlane as the BASE tunables.farZ, but native overrides
/// it per-view before building the projection. Slider therefore controls
/// hit-test reach but not rendering. To use the slider as a standalone
/// diagnostic (slider value drives rendering far again), disable this
/// component in the inspector.
/// </summary>
public class ClipAtDisplayPlane : MonoBehaviour
{
    DisplayXRCamera m_CamCentric;
    DisplayXRDisplay m_DisplayCentric;

    void Awake()
    {
        m_CamCentric = GetComponent<DisplayXRCamera>();
        m_DisplayCentric = GetComponent<DisplayXRDisplay>();
    }

    void OnEnable()
    {
        if (m_DisplayCentric != null) m_DisplayCentric.foregroundOnlyClip = true;
        if (m_CamCentric != null)     m_CamCentric.foregroundOnlyClip = true;
        Debug.Log($"[ClipAtDisplayPlane] Enabled foregroundOnlyClip on '{gameObject.name}' " +
                  $"(display:{m_DisplayCentric != null} cam:{m_CamCentric != null}).");
    }

    void OnDisable()
    {
        // Clearing the flag lets the rig's next LateUpdate push tunables
        // with clip-at-display-plane = 0 → native goes back to using the
        // single Camera.farClipPlane for every view's far.
        if (m_DisplayCentric != null) m_DisplayCentric.foregroundOnlyClip = false;
        if (m_CamCentric != null)     m_CamCentric.foregroundOnlyClip = false;
    }
}
