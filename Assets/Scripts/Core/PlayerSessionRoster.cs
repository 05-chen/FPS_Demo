using System.Collections.Generic;

namespace Core
{
    /// <summary>
    /// 服务端玩家资格表。按 clientId 记状态和阵营，不把 ConnectedClientsIds 当作资格。
    /// 断线调用 Remove，不保留 Left 记录。
    /// </summary>
    public sealed class PlayerSessionRoster
    {
        readonly Dictionary<ulong, PlayerSessionRecord> _records =
            new Dictionary<ulong, PlayerSessionRecord>();

        public int Count => _records.Count;

        public void MarkConnected(ulong clientId)
        {
            Set(clientId, PlayerSessionStatus.InLobby, TeamId.None);
        }

        public void MarkPostMatchWaiting(ulong clientId)
        {
            Set(clientId, PlayerSessionStatus.PostMatchWaiting, GetTeam(clientId));
        }

        public void MarkFactionChosen(ulong clientId, TeamId team)
        {
            Set(clientId, PlayerSessionStatus.FactionChosen, team);
        }

        public void MarkInMatch(ulong clientId, TeamId team)
        {
            Set(clientId, PlayerSessionStatus.InMatch, team);
        }

        /// <summary>断线或退出：直接从表中删除，不再保留记录。</summary>
        public void Remove(ulong clientId)
        {
            _records.Remove(clientId);
        }

        /// <summary>下一局：表中仍在的人都回到大厅，阵营清空。</summary>
        public void ResetConnectedPlayersForNextRound()
        {
            List<ulong> stay = new List<ulong>(_records.Keys);
            _records.Clear();
            for (int i = 0; i < stay.Count; i++)
            {
                Set(stay[i], PlayerSessionStatus.InLobby, TeamId.None);
            }
        }

        public void Clear()
        {
            _records.Clear();
        }

        public bool TryGet(ulong clientId, out PlayerSessionRecord record)
        {
            return _records.TryGetValue(clientId, out record);
        }

        public PlayerSessionStatus GetStatus(ulong clientId)
        {
            return _records.TryGetValue(clientId, out PlayerSessionRecord record)
                ? record.Status
                : PlayerSessionStatus.None;
        }

        public TeamId GetTeam(ulong clientId)
        {
            return _records.TryGetValue(clientId, out PlayerSessionRecord record)
                ? record.Team
                : TeamId.None;
        }

        public bool HasChosenTeam(ulong clientId)
        {
            if (!_records.TryGetValue(clientId, out PlayerSessionRecord record))
            {
                return false;
            }

            if (!TeamIdUtil.IsPlayable(record.Team))
            {
                return false;
            }

            return record.Status == PlayerSessionStatus.FactionChosen
                || record.Status == PlayerSessionStatus.InMatch;
        }

        /// <summary>下一局通知选阵营：结算等待中的人，或尚未进入 InMatch 的人。</summary>
        public bool NeedsFactionSelectForNextRound(ulong clientId)
        {
            PlayerSessionStatus status = GetStatus(clientId);
            if (status == PlayerSessionStatus.None)
            {
                return false;
            }

            return status == PlayerSessionStatus.PostMatchWaiting
                || status == PlayerSessionStatus.InLobby
                || status == PlayerSessionStatus.Connected
                || status == PlayerSessionStatus.FactionChosen;
        }

        void Set(ulong clientId, PlayerSessionStatus status, TeamId team)
        {
            _records[clientId] = new PlayerSessionRecord(clientId, status, team);
        }
    }
}
