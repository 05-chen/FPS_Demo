using System.Collections.Generic;
using Core;
using Unity.Netcode;
using UnityEngine;

namespace World
{
    /// <summary>
    /// 核心争夺圈（约 30m Sphere Trigger）。只统计 Alive 玩家的占领战力。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class StrongpointArea : MonoBehaviour
    {
        [Header("据点数据")]
        public SectorData sectorData;

        [Header("占领权重")]
        [Tooltip("核心圈人数乘以此权重，例如 3")]
        [SerializeField] int captureWeight = 3;

        readonly HashSet<ulong> _playersInZone = new HashSet<ulong>();
        readonly List<TrackedCollider> _trackedColliders = new List<TrackedCollider>();
        readonly List<MockOccupant> _mockOccupants = new List<MockOccupant>();

        struct TrackedCollider
        {
            public Collider Collider;
            public int InstanceId;
            public ulong ClientId;
        }

        public int CaptureWeight => captureWeight;

        /// <summary>灰盒 Mock：虚拟圈内玩家（无需真实 Player 实例）。</summary>
        public struct MockOccupant
        {
            public ulong ClientId;
            public TeamId Team;
            public PlayerLifeState Life;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!TryGetPlayerClientId(other, out ulong clientId))
            {
                return;
            }

            _playersInZone.Add(clientId);
            _trackedColliders.Add(new TrackedCollider
            {
                Collider = other,
                InstanceId = other.GetInstanceID(),
                ClientId = clientId
            });
        }

        void OnTriggerExit(Collider other)
        {
            if (!TryResolveClientId(other, out ulong clientId))
            {
                CleanInvalidEntries();
                return;
            }

            RemoveOccupant(clientId);
        }

        /// <summary>玩家掉线时由 SectorManager 回调清理。</summary>
        public void OnClientDisconnected(ulong clientId)
        {
            RemoveOccupant(clientId);
            _mockOccupants.RemoveAll(occupant => occupant.ClientId == clientId);
        }

