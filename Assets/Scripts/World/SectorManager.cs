using Core;
using Unity.Netcode;
using UnityEngine;

namespace World
{
    /// <summary>
    /// 单个线性战区（Sector_A ~ Sector_E）。场景多实例，左右邻居决定攻防解锁。
    /// 网格只感知驻留；核心圈人数仅在该阵营处于前线时才推动 CaptureProgress。
    /// </summary>
    public class SectorManager : NetworkBehaviour
    {
        [Header("战区标识")]
        [SerializeField] string sectorId = "Sector_C";
        [Tooltip("A 区设 Red，E 区设 Blue，B/C/D 设 None")]
        [SerializeField] TeamId defaultOwner = TeamId.None;

        [Header("区域 Trigger")]
        [SerializeField] GridSectorArea gridSectorArea;
        [SerializeField] StrongpointArea strongPointArea;

        [Header("线性邻居（左=更靠红方 HQ，右=更靠蓝方 HQ）")]
        [SerializeField] SectorManager leftNeighbor;
        [SerializeField] SectorManager rightNeighbor;

        [Header("网络同步变量")]
        public readonly NetworkVariable<TeamId> OwnerTeam = new NetworkVariable<TeamId>(
            TeamId.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>-1 蓝方完全占领，0 中立，1 红方完全占领。</summary>
        public readonly NetworkVariable<float> CaptureProgress = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>核心圈有效红方战力（已按前线过滤，供调试面板）。</summary>
        public readonly NetworkVariable<int> OccupantRedCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>核心圈有效蓝方战力（已按前线过滤）。</summary>
        public readonly NetworkVariable<int> OccupantBlueCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>首次打满 ±1 后永久为 true，HUD 据此禁止再露灰底。</summary>
        public readonly NetworkVariable<bool> HasBeenCaptured = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [Header("实时战力(仅 Server 调试可视)")]
        [SerializeField] int activeRedWeight;
        [SerializeField] int activeBlueWeight;

        int _loggedRedWeight = int.MinValue;
        int _loggedBlueWeight = int.MinValue;
        float _nextCaptureLogTime;
        bool _debugIgnoreFrontline;

        public string SectorId => sectorId;

        public TeamId DefaultOwner => defaultOwner;

        public string PointName
        {
            get
            {
                string name = strongPointArea != null && strongPointArea.sectorData != null
                    ? strongPointArea.sectorData.pointName
                    : null;
                return string.IsNullOrEmpty(name)
                    ? (string.IsNullOrEmpty(sectorId) ? "据点" : sectorId)
                    : name;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer)
            {
                return;
            }

            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            base.OnDestroy();
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || MatchGameManager.IsMatchOver)
            {
                return;
            }

            EvaluateFrontlineForces(out activeRedWeight, out activeBlueWeight);
            SyncOccupantCounts(activeRedWeight, activeBlueWeight);
            UpdateCaptureLogic(activeRedWeight, activeBlueWeight);
            SnapRearGuardedProgress();
            LogCaptureState(activeRedWeight, activeBlueWeight);
        }

        /// <summary>打满 ±1 时闩上历史占领标记，且不再清回 false。</summary>
        void SyncHasBeenCaptured(float progress)
        {
            bool next = SectorCaptureRules.LatchHasBeenCaptured(HasBeenCaptured.Value, progress);
            if (HasBeenCaptured.Value != next)
            {
                HasBeenCaptured.Value = next;
            }

            if (strongPointArea != null && strongPointArea.sectorData != null)
            {
                strongPointArea.sectorData.HasBeenCaptured = next;
            }
        }

        void HandleClientDisconnected(ulong clientId)
        {
            gridSectorArea?.OnClientDisconnected(clientId);
            strongPointArea?.OnClientDisconnected(clientId);
        }

