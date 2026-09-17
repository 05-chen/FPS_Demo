#if UNITY_EDITOR
using System;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Player;
using Weapon;
using static PlayerHierarchyUtils;

/// <summary>
/// 菜单工具：在**当前打开的场景**里直接拼装一套完整的联机 Player（根节点 + 模型 + 骨骼 Hitbox + 相机）。
/// 只作用于 Hierarchy，不会生成或覆盖磁盘上的 Prefab 资产。
/// 用法：菜单 <b>Tools/Build Player in Hierarchy</b>。
/// 公共装配逻辑（查找 / 建节点 / 写引用）在 <see cref="PlayerHierarchyUtils"/>。
/// </summary>
public static class BuildPlayerHierarchy
{
    const string MenuPath = "Tools/Build Player in Hierarchy";
    const string RootName = "Player";
    const string ModelRootName = "ModelRoot";
    const string ModelFolder = "Assets/Model/Soldier";
    // 资源实名为 redSolider.fbx（文件名拼写为 Solider），这里按关键字匹配，兼容 redSoldier 写法。
    const string ModelKeyword = "redsolider";

    [MenuItem(MenuPath)]
    static void Build()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            EditorUtility.DisplayDialog("Build Player", "没有可用的活动场景。", "知道了");
            return;
        }

        GameObject modelAsset = FindSoldierModel();
        if (modelAsset == null)
        {
            EditorUtility.DisplayDialog("Build Player", $"未在 {ModelFolder} 找到 redSolider 模型。", "知道了");
            return;
        }

        if (!ConfirmReplaceExistingPlayer(scene))
        {
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build Player in Hierarchy");

        GameObject player = BuildPlayerRoot();
        GameObject modelRoot = AttachModel(player, modelAsset);

        Bounds modelBounds = CalculateBounds(modelRoot);
        BuildWeaponSocket(modelRoot, out Transform gunMesh);

        BuildHitboxes(modelRoot, modelBounds);
        GameObject muzzlePoint = BuildCameraAndMuzzle(player, out Camera camera, out AudioListener listener);

        WirePlayerReferences(player, camera, listener, muzzlePoint.transform, gunMesh, BuildLogCategory);

        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(undoGroup);

        Selection.activeGameObject = player;
        EditorGUIUtility.PingObject(player);
        FocusSceneView();
        GameLog.Info(BuildLogCategory, $"已在场景中构建 {RootName}：模型 {modelAsset.name}，模型高度约 {modelBounds.size.y:F2}。");
    }

    /// <summary>
    /// 创建 Player 根节点，设置 Tag / Layer 并挂载全部运行期组件。
    /// </summary>
    static GameObject BuildPlayerRoot()
    {
        GameObject player = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(player, "Create Player");
        player.tag = "Player";
        player.layer = PlayerLayer;

        // NetworkObject / ClientNetworkTransform 必须先于其它 NetworkBehaviour，保证网络身份齐全。
        AddComp<NetworkObject>(player);
        AddComp<ClientNetworkTransform>(player);

        CharacterController controller = AddComp<CharacterController>(player);
        controller.height = 1.6f;
        controller.center = Vector3.zero;
        controller.radius = 0.5f;

        AddComp<PlayerController>(player);
        AddComp<PlayerHealth>(player);
        AddComp<PlayerStatusController>(player);
        AddComp<PlayerWeapon>(player);
        AddComp<WeaponADS>(player);
        AddComp<ProceduralRecoil>(player);

        // 说明：CharacterMotor 是普通 C# 类（构造函数注入 CharacterController），并非 MonoBehaviour，
        // 由 PlayerController 在 Awake 里 new 出来，因此这里无法也不应作为组件挂载。
        GameLog.Info(BuildLogCategory, "CharacterMotor 为纯逻辑类，由 PlayerController 内部创建，跳过组件挂载。");

        return player;
    }

    /// <summary>
    /// 实例化 redSolider 模型，彻底解包脱离 FBX 只读保护，改名 ModelRoot。
    /// </summary>
    static GameObject AttachModel(GameObject player, GameObject modelAsset)
    {
        UnityEngine.Object instance = PrefabUtility.InstantiatePrefab(modelAsset, player.transform);
        GameObject modelRoot = (GameObject)instance;
        modelRoot.name = ModelRootName;
        modelRoot.transform.localPosition = Vector3.zero;
        modelRoot.transform.localRotation = Quaternion.identity;
        modelRoot.transform.localScale = Vector3.one;

        if (PrefabUtility.IsPartOfPrefabInstance(modelRoot))
        {
            PrefabUtility.UnpackPrefabInstance(modelRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        // 整棵模型设为 Player 层：`SnapToGround` 的射线掩码是 ~(1 << 玩家层)，避免贴地检测打到自己的身体 Hitbox。
        SetLayerRecursively(modelRoot, PlayerLayer);
        return modelRoot;
    }

    /// <summary>
    /// 在 mixamorig:RightHand 下建立 WeaponSocket，并放入 GunMesh（模型自带则搬运，否则空占位）。
    /// </summary>
    static void BuildWeaponSocket(GameObject modelRoot, out Transform gunMesh)
    {
        Transform rightHand = FindBone(modelRoot.transform, "RightHand");
        if (rightHand == null)
        {
            GameLog.Warn(BuildLogCategory, "未找到 mixamorig:RightHand，武器挂点创建在模型根节点下。");
            rightHand = modelRoot.transform;
        }

        GameObject socket = CreateChild(rightHand, SocketName);
        socket.layer = IgnoreRaycastLayer;

        Transform existingGun = FindDescendantByName(modelRoot.transform, GunMeshName);
        if (existingGun != null && existingGun != socket.transform)
        {
            // 模型自带枪模：搬进挂点，并清除只读的 FBX 层级依赖。
            existingGun.SetParent(socket.transform, false);
            gunMesh = existingGun;
        }
        else
        {
            gunMesh = CreateChild(socket.transform, GunMeshName).transform;
        }

        gunMesh.name = GunMeshName;
        gunMesh.gameObject.layer = IgnoreRaycastLayer;
        RemoveOrDisableColliders(gunMesh.gameObject);

        AddComp<ShellEjector>(socket);
    }

    /// <summary>
    /// 按部位在 mixamorig 骨骼上挂碰撞体 + BodyPartHitbox，尺寸依据模型整体包围盒自动估算。
    /// </summary>
    static void BuildHitboxes(GameObject modelRoot, Bounds modelBounds)
    {
        SetupHitbox(FindBone(modelRoot.transform, "Head"), DetailedBodyPart.Head, modelBounds);
        SetupHitbox(FindBone(modelRoot.transform, "Spine2"), DetailedBodyPart.Torso, modelBounds);
        SetupHitbox(FindBone(modelRoot.transform, "LeftArm"), DetailedBodyPart.Arms, modelBounds);
        SetupHitbox(FindBone(modelRoot.transform, "RightArm"), DetailedBodyPart.Arms, modelBounds);
        SetupHitbox(FindBone(modelRoot.transform, "LeftUpLeg"), DetailedBodyPart.Legs, modelBounds);
        SetupHitbox(FindBone(modelRoot.transform, "RightUpLeg"), DetailedBodyPart.Legs, modelBounds);
    }

    /// <summary>
    /// 创建 PlayerCamera（Camera + AudioListener 默认禁用）及其子节点 muzzlePoint。
    /// </summary>
    static GameObject BuildCameraAndMuzzle(GameObject player, out Camera camera, out AudioListener listener)
    {
        GameObject cameraGo = CreateChild(player.transform, CameraName);
        cameraGo.layer = 0;

        camera = AddComp<Camera>(cameraGo);
        camera.enabled = false;
        camera.nearClipPlane = 0.1f;
        camera.fieldOfView = 70f;
        cameraGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        listener = AddComp<AudioListener>(cameraGo);
        listener.enabled = false;

        GameObject muzzle = CreateChild(cameraGo.transform, MuzzleName);
        muzzle.layer = 0;
        muzzle.transform.localPosition = Vector3.zero;
        return muzzle;
    }

    // ------------------------------------------------------------------
    // Hitbox
    // ------------------------------------------------------------------

    static void SetupHitbox(Transform bone, DetailedBodyPart part, Bounds modelBounds)
    {
        if (bone == null)
        {
            GameLog.Warn(BuildLogCategory, $"未找到 {part} 对应骨骼，跳过该 Hitbox。");
            return;
        }

        if (bone.GetComponent<BodyPartHitbox>() != null)
        {
            return;
        }

        float height = Mathf.Max(0.01f, modelBounds.size.y);
        Vector3 scale = bone.lossyScale;

        if (part == DetailedBodyPart.Head || part == DetailedBodyPart.Torso)
        {
            BoxCollider box = AddComp<BoxCollider>(bone.gameObject);
            box.isTrigger = false;
            box.center = Vector3.zero;
            // 头 / 躯干用盒体：尺寸按身高比例估算，再换算到骨骼本地空间。
            Vector3 worldSize = part == DetailedBodyPart.Head
                ? new Vector3(height * 0.15f, height * 0.17f, height * 0.15f)
                : new Vector3(height * 0.30f, height * 0.34f, height * 0.22f);
            box.size = DivideByScale(worldSize, scale);
        }
        else
        {
            CapsuleCollider capsule = AddComp<CapsuleCollider>(bone.gameObject);
            capsule.isTrigger = false;
            capsule.direction = 1;
            capsule.center = Vector3.zero;
            // 四肢用胶囊：高度沿骨骼本地 Y 轴，半径按身高比例。
            float worldRadius = part == DetailedBodyPart.Arms ? height * 0.05f : height * 0.075f;
            float worldHeight = part == DetailedBodyPart.Arms ? height * 0.30f : height * 0.46f;
            capsule.radius = worldRadius / AverageScale(scale);
            capsule.height = Mathf.Max(worldHeight / Mathf.Max(0.0001f, Mathf.Abs(scale.y)), capsule.radius * 2.01f);
        }

        BodyPartHitbox hitbox = AddComp<BodyPartHitbox>(bone.gameObject);
        hitbox.bodyPart = part;
    }

    // ------------------------------------------------------------------
    // 构建专用工具
    // ------------------------------------------------------------------

    static GameObject FindSoldierModel()
    {
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { ModelFolder });
        if (guids.Length == 0)
        {
            guids = AssetDatabase.FindAssets("t:Model");
        }

        GameObject best = null;
        int bestScore = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                continue;
            }

            string normalized = asset.name.ToLowerInvariant().Replace(" ", string.Empty);
            int score = normalized.Contains(ModelKeyword) ? 3
                : normalized.Contains("red") ? 2
                : normalized.Contains("soldier") || normalized.Contains("solider") ? 1
                : 0;

            if (score > bestScore)
            {
                bestScore = score;
                best = asset;
            }
        }

        return best;
    }

    static bool ConfirmReplaceExistingPlayer(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (!root.name.Equals(RootName, StringComparison.OrdinalIgnoreCase) || root.tag != "Player")
            {
                continue;
            }

            if (!EditorUtility.DisplayDialog("Build Player in Hierarchy",
                "场景中已存在根节点 Player，是否删除后重新构建？", "替换", "取消"))
            {
                return false;
            }

            Undo.DestroyObjectImmediate(root);
            break;
        }

        return true;
    }
}
#endif
