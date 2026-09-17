#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Weapon;

/// <summary>
/// 菜单工具：一键把 PlayerAnimatorController 与主角色 Avatar 绑到 Player 模型子节点的 Animator 上，
/// 并确保根节点挂有 PlayerAnimationManager。
/// 用法：选中 Player（或留空让工具自己找）→ 菜单 <b>Tools/Auto Bind Player Animator</b>。
/// </summary>
public static class PlayerAnimatorBinder
{
    const string MenuPath = "Tools/Auto Bind Player Animator";
    const string LogCategory = "AnimBinder";
    const string ControllerFileName = "PlayerAnimatorController.controller";
    const string ModelFolder = "Assets/Model/Soldier";

    /// <summary>主角色模型文件名（注意工程里拼写是 Solider）。按优先级排列。</summary>
    static readonly string[] PreferredBodyModelNames =
    {
        "redSolider.fbx",
        "blueSolider.fbx",
        "redSoldier.fbx",
        "blueSoldier.fbx",
    };

    /// <summary>动作 FBX 自带的临时 Avatar 名称关键字，绑定主 Avatar 时一律排除。</summary>
    static readonly string[] AnimationAvatarExcludeKeywords =
    {
        "Rifle Idle",
        "Rifle Run",
        "Rifle Aiming Idle",
        "Firing Rifle",
        "Reloading",
        "Jump Up",
        "Jump Down",
        "Jump Loop",
        "Walk Left",
        "Walk Right",
        "Walk Backward",
    };

