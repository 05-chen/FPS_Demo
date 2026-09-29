using Config;

/// <summary>
/// 场景跳转入口。打包必须把配置中的场景都勾进 File → Build Settings。
/// 默认：联机 Testcene_GamePlay；单机 Testcene。
/// </summary>
public static class GameScenes
{
    public const string DefaultOfflinePractice = "Testcene";
    public const string DefaultOnlineMatch = "Testcene_GamePlay";

    public static string OfflinePractice =>
        GameConfigCatalog.SceneFlow != null
            ? GameConfigCatalog.SceneFlow.OfflinePracticeScene
            : DefaultOfflinePractice;

    public static string OnlineMatch =>
        GameConfigCatalog.SceneFlow != null
            ? GameConfigCatalog.SceneFlow.OnlineMatchScene
            : DefaultOnlineMatch;

    public static string ForMatch(bool isOffline)
    {
        return GameConfigCatalog.SceneFlow != null
            ? GameConfigCatalog.SceneFlow.ForMatch(isOffline)
            : (isOffline ? DefaultOfflinePractice : DefaultOnlineMatch);
    }
}
