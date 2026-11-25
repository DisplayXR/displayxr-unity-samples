// #2800 PROBE (not for merge): logs focus + keyboard state so the dead-key run can be read
// from Player.log. Lines start with [DXR-PROBE].
using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public class Probe2800 : MonoBehaviour
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern IntPtr GetFocus();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();

    float _next;
    int _keyEvents, _textEvents, _pressedFrames;
    string _last = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("Probe2800");
        DontDestroyOnLoad(go);
        go.AddComponent<Probe2800>();
    }

    void OnEnable()
    {
        InputSystem.onDeviceChange += OnDev;
        InputSystem.onEvent += OnEvt;
        Log("boot devices=" + DeviceList() + " bg=" + InputSystem.settings.backgroundBehavior);
    }

    void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDev;
        InputSystem.onEvent -= OnEvt;
    }

    void OnApplicationFocus(bool f) { Log("OnApplicationFocus " + f); }

    void OnDev(InputDevice d, InputDeviceChange c)
    {
        Log("deviceChange " + c + " '" + d.name + "' layout=" + d.layout + " enabled=" + d.enabled);
    }

    void OnEvt(InputEventPtr e, InputDevice d)
    {
        if (d is Keyboard)
        {
            if (e.IsA<StateEvent>() || e.IsA<DeltaStateEvent>()) _keyEvents++;
            else if (e.type == TextEvent.Type) _textEvents++;
        }
    }

    static string DeviceList()
    {
        var sb = new StringBuilder();
        foreach (var d in InputSystem.devices) sb.Append(d.name).Append(d.enabled ? "" : "(OFF)").Append(',');
        return sb.ToString();
    }

    static string Title(IntPtr h)
    {
        if (h == IntPtr.Zero) return "0";
        var sb = new StringBuilder(128);
        GetWindowText(h, sb, 128);
        GetWindowThreadProcessId(h, out uint pid);
        return "0x" + h.ToInt64().ToString("X") + "'" + sb + "'" + (pid == GetCurrentProcessId() ? "(self)" : "(pid " + pid + ")");
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.anyKey.wasPressedThisFrame)
        {
            _pressedFrames++;
            var sb = new StringBuilder();
            foreach (var k in kb.allKeys) if (k.wasPressedThisFrame) sb.Append(k.keyCode).Append(' ');
            Log("KEY pressed: " + sb);
        }
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;
        string s = "isFocused=" + Application.isFocused
            + " kb=" + (kb == null ? "null" : ("enabled=" + kb.enabled + " added=" + kb.added + " canRunInBg=" + kb.canRunInBackground))
            + " kbEvents=" + _keyEvents + " textEvents=" + _textEvents + " pressedFrames=" + _pressedFrames
            + " fg=" + Title(GetForegroundWindow()) + " active=" + Title(GetActiveWindow()) + " focus=" + Title(GetFocus())
            + " bg=" + InputSystem.settings.backgroundBehavior;
        if (s != _last) { Log(s); _last = s; }
    }

    static void Log(string m) { Debug.Log("[DXR-PROBE] " + m); }
}
