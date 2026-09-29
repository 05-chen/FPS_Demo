using UnityEngine;

namespace UI.Presenters
{
    /// <summary>
    /// 大厅门闩：进入/离开大厅时的输入阻塞，不直接操作 NetworkManager。
    /// </summary>
    public static class LobbyPresenter
    {
        public static void EnterBlockingLobby()
        {
            GameplayGate.Block(GameplayGate.Reason.Lobby);
        }

        public static void LeaveBlockingLobby()
        {
            GameplayGate.Release(GameplayGate.Reason.Lobby);
        }
    }
}
