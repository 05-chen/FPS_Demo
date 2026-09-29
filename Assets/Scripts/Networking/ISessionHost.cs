using System.Collections;
using UnityEngine;

namespace Networking
{
    /// <summary>
    /// 会话服务共用的宿主：状态通知与协程。由 <see cref="SteamLobbySession"/> 实现。
    /// </summary>
    public interface ISessionHost
    {
        LobbySessionState State { get; }

        void Notify(string message);

        void Fail(string message);

        void SetState(LobbySessionState state);

        Coroutine Run(IEnumerator routine);

        void Halt(Coroutine routine);
    }
}
