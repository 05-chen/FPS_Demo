using System.Collections.Generic;
using UnityEngine;

namespace World
{
    /// <summary>
    /// 大战区网格（A/B/C/D/E 的 Box Trigger）。只记录谁在格子里，不参与占领权重。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class GridSectorArea : MonoBehaviour
    {
        readonly HashSet<ulong> _playersInGrid = new HashSet<ulong>();
        readonly List<TrackedCollider> _trackedColliders = new List<TrackedCollider>();

        struct TrackedCollider
        {
            public Collider Collider;
            public int InstanceId;
            public ulong ClientId;
        }

        /// <summary>该 Client 是否仍在本战区网格内（不看生死）。</summary>
        public bool IsPlayerInGrid(ulong clientId)
        {
            CleanInvalidEntries();
            return _playersInGrid.Contains(clientId);
        }

        void OnTriggerEnter(Collider other)
        {
            if (!TryGetPlayerClientId(other, out ulong clientId))
            {
                return;
            }

            _playersInGrid.Add(clientId);
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

        /// <summary>玩家掉线时由 SectorManager 回调，避免幽灵驻留。</summary>
        public void OnClientDisconnected(ulong clientId) => RemoveOccupant(clientId);

        /// <summary>按 ClientId 清掉网格驻留与 Collider 缓存。</summary>
        public void RemoveOccupant(ulong clientId)
        {
            _playersInGrid.Remove(clientId);
            for (int i = _trackedColliders.Count - 1; i >= 0; i--)
            {
                if (_trackedColliders[i].ClientId == clientId)
                {
                    _trackedColliders.RemoveAt(i);
                }
            }
        }

        /// <summary>丢掉已 Destroy / 未激活的 Collider，不依赖 OnTriggerExit 或 OnDestroy。</summary>
        public void CleanInvalidEntries()
        {
            _trackedColliders.RemoveAll(tracked => IsDeadOrInactive(tracked.Collider));
            _playersInGrid.RemoveWhere(clientId => !HasLiveCollider(clientId));
        }

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

        /// <summary>灰盒测试：直接注入网格驻留 ClientId。</summary>
        public void DebugInjectPlayer(ulong clientId) => _playersInGrid.Add(clientId);

        /// <summary>灰盒测试：移出网格驻留。</summary>
        public void DebugRemovePlayer(ulong clientId) => RemoveOccupant(clientId);

        /// <summary>当前网格内 Client 数量（含 Mock / Dummy）。</summary>
        public int DebugOccupantCount
        {
            get
            {
                CleanInvalidEntries();
                return _playersInGrid.Count;
            }
        }

        static bool TryGetPlayerClientId(Collider other, out ulong clientId) =>
            SectorTriggerFilter.TryGetPlayerClientId(other, out clientId);
    }
}
