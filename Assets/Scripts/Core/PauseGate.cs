using UnityEngine;

/// <summary>
/// 局内暂停。和 GameplayGate 分开：暂停只挡操作、弹出菜单，不关玩家摄像机。
/// 联机时不要把 Time.timeScale 设为 0，否则 NGO 心跳会停。
/// </summary>
public static class PauseGate
{
    public static bool IsPaused { get; private set; }

    public static event System.Action<bool> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        Changed = null;
    }

    public static void Pause(bool freezeTime)
    {
        if (IsPaused)
        {
            return;
        }

        IsPaused = true;
        Time.timeScale = freezeTime ? 0f : 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Changed?.Invoke(true);
    }

    public static void Resume()
    {
        if (!IsPaused)
        {
            Time.timeScale = 1f;
            return;
        }

        IsPaused = false;
        Time.timeScale = 1f;
        if (!GameplayGate.IsBlocked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Changed?.Invoke(false);
    }
}
