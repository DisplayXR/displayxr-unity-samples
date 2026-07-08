// Workaround for the persistent Windows "busy" (IDC_APPSTARTING) cursor in
// transparent-overlay mode.
//
// In opaque builds Windows shows the app-starting cursor briefly during
// process launch, then clears it when the app's main HWND signals readiness
// (typically when it processes its first WM_PAINT / paint cycle on a
// visible window). In transparent-overlay mode Unity's main HWND is cloaked
// (WS_EX_NOREDIRECTIONBITMAP) and parked off-screen — it never becomes a
// "visible, painting" window from Windows' POV, so the busy cursor never
// clears. The cursor stays as a spinning ring even though the app is
// fully interactive via the separate overlay HWND.
//
// Fix: force a SetCursor(IDC_ARROW) on the Unity thread at app start, and
// re-apply periodically in case Windows reverts. Unity's Cursor.SetCursor
// with (null, _, Auto) resolves to the OS default cursor on the active
// window; the PInvoke fallback below directly Win32-SetCursor's an arrow
// in case Unity's cursor path isn't reaching the visible HWND.

using System.Runtime.InteropServices;
using UnityEngine;

public static class CursorReset
{
    [DllImport("user32.dll")]
    private static extern System.IntPtr LoadCursorW(System.IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    private static extern System.IntPtr SetCursor(System.IntPtr hCursor);

    private const int IDC_ARROW = 32512;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        // Reset Unity-side cursor state.
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Win32 direct: force IDC_ARROW on the calling (Unity main) thread.
        // This affects the thread's "current cursor" which Windows uses when
        // a window's WndProc doesn't explicitly SetCursor in WM_SETCURSOR.
        var arrow = LoadCursorW(System.IntPtr.Zero, IDC_ARROW);
        if (arrow != System.IntPtr.Zero) SetCursor(arrow);

        // Drop a tiny driver MonoBehaviour onto a hidden GO so we can re-apply
        // every second — Windows can revert the cursor when the overlay HWND's
        // class default loses out to per-thread state.
        var go = new GameObject("CursorResetDriver");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<CursorResetDriver>();

        Debug.Log("[CursorReset] Forced IDC_ARROW at startup; driver installed for periodic refresh.");
    }

    public static void ApplyArrow()
    {
        var arrow = LoadCursorW(System.IntPtr.Zero, IDC_ARROW);
        if (arrow != System.IntPtr.Zero) SetCursor(arrow);
    }
}

public class CursorResetDriver : MonoBehaviour
{
    private float m_NextApply;

    void Update()
    {
        if (Time.unscaledTime < m_NextApply) return;
        m_NextApply = Time.unscaledTime + 1f;
        CursorReset.ApplyArrow();
    }
}
