using System.Collections.Generic;
using Core;
using Unity.Netcode;
using UnityEngine;

namespace World
{
    /// <summary>
    /// 核心争夺圈。半径以挂在同一物体上的 SphereCollider 为准（当前场景为 8 米），不是大战区盒子。
    /// 只统计 Alive 玩家。每人计 1 人头，单人打满时间等于 SectorData.captureDuration，不乘 captureWeight。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class StrongpointArea : MonoBehaviour
    {
        [Header("据点数据")]
        public SectorData sectorData;

        [Header("占领权重")]
        [Tooltip("不再加快占点。单人从 0 打到满的时间只看 SectorData.captureDuration。保留字段以免场景序列化丢失。")]
        [SerializeField] int captureWeight = 1;

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

        /// <summary>
        /// 触发器没有刚体时，CharacterController 的 OnTriggerEnter 会丢。
        /// 黑盒测试会自己加刚体；场景上的核心圈以前没有，人站在球里也不进名单。
        /// </summary>
        void Awake()
        {
            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null)
            {
                body = gameObject.AddComponent<Rigidbody>();
            }

            body.isKinematic = true;
            body.useGravity = false;
            EnsureRuntimeRing();
        }

        /// <summary>场景视图里画出核心球，避免把 100×30 的大战区当成占点范围。</summary>
        void OnDrawGizmos()
        {
            ResolveSphere(out Vector3 center, out float radius);
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.95f);
            Gizmos.DrawWireSphere(center, radius);
        }

        /// <summary>运行时在球的赤道画一圈，进游戏也能看见 8 米边界。</summary>
        void EnsureRuntimeRing()
        {
            LineRenderer ring = GetComponent<LineRenderer>();
            if (ring == null)
            {
                ring = gameObject.AddComponent<LineRenderer>();
            }

            const int segments = 48;
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.positionCount = segments;
            ring.widthMultiplier = 0.12f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                ring.material = new Material(shader);
            }

            Color color = new Color(1f, 0.85f, 0.2f, 0.9f);
            ring.startColor = color;
            ring.endColor = color;

            ResolveSphere(out Vector3 center, out float radius);
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                ring.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
        }

        /// <summary>优先用 SphereCollider 的世界半径；没有球时退回 8 米。</summary>
        void ResolveSphere(out Vector3 center, out float radius)
        {
            if (GetComponent<Collider>() is SphereCollider sphere)
            {
                center = sphere.transform.TransformPoint(sphere.center);
                Vector3 scale = sphere.transform.lossyScale;
                float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                radius = sphere.radius * maxScale;
                return;
            }

            center = transform.position;
            radius = 8f;
        }

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
            for (int i = 0; i < _trackedColliders.Count; i++)
            {
                TrackedCollider tracked = _trackedColliders[i];
                if (tracked.ClientId == clientId && tracked.Collider == other)
                {
                    return;
                }
            }

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

        /// <summary>
        /// 死亡只把 CharacterController.enabled 设为 false，物体仍然 active。
        /// 不把禁用碰撞体算掉的话，OnTriggerExit 又不会来，名单就残留。
        /// </summary>
        static bool IsDeadOrInactive(Collider collider) =>
            !collider || !collider.enabled || collider.gameObject == null || !collider.gameObject.activeInHierarchy;

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
        /// 输出圈内 Alive 红/蓝人头。每人 +1，不乘 captureWeight，这样 captureDuration=15 时单人正好 15 秒。
        /// 圈里有真人时只算真人。没有真人时，Mock 仅在调试模式（Alt+F9）打开后才计入。
        /// </summary>
        public void EvaluateActivePlayers(out int redCount, out int blueCount)
        {
            CleanInvalidEntries();
            redCount = 0;
            blueCount = 0;

            if (TryScoreLivingPlayers(out redCount, out blueCount))
            {
                return;
            }

            if (!DebugCommandGate.IsEnabled)
            {
                return;
            }

            ScoreMockOccupants(out redCount, out blueCount);
        }

        /// <summary>圈内有存活真人时写出人头并返回 true，残留 Mock 不得盖过这批人。</summary>
        bool TryScoreLivingPlayers(out int redCount, out int blueCount)
        {
            redCount = 0;
            blueCount = 0;
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer)
            {
                return false;
            }

            Collider zone = GetComponent<Collider>();
            bool anyInside = false;
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

                anyInside = true;
                TeamId team = PlayerRegistry.GetPlayerTeam(clientId);
                if (team == TeamId.Red)
                {
                    redCount++;
                }
                else if (team == TeamId.Blue)
                {
                    blueCount++;
                }
            }

            return anyInside;
        }

        /// <summary>灰盒人数。只在没有真人、且调试模式打开时由 EvaluateActivePlayers 调用。</summary>
        void ScoreMockOccupants(out int redCount, out int blueCount)
        {
            redCount = 0;
            blueCount = 0;
            for (int i = 0; i < _mockOccupants.Count; i++)
            {
                MockOccupant occupant = _mockOccupants[i];
                if (occupant.Life != PlayerLifeState.Alive)
                {
                    continue;
                }

                if (occupant.Team == TeamId.Red)
                {
                    redCount++;
                }
                else if (occupant.Team == TeamId.Blue)
                {
                    blueCount++;
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
