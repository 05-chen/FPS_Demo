using Core;
using System.Collections;
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

        public static MatchRoundPhase CurrentPhase =>
            Instance != null && Instance.IsSpawned
                ? Instance.RoundPhase.Value
                : MatchRoundPhase.None;

        /// <summary>结算中 / 等待下一局 / 准备下一局：禁止生成玩家、禁止 TryStartMatch 开旧局。</summary>
        public static bool IsPostMatchBlocked =>
            MatchRoundPhaseRules.IsPostMatchBlocked(CurrentPhase);

        /// <summary>允许连接但只能等待：结算中或结算等待计时中。</summary>
        public static bool IsPostMatchWaitingPhase =>
            MatchRoundPhaseRules.IsPostMatchWaitingPhase(CurrentPhase);

        /// <summary>
        /// 这一局还在打：倒计时没结束，且红或蓝仍占着至少一个点。
        /// 客户端断线重连时用它判断，不能把进度清掉。
        /// </summary>
        public bool IsRoundStillActive =>
            IsSpawned
            && !MatchEnded.Value
            && RoundPhase.Value == MatchRoundPhase.Playing
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

        public readonly NetworkVariable<TeamId> WinningTeam = new NetworkVariable<TeamId>(
            TeamId.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> MatchEndedBySweep = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<MatchRoundPhase> RoundPhase = new NetworkVariable<MatchRoundPhase>(
            MatchRoundPhase.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        Coroutine _matchEndBootstrap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;
            if (!IsServer)
            {
                MatchEnded.OnValueChanged += OnMatchEndedChanged;
                RoundPhase.OnValueChanged += OnRoundPhaseChanged;
                _matchEndBootstrap = StartCoroutine(BootstrapMatchEnd());
                return;
            }

            // 局内状态只允许由 ServerHandleMatchEntry/ServerBeginNewRound 初始化。
            // OnNetworkSpawn 可能因场景重载或网络生命周期重复执行，不能在这里清状态。
        }

        IEnumerator BootstrapMatchEnd()
        {
            // 等待 NetworkVariable 初始快照和玩家对象恢复完成。
            yield return null;
            TryShowSynchronizedMatchEnd();
            _matchEndBootstrap = null;
        }

        void OnMatchEndedChanged(bool previous, bool current)
        {
            if (!current)
            {
                return;
            }

            if (_matchEndBootstrap == null)
            {
                _matchEndBootstrap = StartCoroutine(BootstrapMatchEnd());
            }
        }

        void OnRoundPhaseChanged(MatchRoundPhase previous, MatchRoundPhase current)
        {
            if (current == MatchRoundPhase.FactionSelection
                || current == MatchRoundPhase.Playing
                || current == MatchRoundPhase.PreparingNextRound)
            {
                MatchEndUI.EnsureInstance().ForceHide();
            }

            if (current == MatchRoundPhase.PostMatchWaiting
                || current == MatchRoundPhase.PreparingNextRound)
            {
                // 结算等待阶段：未参与本局的客户端由定向 RPC 显示等待，不弹旧局结算。
                if (SteamLobbyUI.IsAwaitingPostMatchAdmission)
                {
                    MatchEndUI.EnsureInstance().ForceHide();
                }
            }
        }

        void TryShowSynchronizedMatchEnd()
        {
            if (!MatchEnded.Value || MatchEndUI.IsAwaitingDismiss)
            {
                return;
            }

            // 结算后新加入的客户端只看等待提示，不能落入已结束对局的结算 UI。
            if (SteamLobbyUI.IsAwaitingPostMatchAdmission || !HasLocalPlayerObject())
            {
                return;
            }

            MatchRoundPhase phase = RoundPhase.Value;
            if (phase == MatchRoundPhase.PostMatchWaiting
                || phase == MatchRoundPhase.PreparingNextRound
                || phase == MatchRoundPhase.FactionSelection
                || phase == MatchRoundPhase.Playing)
            {
                return;
            }

            GameplayGate.Block();
            MatchEndUI.EnsureInstance().Show(WinningTeam.Value, MatchEndedBySweep.Value);
            GameLog.Info("Match", "根据同步状态恢复结算界面 winner=" +
                WinningTeam.Value + " sweep=" + MatchEndedBySweep.Value);
        }

        static bool HasLocalPlayerObject()
        {
            NetworkManager network = NetworkManager.Singleton;
            return network != null
                && network.LocalClient != null
                && network.LocalClient.PlayerObject != null;
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

        /// <summary>服务器设置回合阶段（幂等：同阶段重复写入可接受）。</summary>
        public void ServerSetRoundPhase(MatchRoundPhase phase)
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            if (RoundPhase.Value == phase)
            {
                return;
            }

            RoundPhase.Value = phase;
            GameLog.Info("Match", "RoundPhase -> " + phase);
        }

        /// <summary>通知所有客户端清掉顶栏缓存，避免还画着上一局的 SectorManager。</summary>
        [ClientRpc]
        public void NotifyMatchResetClientRpc()
        {
            UI.SectorHUDUI.InvalidateManagerCache();
            MatchEndUI.EnsureInstance().ForceHide();
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
            WinningTeam.Value = TeamId.None;
            MatchEndedBySweep.Value = false;
            RoundPhase.Value = MatchRoundPhase.FactionSelection;
            GameLog.Info("Match", "对局计时开始 " + MatchTimer.Value.ToString("F0") + " 秒（待选阵营）");
        }

        /// <summary>至少一名玩家已生成后进入 Playing。</summary>
        public void ServerEnterPlayingIfSelecting()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            if (RoundPhase.Value == MatchRoundPhase.FactionSelection
                || RoundPhase.Value == MatchRoundPhase.None)
            {
                RoundPhase.Value = MatchRoundPhase.Playing;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            MatchEnded.OnValueChanged -= OnMatchEndedChanged;
            RoundPhase.OnValueChanged -= OnRoundPhaseChanged;
            if (_matchEndBootstrap != null)
            {
                StopCoroutine(_matchEndBootstrap);
                _matchEndBootstrap = null;
            }

            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            MatchEnded.OnValueChanged -= OnMatchEndedChanged;
            RoundPhase.OnValueChanged -= OnRoundPhaseChanged;
            if (_matchEndBootstrap != null)
            {
                StopCoroutine(_matchEndBootstrap);
                _matchEndBootstrap = null;
            }

            base.OnDestroy();
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || MatchEnded.Value)
            {
                return;
            }

            // 选阵营阶段已重置计时器，但未开打前不扣时间，避免等人时耗尽。
            if (RoundPhase.Value != MatchRoundPhase.Playing)
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
            WinningTeam.Value = winner;
            MatchEndedBySweep.Value = isSweep;
            RoundPhase.Value = MatchRoundPhase.MatchEnded;
            string reason = isSweep ? "推平" : "限时";
            string winnerName = TeamIdUtil.IsPlayable(winner) ? TeamIdUtil.DisplayName(winner) : "平局";
            GameLog.Info("Match", "对局结束 [" + reason + "] 胜者=" + winnerName);
            AnnounceMatchEndClientRpc((int)winner, isSweep);

            if (SteamLobbySession.Instance != null)
            {
                SteamLobbySession.Instance.ServerOnMatchEnded();
            }
        }

        public void SendMatchEndStateToClient(ulong clientId)
        {
            if (!IsServer || !IsSpawned || !MatchEnded.Value)
            {
                return;
            }

            // 结算后新加入者走等待提示，不发旧局结算播报。
            if (IsPostMatchBlocked || IsPostMatchWaitingPhase)
            {
                return;
            }

            ClientRpcParams target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };
            AnnounceMatchEndClientRpc((int)WinningTeam.Value, MatchEndedBySweep.Value, target);
        }

        [ClientRpc]
        void AnnounceMatchEndClientRpc(int winnerTeamValue, bool isSweep, ClientRpcParams rpcParams = default)
        {
            if (SteamLobbyUI.IsAwaitingPostMatchAdmission || !HasLocalPlayerObject())
            {
                return;
            }

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
