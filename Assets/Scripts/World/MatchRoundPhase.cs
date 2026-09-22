namespace World
{
    /// <summary>
    /// 对局回合服务器权威阶段。PlayerSpawn 按 clientId 独立执行，不作为全局阶段。
    /// </summary>
    public enum MatchRoundPhase : byte
    {
        None = 0,
        Playing = 1,
        MatchEnded = 2,
        PostMatchWaiting = 3,
        PreparingNextRound = 4,
        FactionSelection = 5
    }

    /// <summary>RoundPhase 判定辅助（无 NetworkVariable 依赖，可供 EditMode 测试）。</summary>
    public static class MatchRoundPhaseRules
    {
        /// <summary>禁止生成玩家、禁止 TryStartMatch 重开旧局。</summary>
        public static bool IsPostMatchBlocked(MatchRoundPhase phase) =>
            phase == MatchRoundPhase.MatchEnded
            || phase == MatchRoundPhase.PostMatchWaiting
            || phase == MatchRoundPhase.PreparingNextRound;

        /// <summary>允许连接但只能显示等待提示。</summary>
        public static bool IsPostMatchWaitingPhase(MatchRoundPhase phase) =>
            phase == MatchRoundPhase.MatchEnded
            || phase == MatchRoundPhase.PostMatchWaiting;

        /// <summary>下一局已开放按 clientId 独立选阵营 / 生成。</summary>
        public static bool AllowsIndependentFactionSpawn(MatchRoundPhase phase) =>
            phase == MatchRoundPhase.FactionSelection
            || phase == MatchRoundPhase.Playing;
    }
}
