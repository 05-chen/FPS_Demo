#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Weapon;

/// <summary>
/// Editor 菜单入口：对场景 / 选中预制体中的 Player 执行武器层级诊断。
/// </summary>
public static class WeaponHierarchyDiagnosticMenu
{
    const string MenuPath = "Tools/Diagnose Player Weapon Hierarchy";
    const string LogCategory = "WeaponHierarchyDiag";

    [MenuItem(MenuPath)]
    static void Diagnose()
    {
        // Project 窗口选中的 .prefab：临时加载内容只读诊断后卸载。
        if (TryDiagnoseSelectedPrefabAsset())
        {
            return;
        }

        GameObject player = ResolveSceneOrSelectionPlayer();
        if (player == null)
        {
            GameLog.Error(
                LogCategory,
                "未找到 Player。请在 Hierarchy 选中 Player（或带 PlayerController 的对象），或打开含 Player 的场景 / 在 Project 选中 Player.prefab。");
            return;
        }

        GameLog.Info(LogCategory, $"开始诊断: {player.name}（来源: {DescribeSource(player)}）");
        WeaponHierarchyDiagnostic.DiagnosePlayer(player);
    }

    /// <summary>
    /// 若当前选中的是预制体资产，加载内容诊断并返回 true（无论成败都算已处理选中资产）。
    /// </summary>
    static bool TryDiagnoseSelectedPrefabAsset()
    {
        Object selected = Selection.activeObject;
        if (selected == null)
        {
            return false;
        }

        string path = AssetDatabase.GetAssetPath(selected);
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
        try
        {
            GameObject player = WeaponHierarchyDiagnostic.ResolvePlayer(prefabRoot);
            if (player == null)
            {
                GameLog.Error(LogCategory, $"预制体 {path} 中未找到 Player。");
                return true;
            }

            GameLog.Info(LogCategory, $"开始诊断: {player.name}（来源: Prefab Asset → {path}）");
            WeaponHierarchyDiagnostic.DiagnosePlayer(player);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    /// <summary>
    /// 解析顺序：选中 Hierarchy 物体 → 场景 Tag=Player / PlayerController。
    /// </summary>
    static GameObject ResolveSceneOrSelectionPlayer()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected != null)
        {
            GameObject fromSelection = WeaponHierarchyDiagnostic.ResolvePlayer(selected);
            if (fromSelection != null)
            {
                return fromSelection;
            }
        }

        return WeaponHierarchyDiagnostic.ResolvePlayer(null);
    }

    static string DescribeSource(GameObject player)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(player))
        {
            return "Prefab Instance in Scene";
        }

        return player.scene.IsValid() ? $"Scene:{player.scene.name}" : "Unknown";
    }
}
#endif
