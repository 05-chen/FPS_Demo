using Steamworks;

/// <summary>
/// Steamworks 运行时门面。业务代码走这里，避免到处直接调 SteamManager / SteamUser。
/// </summary>
public static class SteamRuntime
{
    public static bool IsInitialized => SteamManager.Initialized;

    public static bool IsOnline => IsInitialized && SteamUser.BLoggedOn();

    public static string PersonaName => IsInitialized ? SteamFriends.GetPersonaName() : string.Empty;

    public static CSteamID LocalSteamId => IsInitialized ? SteamUser.GetSteamID() : CSteamID.Nil;

    public static bool TryGetLocalSteamId(out CSteamID steamId)
    {
        steamId = LocalSteamId;
        return steamId.IsValid() && steamId.BIndividualAccount();
    }
}
