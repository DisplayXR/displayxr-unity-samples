// Copyright 2024-2026, DisplayXR contributors
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DisplayXR
{
    /// <summary>
    /// Basic keyboard + mouse + touch input for navigating around a DisplayXR rig.
    /// WASD = move horizontally, QE = move up/down, left-mouse drag = rotate.
    /// Scroll wheel = zoom (scale). Space = reset to initial pose.
    /// Touch (Android tablets): one-finger drag = orbit (the same rotation as a mouse
    /// drag), two-finger pinch = zoom (dolly on a display rig, FOV on a camera rig),
    /// two-finger drag = pan, double tap = reset. The touch path only acts while
    /// fingers are down, so desktop keyboard/mouse behaviour is unchanged.
    /// Attach to the same GameObject as DisplayXRDisplay or DisplayXRCamera.
    /// Works in Play Mode (including with the standalone preview via PlayModeIntegration).
    /// </summary>
    [AddComponentMenu("DisplayXR/Input Controller")]
    public class DisplayXRInputController : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Movement speed in meters per second.")]
        public float moveSpeed = 1.0f;

        [Tooltip("Mouse rotation sensitivity (radians per pixel).")]
        public float rotationSensitivity = 0.005f;

        [Tooltip("When false, left-mouse drag does NOT rotate the camera. " +
                 "Useful when the app drives object-drag interactions (e.g. " +
                 "DragRotateCube on a scene object) and wants left-drag to " +
                 "be reserved for the app's hit-tested target, with no " +
                 "fallback camera-look on the off-target case. WASD movement " +
                 "and keyboard controls are unaffected.")]
        public bool mouseLookEnabled = true;

        [Tooltip("Scroll wheel zoom speed (scale factor per scroll tick).")]
        public float zoomSpeed = 0.1f;

        [Tooltip("When false, scroll wheel does NOT zoom the camera. Useful " +
                 "when the app drives its own scroll-based zoom (e.g. " +
                 "WheelZoomVHeight driving DisplayXRDisplay.virtualDisplayHeight " +
                 "for an avatar-style zoom-in-window). Mouse-look and WASD " +
                 "are unaffected.")]
        public bool scrollZoomEnabled = true;

        [Header("Touch")]
        [Tooltip("When false, touch gestures (one-finger orbit, pinch zoom, two-finger " +
                 "pan, double-tap reset) are ignored.")]
        public bool touchEnabled = true;

        [Tooltip("Orbit for a one-finger drag across the full screen WIDTH, in degrees " +
                 "(height maps to half of it). 180 = a full-width drag turns the rig half " +
                 "way round, the DisplayXR runtime's own touch-provider convention.")]
        public float touchOrbitDegreesPerScreen = 180f;

        [Tooltip("Display rig: how far a pinch dollies, in virtual-display heights per unit of " +
                 "finger-spread ratio (spreading the fingers to 2x moves ~this many heights closer).")]
        public float touchPinchDolly = 1.0f;

        private float m_Yaw;
        private float m_Pitch;
        private bool m_Dragging;
        private Vector2 m_LastMousePos;

        private Vector3 m_InitialPosition;
        private float m_InitialYaw, m_InitialPitch;
        private Vector3 m_InitialScale;

        // Rig type detection for zoom behavior
        private Camera m_Camera;
        private bool m_IsCameraCentric;
        private float m_InitialFov;

        void Start()
        {
            Application.runInBackground = true;
#if ENABLE_INPUT_SYSTEM
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
#endif

            Vector3 euler = transform.eulerAngles;
            m_Yaw = euler.y * Mathf.Deg2Rad;
            m_Pitch = euler.x * Mathf.Deg2Rad;
            if (m_Pitch > Mathf.PI) m_Pitch -= 2f * Mathf.PI;

            m_InitialPosition = transform.position;
            m_InitialYaw = m_Yaw;
            m_InitialPitch = m_Pitch;
            m_InitialScale = transform.localScale;

            // Cache camera reference (needed for active check and zoom)
            m_Camera = GetComponent<Camera>();
            m_IsCameraCentric = GetComponent<DisplayXRCamera>() != null;
            if (m_IsCameraCentric)
                m_InitialFov = m_Camera.fieldOfView;

            // Push the initial rendering mode to the runtime so the C# state
            // (m_CurrentRenderingMode default = 1 / 3D) and the runtime's
            // sim_display mode agree from frame 0. Without this the runtime
            // can default to 2D (mono passthrough), and the first V keypress
            // re-requests the same mode the C# is about to leave (no visible
            // effect), making users press V twice to reach 3D. Harmless to
            // call early — the provider queues the request until the session
            // is ready to handle it.
            DisplayXRProvider.RequestRenderingMode((uint)m_CurrentRenderingMode);

            // Follow provider session restarts. On Android a restart is routine: Unity
            // stops the session on pause and starts a new one on resume, and every request
            // pushed into the old session is lost.
            DisplayXRProvider.SessionStarted += OnProviderSessionStarted;
        }

        void OnDestroy()
        {
            DisplayXRProvider.SessionStarted -= OnProviderSessionStarted;
        }

        private void OnProviderSessionStarted()
        {
            // A gesture in flight when the app paused never got its finger-up.
            m_TouchCount = 0;
            m_TouchIdA = m_TouchIdB = -1;
#if UNITY_ANDROID && !UNITY_EDITOR
            // Re-assert the hardware 3D state in the new session (Android only: desktop
            // sessions do not restart under a running app, so its behaviour is unchanged).
            DisplayXRProvider.RequestDisplayMode(m_CurrentRenderingMode != 0);
#endif
        }

        // Rendering mode cycling
        private int m_CurrentRenderingMode = 1;
        private static int s_LastTabFrame = -1;

        void Update()
        {
            // Tab cycles cameras globally (only process once per frame).
            // Shift+Tab is reserved for app-side use (e.g. hiding UI panels)
            // so we explicitly gate this on Shift NOT being held — otherwise
            // pressing Shift+Tab would both toggle the app's UI AND cycle
            // cameras at the same time.
            if (GetKeyDown(KeyCode.Tab) && !IsShiftHeld() &&
                Time.frameCount != s_LastTabFrame)
            {
                s_LastTabFrame = Time.frameCount;
                DisplayXRRigManager.CycleNext();
            }

            if (!IsActiveCamera())
            {
                // Clear drag state so we don't jump on reactivation
                m_Dragging = false;
                m_DragPending = false;
                return;
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // After click-through to another app (transparent-overlay mode,
            // issue #57), Unity still receives keyboard via RawInput
            // INPUTSINK even when not foreground. Skip input handling so
            // WASD doesn't move the cube while the user is typing in
            // Notepad/Explorer/etc. Re-engages when the user clicks the
            // cube — overlay_wnd_proc calls SetForegroundWindow on cube
            // press messages.
            if (!IsOurProcessForeground())
            {
                m_Dragging = false;
                m_DragPending = false;
                return;
            }
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            UpdateShellMouse();
#endif
            HandleMouseRotation();
            HandleTouch();
            HandleKeyboardMovement();
            HandleScrollZoom();
            HandleReset();
            HandleQuit();
            HandleFullscreen();
            HandleModeCycle();
            HandleScreenshot();
        }

        private bool IsActiveCamera()
        {
            var active = DisplayXRRigManager.ActiveCamera;
            return active == null || active == m_Camera;
        }

        private const float kDragThreshold = 3f; // pixels before drag starts
        private bool m_DragPending;
        private Vector2 m_DragStartPos;

        private void HandleMouseRotation()
        {
            // Opt-out: app wants left-drag reserved for its own hit-tested
            // interactions (e.g. DragRotateCube on a target object).
            if (!mouseLookEnabled)
            {
                m_Dragging = false;
                m_DragPending = false;
                return;
            }

            // Cancel any in-progress drag if input should be ignored
            // (e.g. preview window is being moved/resized).
            // Checked every frame, not just on mouseDown, because the
            // window interaction flag may arrive after the mouseDown.
            if (ShouldIgnoreInput())
            {
                m_Dragging = false;
                m_DragPending = false;
                return;
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Shell mode AND the provider (built-player overlay or #173 editor
            // dedicated window) present the woven output in a window separate from
            // Unity's game/Game-View surface, so Mouse.current.position does NOT track
            // the cursor while it's over that window (the position-delta path below
            // would read a frozen position and never rotate). Use Mouse.current.delta
            // instead — it's Raw Input, so it's cursor-location-independent and reaches
            // Unity via the focus hook's RIDEV_INPUTSINK even when our window (not
            // Unity's) is under the cursor. Button state comes from the native WndProc
            // tracker (Unity's Input System doesn't see clicks over the weave window).
            bool providerMouse = IsProviderActive() && !IsShellMode();
            if ((IsShellMode() || providerMouse) && Mouse.current != null)
            {
                if (ShellGetMouseButtonDown(0))
                    m_Dragging = true;
                if (ShellGetMouseButtonUp(0))
                    m_Dragging = false;
                if (m_Dragging)
                {
                    Vector2 delta = Mouse.current.delta.ReadValue();
                    m_Yaw += delta.x * rotationSensitivity;
                    m_Pitch -= delta.y * rotationSensitivity;
                    m_Pitch = Mathf.Clamp(m_Pitch, -1.4f, 1.4f);
                    transform.rotation = Quaternion.Euler(
                        m_Pitch * Mathf.Rad2Deg,
                        m_Yaw * Mathf.Rad2Deg,
                        0f);
                }
                return;
            }
#endif
            if (GetMouseButtonDown(0))
            {
                m_DragPending = true;
                m_DragStartPos = GetMousePosition();
                m_LastMousePos = m_DragStartPos;
            }
            if (GetMouseButtonUp(0))
            {
                m_Dragging = false;
                m_DragPending = false;
            }

            if (m_DragPending && !m_Dragging)
            {
                Vector2 pos = GetMousePosition();
                if ((pos - m_DragStartPos).sqrMagnitude > kDragThreshold * kDragThreshold)
                {
                    m_Dragging = true;
                    m_DragPending = false;
                    m_LastMousePos = pos;
                }
            }

            if (m_Dragging)
            {
                Vector2 pos = GetMousePosition();
                Vector2 delta = pos - m_LastMousePos;
                m_Yaw += delta.x * rotationSensitivity;
                m_Pitch -= delta.y * rotationSensitivity;
                m_Pitch = Mathf.Clamp(m_Pitch, -1.4f, 1.4f);
                m_LastMousePos = pos;

                transform.rotation = Quaternion.Euler(
                    m_Pitch * Mathf.Rad2Deg,
                    m_Yaw * Mathf.Rad2Deg,
                    0f);
            }
        }

        // --- Shell mode mouse input (bypasses Input System entirely) ---
        // Unity reads mouse buttons from legacy WM_LBUTTONDOWN which only goes
        // to the foreground window. In shell mode we read button state directly
        // from the native WM_INPUT tracker via P/Invoke.
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static bool s_shellModeChecked;
        private static bool s_shellMode;
        private static int s_shellButtonsPrev;
        private static int s_shellButtonsCurr;

        private static bool IsShellMode()
        {
            if (!s_shellModeChecked)
            {
                s_shellMode = DisplayXRNative.displayxr_is_shell_mode() != 0;
                s_shellModeChecked = true;
            }
            return s_shellMode;
        }

        // True when the custom IUnityXRDisplay provider is driving rendering (built
        // player OR editor Play Mode). Used to route mouse rotation through the
        // Raw-Input delta path (the woven output is a separate window, so
        // Mouse.current.position doesn't track the cursor there).
        private static bool IsProviderActive()
        {
            try { return DisplayXRProviderDriver.IsActive; }
            catch { return false; }
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static bool IsOurProcessForeground()
        {
            try { return DisplayXRNative.displayxr_is_our_process_foreground() != 0; }
            catch { return true; } // fail-open: don't break input if symbol missing
        }
#endif

        /// Call once per frame (from Update) to snapshot mouse button state from
        /// either the shell-mode tracker or the SA preview window's WndProc.
        private static void UpdateShellMouse()
        {
            s_shellButtonsPrev = s_shellButtonsCurr;
            if (IsShellMode())
            {
                DisplayXRNative.displayxr_get_shell_mouse_state(
                    out s_shellButtonsCurr, out _, out _);
            }
            else if (IsProviderActive())
            {
                // Provider: the woven output is a separate window, so Unity's Input
                // System doesn't register clicks over it. The dedicated window's
                // WndProc tracks button state into the native pointer, read here.
                try
                {
                    DisplayXRNative.displayxr_get_overlay_pointer(out _, out _, out s_shellButtonsCurr);
                }
                catch (System.EntryPointNotFoundException) { /* old binary */ }
            }
        }

        private static bool ShellGetMouseButtonDown(int b)
        {
            int mask = 1 << b;
            return (s_shellButtonsCurr & mask) != 0 && (s_shellButtonsPrev & mask) == 0;
        }

        private static bool ShellGetMouseButtonUp(int b)
        {
            int mask = 1 << b;
            return (s_shellButtonsCurr & mask) == 0 && (s_shellButtonsPrev & mask) != 0;
        }

        private static bool ShellGetMouseButton(int b)
        {
            return (s_shellButtonsCurr & (1 << b)) != 0;
        }
#endif

        // --- Touch (Android tablets, displayxr-unity#418) ---
        //
        // Policy lives HERE, with the rig, not in the runtime: the runtime's touch input
        // provider drives its own navigation rig, which only moves the views of a session
        // whose window the RUNTIME owns. An app that renders through XR_DXR_view_rig (this
        // plug-in) owns its camera, so the gestures have to move the Unity rig itself.
        // The provider still forwards every touch to the runtime (xrSubmitAndroidInputEventDXR)
        // for its emulated controller; this is the camera half.
        private const float kTouchSlop = 12f;     // px before a one-finger drag orbits
        private const float kDoubleTapSec = 0.3f;
        private const float kDoubleTapSlop = 40f; // px between the two taps
        private int m_TouchCount;                 // touches down last frame
        private int m_TouchIdA = -1, m_TouchIdB = -1;
        private Vector2 m_TouchPosA, m_TouchPosB, m_TouchStart;
        private bool m_TouchOrbiting, m_TouchMoved;
        private float m_TouchDownTime, m_LastTapTime = -10f;
        private Vector2 m_LastTapPos;
        private float m_GestureZoom = 1f;          // accumulated pinch ratio this gesture
        private Vector2 m_GesturePan, m_GestureOrbit; // accumulated px this gesture

        private struct TouchSample { public int id; public Vector2 pos; }
        private readonly TouchSample[] m_Touches = new TouchSample[10];

        private int ReadTouches()
        {
            int n = 0;
#if ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            if (ts == null) return 0;
            foreach (var t in ts.touches)
            {
                if (!t.press.isPressed) continue;
                if (n >= m_Touches.Length) break;
                m_Touches[n].id = t.touchId.ReadValue();
                m_Touches[n].pos = t.position.ReadValue();
                n++;
            }
#else
            if (!Input.touchSupported) return 0;
            for (int i = 0; i < Input.touchCount && n < m_Touches.Length; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) continue;
                m_Touches[n].id = t.fingerId;
                m_Touches[n].pos = t.position;
                n++;
            }
#endif
            return n;
        }

        private bool FindTouch(int id, int n, out Vector2 pos)
        {
            for (int i = 0; i < n; i++)
                if (m_Touches[i].id == id) { pos = m_Touches[i].pos; return true; }
            pos = Vector2.zero;
            return false;
        }

        private void HandleTouch()
        {
            if (!touchEnabled) { m_TouchCount = 0; return; }
            int n = ReadTouches();
            float now = Time.unscaledTime;

            if (n == 0)
            {
                // Release: a short, still single touch is a tap; two of them close
                // together in time and place reset the rig.
                if (m_TouchCount == 1 && !m_TouchMoved && now - m_TouchDownTime < kDoubleTapSec)
                {
                    if (now - m_LastTapTime < kDoubleTapSec &&
                        (m_TouchStart - m_LastTapPos).sqrMagnitude < kDoubleTapSlop * kDoubleTapSlop)
                    {
                        ResetPose();
                        m_LastTapTime = -10f;
                        Debug.Log("[DisplayXR] touch: double tap -> reset");
                    }
                    else
                    {
                        m_LastTapTime = now;
                        m_LastTapPos = m_TouchStart;
                    }
                }
                if (m_TouchCount > 0 && (m_GestureZoom != 1f || m_GesturePan != Vector2.zero || m_GestureOrbit != Vector2.zero))
                    Debug.Log($"[DisplayXR] touch gesture: orbit {m_GestureOrbit} px, pinch x{m_GestureZoom:F2}, pan {m_GesturePan} px ({(m_IsCameraCentric ? "camera rig: FOV" : "display rig: position")} {(m_IsCameraCentric ? m_Camera.fieldOfView.ToString("F1") : transform.position.ToString("F2"))})");
                m_GestureZoom = 1f; m_GesturePan = m_GestureOrbit = Vector2.zero;
                m_TouchCount = 0;
                m_TouchIdA = m_TouchIdB = -1;
                m_TouchOrbiting = false;
                return;
            }
            if (ShouldIgnoreInput()) { m_TouchCount = n; m_TouchIdA = m_TouchIdB = -1; return; }

            float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);

            if (n == 1)
            {
                Vector2 p = m_Touches[0].pos;
                if (m_TouchCount == 0)
                {
                    // First finger down: arm a tap / an orbit.
                    m_TouchIdA = m_Touches[0].id;
                    m_TouchPosA = p;
                    m_TouchStart = p;
                    m_TouchDownTime = now;
                    m_TouchMoved = false;
                    m_TouchOrbiting = false;
                }
                else if (m_TouchIdA != m_Touches[0].id)
                {
                    // Came down from two fingers (or a new finger): restart from here
                    // instead of jumping by the distance between the two fingers.
                    m_TouchIdA = m_Touches[0].id;
                    m_TouchPosA = p;
                    m_TouchStart = p;
                    m_TouchMoved = true; // never a tap
                    m_TouchOrbiting = true;
                }
                if (!m_TouchOrbiting && (p - m_TouchStart).sqrMagnitude > kTouchSlop * kTouchSlop)
                {
                    m_TouchOrbiting = true;
                    m_TouchMoved = true;
                    m_TouchPosA = p; // start from here: the slop is not part of the orbit
                }
                if (m_TouchOrbiting && mouseLookEnabled)
                {
                    Vector2 d = p - m_TouchPosA;
                    m_GestureOrbit += d;
                    float perW = touchOrbitDegreesPerScreen * Mathf.Deg2Rad;
                    // Same signs as the mouse drag above, so desktop and tablet agree.
                    m_Yaw += d.x / w * perW;
                    m_Pitch -= d.y / h * perW * 0.5f;
                    m_Pitch = Mathf.Clamp(m_Pitch, -1.4f, 1.4f);
                    transform.rotation = Quaternion.Euler(m_Pitch * Mathf.Rad2Deg, m_Yaw * Mathf.Rad2Deg, 0f);
                }
                m_TouchPosA = p;
                m_TouchCount = 1;
                return;
            }

            // Two (or more) fingers: the first two by id.
            int ia = 0, ib = 1;
            Vector2 a = m_Touches[ia].pos, b = m_Touches[ib].pos;
            bool same = m_TouchCount >= 2 && m_Touches[ia].id == m_TouchIdA && m_Touches[ib].id == m_TouchIdB;
            if (same)
            {
                float d0 = (m_TouchPosB - m_TouchPosA).magnitude, d1 = (b - a).magnitude;
                Vector2 c0 = (m_TouchPosA + m_TouchPosB) * 0.5f, c1 = (a + b) * 0.5f;

                // Pinch = zoom: spreading the fingers makes the content bigger. Camera rig:
                // a narrower FOV. Display rig: DOLLY the rig toward what it looks at, the
                // runtime touch provider's own pinch semantics. (Measured on a Nubia Pad 2:
                // the scroll wheel's scale-as-zoom leaves the projected size unchanged
                // there, so a pinch through localScale did nothing visible.) The step is a
                // fraction of the virtual display's world height, so it feels the same at
                // every rig scale.
                if (scrollZoomEnabled && d0 > 1f && d1 > 1f)
                {
                    float ratio = Mathf.Clamp(d1 / d0, 0.8f, 1.25f);
                    m_GestureZoom *= ratio;
                    if (m_IsCameraCentric)
                        m_Camera.fieldOfView = Mathf.Clamp(m_Camera.fieldOfView / ratio, 5f, 120f);
                    else
                    {
                        float worldH = PanWorldPerPixel(h) * h;
                        Quaternion o = Quaternion.Euler(m_Pitch * Mathf.Rad2Deg, m_Yaw * Mathf.Rad2Deg, 0f);
                        transform.position += (o * Vector3.forward) * ((ratio - 1f) * worldH * touchPinchDolly);
                    }
                }

                // Two-finger drag = pan: the content follows the fingers, 1:1 on the virtual
                // display plane.
                Vector2 dc = c1 - c0;
                m_GesturePan += dc;
                if (dc.sqrMagnitude > 0f)
                {
                    float worldPerPx = PanWorldPerPixel(h);
                    Quaternion ori = Quaternion.Euler(m_Pitch * Mathf.Rad2Deg, m_Yaw * Mathf.Rad2Deg, 0f);
                    transform.position -= (ori * Vector3.right) * (dc.x * worldPerPx)
                                        + (ori * Vector3.up) * (dc.y * worldPerPx);
                }
            }
            m_TouchIdA = m_Touches[ia].id; m_TouchIdB = m_Touches[ib].id;
            m_TouchPosA = a; m_TouchPosB = b;
            m_TouchMoved = true;
            m_TouchOrbiting = false;
            m_TouchCount = n;
        }

        // World units per screen pixel on the plane the content sits at.
        private float PanWorldPerPixel(float screenH)
        {
            var disp = GetComponent<DisplayXRDisplay>();
            if (disp != null && disp.TryGetVirtualDisplayPullback(m_Camera, out _, out float worldH))
                return worldH / screenH;
            // Camera rig: the plane at the convergence distance (1 m if unset).
            var cam = GetComponent<DisplayXRCamera>();
            float dist = (cam != null && cam.invConvergenceDistance > 1e-3f) ? 1f / cam.invConvergenceDistance : 1f;
            float fov = m_Camera != null ? m_Camera.fieldOfView : 60f;
            return 2f * dist * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / screenH;
        }

        private void HandleKeyboardMovement()
        {
            // Compute direction vectors from stored yaw/pitch, NOT from transform.
            // In XR mode, Unity's XR subsystem overwrites the camera transform each
            // frame with the tracked eye pose — reading transform.forward/right/up
            // gives the XR-modified orientation, not the controller's intended one.
            Quaternion ori = Quaternion.Euler(m_Pitch * Mathf.Rad2Deg, m_Yaw * Mathf.Rad2Deg, 0f);
            Vector3 fwd = ori * Vector3.forward;
            Vector3 rt = ori * Vector3.right;
            Vector3 up = ori * Vector3.up;

            Vector3 move = Vector3.zero;
            if (GetKey(KeyCode.W)) move += fwd;
            if (GetKey(KeyCode.S)) move -= fwd;
            if (GetKey(KeyCode.D)) move += rt;
            if (GetKey(KeyCode.A)) move -= rt;
            if (GetKey(KeyCode.E)) move += up;
            if (GetKey(KeyCode.Q)) move -= up;

            if (move.sqrMagnitude > 0f)
                transform.position += move.normalized * moveSpeed * Time.deltaTime;
        }

        private void HandleScrollZoom()
        {
            if (!scrollZoomEnabled) return;
            if (ShouldIgnoreInput()) return;
            float scroll = GetScrollDelta();
            if (Mathf.Abs(scroll) < 0.001f) return;

            if (m_IsCameraCentric)
            {
                // Camera-centric: zoom by adjusting FOV
                m_Camera.fieldOfView = Mathf.Clamp(
                    m_Camera.fieldOfView - scroll * zoomSpeed * 10f, 5f, 120f);
            }
            else
            {
                // Display-centric: zoom by scaling transform
                float factor = 1f + scroll * zoomSpeed;
                factor = Mathf.Clamp(factor, 0.5f, 2f);
                transform.localScale *= factor;
            }
        }

        private void HandleReset()
        {
            if (GetKeyDown(KeyCode.Space))
                ResetPose();
        }

        private void ResetPose()
        {
            {
                transform.position = m_InitialPosition;
                transform.localScale = m_InitialScale;
                m_Yaw = m_InitialYaw;
                m_Pitch = m_InitialPitch;
                transform.rotation = Quaternion.Euler(
                    m_Pitch * Mathf.Rad2Deg,
                    m_Yaw * Mathf.Rad2Deg,
                    0f);

                if (m_IsCameraCentric)
                    m_Camera.fieldOfView = m_InitialFov;
            }
        }

        private void HandleQuit()
        {
            if (GetKeyDown(KeyCode.Escape))
                Application.Quit();
        }

        private void HandleFullscreen()
        {
            if (GetKeyDown(KeyCode.F11))
                Screen.fullScreen = !Screen.fullScreen;
        }

        private void HandleModeCycle()
        {
            if (!GetKeyDown(KeyCode.V)) return;

            // Under the provider, DisplayXRTuningUI owns the V key and drives a smooth
            // ramped 2D<->3D switch (via DisplayXRModeSwitch). The controller does not
            // handle V here so it isn't toggled twice (double-toggle).
        }

        private static int s_LastScreenshotFrame = -1;
        private void HandleScreenshot()
        {
            // I key matches the convention used by the C++ test apps and the
            // Unreal plugin. Guarded against multi-rig double-fire per frame.
            if (GetKeyDown(KeyCode.I) && Time.frameCount != s_LastScreenshotFrame)
            {
                s_LastScreenshotFrame = Time.frameCount;
                DisplayXRScreenshot.Capture();
            }
        }

        private static bool ShouldIgnoreInput()
        {
            // Wsui composition layer: a slider drag or button click in flight
            // through DisplayXRWindowSpaceUI shouldn't double-route to scene
            // input. App-side router (e.g. DisplayXRWsuiMouseRouter) flips
            // this flag while it owns the cursor.
            if (DisplayXRWindowSpaceUI.IsCursorOverInteractive) return true;

            // Runtime UI (Canvas/EventSystem). Reflection-based to avoid a hard
            // UGUI compile dependency from the plugin assembly.
            if (IsPointerOverUgui()) return true;

#if UNITY_EDITOR
            // In provider Play Mode the woven output is a separate dedicated window
            // (#173), not an EditorWindow. Don't gate input on EditorWindow focus then,
            // otherwise clicking the Inspector or any other editor pane would freeze
            // input until the user clicks back into Game View.
            if (!DisplayXRProviderDriver.IsActive)
            {
                var focused = UnityEditor.EditorWindow.focusedWindow;
                if (focused == null)
                    return true;
                string typeName = focused.GetType().Name;
                if (typeName != "GameView")
                    return true;
            }
#endif
            return false;
        }

        private static System.Reflection.MethodInfo s_IsPointerOver;
        private static System.Reflection.PropertyInfo s_CurrentEs;
        private static bool s_UguiResolved;

        private static bool IsPointerOverUgui()
        {
            if (!s_UguiResolved)
            {
                s_UguiResolved = true;
                var t = System.Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI");
                if (t != null)
                {
                    s_CurrentEs = t.GetProperty("current",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    s_IsPointerOver = t.GetMethod("IsPointerOverGameObject", System.Type.EmptyTypes);
                }
            }
            if (s_CurrentEs == null || s_IsPointerOver == null) return false;
            var es = s_CurrentEs.GetValue(null);
            if (es == null) return false;
            return (bool)s_IsPointerOver.Invoke(es, null);
        }

        // --- Input abstraction (keyboard + mouse) ---

#if ENABLE_INPUT_SYSTEM
        private static bool GetKey(KeyCode k) =>
            Keyboard.current != null && Keyboard.current[ToKey(k)].isPressed;
        private static bool GetKeyDown(KeyCode k) =>
            Keyboard.current != null && Keyboard.current[ToKey(k)].wasPressedThisFrame;
        private static bool GetMouseButtonDown(int b)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (IsShellMode()) return ShellGetMouseButtonDown(b);
#endif
            return Mouse.current != null && (b == 0 ? Mouse.current.leftButton.wasPressedThisFrame
                : b == 1 ? Mouse.current.rightButton.wasPressedThisFrame
                : Mouse.current.middleButton.wasPressedThisFrame);
        }
        private static bool GetMouseButtonUp(int b)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (IsShellMode()) return ShellGetMouseButtonUp(b);
#endif
            return Mouse.current != null && (b == 0 ? Mouse.current.leftButton.wasReleasedThisFrame
                : b == 1 ? Mouse.current.rightButton.wasReleasedThisFrame
                : Mouse.current.middleButton.wasReleasedThisFrame);
        }
        private static Vector2 GetMousePosition() =>
            Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        private static float GetScrollDelta() =>
            Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;

        private static Key ToKey(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.W: return Key.W;
                case KeyCode.A: return Key.A;
                case KeyCode.S: return Key.S;
                case KeyCode.D: return Key.D;
                case KeyCode.Q: return Key.Q;
                case KeyCode.E: return Key.E;
                case KeyCode.I: return Key.I;
                case KeyCode.V: return Key.V;
                case KeyCode.Space: return Key.Space;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.F11: return Key.F11;
                default: return Key.None;
            }
        }

        private static bool IsShiftHeld() =>
            Keyboard.current != null &&
            (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
#else
        private static bool GetKey(KeyCode k) => Input.GetKey(k);
        private static bool GetKeyDown(KeyCode k) => Input.GetKeyDown(k);
        private static bool GetMouseButtonDown(int b) => Input.GetMouseButtonDown(b);
        private static bool GetMouseButtonUp(int b) => Input.GetMouseButtonUp(b);
        private static Vector2 GetMousePosition() => Input.mousePosition;
        private static float GetScrollDelta() => Input.mouseScrollDelta.y;
        private static bool IsShiftHeld() =>
            Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
    }
}
