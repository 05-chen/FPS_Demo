/// <summary>
/// 场景跳转入口。打包必须把这两个场景都勾进 File → Build Settings。
/// 联机对局：Testcene_GamePlay；单机练习：Testcene。
/// </summary>
public static class GameScenes
{
    public const string OfflinePractice = "Testcene";
    public const string OnlineMatch = "Testcene_GamePlay";

    public static string ForMatch(bool isOffline)
    {
        return isOffline ? OfflinePractice : OnlineMatch;
    }
}
