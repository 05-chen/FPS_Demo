using World;

namespace Match
{
    /// <summary>
    /// 比赛回合阶段转换表（纯逻辑，可供 EditMode 单测）。
    /// 网络同步仍使用 <see cref="MatchRoundPhase"/> NetworkVariable；
    /// 大厅流程继续用 <see cref="LobbySessionState"/>，不混进本状态机。
    /// </summary>
    public static class MatchStateMachine
    {
        /// <summary>同阶段视为允许（由调用方做幂等跳过）。</summary>
        public static bool CanTransition(MatchRoundPhase from, MatchRoundPhase to)
        {
            if (from == to)
            {
                return true;
            }

            switch (from)
            {
                case MatchRoundPhase.None:
                    return to == MatchRoundPhase.FactionSelection
                        || to == MatchRoundPhase.Playing;

                case MatchRoundPhase.FactionSelection:
                    return to == MatchRoundPhase.Playing;

                case MatchRoundPhase.Playing:
                    return to == MatchRoundPhase.MatchEnded;

                case MatchRoundPhase.MatchEnded:
                    return to == MatchRoundPhase.PostMatchWaiting;

                case MatchRoundPhase.PostMatchWaiting:
                    return to == MatchRoundPhase.PreparingNextRound;

                case MatchRoundPhase.PreparingNextRound:
                    return to == MatchRoundPhase.FactionSelection;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 尝试转换。非法时返回 false，并给出拒绝原因（供日志）。
        /// </summary>
        public static bool TryTransition(
            MatchRoundPhase from,
            MatchRoundPhase to,
            out string rejectReason)
        {
            if (from == to)
            {
                rejectReason = null;
                return true;
            }

            if (CanTransition(from, to))
            {
                rejectReason = null;
                return true;
            }

            rejectReason = "不允许 " + from + " -> " + to
                + "。合法路径：None→FactionSelection/Playing，"
                + "FactionSelection→Playing，Playing→MatchEnded，"
                + "MatchEnded→PostMatchWaiting，PostMatchWaiting→PreparingNextRound，"
                + "PreparingNextRound→FactionSelection。";
            return false;
        }
    }
}