        /// <summary>按 ClientId 清掉圈内记录与 Collider 缓存，避免幽灵人数。</summary>
        public void RemoveOccupant(ulong clientId)
        {
            _playersInZone.Remove(clientId);
            for (int i = _trackedColliders.Count - 1; i >= 0; i--)
            {
                if (_trackedColliders[i].ClientId == clientId)
                {
                    _trackedColliders.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 丢掉已 Destroy / 未激活的 Collider。不依赖 OnTriggerExit 或 Dummy OnDestroy。
        /// </summary>
        public void CleanInvalidEntries()
        {
            _trackedColliders.RemoveAll(tracked => IsDeadOrInactive(tracked.Collider));
            _playersInZone.RemoveWhere(clientId => !IsMockClient(clientId) && !HasLiveCollider(clientId));
        }

        /// <summary>兼容旧调用名，内部转到 CleanInvalidEntries。</summary>
        public void CleanupUnusedReferences() => CleanInvalidEntries();

        static bool IsDeadOrInactive(Collider collider) =>
            !collider || collider.gameObject == null || !collider.gameObject.activeInHierarchy;

        bool HasLiveCollider(ulong clientId)
        {
            for (int i = 0; i < _trackedColliders.Count; i++)
            {
                if (_trackedColliders[i].ClientId == clientId && !IsDeadOrInactive(_trackedColliders[i].Collider))
                {
                    return true;
                }
            }

            return false;
        }

        bool IsMockClient(ulong clientId)
        {
            for (int i = 0; i < _mockOccupants.Count; i++)
            {
                if (_mockOccupants[i].ClientId == clientId)
                {
                    return true;
                }
            }

            return false;
        }

        bool TryResolveClientId(Collider other, out ulong clientId)
        {
            if (TryGetPlayerClientId(other, out clientId))
            {
                return true;
            }

            if (other == null)
            {
                clientId = 0;
                return false;
            }

            int instanceId = other.GetInstanceID();
            for (int i = 0; i < _trackedColliders.Count; i++)
            {
                TrackedCollider tracked = _trackedColliders[i];
                if (tracked.InstanceId != instanceId && tracked.Collider != other)
                {
                    continue;
                }

                clientId = tracked.ClientId;
                return true;
            }

            clientId = 0;
            return false;
        }

        /// <summary>当前核心圈 Client 数量（含 Mock / Dummy）。读前先清已销毁引用。</summary>
        public int DebugOccupantCount
        {
            get
            {
                CleanInvalidEntries();
                return _playersInZone.Count;
            }
        }

        /// <summary>灰盒：指定阵营当前 Alive Mock 人数。</summary>
        public int DebugCountMocks(TeamId team)
        {
            int count = 0;
            for (int i = 0; i < _mockOccupants.Count; i++)
            {
                MockOccupant occupant = _mockOccupants[i];
                if (occupant.Team == team && occupant.Life == PlayerLifeState.Alive)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>灰盒：按增量增减某阵营模拟人数，最少为 0。</summary>
        public void DebugAdjustMockCount(TeamId team, int delta, PlayerLifeState life = PlayerLifeState.Alive)
        {
            if (!TeamIdUtil.IsPlayable(team) || delta == 0)
            {
                return;
            }

            int next = Mathf.Clamp(DebugCountMocks(team) + delta, 0, 16);
            ReplaceTeamMocks(team, next, life);
        }

        void ReplaceTeamMocks(TeamId team, int count, PlayerLifeState life)
        {
            ulong baseId = team == TeamId.Red ? 9100UL : 9200UL;
            for (int i = _mockOccupants.Count - 1; i >= 0; i--)
            {
                if (_mockOccupants[i].Team != team)
                {
                    continue;
                }

                RemoveOccupant(_mockOccupants[i].ClientId);
                _mockOccupants.RemoveAt(i);
            }

            for (int i = 0; i < count; i++)
            {
                DebugInjectMockPlayer(baseId + (ulong)i, team, life);
            }
        }

        /// <summary>灰盒：把全部 Mock 标为死亡，用于验证 Dead 不计分。</summary>
        public void DebugMarkMocksDead()
        {
            for (int i = 0; i < _mockOccupants.Count; i++)
            {
                MockOccupant occupant = _mockOccupants[i];
                occupant.Life = PlayerLifeState.Dead;
                _mockOccupants[i] = occupant;
            }
        }

        /// <summary>灰盒：注入虚拟玩家并写入圈内集合。</summary>
        public void DebugInjectMockPlayer(ulong clientId, TeamId team, PlayerLifeState life = PlayerLifeState.Alive)
        {
            _playersInZone.Add(clientId);
            _mockOccupants.RemoveAll(occupant => occupant.ClientId == clientId);
            _mockOccupants.Add(new MockOccupant
            {
                ClientId = clientId,
                Team = team,
                Life = life
            });
        }

        /// <summary>灰盒：模拟死亡 / 传送离开，清掉指定 Mock。</summary>
        public void DebugRemoveMockPlayer(ulong clientId)
        {
            OnClientDisconnected(clientId);
        }

        /// <summary>灰盒：清空全部 Mock 与圈内记录。</summary>
        public void DebugClearMocks()
        {
            for (int i = 0; i < _mockOccupants.Count; i++)
            {
                RemoveOccupant(_mockOccupants[i].ClientId);
            }

            _mockOccupants.Clear();
        }

        /// <summary>
        /// 输出圈内 Alive 红/蓝有效战力（人数 × captureWeight）。Downed / Dead 不计。
        /// 死亡会关掉 CharacterController，Unity 不发 OnTriggerExit；远端位移靠 NetworkTransform 写入，也不一定发 Enter。
        /// 所以每拍按球体位置重算活人，不把旧 ClientId 留在名单里。
        /// </summary>
        public void EvaluateActivePlayers(out int redCount, out int blueCount)
        {
            CleanInvalidEntries();
            redCount = 0;
            blueCount = 0;
            int weight = Mathf.Max(1, captureWeight);

            if (_mockOccupants.Count > 0)
            {
                for (int i = 0; i < _mockOccupants.Count; i++)
                {
                    MockOccupant occupant = _mockOccupants[i];
                    if (occupant.Life != PlayerLifeState.Alive)
                    {
                        continue;
                    }

                    if (occupant.Team == TeamId.Red)
                    {
                        redCount += weight;
                    }
                    else if (occupant.Team == TeamId.Blue)
                    {
                        blueCount += weight;
                    }
                }

                return;
            }

            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer)
            {
                return;
            }

            Collider zone = GetComponent<Collider>();
            // 补上 Trigger 漏报的活人；人已经走出球体、或不再 Alive，下面的遍历会删掉。
            foreach (var pair in network.ConnectedClients)
            {
                NetworkObject playerObject = pair.Value.PlayerObject;
                if (playerObject != null && IsLivingInside(playerObject, zone))
                {
                    _playersInZone.Add(pair.Key);
                }
            }

            foreach (ulong clientId in new List<ulong>(_playersInZone))
            {
                if (!network.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
                {
                    // Dummy 假人没有 ConnectedClients 条目，不能当幽灵清掉
                    if (SectorTestDummy.IsTracked(clientId))
                    {
                        continue;
                    }

                    RemoveOccupant(clientId);
                    continue;
                }

                // 倒地 / 死亡立刻出名单。只 continue 的话，碰撞被关掉后这份 Id 会一直留着。
                if (!IsLivingInside(client.PlayerObject, zone))
                {
                    RemoveOccupant(clientId);
                    continue;
                }

                TeamId team = PlayerRegistry.GetPlayerTeam(clientId);
                if (team == TeamId.Red)
                {
                    redCount += weight;
                }
                else if (team == TeamId.Blue)
                {
                    blueCount += weight;
                }
            }
        }

        /// <summary>只有 Alive 且站在核心球体里才算占领人头。用球心距离，不用包围盒（盒子比球大，人走出圈仍会算在里面）。</summary>
        static bool IsLivingInside(NetworkObject playerObject, Collider zone)
        {
            if (playerObject == null || zone == null)
            {
                return false;
            }

            if (playerObject.GetComponent<PlayerHealth>() is not PlayerHealth health ||
                health.LifeState != PlayerLifeState.Alive)
            {
                return false;
            }

            return ContainsOccupant(zone, playerObject.transform.position);
        }

        /// <summary>Trigger 上 ClosestPoint 不可靠，球体直接比距离。</summary>
        static bool ContainsOccupant(Collider zone, Vector3 position)
        {
            if (zone is SphereCollider sphere)
            {
                Vector3 center = sphere.transform.TransformPoint(sphere.center);
                Vector3 scale = sphere.transform.lossyScale;
                float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                float radius = sphere.radius * maxScale;
                return (position - center).sqrMagnitude <= radius * radius;
            }

            return zone.bounds.Contains(position);
        }

        static bool TryGetPlayerClientId(Collider other, out ulong clientId) =>
            SectorTriggerFilter.TryGetPlayerClientId(other, out clientId);
    }
}
