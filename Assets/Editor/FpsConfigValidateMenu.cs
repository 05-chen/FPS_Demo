#if UNITY_EDITOR
using Config;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Validate：只检查并报告配置是否齐全，不修改资源。
/// </summary>
public static class FpsConfigValidateMenu
{
    [MenuItem("Tools/FPS/Validate/Game Configs", false, 20)]
    public static void ValidateGameConfigs()
    {
        int issues = 0;
        issues += ReportMissing<MatchConfig>("Assets/Resources/Configs/MatchConfig.asset");
        issues += ReportMissing<TeamSpawnConfig>("Assets/Resources/Configs/TeamSpawnConfig.asset");
        issues += ReportMissing<WeaponConfig>("Assets/Resources/Configs/WeaponConfig.asset");
        issues += ReportMissing<SceneFlowConfig>("Assets/Resources/Configs/SceneFlowConfig.asset");

        SceneFlowConfig sceneFlow = AssetDatabase.LoadAssetAtPath<SceneFlowConfig>(
            "Assets/Resources/Configs/SceneFlowConfig.asset");
        if (sceneFlow != null)
        {
            if (string.IsNullOrWhiteSpace(sceneFlow.OfflinePracticeScene)
                || string.IsNullOrWhiteSpace(sceneFlow.OnlineMatchScene))
            {
                issues++;
                Debug.LogWarning("[FPS/Validate] SceneFlowConfig 场景名为空。");
            }
            else
            {
                issues += ReportSceneNotInBuild(sceneFlow.OfflinePracticeScene);
                issues += ReportSceneNotInBuild(sceneFlow.OnlineMatchScene);
            }
        }

        MatchConfig match = AssetDatabase.LoadAssetAtPath<MatchConfig>(
            "Assets/Resources/Configs/MatchConfig.asset");
        if (match != null && match.MaxPlayers < 2)
        {
            issues++;
            Debug.LogWarning("[FPS/Validate] MatchConfig.MaxPlayers < 2。");
        }

        if (issues == 0)
        {
            Debug.Log("[FPS/Validate] 游戏配置检查通过。");
        }
        else
        {
            Debug.LogWarning("[FPS/Validate] 发现问题数=" + issues
                + "。缺省资源可用 Tools/FPS/Build/Create Default Config Assets 生成（不会覆盖已有）。");
        }
    }

    static int ReportMissing<T>(string path) where T : Object
    {
        if (AssetDatabase.LoadAssetAtPath<T>(path) != null)
        {
            return 0;
        }

        Debug.LogWarning("[FPS/Validate] 缺少配置：" + path);
        return 1;
    }

    static int ReportSceneNotInBuild(string sceneName)
    {
        HashSet<string> enabled = new HashSet<string>();
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (!scenes[i].enabled)
            {
                continue;
            }

            string path = scenes[i].path;
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            enabled.Add(System.IO.Path.GetFileNameWithoutExtension(path));
        }

        if (enabled.Contains(sceneName))
        {
            return 0;
        }

        Debug.LogWarning("[FPS/Validate] 场景未勾进 Build Settings（或未启用）：" + sceneName);
        return 1;
    }
}
#endif