        /// <summary>
        /// 该战区对该阵营是否可争夺：直接邻居至少有一个属于该阵营。禁止越级。
        /// </summary>
        public bool IsActiveFrontlineFor(TeamId team)
        {
            return SectorCaptureRules.IsActiveFrontlineFor(
                team,
                OwnerTeam.Value,
                IsOwnedBy(leftNeighbor, team),
                IsOwnedBy(rightNeighbor, team));
        }

        /// <summary>邻接控制链：该阵营是否已连接本区（可攻）。不含己方占领区的防守权。</summary>
        public bool IsUnlockedFor(TeamId team) => IsActiveFrontlineFor(team);

        /// <summary>进圈/防守：己方占领区（含 HQ）永远允许；否则必须已进攻解锁。</summary>
        public bool CanOccupyOrDefend(TeamId team) =>
            SectorCaptureRules.CanOccupyOrDefend(team, OwnerTeam.Value, IsUnlockedFor(team));

        /// <summary>
        /// 后方保护：本区已占领，且朝敌方向的邻居也是己方时，对敌上锁（如 B 变红后的 A）。
        /// </summary>
        public bool IsRearGuarded
        {
            get
            {
                if (!IsSpawned)
                {
                    return false;
                }

                if (OwnerTeam.Value == TeamId.Red)
                {
                    return IsOwnedBy(rightNeighbor, TeamId.Red);
                }

                if (OwnerTeam.Value == TeamId.Blue)
                {
                    return IsOwnedBy(leftNeighbor, TeamId.Blue);
                }

                return false;
            }
        }

        /// <summary>红或蓝至少一方已连上本区，核心圈人数才会推动进度。</summary>
        public bool IsCapturable =>
            IsActiveFrontlineFor(TeamId.Red) || IsActiveFrontlineFor(TeamId.Blue);

        /// <summary>己方已完全占领。</summary>
        public bool IsFriendlySector(TeamId team) => OwnerTeam.Value == team;

        /// <summary>已被对方占领（中立不算敌区）。</summary>
        public bool IsEnemySector(TeamId team) =>
            OwnerTeam.Value != TeamId.None && OwnerTeam.Value != team;

        /// <summary>
        /// 常规兵种出生点：只能放在己方占领区。侦察兵等可跳过此限制，改查 IsEnemySector。
        /// </summary>
        public bool CanPlaceStandardSpawnPoint(TeamId team) => IsFriendlySector(team);

        /// <summary>网格驻留查询，供后续兵种部署使用。</summary>
        public bool IsPlayerInGrid(ulong clientId) =>
            gridSectorArea != null && gridSectorArea.IsPlayerInGrid(clientId);

        static bool IsOwnedBy(SectorManager neighbor, TeamId team)
        {
            if (neighbor == null)
            {
                return false;
            }

            return SectorCaptureRules.IsNeighborOwnedBy(
                true,
                neighbor.IsSpawned,
                neighbor.IsSpawned ? neighbor.OwnerTeam.Value : neighbor.DefaultOwner,
                neighbor.DefaultOwner,
                team);
        }

        /// <summary>统计核心圈战力：进攻已解锁，或本区已是该阵营占领（HQ 防守）。</summary>
        void EvaluateFrontlineForces(out int redWeight, out int blueWeight)
        {
            redWeight = 0;
            blueWeight = 0;
            if (strongPointArea == null)
            {
                return;
            }

            strongPointArea.EvaluateActivePlayers(out int redInCircle, out int blueInCircle);
            if (CanOccupyOrDefend(TeamId.Red))
            {
                redWeight = redInCircle;
            }

            if (CanOccupyOrDefend(TeamId.Blue))
            {
                blueWeight = blueInCircle;
            }
        }

