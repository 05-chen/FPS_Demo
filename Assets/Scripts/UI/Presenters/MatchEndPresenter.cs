using Core;
using UnityEngine;
using World;

namespace UI.Presenters
{
    /// <summary>
    /// 结算播报业务：是否显示、点击后进入等待下一局。不负责断网。
    /// </summary>
    public static class MatchEndPresenter
    {
        public static void Show(TeamId winner, bool isSweep)
        {
            if (SteamLobbyUI.IsAwaitingPostMatchAdmission)
            {
                return;
            }

            GameplayGate.Block(GameplayGate.Reason.MatchEnd);
            MatchEndUI.EnsureInstance().ShowVisual(winner, isSweep);
            SteamLobbyUI.HideForMatchEnd();
        }

        public static void ForceHide()
        {
            MatchEndUI.EnsureInstance().ForceHide();
            GameplayGate.Release(GameplayGate.Reason.MatchEnd);
        }

        public static void RequestDismiss()
        {
            MatchEndUI.EnsureInstance().ForceHide();
            GameplayGate.Release(GameplayGate.Reason.MatchEnd);
            MatchGameManager.NotifyMatchEndDismissed();
        }
    }
}
