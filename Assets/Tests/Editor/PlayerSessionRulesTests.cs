using Core;
using NUnit.Framework;

/// <summary>
/// 玩家资格规则白盒测试（无 NetworkManager 依赖）。
/// </summary>
public class PlayerSessionRulesTests
{
    [Test]
    public void NoneAndWaiting_CannotSpawn()
    {
        Assert.IsTrue(PlayerSessionRules.MustRefuseSpawn(PlayerSessionStatus.None));
        Assert.IsTrue(PlayerSessionRules.MustRefuseSpawn(PlayerSessionStatus.PostMatchWaiting));
        Assert.IsFalse(PlayerSessionRules.MustRefuseSpawn(PlayerSessionStatus.FactionChosen));
        Assert.IsFalse(PlayerSessionRules.MustRefuseSpawn(PlayerSessionStatus.InMatch));
    }

    [Test]
    public void DeadInMatch_IsEliminated_ButStillParticipant()
    {
        Assert.IsTrue(PlayerSessionRules.IsMatchParticipant(PlayerSessionStatus.InMatch));
        Assert.IsTrue(PlayerSessionRules.IsEliminated(PlayerSessionStatus.InMatch, true));
        Assert.IsFalse(PlayerSessionRules.IsEliminated(PlayerSessionStatus.InMatch, false));
        Assert.IsFalse(PlayerSessionRules.IsEliminated(PlayerSessionStatus.None, true));
    }

    [Test]
    public void WaitingClient_CannotSubmitFaction()
    {
        Assert.IsFalse(PlayerSessionRules.CanSubmitFaction(PlayerSessionStatus.PostMatchWaiting));
        Assert.IsFalse(PlayerSessionRules.CanSubmitFaction(PlayerSessionStatus.None));
        Assert.IsTrue(PlayerSessionRules.CanSubmitFaction(PlayerSessionStatus.InLobby));
        Assert.IsTrue(PlayerSessionRules.CanSubmitFaction(PlayerSessionStatus.FactionChosen));
    }

    [Test]
    public void Roster_RemoveClearsRecord()
    {
        var roster = new PlayerSessionRoster();
        roster.MarkConnected(7);
        roster.MarkFactionChosen(7, TeamId.Red);
        Assert.IsTrue(roster.HasChosenTeam(7));

        roster.Remove(7);
        Assert.AreEqual(PlayerSessionStatus.None, roster.GetStatus(7));
        Assert.IsFalse(roster.HasChosenTeam(7));
        Assert.IsTrue(PlayerSessionRules.MustRefuseSpawn(roster.GetStatus(7)));
    }

    [Test]
    public void Roster_ResetNextRoundKeepsConnectedClearsTeam()
    {
        var roster = new PlayerSessionRoster();
        roster.MarkInMatch(1, TeamId.Red);
        roster.MarkPostMatchWaiting(2);
        roster.Remove(3);

        roster.ResetConnectedPlayersForNextRound();
        Assert.AreEqual(PlayerSessionStatus.InLobby, roster.GetStatus(1));
        Assert.AreEqual(TeamId.None, roster.GetTeam(1));
        Assert.AreEqual(PlayerSessionStatus.InLobby, roster.GetStatus(2));
        Assert.AreEqual(PlayerSessionStatus.None, roster.GetStatus(3));
    }
}
