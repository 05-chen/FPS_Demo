using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局输入门闩（带原因堆叠）。
/// FullBlock：大厅 / 选阵营 / 结算等，锁操作并关掉玩家相机。
/// InputLocked：倒地等，只锁移动和开火，保留第一人称画面。
/// 多原因可并存；全部 Pop 后才 Open，避免「面板关了但输入仍锁」。
/// </summary>
public static class GameplayGate
{
    public enum Mode
    {
        Open,
        InputLocked,
        FullBlock
    }

    public enum Reason : byte
    {
        Lobby = 1,
        FactionSelection = 2,
        MatchEnd = 3,
        DisconnectNotice = 4,
        Respawn = 5,
        Downed = 6
    }

    static readonly HashSet<Reason> _fullBlocks = new HashSet<Reason>();
    static readonly HashSet<Reason> _inputLocks = new HashSet<Reason>();

    public static Mode CurrentMode { get; private set; } = Mode.Open;
    public static bool IsBlocked => CurrentMode != Mode.Open;
    public static bool SuppressPlayerView => CurrentMode == Mode.FullBlock;
    public static bool HasReason(Reason reason) =>
        _fullBlocks.Contains(reason) || _inputLocks.Contains(reason);

    public static event System.Action<bool> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _fullBlocks.Clear();
        _inputLocks.Clear();
        CurrentMode = Mode.Open;
        Changed = null;
    }

    /// <summary>FullBlock 原因（大厅、选阵营、结算等）。</summary>
    public static void Block(Reason reason)
    {
        if (reason == Reason.Downed)
        {
            BlockInputOnly(reason);
            return;
        }

        if (!_fullBlocks.Add(reason))
        {
            return;
        }

        Recompute();
    }

    /// <summary>InputLocked 原因（默认倒地）。</summary>
    public static void BlockInputOnly(Reason reason = Reason.Downed)
    {
        if (!_inputLocks.Add(reason))
        {
            return;
        }

        Recompute();
    }

    /// <summary>移除指定原因；若仍有其它原因，门闩保持关闭。</summary>
    public static void Release(Reason reason)
    {
        bool removed = _fullBlocks.Remove(reason) | _inputLocks.Remove(reason);
        if (!removed)
        {
            return;
        }

        Recompute();
    }

    /// <summary>清除全部原因（进入可玩状态）。</summary>
    public static void ReleaseAll()
    {
        if (_fullBlocks.Count == 0 && _inputLocks.Count == 0 && CurrentMode == Mode.Open)
        {
            return;
        }

        _fullBlocks.Clear();
        _inputLocks.Clear();
        Recompute();
    }

    /// <summary>兼容旧调用：等价于 <see cref="ReleaseAll"/>。</summary>
    public static void Release() => ReleaseAll();

    static void Recompute()
    {
        Mode next = Mode.Open;
        if (_fullBlocks.Count > 0)
        {
            next = Mode.FullBlock;
        }
        else if (_inputLocks.Count > 0)
        {
            next = Mode.InputLocked;
        }

        if (CurrentMode == next)
        {
            ApplyCursorPolicy(next);
            return;
        }

        CurrentMode = next;
        ApplyCursorPolicy(next);
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
