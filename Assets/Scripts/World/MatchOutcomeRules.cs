using Core;

namespace World
{
    /// <summary>
    /// 局内胜负纯规则：推平（占对方 HQ）与限时比占领数。供 MatchGameManager 与白盒单测共用。
    /// </summary>
    public static class MatchOutcomeRules
    {
        /// <summary>红占到 E 或蓝占到 A 立即分出胜负；尚未推平返回 None。</summary>
        public static TeamId EvaluateSweep(TeamId ownerOfSectorA, TeamId ownerOfSectorE)
        {
            if (ownerOfSectorE == TeamId.Red)
            {
                return TeamId.Red;
            }

            if (ownerOfSectorA == TeamId.Blue)
            {
                return TeamId.Blue;
            }

            return TeamId.None;
        }

        /// <summary>倒计时结束：占领数多者胜，相等为 None（平局）。</summary>
        public static TeamId EvaluateTimeout(int redOwnedCount, int blueOwnedCount)
        {
            if (redOwnedCount > blueOwnedCount)
            {
                return TeamId.Red;
            }

            if (blueOwnedCount > redOwnedCount)
            {
                return TeamId.Blue;
            }

            return TeamId.None;
        }
    }
}