    [MenuItem(MenuPath)]
    static void Bind()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Auto Bind Player Animator",
                "请先退出 Play 模式再执行绑定。", "知道了");
            return;
        }

        GameObject player = ResolvePlayerRoot();
        if (player == null)
        {
            EditorUtility.DisplayDialog("Auto Bind Player Animator",
                "找不到 Player 根节点。\n请先在 Hierarchy 中选中 Player，或确认场景里存在带 PlayerController / Tag=Player 的对象。",
                "知道了");
            return;
        }

        Transform modelRoot = ResolveModelRoot(player.transform);
        if (modelRoot == null)
        {
            EditorUtility.DisplayDialog("Auto Bind Player Animator",
                $"在 {player.name} 下找不到模型根节点（ModelRoot / SkinnedMesh / Soldier）。", "知道了");
            return;
        }

        RuntimeAnimatorController controller = FindAnimatorController();
        if (controller == null)
        {
            EditorUtility.DisplayDialog("Auto Bind Player Animator",
                $"工程中找不到 {ControllerFileName}。", "知道了");
            return;
        }

        Avatar avatar = FindOrCreateBodyAvatar(out string avatarSource);
        if (avatar == null)
        {
            EditorUtility.DisplayDialog("Auto Bind Player Animator",
                "找不到可用的主角色 Avatar。\n请将 redSolider.fbx 的 Rig 设为 Humanoid → Create From This Model 后重试。",
                "知道了");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Auto Bind Player Animator");

        Animator animator = EnsureAnimator(modelRoot.gameObject);
        Undo.RecordObject(animator, "Bind Animator Assets");
        animator.runtimeAnimatorController = controller;
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        EditorUtility.SetDirty(animator);

        PlayerAnimationManager manager = EnsureAnimationManager(player, animator);

        MarkDirty(player);
        Undo.CollapseUndoOperations(undoGroup);

        Selection.activeGameObject = player;
        EditorGUIUtility.PingObject(modelRoot.gameObject);

        GameLog.Info(LogCategory,
            $"✅ 已成功将 {controller.name} 与 {avatar.name}（来源：{avatarSource}）绑定至 {modelRoot.name} 节点；" +
            $"applyRootMotion=false；PlayerAnimationManager={(manager != null ? "已就绪" : "缺失")}。");
    }

    // ------------------------------------------------------------------
    // Player / ModelRoot
    // ------------------------------------------------------------------

    /// <summary>
    /// 优先用选中对象（可向上追溯到带 PlayerController / Tag=Player 的根），
    /// 否则在活动场景里搜 PlayerController，再退到 Tag=Player。
    /// </summary>
    static GameObject ResolvePlayerRoot()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected != null && !EditorUtility.IsPersistent(selected) && selected.scene.IsValid())
        {
            Transform current = selected.transform;
            while (current != null)
            {
                if (current.GetComponent<PlayerController>() != null || current.CompareTag("Player"))
                {
                    return current.gameObject;
                }

                current = current.parent;
            }
        }

        PlayerController[] controllers = UnityEngine.Object.FindObjectsOfType<PlayerController>();
        foreach (PlayerController controller in controllers)
        {
            if (controller != null && controller.gameObject.scene.IsValid())
            {
                return controller.gameObject;
            }
        }

        GameObject[] tagged = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject candidate in tagged)
        {
            if (candidate != null && candidate.scene.IsValid() && candidate.transform.parent == null)
            {
                return candidate;
            }
        }

        return tagged.Length > 0 ? tagged[0] : null;
    }

    /// <summary>
    /// 在 Player 子层级找模型根：优先 ModelRoot 名，其次带 SkinnedMesh 的子树根，
    /// 再按名称关键字 Model / Soldier / Solider / Red / Blue。
    /// </summary>
    static Transform ResolveModelRoot(Transform player)
    {
        Transform named = FindDirectChild(player, "ModelRoot");
        if (named != null)
        {
            return named;
        }

        SkinnedMeshRenderer[] skins = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (skins != null && skins.Length > 0)
        {
            Transform skinRoot = skins[0].transform;
            while (skinRoot.parent != null && skinRoot.parent != player)
            {
                skinRoot = skinRoot.parent;
            }

            return skinRoot;
        }

        for (int i = 0; i < player.childCount; i++)
        {
            Transform child = player.GetChild(i);
            string lower = child.name.ToLowerInvariant();
            if (lower.Contains("model")
                || lower.Contains("soldier")
                || lower.Contains("solider")
                || lower.Contains("red")
                || lower.Contains("blue"))
            {
                return child;
            }
        }

        // 最后兜底：带 mixamorig 骨架的直接子节点也算模型根。
        for (int i = 0; i < player.childCount; i++)
        {
            Transform child = player.GetChild(i);
            if (FindDescendantNameContains(child, "mixamorig") != null)
            {
                return child;
            }
        }

        return null;
    }

    static Transform FindDirectChild(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    static Transform FindDescendantNameContains(Transform root, string keyword)
    {
        string lowerKeyword = keyword.ToLowerInvariant();
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            if (t.name.ToLowerInvariant().Contains(lowerKeyword))
            {
                return t;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Animator / Manager
    // ------------------------------------------------------------------

    static Animator EnsureAnimator(GameObject modelRoot)
    {
        Animator animator = modelRoot.GetComponent<Animator>();
        if (animator == null)
        {
            animator = Undo.AddComponent<Animator>(modelRoot);
            GameLog.Info(LogCategory, $"在 {modelRoot.name} 上新增 Animator 组件。");
        }

        return animator;
    }

    /// <summary>
    /// 根节点没有 PlayerAnimationManager 就补挂，并把 animator 引用写进序列化字段。
    /// </summary>
    static PlayerAnimationManager EnsureAnimationManager(GameObject player, Animator animator)
    {
        PlayerAnimationManager manager = player.GetComponent<PlayerAnimationManager>();
        if (manager == null)
        {
            manager = Undo.AddComponent<PlayerAnimationManager>(player);
            GameLog.Info(LogCategory, $"在 {player.name} 根节点挂载 PlayerAnimationManager。");
        }

        SerializedObject so = new SerializedObject(manager);
        SerializedProperty animatorProp = so.FindProperty("animator");
        if (animatorProp != null && animatorProp.objectReferenceValue != animator)
        {
            animatorProp.objectReferenceValue = animator;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
        }

        SerializedProperty controllerProp = so.FindProperty("playerController");
        if (controllerProp != null && controllerProp.objectReferenceValue == null)
        {
            controllerProp.objectReferenceValue = player.GetComponent<PlayerController>();
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
        }

        SerializedProperty weaponProp = so.FindProperty("playerWeapon");
        if (weaponProp != null && weaponProp.objectReferenceValue == null)
        {
            PlayerWeapon weapon = player.GetComponent<PlayerWeapon>();
            if (weapon == null)
            {
                weapon = player.GetComponentInChildren<PlayerWeapon>(true);
            }

            weaponProp.objectReferenceValue = weapon;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
        }

        SerializedProperty adsProp = so.FindProperty("weaponADS");
        if (adsProp != null && adsProp.objectReferenceValue == null)
        {
            WeaponADS ads = player.GetComponent<WeaponADS>();
            if (ads == null)
            {
                ads = player.GetComponentInChildren<WeaponADS>(true);
            }

            adsProp.objectReferenceValue = ads;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
        }

        return manager;
    }

    static void MarkDirty(GameObject player)
    {
        EditorUtility.SetDirty(player);
        if (player.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(player.scene);
        }

        if (PrefabUtility.IsPartOfPrefabInstance(player))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            GameLog.Warn(LogCategory, "选中的是 Prefab 实例：以上改动会记为 Override，请在 Inspector 上 Apply 后才会写回 Prefab。");
        }
    }

    // ------------------------------------------------------------------
    // Assets
    // ------------------------------------------------------------------

    static RuntimeAnimatorController FindAnimatorController()
    {
        string[] guids = AssetDatabase.FindAssets("t:AnimatorController");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.Equals(Path.GetFileName(path), ControllerFileName, StringComparison.OrdinalIgnoreCase))
            {
                return AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
            }
        }

        return null;
    }

    /// <summary>
    /// 优先从主角色 FBX（redSolider / blueSolider）取 Avatar；
    /// 若主模型还是 Generic/无 Avatar，则自动切到 Humanoid 并 Create From This Model 后重取。
    /// 绝不使用 Rifle Idle 等动作 FBX 自带的临时 Avatar。
    /// </summary>
    static Avatar FindOrCreateBodyAvatar(out string sourceLabel)
    {
        sourceLabel = string.Empty;

        foreach (string fileName in PreferredBodyModelNames)
        {
            string path = $"{ModelFolder}/{fileName}";
            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
            {
                continue;
            }

            Avatar avatar = LoadAvatarAtPath(path);
            if (avatar != null)
            {
                sourceLabel = path;
                return avatar;
            }

            if (TryEnsureHumanoidAvatar(path))
            {
                avatar = LoadAvatarAtPath(path);
                if (avatar != null)
                {
                    sourceLabel = path + "（已自动切为 Humanoid）";
                    return avatar;
                }
            }
        }

        // 兜底：扫工程里的 Avatar，但排除 Animations 目录与动作名临时 Avatar。
        Avatar fallback = FindPreferredAvatarInProject(out sourceLabel);
        return fallback;
    }

    static Avatar LoadAvatarAtPath(string assetPath)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        if (assets == null)
        {
            return null;
        }

        foreach (UnityEngine.Object asset in assets)
        {
            if (asset is Avatar avatar && avatar.isValid)
            {
                return avatar;
            }
        }

        return null;
    }

    /// <summary>
    /// 主模型若仍是 Generic / No Avatar，则改为 Humanoid + Create From This Model 并重导入。
    /// 否则 Animator 无法挂 Humanoid Controller，绑定会白做。
    /// </summary>
    static bool TryEnsureHumanoidAvatar(string assetPath)
    {
        ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer == null)
        {
            return false;
        }

        bool dirty = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            dirty = true;
        }

        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            dirty = true;
        }

        if (!dirty)
        {
            return LoadAvatarAtPath(assetPath) != null;
        }

        GameLog.Info(LogCategory, $"{Path.GetFileName(assetPath)} 尚无主 Avatar，正在切换为 Humanoid / Create From This Model…");
        importer.SaveAndReimport();
        return true;
    }

    static Avatar FindPreferredAvatarInProject(out string sourceLabel)
    {
        sourceLabel = string.Empty;
        string[] guids = AssetDatabase.FindAssets("t:Avatar");
        List<(Avatar avatar, string path, int score)> candidates = new List<(Avatar, string, int)>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (IsExcludedAnimationAvatarPath(path))
            {
                continue;
            }

            Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(path);
            if (avatar == null || !avatar.isValid)
            {
                // Avatar 常作为 FBX 子资源，LoadAssetAtPath 可能拿不到，改走 LoadAllAssetsAtPath。
                avatar = LoadAvatarAtPath(path);
            }

            if (avatar == null || !avatar.isValid || IsExcludedAnimationAvatarName(avatar.name))
            {
                continue;
            }

            int score = 0;
            string lowerPath = path.ToLowerInvariant();
            string lowerName = avatar.name.ToLowerInvariant();
            if (lowerPath.Contains("redsolider") || lowerName.Contains("redsolider")
                || lowerPath.Contains("redsoldier") || lowerName.Contains("redsoldier"))
            {
                score += 100;
            }

            if (lowerPath.Contains("bluesolider") || lowerName.Contains("bluesolider")
                || lowerPath.Contains("bluesoldier") || lowerName.Contains("bluesoldier"))
            {
                score += 80;
            }

            if (lowerPath.Contains("/soldier/") && !lowerPath.Contains("/animations/"))
            {
                score += 50;
            }

            if (avatar.isHuman)
            {
                score += 20;
            }

            candidates.Add((avatar, path, score));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        sourceLabel = candidates[0].path;
        return candidates[0].avatar;
    }

    static bool IsExcludedAnimationAvatarPath(string path)
    {
        string lower = path.Replace('\\', '/').ToLowerInvariant();
        if (lower.Contains("/animations/"))
        {
            return true;
        }

        string fileName = Path.GetFileNameWithoutExtension(path);
        return IsExcludedAnimationAvatarName(fileName);
    }

    static bool IsExcludedAnimationAvatarName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (string keyword in AnimationAvatarExcludeKeywords)
        {
            if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
#endif
