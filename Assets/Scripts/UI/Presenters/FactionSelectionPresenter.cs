using Core;
using Managers;
using Match;
using Unity.Netcode;
using UnityEngine;
using World;

namespace UI.Presenters
{
    /// <summary>
    /// 选阵营业务：是否允许提交、如何申请生成。面板只负责显示与点击回调。
    /// </summary>
    public static class FactionSelectionPresenter
    {
        public static void Show()
        {
            GameplayGate.Block(GameplayGate.Reason.FactionSelection);
            FactionSelectionPanel panel = FactionSelectionPanel.Instance;
            if (panel == null)
            {
                panel = Object.FindFirstObjectByType<FactionSelectionPanel>(FindObjectsInactive.Include);
            }

            if (panel == null)
            {
                GameLog.Warn("Faction", "找不到 FactionSelectionPanel。");
                return;
            }

            panel.gameObject.SetActive(true);
            panel.ShowVisual(true);
        }

        public static void Hide()
        {
            if (FactionSelectionPanel.Instance != null)
            {
                FactionSelectionPanel.Instance.ShowVisual(false);
            }

            GameplayGate.Release(GameplayGate.Reason.FactionSelection);
        }

        /// <summary>用户点击红/蓝。合法性在此判断，UI 不直接改比赛状态。</summary>
        public static bool TrySelectFaction(TeamId team)
        {
            if (!TeamIdUtil.IsPlayable(team))
            {
                return false;
            }

            if (!MatchRules.AllowsFactionSubmit(MatchGameManager.CurrentPhase))
            {
                GameLog.Warn("Faction", "当前阶段不允许选阵营 phase=" + MatchGameManager.CurrentPhase);
                return false;
            }

            MatchEndUI.EnsureInstance().RememberLocalTeam(team);
            GameLog.Info("Faction", TeamIdUtil.DisplayName(team));

            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsListening)
            {
                GameLog.Warn("Faction", "尚未进入网络会话，无法选阵营。请先创建/加入房间或进入单机练习。");
                return false;
            }

            if (SteamLobbySession.Instance == null)
            {
                GameLog.Warn("Faction", "找不到会话组件。");
                return false;
            }

            if (network.IsServer)
            {
                SteamLobbySession.Instance.OnClientChoseFaction(network.LocalClientId, team);
                return true;
            }

            if (SpawnManager.Instance == null)
            {
                GameLog.Warn("Faction", "SpawnManager 未就绪，无法申请出生。");
                return false;
            }

            SpawnManager.Instance.SubmitFactionServerRpc((int)team);
            return true;
        }

        /// <summary>本地玩家物体已同步：松开门闩并切到局内视角。</summary>
        public static void NotifyLocalSpawnPresentationReady()
        {
            GameplayGate.Release(GameplayGate.Reason.FactionSelection);
            GameplayGate.Release(GameplayGate.Reason.Lobby);
            GameplayGate.Release(GameplayGate.Reason.Respawn);
            GameplayGate.Release(GameplayGate.Reason.Downed);
            SteamLobbyUI.HideOverviewForGameplay();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
