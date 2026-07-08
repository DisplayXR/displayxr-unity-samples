// Diagnostic: dump cursor position + screen/camera dimensions on every left
// or right mouse click, plus a rate-limited periodic snapshot. Used to chase
// the "input only works in bottom half of window" bug for displayxr-unity#57.
// Attach to the Cube alongside CubeClickLog.

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class InputDiagnostic : MonoBehaviour
{
    private Camera m_Cam;
    private float m_NextPeriodicLog;
    private bool m_LeftWasDown;
    private bool m_RightWasDown;

    void Start()
    {
        m_Cam = Camera.main;
        if (m_Cam == null) m_Cam = FindAnyObjectByType<Camera>();
        Debug.Log($"[InputDiag] Start. Screen={Screen.width}x{Screen.height} " +
                  $"fullScreenMode={Screen.fullScreenMode} " +
                  $"currentResolution={Screen.currentResolution.width}x{Screen.currentResolution.height} " +
                  $"dpi={Screen.dpi}");
        if (m_Cam != null)
        {
            Debug.Log($"[InputDiag] Camera={m_Cam.name} pixelRect={m_Cam.pixelRect} " +
                      $"pixelW={m_Cam.pixelWidth} pixelH={m_Cam.pixelHeight} " +
                      $"rect={m_Cam.rect} aspect={m_Cam.aspect:F3}");
        }
    }

    void Update()
    {
        Vector2 pos = ReadCursor();

        // Periodic snapshot once per second
        if (Time.time >= m_NextPeriodicLog)
        {
            m_NextPeriodicLog = Time.time + 1f;
            Debug.Log($"[InputDiag] tick cursor={pos} Screen={Screen.width}x{Screen.height}" +
                      (m_Cam != null
                          ? $" cam.pixelW={m_Cam.pixelWidth} cam.pixelH={m_Cam.pixelHeight} cam.rect={m_Cam.rect}"
                          : ""));
        }

        // Edge-detected click logs (independent of OnMouseDown raycast gating —
        // fires whenever Unity sees the button event at all, regardless of
        // whether the cursor is over a collider).
        bool left = ReadButton(0);
        if (left && !m_LeftWasDown)
            Debug.Log($"[InputDiag] LEFT_DOWN cursor={pos} Screen={Screen.width}x{Screen.height}");
        m_LeftWasDown = left;

        bool right = ReadButton(1);
        if (right && !m_RightWasDown)
            Debug.Log($"[InputDiag] RIGHT_DOWN cursor={pos} Screen={Screen.width}x{Screen.height}");
        m_RightWasDown = right;
    }

    static Vector2 ReadCursor()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) return Mouse.current.position.ReadValue();
#endif
        return Input.mousePosition;
    }

    static bool ReadButton(int b)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return false;
        return b == 0 ? Mouse.current.leftButton.isPressed
             : b == 1 ? Mouse.current.rightButton.isPressed
             : Mouse.current.middleButton.isPressed;
#else
        return Input.GetMouseButton(b);
#endif
    }
}
