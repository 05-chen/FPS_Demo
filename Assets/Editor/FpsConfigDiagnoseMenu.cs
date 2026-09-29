#if UNITY_EDITOR
using Config;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Diagnose：输出当前配置解析结果，不修改资源。
/// </summary>
public static class FpsConfigDiagnoseMenu
{
    [MenuItem("Tools/FPS/Diagnose/Game Configs", false, 30)]
    public static void DiagnoseGameConfigs()
    {
        // 强制走与运行时相同的目录加载路径（Editor 下也可用 Resources.Load）。
        MatchConfig match = Resources.Load<MatchConfig>("Configs/MatchConfig");
        TeamSpawnConfig spawn = Resources.Load<TeamSpawnConfig>("Configs/TeamSpawnConfig");
        WeaponConfig weapon = Resources.Load<WeaponConfig>("Configs/WeaponConfig");
        SceneFlowConfig scenes = Resources.Load<SceneFlowConfig>("Configs/SceneFlowConfig");

        Debug.Log(
            "[FPS/Diagnose] Match=" + Describe(match)
            + " duration=" + (match != null ? match.MatchDurationSeconds.ToString("F0") : "-")
            + " maxPlayers=" + (match != null ? match.MaxPlayers.ToString() : "-"));
        Debug.Log(
            "[FPS/Diagnose] TeamSpawn=" + Describe(spawn)
            + " red=" + (spawn != null ? spawn.RedSpawnPosition.ToString() : "-")
            + " blue=" + (spawn != null ? spawn.BlueSpawnPosition.ToString() : "-"));
        Debug.Log(
            "[FPS/Diagnose] Weapon=" + Describe(weapon)
            + " fireRate=" + (weapon != null ? weapon.FireRate.ToString("F2") : "-")
            + " damage=" + (weapon != null ? weapon.HitscanDamage.ToString() : "-"));
        Debug.Log(
            "[FPS/Diagnose] SceneFlow=" + Describe(scenes)
            + " offline=" + (scenes != null ? scenes.OfflinePracticeScene : "-")
            + " online=" + (scenes != null ? scenes.OnlineMatchScene : "-"));
    }

    static string Describe(Object asset) =>
        asset != null ? asset.name : "<missing, runtime will use memory defaults>";
}
#endif
