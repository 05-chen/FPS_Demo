#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

/// <summary>
/// 编辑器启动时把 steam_appid.txt 放到 Unity.exe 旁边；
/// 打包后再拷到游戏 exe 旁边。Steam 会在这两个位置找 AppID。
/// </summary>
[InitializeOnLoad]
public static class SteamBuildCopyAppId
{
    const string AppId = "480";

    static SteamBuildCopyAppId()
    {
        EditorApplication.delayCall += CopyAppIdBesideUnityEditor;
    }

    static void CopyAppIdBesideUnityEditor()
    {
        try
        {
            string editorDir = Path.GetDirectoryName(EditorApplication.applicationPath);
            if (string.IsNullOrEmpty(editorDir))
            {
                return;
            }

            string dest = Path.Combine(editorDir, "steam_appid.txt");
            if (File.Exists(dest) && File.ReadAllText(dest).Trim() == AppId)
            {
                return;
            }

            File.WriteAllText(dest, AppId);
            Debug.Log("已把 steam_appid.txt 写到 Unity 编辑器目录: " + dest);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("无法把 steam_appid.txt 写到 Unity 安装目录（可忽略，项目根目录已有该文件）。\n" + e.Message);
        }
    }

    [PostProcessBuild]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.StandaloneWindows && target != BuildTarget.StandaloneWindows64
            && target != BuildTarget.StandaloneLinux64 && target != BuildTarget.StandaloneOSX)
        {
            return;
        }

        string source = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "steam_appid.txt");
        if (!File.Exists(source))
        {
            File.WriteAllText(source, AppId);
        }

        string outputDir = Path.GetDirectoryName(pathToBuiltProject);
        string dest = Path.Combine(outputDir, "steam_appid.txt");
        File.Copy(source, dest, true);
        Debug.Log("已复制 steam_appid.txt -> " + dest);
    }
}
#endif
