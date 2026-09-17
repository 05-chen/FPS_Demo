#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 菜单工具：批量校验并修复角色动画 FBX 的 ModelImporter 配置（Rig / Bake Into Pose / Loop Time）。
/// 只处理目标动画清单里的 11 个文件，避免误伤模型或其它 FBX。
/// 用法：菜单 <b>Tools/Validate and Fix Animation Settings</b>。
///
/// 扫描范围：Project 中若选中了文件夹则只扫这些目录，否则扫整个 Assets。
/// 只按 .fbx 后缀取资源，.meta 天然不在扫描结果里。
/// </summary>
public static class AnimationSettingsValidator
{
    const string MenuPath = "Tools/Validate and Fix Animation Settings";
    const string LogCategory = "AnimSettings";
    const string FbxExtension = ".fbx";

    /// <summary>
    /// 目标动画清单：只有这些同名 FBX 会被校验，防止批量工具误改其它资源。
    /// </summary>
    static readonly string[] TargetNames =
    {
        "Firing Rifle",
        "Jump Down",
        "Jump Loop",
        "Jump Up",
        "Reloading",
        "Rifle Aiming Idle",
        "Rifle Idle",
        "Rifle Run",
        "Walk Backward",
        "Walk Left",
        "Walk Right",
    };

    /// <summary>
    /// 需要开启 Loop Time 的白名单。目标清单里不在此集合的动画一律关闭 Loop Time
    /// （Firing Rifle / Reloading / Jump Up / Jump Down 为一次性动作，循环会卡在末帧）。
    /// </summary>
    static readonly HashSet<string> LoopEnabledNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Jump Loop",
        "Rifle Aiming Idle",
        "Rifle Idle",
        "Rifle Run",
        "Walk Backward",
        "Walk Left",
        "Walk Right",
    };

    /// <summary>单个文件的修复结果，用于最后汇总打印。</summary>
    struct FixResult
    {
        public string Name;
        public bool Changed;
        public List<string> Changes;
    }

    [MenuItem(MenuPath)]
    static void ValidateAndFix()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Animation Settings",
                "请先退出 Play 模式再执行：导入设置在运行时重导入会打断对局。", "知道了");
            return;
        }

        List<string> paths = CollectTargetPaths();
        if (paths.Count == 0)
        {
            GameLog.Warn(LogCategory, "没有在扫描范围内找到任何目标动画 FBX，请确认选中了正确的文件夹。");
            EditorUtility.DisplayDialog("Animation Settings",
                "没有找到目标动画 FBX。\n请选中 Assets/Model/Soldier/Animations 后再执行。", "知道了");
            return;
        }

        List<FixResult> results = new List<FixResult>();
        List<string> foundNames = new List<string>();
        int fixedCount = 0;

        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                if (EditorUtility.DisplayCancelableProgressBar("校验并修复动画导入设置",
                        path, (float)i / paths.Count))
                {
                    GameLog.Warn(LogCategory, "用户中止了本次批量修复。");
                    break;
                }

                foundNames.Add(Path.GetFileNameWithoutExtension(path));
                FixResult result = FixOne(path);
                results.Add(result);
                if (result.Changed)
                {
                    fixedCount++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        ReportMissingTargets(foundNames);
        ReportResults(results, fixedCount);
    }

    // ------------------------------------------------------------------
    // 扫描
    // ------------------------------------------------------------------

    /// <summary>
    /// 在选中目录（无选中则整个 Assets）下找出目标清单里的 FBX 路径。
    /// 同名文件（不同目录重复导入）只取第一个，避免重复修复同一套设置。
    /// </summary>
    static List<string> CollectTargetPaths()
    {
        HashSet<string> wanted = new HashSet<string>(TargetNames, StringComparer.OrdinalIgnoreCase);
        string[] roots = ResolveScanRoots();
        string[] guids = AssetDatabase.FindAssets("t:Model", roots);

        List<string> found = new List<string>();
        HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // 只认 .fbx：同时把 .meta 与 .obj/.blend 等其它模型格式排除在外。
            if (!path.EndsWith(FbxExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = Path.GetFileNameWithoutExtension(path);
            if (!wanted.Contains(name) || !seenNames.Add(name))
            {
                continue;
            }

            found.Add(path);
        }

        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    /// <summary>选中文件夹时以其为扫描根，否则回退到整个 Assets。</summary>
    static string[] ResolveScanRoots()
    {
        List<string> roots = new List<string>();
        foreach (UnityEngine.Object selected in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(selected);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
            {
                roots.Add(path);
            }
        }

        return roots.Count > 0 ? roots.ToArray() : new[] { "Assets" };
    }

    // ------------------------------------------------------------------
    // 修复
    // ------------------------------------------------------------------

    /// <summary>
    /// 校验并修复单个 FBX：Humanoid Rig、三项 Bake Into Pose、按白名单设置 Loop Time。
    /// 只在确有改动时才 SaveAndReimport，避免无谓地重导入 11 个文件。
    /// </summary>
    static FixResult FixOne(string path)
    {
        FixResult result = new FixResult
        {
            Name = Path.GetFileName(path),
            Changed = false,
            Changes = new List<string>(),
        };

        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            GameLog.Warn(LogCategory, $"{result.Name} 不是 ModelImporter 资源，已跳过。");
            return result;
        }

        bool changed = false;
        bool dirty = false;
        bool loopEnabled = LoopEnabledNames.Contains(Path.GetFileNameWithoutExtension(path));

        // 1. Rig 必须是 Humanoid，否则动画无法重定向到玩家的 Humanoid Avatar。
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            result.Changes.Add($"Rig {importer.animationType} → Human");
            importer.animationType = ModelImporterAnimationType.Human;
            importer.SaveAndReimport();
            changed = true;

            // 切换 Humanoid 会重建默认剪辑列表，先让 Rig 变更落地再改剪辑，
            // 否则写回的目标可能是旧的 Generic 剪辑列表。故此处重取 importer。
            importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                GameLog.Warn(LogCategory, $"{result.Name} 切换 Humanoid 后重新获取 importer 失败，剪辑设置未写入。");
                result.Changed = true;
                return result;
            }
        }

        // 关掉 Import Animation 时读不到任何剪辑，先打开再进入下面的剪辑修复。
        if (!importer.importAnimation)
        {
            result.Changes.Add("Import Animation 关 → 开");
            importer.importAnimation = true;
            dirty = true;
        }

        // 2 & 3. 逐剪辑修复 Bake Into Pose 与 Loop Time
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            // 尚未在 Inspector 里展开过剪辑列表，用文件默认剪辑作为基线再写回。
            clips = importer.defaultClipAnimations;
        }

        if (clips == null || clips.Length == 0)
        {
            GameLog.Warn(LogCategory, $"{result.Name} 未解析出任何动画剪辑，无法配置 Bake Into Pose / Loop Time。");
            if (dirty)
            {
                importer.SaveAndReimport();
                changed = true;
            }

            result.Changed = changed;
            return result;
        }

        bool clipTouched = false;
        bool bakeFixed = false;
        bool basisFixed = false;
        bool loopFixed = false;

        for (int i = 0; i < clips.Length; i++)
        {
            ModelImporterClipAnimation clip = clips[i];
            bool clipDirty = false;

            // 位移与旋转全部锁定在根节点：否则播动画时角色会被根骨骼自带位移带跑，
            // 与 NGO 的 NetworkTransform 位置同步打架，出现「越走越偏」。
            if (!clip.lockRootRotation)
            {
                clip.lockRootRotation = true;
                clipDirty = true;
                bakeFixed = true;
            }

            if (!clip.lockRootHeightY)
            {
                clip.lockRootHeightY = true;
                clipDirty = true;
                bakeFixed = true;
            }

            if (!clip.lockRootPositionXZ)
            {
                clip.lockRootPositionXZ = true;
                clipDirty = true;
                bakeFixed = true;
            }

            // Based Upon = Original，保留原始帧基准，避免重定向时被参考姿势改写。
            if (!clip.keepOriginalOrientation)
            {
                clip.keepOriginalOrientation = true;
                clipDirty = true;
                basisFixed = true;
            }

            if (!clip.keepOriginalPositionY)
            {
                clip.keepOriginalPositionY = true;
                clipDirty = true;
                basisFixed = true;
            }

            if (!clip.keepOriginalPositionXZ)
            {
                clip.keepOriginalPositionXZ = true;
                clipDirty = true;
                basisFixed = true;
            }

            if (clip.loopTime != loopEnabled)
            {
                clip.loopTime = loopEnabled;
                clipDirty = true;
                loopFixed = true;
            }

            if (clipDirty)
            {
                clips[i] = clip;
                clipTouched = true;
            }
        }

        // 任一剪辑字段有改动就整体写回；用默认剪辑作为基线时，这一步同时把列表展开到 meta 里。
        if (clipTouched)
        {
            importer.clipAnimations = clips;
            dirty = true;
        }

        if (bakeFixed)
        {
            result.Changes.Add("Bake Into Pose（Rotation / Position Y / Position XZ）已开启");
        }

        if (basisFixed)
        {
            result.Changes.Add("Based Upon 已改为 Original");
        }

        if (loopFixed)
        {
            result.Changes.Add($"Loop Time → {(loopEnabled ? "开" : "关")}");
        }

        if (dirty)
        {
            importer.SaveAndReimport();
            changed = true;
        }

        result.Changed = changed;
        return result;
    }

    // ------------------------------------------------------------------
    // 汇总
    // ------------------------------------------------------------------

    /// <summary>目标清单里有、但扫描范围内没找到的文件单独告警，避免「以为全修好了」。</summary>
    static void ReportMissingTargets(List<string> foundNames)
    {
        HashSet<string> found = new HashSet<string>(foundNames, StringComparer.OrdinalIgnoreCase);
        List<string> missing = new List<string>();
        foreach (string target in TargetNames)
        {
            if (!found.Contains(target))
            {
                missing.Add(target + ".fbx");
            }
        }

        if (missing.Count > 0)
        {
            GameLog.Warn(LogCategory, $"目标清单中有 {missing.Count} 个文件未在扫描范围内找到：{string.Join("、", missing)}");
        }
    }

    /// <summary>打印修复数量与逐个文件的详细清单。</summary>
    static void ReportResults(List<FixResult> results, int fixedCount)
    {
        int unchanged = results.Count - fixedCount;
        GameLog.Info(LogCategory,
            $"动画导入设置校验完成：检查 {results.Count} 个，修复 {fixedCount} 个，已符合 {unchanged} 个。");

        StringBuilder detail = new StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            FixResult result = results[i];
            if (result.Changed)
            {
                detail.AppendLine($"  [已修复] {result.Name} → {string.Join("；", result.Changes)}");
            }
            else
            {
                detail.AppendLine($"  [无需改动] {result.Name}");
            }
        }

        GameLog.Info(LogCategory, "详细清单：\n" + detail.ToString().TrimEnd());
    }
}
#endif
