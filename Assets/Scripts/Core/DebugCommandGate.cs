using UnityEngine;

/// <summary>
/// 测试快捷键总开关。默认关闭，按 Alt+F9 才能使用 F1–F5 等调试键。
/// </summary>
public static class DebugCommandGate
{
    public static bool isDebugModeEnabled;

    public static bool IsEnabled => isDebugModeEnabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        isDebugModeEnabled = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootstrapListener()
    {
        if (Object.FindFirstObjectByType<DebugCommandGateListener>() != null)
        {
            return;
        }

        var host = new GameObject("DebugCommandGate");
        Object.DontDestroyOnLoad(host);
        host.AddComponent<DebugCommandGateListener>();
    }

    public static void Toggle()
    {
        isDebugModeEnabled = !isDebugModeEnabled;
        Debug.Log("[Debug] Debug Mode Toggle: " + (isDebugModeEnabled ? "ON" : "OFF"));
    }
}

[DefaultExecutionOrder(-200)]
public sealed class DebugCommandGateListener : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9) &&
            (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
        {
            DebugCommandGate.Toggle();
        }
    }
}
