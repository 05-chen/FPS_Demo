#if UNITY_EDITOR
using Config;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Build：创建默认玩法配置资源（写入 Resources/Configs，不静默改其它资产）。
/// </summary>
public static class FpsConfigBuildMenu
{
    const string ResourcesConfigsDir = "Assets/Resources/Configs";

    [MenuItem("Tools/FPS/Build/Create Default Config Assets", false, 10)]
    public static void CreateDefaultConfigAssets()
    {
        EnsureFolder("Assets/Resources");
        EnsureFolder(ResourcesConfigsDir);

        CreateIfMissing<MatchConfig>(ResourcesConfigsDir + "/MatchConfig.asset");
        CreateIfMissing<TeamSpawnConfig>(ResourcesConfigsDir + "/TeamSpawnConfig.asset");
        CreateIfMissing<WeaponConfig>(ResourcesConfigsDir + "/WeaponConfig.asset");
        CreateIfMissing<SceneFlowConfig>(ResourcesConfigsDir + "/SceneFlowConfig.asset");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[FPS/Build] 默认配置已就绪：" + ResourcesConfigsDir);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
        {
            return;
        }

        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, name);
    }

    static void CreateIfMissing<T>(string assetPath) where T : ScriptableObject
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (existing != null)
        {
            Debug.Log("[FPS/Build] 已存在，跳过：" + assetPath);
            return;
        }

        T asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, assetPath);
        Debug.Log("[FPS/Build] 已创建：" + assetPath);
    }
}
#endif
