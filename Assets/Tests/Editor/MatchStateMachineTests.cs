using Match;
using NUnit.Framework;
using World;

/// <summary>
/// MatchStateMachine 合法/非法转换白盒测试。
/// </summary>
public class MatchStateMachineTests
{
    [Test]
    public void HappyPath_NoneToPlayingToNextRound()
    {
        Assert.IsTrue(MatchStateMachine.CanTransition(MatchRoundPhase.None, MatchRoundPhase.FactionSelection));
        Assert.IsTrue(MatchStateMachine.CanTransition(MatchRoundPhase.FactionSelection, MatchRoundPhase.Playing));
        Assert.IsTrue(MatchStateMachine.CanTransition(MatchRoundPhase.Playing, MatchRoundPhase.MatchEnded));
        Assert.IsTrue(MatchStateMachine.CanTransition(MatchRoundPhase.MatchEnded, MatchRoundPhase.PostMatchWaiting));
        Assert.IsTrue(MatchStateMachine.CanTransition(MatchRoundPhase.PostMatchWaiting, MatchRoundPhase.PreparingNextRound));
        Assert.IsTrue(MatchStateMachine.CanTransition(MatchRoundPhase.PreparingNextRound, MatchRoundPhase.FactionSelection));
    }

    [Test]
    public void None_MaySkipStraightToPlaying()
    {
        Assert.IsTrue(MatchStateMachine.CanTransition(MatchRoundPhase.None, MatchRoundPhase.Playing));
    }

    [Test]
    public void SamePhase_IsAllowed()
    {
        Assert.IsTrue(MatchStateMachine.CanTransition(MatchRoundPhase.Playing, MatchRoundPhase.Playing));
        Assert.IsTrue(MatchStateMachine.TryTransition(
            MatchRoundPhase.MatchEnded,
            MatchRoundPhase.MatchEnded,
            out string reason));
        Assert.IsNull(reason);
    }

    [Test]
    public void Illegal_PlayingToFactionSelection_RejectedWithReason()
    {
        Assert.IsFalse(MatchStateMachine.CanTransition(
            MatchRoundPhase.Playing,
            MatchRoundPhase.FactionSelection));
        Assert.IsFalse(MatchStateMachine.TryTransition(
            MatchRoundPhase.Playing,
            MatchRoundPhase.FactionSelection,
            out string reason));
        Assert.IsNotNull(reason);
        StringAssert.Contains("Playing", reason);
        StringAssert.Contains("FactionSelection", reason);
    }

    [Test]
    public void Illegal_MatchEndedToFactionSelection_Rejected()
    {
        Assert.IsFalse(MatchStateMachine.CanTransition(
            MatchRoundPhase.MatchEnded,
            MatchRoundPhase.FactionSelection));
    }

    [Test]
    public void Illegal_PostMatchWaitingToPlaying_Rejected()
    {
        Assert.IsFalse(MatchStateMachine.CanTransition(
            MatchRoundPhase.PostMatchWaiting,
            MatchRoundPhase.Playing));
    }

    [Test]
    public void MatchRules_SpawnAndFactionGates()
    {
        Assert.IsFalse(MatchRules.AllowsPlayerSpawn(MatchRoundPhase.MatchEnded));
        Assert.IsFalse(MatchRules.AllowsFactionSubmit(MatchRoundPhase.PreparingNextRound));
        Assert.IsTrue(MatchRules.AllowsPlayerSpawn(MatchRoundPhase.FactionSelection));
        Assert.IsTrue(MatchRules.AllowsIndependentFactionSpawn(MatchRoundPhase.Playing));
        Assert.IsTrue(MatchRules.IsMatchInProgress(MatchRoundPhase.Playing));
        Assert.IsTrue(MatchRules.IsAwaitingNextRound(MatchRoundPhase.PostMatchWaiting));
    }
}
