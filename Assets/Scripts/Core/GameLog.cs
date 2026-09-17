using UnityEngine;

/// <summary>
/// 带分类前缀的日志。正式项目里可以再接到文件 / 遥测，调用点不用改。
/// </summary>
public static class GameLog
{
    const string Prefix = "[Game]";

    public static void Info(string category, string message)
    {
        Debug.Log(Prefix + "[" + category + "] " + message);
    }

    public static void Warn(string category, string message)
    {
        Debug.LogWarning(Prefix + "[" + category + "] " + message);
    }

    public static void Error(string category, string message)
    {
        Debug.LogError(Prefix + "[" + category + "] " + message);
    }
}
