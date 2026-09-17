#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Weapon;
using static PlayerHierarchyUtils;

/// <summary>
/// 在枪模下创建 LeftHandIK / RightHandIK 握点，接线 WeaponADS，并在 ModelRoot 挂 WeaponHandIK、开启 Animator IK Pass。
/// </summary>
public static class WeaponHandIkSetup
{
    const string MenuPath = "Tools/Setup Weapon Hand IK";
    const string LogCategory = "WeaponHandIK";

    public const string LeftHandIkName = "LeftHandIK";
    public const string RightHandIkName = "RightHandIK";
    public const string AimAxisName = "AimAxis";
    public const string AdsPivotName = "AdsPivot";

    /// <summary>护木：本枪枪管沿 local +Y，左手在握把前方偏中段。</summary>
    static readonly Vector3 DefaultLeftHandLocal = new Vector3(0f, 0.15f, -0.0334f);
    /// <summary>握把：靠近枪托/手枪握把，约 local -Y。</summary>
    static readonly Vector3 DefaultRightHandLocal = new Vector3(0f, -0.32f, -0.03f);
    /// <summary>
    /// M1 Garand Lowpoly 枪管沿 local +Y（握把→护木），顶部约 local +Z。
    /// AimAxis.forward 必须沿枪管，不能再用错误的 (0,0,0.55)+identity。
    /// </summary>
    static readonly Vector3 DefaultAimAxisLocal = new Vector3(0f, 0.55f, 0f);
    /// <summary>ADS 位置补偿支点：放置在右手握把/枪托附近，不是相机/瞄准点。</summary>
    static readonly Vector3 DefaultAdsPivotLocal = new Vector3(0f, -0.32f, -0.03f);

