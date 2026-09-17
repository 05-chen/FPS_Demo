using UnityEngine;

/// <summary>
/// 全局输入门闩。
/// FullBlock：大厅 / 选阵营 / 死亡后选人，锁操作并关掉玩家相机。
/// InputLocked：倒地等待救援，只锁移动和开火，保留第一人称画面。
/// </summary>
public static class GameplayGate
{
    public enum Mode
    {
        Open,
        InputLocked,
        FullBlock
    }

    public static Mode CurrentMode { get; private set; } = Mode.Open;
    public static bool IsBlocked => CurrentMode != Mode.Open;
    public static bool SuppressPlayerView => CurrentMode == Mode.FullBlock;

    public static event System.Action<bool> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        CurrentMode = Mode.Open;
        Changed = null;
    }

    /// <summary>大厅、选阵营：锁操作 + 关玩家相机 + 释放鼠标。</summary>
    public static void Block()
    {
        SetMode(Mode.FullBlock);
    }

    /// <summary>倒地：锁移动/开火，但保留游戏画面和锁定鼠标。</summary>
    public static void BlockInputOnly()
    {
        SetMode(Mode.InputLocked);
    }

    public static void Release()
    {
        SetMode(Mode.Open);
    }

    static void SetMode(Mode mode)
    {
        if (CurrentMode == mode)
        {
            return;
        }

        CurrentMode = mode;
        ApplyCursorPolicy(mode);
        Changed?.Invoke(IsBlocked);
    }

    static void ApplyCursorPolicy(Mode mode)
    {
        if (mode == Mode.FullBlock)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (mode == Mode.Open && !PauseGate.IsPaused)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
