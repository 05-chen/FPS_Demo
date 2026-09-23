namespace Core
{
    /// <summary>
    /// 服务端玩家资格。只描述「这个连接现在处在哪一步」，不描述血量。
    /// 断线不设单独状态：从表中 Remove，查不到即为 None。
    /// </summary>
    public enum PlayerSessionStatus : byte
    {
        None = 0,
        Connected = 1,
        InLobby = 2,
        PostMatchWaiting = 3,
        FactionChosen = 4,
        InMatch = 5
    }

    public readonly struct PlayerSessionRecord
    {
        public readonly ulong ClientId;
        public readonly PlayerSessionStatus Status;
        public readonly TeamId Team;

        public PlayerSessionRecord(ulong clientId, PlayerSessionStatus status, TeamId team)
        {
            ClientId = clientId;
            Status = status;
            Team = team;
        }
    }

    /// <summary>无网络依赖，可供 EditMode 单测。</summary>
    public static class PlayerSessionRules
    {
        public static bool CanSubmitFaction(PlayerSessionStatus status)
        {
            return status == PlayerSessionStatus.Connected
                || status == PlayerSessionStatus.InLobby
                || status == PlayerSessionStatus.FactionChosen
                || status == PlayerSessionStatus.InMatch;
        }

        public static bool CanSpawnPlayer(PlayerSessionStatus status)
        {
            return status == PlayerSessionStatus.FactionChosen
                || status == PlayerSessionStatus.InMatch;
        }

        public static bool IsMatchParticipant(PlayerSessionStatus status)
        {
            return status == PlayerSessionStatus.InMatch;
        }

        /// <summary>淘汰仍是本局玩家；isDownedOrDead 由 PlayerHealth 提供。</summary>
        public static bool IsEliminated(PlayerSessionStatus status, bool isDownedOrDead)
        {
            return status == PlayerSessionStatus.InMatch && isDownedOrDead;
        }

        /// <summary>None（已 Remove 或未登记）与结算等待中禁止生成。</summary>
        public static bool MustRefuseSpawn(PlayerSessionStatus status)
        {
            return status == PlayerSessionStatus.None
                || status == PlayerSessionStatus.PostMatchWaiting;
        }
    }
}
