using NUnit.Framework;
using World;

/// <summary>
/// 对局结束后加入 / 下一局阶段机的白盒规则。
/// </summary>
public class MatchRoundPhaseTests
{
    [Test]
    public void PostMatchBlocked_CoversEndedWaitingAndPreparing()
    {
        Assert.IsTrue(MatchRoundPhaseRules.IsPostMatchBlocked(MatchRoundPhase.MatchEnded));
        Assert.IsTrue(MatchRoundPhaseRules.IsPostMatchBlocked(MatchRoundPhase.PostMatchWaiting));
        Assert.IsTrue(MatchRoundPhaseRules.IsPostMatchBlocked(MatchRoundPhase.PreparingNextRound));
        Assert.IsFalse(MatchRoundPhaseRules.IsPostMatchBlocked(MatchRoundPhase.FactionSelection));
        Assert.IsFalse(MatchRoundPhaseRules.IsPostMatchBlocked(MatchRoundPhase.Playing));
        Assert.IsFalse(MatchRoundPhaseRules.IsPostMatchBlocked(MatchRoundPhase.None));
    }

    [Test]
    public void WaitingPhase_OnlyEndedAndPostMatchWaiting()
    {
        Assert.IsTrue(MatchRoundPhaseRules.IsPostMatchWaitingPhase(MatchRoundPhase.MatchEnded));
        Assert.IsTrue(MatchRoundPhaseRules.IsPostMatchWaitingPhase(MatchRoundPhase.PostMatchWaiting));
        Assert.IsFalse(MatchRoundPhaseRules.IsPostMatchWaitingPhase(MatchRoundPhase.PreparingNextRound));
        Assert.IsFalse(MatchRoundPhaseRules.IsPostMatchWaitingPhase(MatchRoundPhase.FactionSelection));
    }

    [Test]
    public void IndependentFactionSpawn_OnlyAfterNextRoundReady()
    {
        Assert.IsTrue(MatchRoundPhaseRules.AllowsIndependentFactionSpawn(MatchRoundPhase.FactionSelection));
        Assert.IsTrue(MatchRoundPhaseRules.AllowsIndependentFactionSpawn(MatchRoundPhase.Playing));
        Assert.IsFalse(MatchRoundPhaseRules.AllowsIndependentFactionSpawn(MatchRoundPhase.MatchEnded));
        Assert.IsFalse(MatchRoundPhaseRules.AllowsIndependentFactionSpawn(MatchRoundPhase.PostMatchWaiting));
        Assert.IsFalse(MatchRoundPhaseRules.AllowsIndependentFactionSpawn(MatchRoundPhase.PreparingNextRound));
    }
}
