// Diagnostic component for the displayxr-unity#57 click-through verification.
// Attached to the Cube by TransparentAutoSetup. Logs every mouse event Unity
// fires on the cube — proves PostMessage-forwarded clicks from the
// transparent overlay's wndproc reach the cloaked Unity HWND and trigger
// raycast hits. Logs land in displayxr.log next to the exe.

using UnityEngine;

public class CubeClickLog : MonoBehaviour
{
    void OnMouseDown()
    {
        Debug.Log($"[CubeTest] OnMouseDown fired (Input.mousePosition={Input.mousePosition})");
    }

    void OnMouseUp()
    {
        Debug.Log("[CubeTest] OnMouseUp fired");
    }

    void OnMouseEnter()
    {
        Debug.Log("[CubeTest] OnMouseEnter (cursor entered cube silhouette)");
    }

    void OnMouseExit()
    {
        Debug.Log("[CubeTest] OnMouseExit (cursor left cube silhouette)");
    }
}
