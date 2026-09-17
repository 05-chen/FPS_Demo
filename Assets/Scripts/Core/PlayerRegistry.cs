using System.Collections.Generic;
using Core;

/// <summary>
/// 玩家实例注册表。替代满场景 FindObjectsByType，避免每帧 / 每次选阵营扫全场景。
/// </summary>
public static class PlayerRegistry
{
    static readonly List<PlayerController> Players = new List<PlayerController>(4);

    public static IReadOnlyList<PlayerController> All => Players;

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Players.Clear();
    }

    public static void Register(PlayerController player)
    {
        if (player != null && !Players.Contains(player))
        {
            Players.Add(player);
        }
    }

    public static void Unregister(PlayerController player)
    {
        if (player != null)
        {
            Players.Remove(player);
        }
    }

    public static PlayerController FindLocalOwned()
    {
        for (int i = 0; i < Players.Count; i++)
        {
            PlayerController player = Players[i];
            if (player != null && player.IsSpawned && player.IsOwner)
            {
                return player;
            }
        }

        return null;
    }

    public static TeamId GetPlayerTeam(ulong clientId)
    {
        for (int i = 0; i < Players.Count; i++)
        {
            PlayerController player = Players[i];
            if (player == null || !player.IsSpawned)
            {
                continue;
            }

            if (player.OwnerClientId == clientId)
            {
                return player.ResolveTeam();
            }
        }

        return TeamId.None;
    }

    public static bool IsTeamTaken(TeamId team, PlayerController except = null)
    {
        if (team == TeamId.None)
        {
            return false;
        }

        for (int i = 0; i < Players.Count; i++)
        {
            PlayerController player = Players[i];
            if (player == null || player == except || !player.IsSpawned)
            {
                continue;
            }

            if (player.OccupiesTeam(team))
            {
                return true;
            }
        }

        return false;
    }
}