    [MenuItem(MenuPath)]
    static void SetupMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Setup Weapon Hand IK", "请先退出 Play 模式再执行。", "知道了");
            return;
        }

        if (!TryResolvePlayer(out GameObject root, out bool isPrefabAsset, out string prefabPath, out string label))
        {
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Setup Weapon Hand IK");

        bool ok = SetupHandIk(root);
        Undo.CollapseUndoOperations(undoGroup);

        if (!ok)
        {
            if (isPrefabAsset)
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return;
        }

        if (isPrefabAsset)
        {
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();
            GameLog.Info(LogCategory, $"已写回 Prefab：{prefabPath}");
        }
        else
        {
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            GameLog.Info(LogCategory, $"已为场景中的 {label} 创建握点并接线 Hand IK。");
        }
    }

    /// <summary>
    /// 幂等：确保枪上有双手握点、WeaponADS 已引用、ModelRoot 有 WeaponHandIK、Animator 开启 IK Pass。
    /// </summary>
    public static bool SetupHandIk(GameObject player)
    {
        if (player == null)
        {
            return false;
        }

        Transform gunMesh = ResolveGunMesh(player.transform);
        if (gunMesh == null)
        {
            GameLog.Error(LogCategory, $"在 {player.name} 下找不到枪模（M1 Garand Lowpoly / GunMesh）。");
            return false;
        }

        // 兼容旧命名 Letf_IK / Left_IK / Right_IK
        Transform leftIk = EnsureIkPoint(gunMesh, LeftHandIkName, new[] { "Letf_IK", "Left_IK", "LeftHand_IK" }, DefaultLeftHandLocal);
        Transform rightIk = EnsureIkPoint(gunMesh, RightHandIkName, new[] { "Right_IK", "RightHand_IK" }, DefaultRightHandLocal);

        WeaponADS ads = GetOrAddComp<WeaponADS>(player);
        Transform aimAxis = EnsureAimAxis(gunMesh);
        Transform adsPivot = EnsureAdsPivot(gunMesh);
        ads.leftHandIKTarget = leftIk;
        ads.rightHandIKTarget = rightIk;
        SerializedObject adsSo = new SerializedObject(ads);
        SerializedProperty aimAxisProp = adsSo.FindProperty("adsAimAxis");
        SerializedProperty adsPivotProp = adsSo.FindProperty("adsPivot");
        if (aimAxisProp == null || adsPivotProp == null)
        {
            GameLog.Error(LogCategory, "WeaponADS 缺少 adsAimAxis 或 adsPivot 序列化字段，无法完成握点接线。");
            return false;
        }

        aimAxisProp.objectReferenceValue = aimAxis;
        adsPivotProp.objectReferenceValue = adsPivot;
        adsSo.ApplyModifiedPropertiesWithoutUndo();
        ads.isLeftHandAttached = true;
        EditorUtility.SetDirty(ads);

        Transform modelRoot = FindDescendantByName(player.transform, "ModelRoot")
                              ?? FindAnimatorTransform(player.transform);
        if (modelRoot == null)
        {
            GameLog.Error(LogCategory, "找不到 ModelRoot / Animator，无法挂 WeaponHandIK（OnAnimatorIK 必须与 Animator 同物体）。");
            return false;
        }

        WeaponHandIK handIk = GetOrAddComp<WeaponHandIK>(modelRoot.gameObject);
        SerializedObject handSo = new SerializedObject(handIk);
        SerializedProperty animProp = handSo.FindProperty("animator");
        SerializedProperty adsProp = handSo.FindProperty("weaponADS");
        if (animProp != null)
        {
            animProp.objectReferenceValue = modelRoot.GetComponent<Animator>();
        }

        if (adsProp != null)
        {
            adsProp.objectReferenceValue = ads;
        }

        handSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(handIk);

        EnableAnimatorIkPass(modelRoot.GetComponent<Animator>());

        GameLog.Info(
            LogCategory,
            $"握点就绪：{GetPath(leftIk)} / {GetPath(rightIk)}；WeaponHandIK → {modelRoot.name}。" +
            "请在 Scene 视图选中握点微调位置，使手心贴合握把/护木。");
        return true;
    }

    static Transform EnsureIkPoint(Transform gunMesh, string preferredName, string[] aliases, Vector3 defaultLocal)
    {
        Transform existing = FindDirectChild(gunMesh, preferredName);
        if (existing == null)
        {
            foreach (string alias in aliases)
            {
                existing = FindDirectChild(gunMesh, alias);
                if (existing != null)
                {
                    Undo.RecordObject(existing.gameObject, "Rename IK Point");
                    existing.name = preferredName;
                    break;
                }
            }
        }

        if (existing == null)
        {
            // 整棵枪子树再搜一次（防止握点被误挂到更深层级）
            existing = FindDescendantByName(gunMesh, preferredName);
            foreach (string alias in aliases)
            {
                if (existing != null)
                {
                    break;
                }

                existing = FindDescendantByName(gunMesh, alias);
            }

            if (existing != null && existing.parent != gunMesh)
            {
                Undo.SetTransformParent(existing, gunMesh, "Reparent IK Point");
                existing.name = preferredName;
            }
        }

        if (existing == null)
        {
            existing = CreateChild(gunMesh, preferredName).transform;
            existing.localPosition = defaultLocal;
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            GameLog.Info(LogCategory, $"已创建握点 {preferredName} @ local {defaultLocal}");
        }

        return existing;
    }

    static Transform EnsureAimAxis(Transform gunMesh)
    {
        Transform existing = FindDirectChild(gunMesh, AimAxisName);
        bool created = existing == null;
        if (created)
        {
            existing = CreateChild(gunMesh, AimAxisName).transform;
            existing.localScale = Vector3.one;
        }

        // 每次 Setup 都按握点/照门重算枪管轴，避免旧版 (0,0,Z)+identity 把 ADS 拧到竖直。
        AlignAimAxisToBarrel(gunMesh, existing);
        if (created)
        {
            GameLog.Info(LogCategory,
                $"已创建 ADS 轴 {AimAxisName} @ localPos={existing.localPosition}, localEuler={existing.localEulerAngles}");
        }

        return existing;
    }

    /// <summary>
    /// 用 RightHandIK→LeftHandIK（或默认 +Y）定枪管 forward，用 SightPoint 相对枪管的侧向定 up。
    /// </summary>
    static void AlignAimAxisToBarrel(Transform gunMesh, Transform aimAxis)
    {
        Transform leftIk = FindDirectChild(gunMesh, LeftHandIkName);
        Transform rightIk = FindDirectChild(gunMesh, RightHandIkName);
        Transform sight = FindDirectChild(gunMesh, SightPointName);

        Vector3 barrelDir = Vector3.up;
        Vector3 pivot = DefaultAdsPivotLocal;
        if (leftIk != null && rightIk != null)
        {
            Vector3 delta = leftIk.localPosition - rightIk.localPosition;
            if (delta.sqrMagnitude > 1e-6f)
            {
                barrelDir = delta.normalized;
            }

            pivot = rightIk.localPosition;
        }

        Vector3 gunUp = Vector3.forward;
        if (sight != null)
        {
            Vector3 toSight = sight.localPosition - pivot;
            Vector3 projected = toSight - Vector3.Dot(toSight, barrelDir) * barrelDir;
            if (projected.sqrMagnitude > 1e-6f)
            {
                gunUp = projected.normalized;
            }
        }

        aimAxis.localRotation = Quaternion.LookRotation(barrelDir, gunUp);
        aimAxis.localPosition = pivot + barrelDir * 0.55f;
    }

    static Transform EnsureAdsPivot(Transform gunMesh)
    {
        Transform existing = FindDirectChild(gunMesh, AdsPivotName);
        if (existing == null)
        {
            existing = CreateChild(gunMesh, AdsPivotName).transform;
            existing.localPosition = DefaultAdsPivotLocal;
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            GameLog.Info(LogCategory, $"已创建 ADS 支点 {AdsPivotName} @ local {DefaultAdsPivotLocal}");
            return existing;
        }

        // 已有支点但若几乎在原点，拉回握把默认位，避免 ADS 反解漂到相机一侧。
        if (existing.localPosition.sqrMagnitude < 1e-6f)
        {
            existing.localPosition = DefaultAdsPivotLocal;
        }

        return existing;
    }

    static Transform ResolveGunMesh(Transform player)
    {
        Transform named = FindDescendantByName(player, "M1 Garand Lowpoly");
        if (named != null)
        {
            return named;
        }

        named = FindDescendantByName(player, GunMeshName);
        if (named != null)
        {
            return named;
        }

        Transform socket = FindDescendantByName(player, SocketName);
        if (socket == null)
        {
            return null;
        }

        for (int i = 0; i < socket.childCount; i++)
        {
            Transform child = socket.GetChild(i);
            if (child.GetComponentInChildren<Renderer>(true) != null)
            {
                return child;
            }
        }

        return socket.childCount > 0 ? socket.GetChild(0) : null;
    }

    static Transform FindAnimatorTransform(Transform player)
    {
        Animator animator = player.GetComponentInChildren<Animator>(true);
        return animator != null ? animator.transform : null;
    }

    /// <summary>
    /// 打开 AnimatorController 各层的 IK Pass（至少 UpperBody），否则 OnAnimatorIK 不会触发。
    /// </summary>
    static void EnableAnimatorIkPass(Animator animator)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            GameLog.Warn(LogCategory, "Animator 或 Controller 为空，跳过 IK Pass。");
            return;
        }

        RuntimeAnimatorController runtime = animator.runtimeAnimatorController;
        AnimatorController controller = runtime as AnimatorController;
        if (controller == null && runtime is AnimatorOverrideController overrideController)
        {
            controller = overrideController.runtimeAnimatorController as AnimatorController;
        }

        if (controller == null)
        {
            GameLog.Warn(LogCategory, "无法解析 AnimatorController，请手动在 UpperBody 层勾选 IK Pass。");
            return;
        }

        AnimatorControllerLayer[] layers = controller.layers;
        bool changed = false;
        for (int i = 0; i < layers.Length; i++)
        {
            // Base 与 UpperBody 都打开，避免只开一层时权重层不回调。
            if (!layers[i].iKPass)
            {
                layers[i].iKPass = true;
                changed = true;
            }
        }

        if (changed)
        {
            controller.layers = layers;
            EditorUtility.SetDirty(controller);
            GameLog.Info(LogCategory, $"已为 {controller.name} 开启各层 IK Pass。");
        }
    }

    static bool TryResolvePlayer(out GameObject root, out bool isPrefabAsset, out string prefabPath, out string label)
    {
        root = null;
        isPrefabAsset = false;
        prefabPath = null;
        label = null;

        Object selected = Selection.activeObject;
        if (selected == null)
        {
            GameLog.Error(LogCategory, "请先在 Hierarchy 选中 Player，或在 Project 选中 Player.prefab。");
            return false;
        }

        prefabPath = AssetDatabase.GetAssetPath(selected);
        if (!string.IsNullOrEmpty(prefabPath) && prefabPath.EndsWith(".prefab"))
        {
            isPrefabAsset = true;
            root = PrefabUtility.LoadPrefabContents(prefabPath);
            label = prefabPath;
            return true;
        }

        GameObject go = selected as GameObject;
        if (go == null)
        {
            GameLog.Error(LogCategory, "选中对象不是 GameObject / Prefab。");
            return false;
        }

        // 向上找到带 WeaponADS 或名为 Player 的根
        Transform t = go.transform;
        while (t != null)
        {
            if (t.GetComponent<WeaponADS>() != null
                || string.Equals(t.name, "Player", System.StringComparison.OrdinalIgnoreCase)
                || t.CompareTag("Player"))
            {
                root = t.gameObject;
                label = root.name;
                return true;
            }

            t = t.parent;
        }

        root = go;
        label = go.name;
        return true;
    }

    static string GetPath(Transform t)
    {
        if (t == null)
        {
            return "null";
        }

        System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
        Transform cur = t;
        while (cur != null)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }
}
#endif
