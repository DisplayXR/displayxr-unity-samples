// Tiger-branch tweak: disable A/Q/D/E camera-translation by locking the
// rig camera's world X and Y to their startup values every Update, AFTER
// the plugin's DisplayXRInputController has moved.
//
// Why this approach: the plugin's DisplayXRInputController has W/S move
// the camera along its forward (world Z for forward-facing rigs), while
// A/D and Q/E move along right (world X) and up (world Y). There's no
// per-axis disable flag in the plugin, and the user wants "only W/S"
// without any plugin changes. So we let the plugin do its full move,
// then revert the X / Y components — net effect is W/S still pushes
// the tiger into / out of the display plane while A/Q/D/E become
// no-ops. Mouse-drag rotation, Tab cycling, scroll zoom, screenshot,
// etc. all keep working because the plugin controller is not disabled.
//
// Why Update with DefaultExecutionOrder(int.MaxValue) instead of
// LateUpdate: DisplayXRDisplay/Camera push the camera transform to the
// native runtime via xrLocateViews tunables in *their* LateUpdate. If
// we revert in our LateUpdate, the order between us and the rig's
// LateUpdate is undefined — sometimes the rig pushes the bad pre-revert
// position first, the runtime renders with that, and the user sees a
// one-frame visible shift before our revert. Putting the revert at the
// END of the Update phase (via the int.MaxValue execution order)
// guarantees the LateUpdate phase begins with the corrected transform.
//
// Caveat: this assumes the camera stays roughly forward-facing. If
// pitch/yaw deviate significantly from identity, "forward" shifts away
// from world Z and W/S would also start affecting world X/Y — which
// we'd then revert, breaking W/S too. For a stationary Display rig
// the assumption holds.

using UnityEngine;

[DefaultExecutionOrder(int.MaxValue)]
public class LockToForwardAxis : MonoBehaviour
{
    private float m_StartX;
    private float m_StartY;
    private bool m_Initialized;

    void Update()
    {
        Vector3 p = transform.position;
        if (!m_Initialized)
        {
            m_StartX = p.x;
            m_StartY = p.y;
            m_Initialized = true;
            return;
        }
        if (!Mathf.Approximately(p.x, m_StartX) || !Mathf.Approximately(p.y, m_StartY))
        {
            transform.position = new Vector3(m_StartX, m_StartY, p.z);
        }
    }
}
