using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Owner 动画 NetworkVariable 写入。NV 实例仍挂在 PlayerController 上。
/// </summary>
public sealed class PlayerAnimationSync
{
    readonly NetworkVariable<Vector2> _syncedAnimMove;
    readonly NetworkVariable<bool> _syncedAnimGrounded;
    readonly NetworkVariable<bool> _syncedAnimAds;
    readonly NetworkVariable<byte> _syncedFireSeq;
    readonly NetworkVariable<byte> _syncedJumpSeq;
    readonly NetworkVariable<byte> _syncedReloadSeq;

    public PlayerAnimationSync(
        NetworkVariable<Vector2> syncedAnimMove,
        NetworkVariable<bool> syncedAnimGrounded,
        NetworkVariable<bool> syncedAnimAds,
        NetworkVariable<byte> syncedFireSeq,
        NetworkVariable<byte> syncedJumpSeq,
        NetworkVariable<byte> syncedReloadSeq)
    {
        _syncedAnimMove = syncedAnimMove;
        _syncedAnimGrounded = syncedAnimGrounded;
        _syncedAnimAds = syncedAnimAds;
        _syncedFireSeq = syncedFireSeq;
        _syncedJumpSeq = syncedJumpSeq;
        _syncedReloadSeq = syncedReloadSeq;
    }

    public void PublishState(bool isSpawned, bool isOwner, Vector2 move, bool grounded, bool isAds)
    {
        if (!isSpawned || !isOwner)
        {
            return;
        }

        if (_syncedAnimMove.Value != move)
        {
            _syncedAnimMove.Value = move;
        }

        if (_syncedAnimGrounded.Value != grounded)
        {
            _syncedAnimGrounded.Value = grounded;
        }

        if (_syncedAnimAds.Value != isAds)
        {
            _syncedAnimAds.Value = isAds;
        }
    }

    public void PublishFire(bool isSpawned, bool isOwner)
    {
        if (!isSpawned || !isOwner)
        {
            return;
        }

        _syncedFireSeq.Value++;
    }

    public void PublishJump(bool isSpawned, bool isOwner)
    {
        if (!isSpawned || !isOwner)
        {
            return;
        }

        _syncedJumpSeq.Value++;
    }

    public void PublishReload(bool isSpawned, bool isOwner)
    {
        if (!isSpawned || !isOwner)
        {
            return;
        }

        _syncedReloadSeq.Value++;
    }
}
