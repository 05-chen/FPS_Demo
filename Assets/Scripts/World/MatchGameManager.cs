using Core;
using UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace World
{
    /// <summary>
    /// 局内倒计时与胜负结算（服务端权威）。推平：红占 E 或蓝占 A；限时：比占领数。
    /// </summary>
    public class MatchGameManager : NetworkBehaviour
    {
        public static MatchGameManager Instance { get; private set; }

        public static bool IsMatchOver =>
            Instance != null && Instance.IsSpawned && Instance.MatchEnded.Value;

        /// <summary>
        /// 这一局还在打：倒计时没结束，且红或蓝仍占着至少一个点。
        /// 客户端断线重连时用它判断，不能把进度清掉。
        /// </summary>
        public bool IsRoundStillActive =>
            IsSpawned
            && !MatchEnded.Value
            && MatchTimer.Value > 0f
            && SectorManager.AnySideHoldsPoint();

        [Header("倒计时")]
        [SerializeField] float matchDurationSeconds = 900f;

        public readonly NetworkVariable<float> MatchTimer = new NetworkVariable<float>(
            900f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> MatchEnded = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;
            if (!IsServer)
            {
                return;
            }

            MatchEnded.Value = false;
            MatchTimer.Value = Mathf.Max(1f, matchDurationSeconds);
            GameLog.Info("Match", "对局计时开始 " + MatchTimer.Value.ToString("F0") + " 秒");
        }

        /// <summary>
        /// 主机权威的进场判定。只有主机新开一局才重置进度和倒计时；
        /// 对局还在时（例如非主机退出再进）一律保留。
        /// </summary>
        public static void ServerHandleMatchEntry(bool hostOpenedNewRound)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            if (!hostOpenedNewRound)
            {
                if (Instance != null && Instance.IsRoundStillActive)
                {
                    GameLog.Info("Match", "对局未结束，保留占领进度与倒计时。");
                }

                return;
            }

            SectorManager.ServerResetAllForNewMatch();
            if (Instance != null && Instance.IsSpawned)
            {
                Instance.ServerBeginNewRound();
                Instance.NotifyMatchResetClientRpc();
            }

            GameLog.Info("Match", "主机新开一局，占领进度与倒计时已重置。");
        }

        /// <summary>通知所有客户端清掉顶栏缓存，避免还画着上一局的 SectorManager。</summary>
        [ClientRpc]
        public void NotifyMatchResetClientRpc()
        {
            UI.SectorHUDUI.InvalidateManagerCache();
            GameLog.Info("Match", "客户端已收到开局重置通知。");
        }

        /// <summary>主机新开一局时清倒计时和胜负标记，否则占点 Update 会一直停着。</summary>
        public void ServerBeginNewRound()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            MatchEnded.Value = false;
            MatchTimer.Value = Mathf.Max(1f, matchDurationSeconds);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDestroy();
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || MatchEnded.Value)
            {
                return;
            }

            MatchTimer.Value = Mathf.Max(0f, MatchTimer.Value - Time.deltaTime);
            TryEndBySweep();
            if (!MatchEnded.Value && MatchTimer.Value <= 0f)
            {
                EndByTimeout();
            }
        }

        void TryEndBySweep()
        {
            SectorManager sectorA = SectorManager.FindBySectorId("Sector_A");
            SectorManager sectorE = SectorManager.FindBySectorId("Sector_E");
            TeamId ownerA = sectorA != null && sectorA.IsSpawned ? sectorA.OwnerTeam.Value : TeamId.None;
            TeamId ownerE = sectorE != null && sectorE.IsSpawned ? sectorE.OwnerTeam.Value : TeamId.None;
            TeamId winner = MatchOutcomeRules.EvaluateSweep(ownerA, ownerE);
            if (!TeamIdUtil.IsPlayable(winner))
            {
                return;
            }

            Conclude(winner, true);
        }

        void EndByTimeout()
        {
            CountOwnedSectors(out int redOwned, out int blueOwned);
            TeamId winner = MatchOutcomeRules.EvaluateTimeout(redOwned, blueOwned);
            Conclude(winner, false);
        }

        static void CountOwnedSectors(out int redOwned, out int blueOwned)
        {
            redOwned = 0;
            blueOwned = 0;
            SectorManager[] managers = SectorManager.FindAll();
            for (int i = 0; i < managers.Length; i++)
            {
                SectorManager manager = managers[i];
                if (manager == null || !manager.IsSpawned)
                {
                    continue;
                }

                if (manager.OwnerTeam.Value == TeamId.Red)
                {
                    redOwned++;
                }
                else if (manager.OwnerTeam.Value == TeamId.Blue)
                {
                    blueOwned++;
                }
            }
        }

        void Conclude(TeamId winner, bool isSweep)
        {
            if (!IsServer || !IsSpawned || MatchEnded.Value)
            {
                return;
            }

            MatchEnded.Value = true;
            MatchTimer.Value = 0f;
            string reason = isSweep ? "推平" : "限时";
            string winnerName = TeamIdUtil.IsPlayable(winner) ? TeamIdUtil.DisplayName(winner) : "平局";
            GameLog.Info("Match", "对局结束 [" + reason + "] 胜者=" + winnerName);
            AnnounceMatchEndClientRpc((int)winner, isSweep);
        }

        [ClientRpc]
        void AnnounceMatchEndClientRpc(int winnerTeamValue, bool isSweep)
        {
            TeamId winner = TeamIdUtil.FromNetwork(winnerTeamValue);
            GameplayGate.Block();
            MatchEndUI.EnsureInstance().Show(winner, isSweep);
            GameLog.Info("Match", "收到结算 RPC winner=" + winner + " sweep=" + isSweep);
        }

        /// <summary>
        /// 结算界面被点击。只切换 UI，保持 NGO 连接与 Steam 大厅不动。
        /// 对局结束不等于退出房间：断网只允许由暂停菜单的「退出到大厅」触发。
        /// </summary>
        public static void NotifyMatchEndDismissed()
        {
            if (SteamLobbyUI.Instance != null)
            {
                SteamLobbyUI.Instance.EnterPostMatchWaiting();
                return;
            }

            //单机练习没有会话，退回练习场景是安全的(没有远端会因此掉线)
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }

            SceneManager.LoadScene(GameScenes.OfflinePractice);
        }

        /// <summary>Debug：立刻按限时规则结算。</summary>
        public void DebugExpireTimer()
        {
            if (!IsServer || !IsSpawned || MatchEnded.Value)
            {
                return;
            }

            MatchTimer.Value = 0f;
            EndByTimeout();
        }

        /// <summary>Debug：把对方 HQ 写成己方并触发推平。</summary>
        public void DebugForceSweep(TeamId winner)
        {
            if (!IsServer || !IsSpawned || !TeamIdUtil.IsPlayable(winner))
            {
                return;
            }

            string hqId = winner == TeamId.Red ? "Sector_E" : "Sector_A";
            SectorManager hq = SectorManager.FindBySectorId(hqId);
            if (hq != null)
            {
                hq.DebugSetCaptureState(winner == TeamId.Red ? 1f : -1f, winner, false);
            }

            Conclude(winner, true);
        }
    }
}