        void LogCaptureState(int redWeight, int blueWeight)
        {
            bool weightsChanged = redWeight != _loggedRedWeight || blueWeight != _loggedBlueWeight;
            if (!weightsChanged && Time.unscaledTime < _nextCaptureLogTime)
            {
                return;
            }

            if (!weightsChanged && redWeight == 0 && blueWeight == 0)
            {
                return;
            }

            _loggedRedWeight = redWeight;
            _loggedBlueWeight = blueWeight;
            _nextCaptureLogTime = Time.unscaledTime + 0.25f;
            GameLog.Info(
                "Sector",
                sectorId + " 实时战力: Red=" + redWeight + ", Blue=" + blueWeight +
                ", Progress=" + CaptureProgress.Value.ToString("F3"));
        }

        void SyncOccupantCounts(int redWeight, int blueWeight)
        {
            if (OccupantRedCount.Value != redWeight)
            {
                OccupantRedCount.Value = redWeight;
            }

            if (OccupantBlueCount.Value != blueWeight)
            {
                OccupantBlueCount.Value = blueWeight;
            }
        }

        void UpdateCaptureLogic(int redWeight, int blueWeight)
        {
            if (!IsCapturable)
            {
                return;
            }

            SectorData data = strongPointArea != null ? strongPointArea.sectorData : null;
            float duration = Mathf.Max(1f, data != null ? data.captureDuration : 15f);
            float decay = data != null ? Mathf.Max(0f, data.emptyDecayPerSecond) : 0f;
            float current = SectorCaptureRules.ApplyCaptureTick(
                CaptureProgress.Value,
                redWeight,
                blueWeight,
                duration,
                Time.deltaTime,
                decay);
            if (Mathf.Approximately(current, CaptureProgress.Value))
            {
                return;
            }

            CaptureProgress.Value = current;
            SyncHasBeenCaptured(current);

            TeamId nextOwner = SectorCaptureRules.ResolveOwnerAfterProgress(OwnerTeam.Value, current);
            if (nextOwner != OwnerTeam.Value)
            {
                OwnerTeam.Value = nextOwner;
                GameLog.Info("Sector", "[战线] " + PointName + " 已被" +
                    (nextOwner == TeamId.Red ? "红方" : "蓝方") + "占领。");
            }
        }

        /// <summary>
        /// 前方据点已被己方占住时，本点不再保留拉锯进度。
        /// 否则 C 被对方抢回去后，B 会把藏起来的半截进度再画出来。
        /// </summary>
        void SnapRearGuardedProgress()
        {
            if (!IsRearGuarded)
            {
                return;
            }

            float target = OwnerTeam.Value == TeamId.Red ? 1f : OwnerTeam.Value == TeamId.Blue ? -1f : 0f;
            if (Mathf.Approximately(CaptureProgress.Value, target))
            {
                return;
            }

            CaptureProgress.Value = target;
            SyncHasBeenCaptured(target);
        }

        /// <summary>新开一局时写回开局归属。HasBeenCaptured 必须清掉，否则中立点会继续画上一局的红蓝条。</summary>
        public void ServerResetForNewMatch()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            OwnerTeam.Value = defaultOwner;
            CaptureProgress.Value = defaultOwner switch
            {
                TeamId.Red => 1f,
                TeamId.Blue => -1f,
                _ => 0f
            };
            bool fullyOwned = CaptureProgress.Value >= 1f || CaptureProgress.Value <= -1f;
            HasBeenCaptured.Value = fullyOwned;
            OccupantRedCount.Value = 0;
            OccupantBlueCount.Value = 0;
            if (strongPointArea != null && strongPointArea.sectorData != null)
            {
                strongPointArea.sectorData.HasBeenCaptured = fullyOwned;
            }
        }

