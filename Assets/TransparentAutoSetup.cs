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
        var cube = GameObject.Find("Cube");
        Renderer[] hit = null;
        if (cube != null)
        {
            var r = cube.GetComponent<Renderer>();
            if (r != null)
                hit = new Renderer[] { r };

            // Make sure the Cube has a Collider — Physics.Raycast (used by the
            // transparent-overlay pointer events) needs one. CubeTest.unity is
            // a custom mesh so the default cube collider isn't there.
            if (cube.GetComponent<Collider>() == null)
                cube.AddComponent<BoxCollider>();
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

            // Visible behavior: click-to-tint + drag-to-rotate the cube.
            // Confirms left-click events reach the cube and exercises the
            // PointerPosition / PointerDelta polling API for drag interactions.
            if (cube != null && hit != null && hit.Length > 0)
            {
                var rotate = cube.GetComponent<DragRotateCube>();
                if (rotate == null)
                    rotate = cube.AddComponent<DragRotateCube>();
                rotate.overlay = overlay;
                rotate.target = hit[0];
            }
            installed++;
        }

        if (installed == 0)
            Debug.LogWarning("[TransparentAutoSetup] No DisplayXR rig cameras found; transparent overlay not installed.");
        else
            Debug.Log($"[TransparentAutoSetup] Transparent overlay installed on {installed} rig camera(s)" +
                      (cube != null ? " (Cube wired as hit region)" : " (no Cube found — whole window stays clickable)"));

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
