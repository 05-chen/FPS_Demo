using World;

namespace Match
{
    /// <summary>
    /// 比赛规则门闩：阶段是否允许生成 / 选阵营 / 开局加载。
    /// 资格表仍由 <see cref="Core.PlayerSessionRoster"/> 负责。
    /// </summary>
    public static class MatchRules
    {
        public static bool AllowsPlayerSpawn(MatchRoundPhase phase) =>
            !MatchRoundPhaseRules.IsPostMatchBlocked(phase);

        public static bool AllowsFactionSubmit(MatchRoundPhase phase) =>
            !MatchRoundPhaseRules.IsPostMatchBlocked(phase);

        public static bool AllowsIndependentFactionSpawn(MatchRoundPhase phase) =>
            MatchRoundPhaseRules.AllowsIndependentFactionSpawn(phase);

        public static bool IsMatchInProgress(MatchRoundPhase phase) =>
            phase == MatchRoundPhase.Playing;

        public static bool IsAwaitingNextRound(MatchRoundPhase phase) =>
            MatchRoundPhaseRules.IsPostMatchWaitingPhase(phase)
            || phase == MatchRoundPhase.PreparingNextRound;
    }
}
