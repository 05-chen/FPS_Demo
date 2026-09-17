using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// 默认 NetworkTransform 是服务器权威：客户端自己走路不会被承认。
/// FPS 由本地玩家自己算位移，所以改成拥有者权威，本机位置再同步给对方。
/// </summary>
[DisallowMultipleComponent]
public sealed class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }

    /// <summary>
    /// Owner 权威下，只有 CanCommitToTransform 的一侧调用 Teleport 才会同步给其他人。
    /// 服务器直接改 transform.position 会被 Owner 下一帧覆盖。
    /// </summary>
    public void TeleportToSpawn(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
        if (!CanCommitToTransform)
        {
            return;
        }

        Teleport(position, rotation, transform.localScale);
    }
}