        /// <summary>红或蓝是否还占着至少一个点。用来判断这一局的占领状态还在不在。</summary>
        public static bool AnySideHoldsPoint()
        {
            SectorManager[] managers = FindAll();
            for (int i = 0; i < managers.Length; i++)
            {
                SectorManager manager = managers[i];
                if (manager == null || !manager.IsSpawned)
                {
                    continue;
                }

                if (manager.OwnerTeam.Value == TeamId.Red || manager.OwnerTeam.Value == TeamId.Blue)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>主机开新局时重置场景里全部战区。对局进行中不得调用。</summary>
        public static void ServerResetAllForNewMatch()
        {
            SectorManager[] managers = FindAll();
            for (int i = 0; i < managers.Length; i++)
            {
                if (managers[i] != null)
                {
                    managers[i].ServerResetForNewMatch();
                }
            }
        }

        /// <summary>灰盒：邻接锁定不可关闭，调用会被拒绝。</summary>
        public void DebugSetIgnoreFrontline(bool ignore)
        {
            _debugIgnoreFrontline = false;
            if (ignore)
            {
                GameLog.Error("Sector", sectorId + " 拒绝 IgnoreFrontline：必须走邻接解锁。");
            }
        }

        /// <summary>灰盒：写入占领。未对该阵营解锁时拒绝赋值。</summary>
        public bool DebugSetCaptureState(float progress, TeamId owner, bool requireUnlock = true)
        {
            if (!IsSpawned || !IsServer)
            {
                return false;
            }

            if (requireUnlock && !CanDebugWriteCapture(owner))
            {
                GameLog.Error(
                    "Sector",
                    sectorId + " 未对 " + owner + " 解锁（无己方邻接领地），拒绝写入 CaptureProgress。");
                return false;
            }

            CaptureProgress.Value = Mathf.Clamp(progress, -1f, 1f);
            OwnerTeam.Value = owner;
            SyncHasBeenCaptured(CaptureProgress.Value);
            return true;
        }

        /// <summary>
        /// 只改进度。未打满 ±1 不改归属，便于测 0.5 / 0.95。未解锁则拒绝。
        /// </summary>
        public bool DebugSetCaptureProgress(float progress, TeamId actingTeam)
        {
            if (!IsSpawned || !IsServer)
            {
                return false;
            }

            if (!TeamIdUtil.IsPlayable(actingTeam) || !IsUnlockedFor(actingTeam))
            {
                GameLog.Warn(
                    "Sector",
                    sectorId + " 对" + TeamIdUtil.DisplayName(actingTeam) +
                    "未解锁，拒绝写入半进度/临界进度。");
                return false;
            }

            float clamped = Mathf.Clamp(progress, -1f, 1f);
            CaptureProgress.Value = clamped;
            SyncHasBeenCaptured(clamped);
            TeamId nextOwner = SectorCaptureRules.ResolveOwnerAfterProgress(OwnerTeam.Value, clamped);
            if (nextOwner != OwnerTeam.Value)
            {
                OwnerTeam.Value = nextOwner;
                GameLog.Info("Sector", "[战线] " + PointName + " 已被" +
                    (nextOwner == TeamId.Red ? "红方" : "蓝方") + "占领。");
            }

            return true;
        }

        bool CanDebugWriteCapture(TeamId owner)
        {
            if (TeamIdUtil.IsPlayable(owner))
            {
                return IsUnlockedFor(owner);
            }

            return IsCapturable;
        }

        public GridSectorArea DebugGrid => gridSectorArea;
        public StrongpointArea DebugStrongpoint => strongPointArea;
        public int DebugActiveRedWeight => activeRedWeight;
        public int DebugActiveBlueWeight => activeBlueWeight;
        public bool DebugIgnoresFrontline => _debugIgnoreFrontline;

        /// <summary>场景内全部战区（含尚未 Spawn）。</summary>
        public static SectorManager[] FindAll() =>
            FindObjectsByType<SectorManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        /// <summary>按 SectorId 查找（如 Sector_C）。</summary>
        public static SectorManager FindBySectorId(string id)
        {
            SectorManager[] managers = FindAll();
            for (int i = 0; i < managers.Length; i++)
            {
                if (managers[i] != null && managers[i].SectorId == id)
                {
                    return managers[i];
                }
            }

            return null;
        }
    }
}
