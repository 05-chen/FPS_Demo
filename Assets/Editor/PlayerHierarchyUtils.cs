#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Player;
using Weapon;

/// <summary>
/// Player 装配菜单（Build / Attach）共用的工具集：骨骼与子节点查找、组件创建、引用写入、Layer 与碰撞体处理。
/// 集中在一处，避免两个菜单各自维护一套查找与装配逻辑。
/// </summary>
internal static class PlayerHierarchyUtils
{
    public const string SocketName = "WeaponSocket";
    public const string GunMeshName = "GunMesh";
    public const string CameraName = "PlayerCamera";
    public const string MuzzleName = "muzzlePoint";
    public const string SightPointName = "SightPoint";
    public const string CameraSightTargetName = "CameraSightTarget";

    /// <summary>整只 Player 从零构建（Build 菜单）时的日志分类。</summary>
    public const string BuildLogCategory = "BuildPlayer";

    /// <summary>给已有 Player 补挂组件（Attach 菜单）时的日志分类。</summary>
    public const string AttachLogCategory = "AttachComponents";

    public const int PlayerLayer = 7;      // TagManager 中第 7 层为 Player
    public const int IgnoreRaycastLayer = 2;

    // ------------------------------------------------------------------
    // 查找
    // ------------------------------------------------------------------

    /// <summary>
    /// 骨骼名在不同 DCC 可能带 mixamorig 前缀或冒号，统一去掉前缀与非字母数字后比较。
    /// </summary>
    public static Transform FindBone(Transform root, string boneName)
    {
        string target = Normalize(boneName);
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (Normalize(t.name) == target)
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>在整棵子树里按名字找节点（忽略大小写）。</summary>
    public static Transform FindDescendantByName(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t != root && string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>只在直接子节点里按名字找节点（忽略大小写），避免误抓到深层同名物体。</summary>
    public static Transform FindDirectChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    public static string Normalize(string raw)
    {
        string lower = raw.ToLowerInvariant();
        int colon = lower.LastIndexOf(':');
        if (colon >= 0)
        {
            lower = lower.Substring(colon + 1);
        }

        return lower.Replace("mixamorig", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
    }

    /// <summary>合并所有 Renderer 的世界包围盒，用来估算模型身高（世界米）。</summary>
    public static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = default;
        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds ? bounds : new Bounds(root.transform.position, Vector3.one);
    }

    /// <summary>
    /// 合并所有 Renderer 的包围盒，并换算到 <paramref name="space"/> 的本地空间。
    /// 自动定向 / 缩放必须用本地空间的尺寸：直接读世界盒会被父级的旋转与缩放干扰，
    /// 算出来的「最长边」可能是父级斜放导致的假象。
    /// </summary>
    public static Bounds CalculateBoundsInSpace(Transform root, Transform space)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            // 粒子 / 拖尾 / 线条的 bounds 随运行时长变化，不参与静态尺寸测量。
            if (renderer == null || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
            {
                continue;
            }

            Bounds world = renderer.bounds;
            Vector3 center = world.center;
            Vector3 extents = world.extents;

            // 逐角点换算：父级带旋转时，世界 AABB 与本地 AABB 不是同一个盒子，直接搬 center/size 会偏大。
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = center + new Vector3(
                    (i & 1) == 0 ? -extents.x : extents.x,
                    (i & 2) == 0 ? -extents.y : extents.y,
                    (i & 4) == 0 ? -extents.z : extents.z);

                Vector3 local = space.InverseTransformPoint(corner);
                if (!hasBounds)
                {
                    bounds = new Bounds(local, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }
        }

        return hasBounds ? bounds : new Bounds(Vector3.zero, Vector3.one);
    }

    // ------------------------------------------------------------------
    // Layer 与碰撞体
    // ------------------------------------------------------------------

    /// <summary>遍历整棵子树设为同一 Layer。</summary>
    public static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    /// <summary>禁用（而非删除）目标及其子节点上的所有碰撞体，保留组件引用不丢失。</summary>
    public static void RemoveOrDisableColliders(GameObject target)
    {
        foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
        {
            if (collider != null)
            {
                collider.enabled = false;
            }
        }
    }

    /// <summary>销毁目标整棵子树上的 Collider（枪模防挡子弹 / 误占点用）。</summary>
    public static void DestroyCollidersInHierarchy(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        for (int i = colliders.Length - 1; i >= 0; i--)
        {
            if (colliders[i] != null)
            {
                Undo.DestroyObjectImmediate(colliders[i]);
            }
        }
    }

    // ------------------------------------------------------------------
    // 创建与组件
    // ------------------------------------------------------------------

    public static GameObject CreateChild(Transform parent, string name)
    {
        GameObject child = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(child, "Create " + name);
        child.transform.SetParent(parent, false);
        return child;
    }

    public static T AddComp<T>(GameObject target) where T : Component
    {
        T component = target.AddComponent<T>();
        Undo.RegisterCreatedObjectUndo(component, "Add " + typeof(T).Name);
        return component;
    }

    /// <summary>已有就直接返回，没有才挂载。补挂组件时应统一走这里，保证幂等。</summary>
    public static T GetOrAddComp<T>(GameObject target) where T : Component
    {
        T existing = target.GetComponent<T>();
        return existing != null ? existing : AddComp<T>(target);
    }

    // ------------------------------------------------------------------
    // 引用写入
    // ------------------------------------------------------------------

    /// <summary>
    /// 通过 SerializedObject 写入脚本的私有 [SerializeField] 引用，避免为此暴露公开 setter。
    /// </summary>
    public static void SetReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value, string category = BuildLogCategory)
    {
        if (target == null)
        {
            return;
        }

        // 传 null 时直接跳过：补挂场景下「没找到」不应把已有的正确引用清空。
        if (value == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null)
        {
            GameLog.Warn(category, $"{target.GetType().Name} 上找不到字段 {fieldName}，引用未写入。");
            return;
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 一次性接好 Player 上各脚本互相引用的字段（相机 / 武器 / 枪模 / 后坐力）。
    /// Build 与 Attach 两个菜单共用，避免漏接导致运行期空引用。
    /// </summary>
    public static void WirePlayerReferences(GameObject player, Camera camera, AudioListener listener, Transform muzzlePoint, Transform gunMesh, string category = BuildLogCategory)
    {
        SetReference(player.GetComponent<PlayerController>(), "playerCamera", camera, category);
        SetReference(player.GetComponent<PlayerController>(), "audioListener", listener, category);
        SetReference(player.GetComponent<PlayerController>(), "playerWeapon", player.GetComponent<PlayerWeapon>(), category);
        SetReference(player.GetComponent<PlayerController>(), "weaponADS", player.GetComponent<WeaponADS>(), category);

        SetReference(player.GetComponent<PlayerWeapon>(), "playerCamera", camera, category);
        SetReference(player.GetComponent<PlayerWeapon>(), "muzzlePoint", muzzlePoint, category);

        WeaponADS ads = player.GetComponent<WeaponADS>();
        SetReference(ads, "playerCamera", camera, category);
        SetReference(ads, "weaponHolder", gunMesh, category);

        // 优先用枪上的 SightPoint；没有则退回相机 muzzlePoint（兼容旧预制体）。
        Transform sightPoint = FindDescendantByName(player.transform, SightPointName);
        SetReference(ads, "sightPoint", sightPoint != null ? sightPoint : muzzlePoint, category);

        Transform cameraSightTarget = FindDescendantByName(player.transform, CameraSightTargetName);
        SetReference(ads, "cameraSightTarget", cameraSightTarget, category);

        SetReference(player.GetComponent<ProceduralRecoil>(), "weaponADS", ads, category);
        SetReference(player.GetComponent<ProceduralRecoil>(), "weaponHolder", gunMesh, category);
    }

    // ------------------------------------------------------------------
    // 尺度换算：把「世界米」折算成骨骼本地空间，避免模型被缩放后 Hitbox 走样
    // ------------------------------------------------------------------

    public static Vector3 DivideByScale(Vector3 worldSize, Vector3 scale)
    {
        return new Vector3(
            worldSize.x / SafeScale(scale.x),
            worldSize.y / SafeScale(scale.y),
            worldSize.z / SafeScale(scale.z));
    }

    public static float AverageScale(Vector3 scale)
    {
        return (SafeScale(scale.x) + SafeScale(scale.z)) * 0.5f;
    }

    public static float SafeScale(float value)
    {
        float abs = Mathf.Abs(value);
        return abs < 0.0001f ? 0.0001f : abs;
    }

    public static void FocusSceneView()
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null)
        {
            return;
        }

        view.FrameSelected();
        view.Repaint();
    }
}
#endif
