#if UNITY_EDITOR
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Player;
using Weapon;
using static PlayerHierarchyUtils;

/// <summary>
/// 菜单工具：给选中的 Player 补挂核心组件，以及枪械挂载 / ADS 屏幕中心对齐节点。
/// <list type="bullet">
/// <item><b>Tools/Attach Components To Selected Player</b>：幂等补挂根组件 / Hitbox / 相机（原逻辑保留）</item>
/// <item><b>Tools/Setup UpperBody Weapon and ADS</b>：补 WeaponSocket / GunMesh / SightPoint / CameraSightTarget，清洗 Layer&amp;Collider，接线 WeaponADS</item>
/// </list>
/// 公共装配逻辑在 <see cref="PlayerHierarchyUtils"/>。
/// </summary>
public static class AttachComponentsToPlayer
{
    const string MenuPath = "Tools/Attach Components To Selected Player";
    const string SetupWeaponAdsMenuPath = "Tools/Setup UpperBody Weapon and ADS";
    const string LogCategory = AttachLogCategory;
    const string WeaponAdsLogCategory = "SetupWeaponADS";

    static readonly Vector3 DefaultSightPointLocal = new Vector3(0f, 0.05f, 0f);
    static readonly Vector3 DefaultCameraSightTargetLocal = new Vector3(0f, 0f, 0.4f);

    [MenuItem(MenuPath)]
    static void Attach()
    {
        GameObject player = Selection.activeGameObject;
        if (!ValidateSelection(player))
        {
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Attach Components To Selected Player");

        ConfigureRoot(player);

        Transform modelRoot = FindModelRoot(player.transform);
        if (modelRoot == null)
        {
            GameLog.Warn(LogCategory, "没找到 mixamorig 骨架，跳过武器挂点与部位 Hitbox。请确认选中的是带角色的 Player。");
        }
        else
        {
            AttachWeapon(modelRoot);
            AttachHitboxes(modelRoot);
        }

        GameObject muzzlePoint = AttachCamera(player, out Camera camera, out AudioListener listener, out Transform gunMesh);

        // 武器相关引用统一补一遍：相机 / muzzlePoint / 枪模 / 后坐力，避免运行时空引用。
        WirePlayerReferences(player, camera, listener, muzzlePoint.transform, gunMesh, LogCategory);

        EditorSceneManager.MarkSceneDirty(player.scene);
        Undo.CollapseUndoOperations(undoGroup);

        Selection.activeGameObject = player;
        FocusSceneView();

        if (PrefabUtility.IsPartOfPrefabInstance(player))
        {
            GameLog.Warn(LogCategory, "选中的是 Prefab 实例：以上改动会记为 Override，请在 Inspector 上 Apply 后才会写回 Prefab。");
        }

        GameLog.Info(LogCategory, $"已为 {player.name} 补挂并配置核心组件。");
    }

    /// <summary>
    /// 补全上半身枪械挂点与 ADS 屏幕中心对齐节点，并清洗枪模 Layer / Collider、接线 WeaponADS。
    /// 支持 Hierarchy 场景对象，或 Project 窗口选中的 Player.prefab 资产。
    /// </summary>
    [MenuItem(SetupWeaponAdsMenuPath)]
    static void SetupUpperBodyWeaponAndAdsMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Setup Weapon ADS", "请先退出 Play 模式再执行。", "知道了");
            return;
        }

        if (!TryResolvePlayerTarget(out GameObject root, out bool isPrefabAsset, out string prefabPath, out string label))
        {
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Setup UpperBody Weapon and ADS");

        bool ok = SetupUpperBodyWeaponAndAds(root);
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
            GameLog.Info(WeaponAdsLogCategory, $"已写回 Prefab：{prefabPath}");
        }
        else
        {
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            if (PrefabUtility.IsPartOfPrefabInstance(root))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(root);
                GameLog.Warn(WeaponAdsLogCategory, "选中的是 Prefab 实例：请在 Inspector 上 Apply 后才会写回 Prefab。");
            }
        }

        GameLog.Info(WeaponAdsLogCategory, "【已成功补全枪械挂载与 ADS 节点，已自动清洗 Layer 与 Collider！】");
        GameLog.Info(WeaponAdsLogCategory, $"目标：{label}");
    }

    /// <summary>解析选中目标：场景 Player，或 Project 里的 .prefab 资产。</summary>
    static bool TryResolvePlayerTarget(out GameObject root, out bool isPrefabAsset, out string prefabPath, out string label)
    {
        root = null;
        isPrefabAsset = false;
        prefabPath = string.Empty;
        label = string.Empty;

        UnityEngine.Object selected = Selection.activeObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("Setup Weapon ADS",
                "请先选中 Hierarchy 里的 Player，或 Project 窗口里的 Player.prefab。", "知道了");
            return false;
        }

        prefabPath = AssetDatabase.GetAssetPath(selected);
        if (!string.IsNullOrEmpty(prefabPath)
            && prefabPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
        {
            root = PrefabUtility.LoadPrefabContents(prefabPath);
            isPrefabAsset = true;
            label = prefabPath;
            return true;
        }

        GameObject go = selected as GameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("Setup Weapon ADS",
                "请选中 GameObject（Player）或 Prefab 资产。", "知道了");
            return false;
        }

        if (EditorUtility.IsPersistent(go) || !go.scene.IsValid())
        {
            EditorUtility.DisplayDialog("Setup Weapon ADS",
                "选中的不是场景对象。请从 Hierarchy 选 Player，或从 Project 直接选 .prefab 文件。", "知道了");
            return false;
        }

        root = go;
        label = go.name;
        return true;
    }

    /// <summary>
    /// 核心：补节点 → 清洗 Layer/Collider → 接线 WeaponADS（挂在 Player 根，与现有玩法一致）。
    /// </summary>
    static bool SetupUpperBodyWeaponAndAds(GameObject player)
    {
        if (player == null)
        {
            return false;
        }

        Transform modelRoot = FindModelRoot(player.transform);
        if (modelRoot == null)
        {
            GameLog.Error(WeaponAdsLogCategory, $"在 {player.name} 下找不到 ModelRoot / 骨架，无法建 WeaponSocket。");
            return false;
        }

        Transform rightHand = FindBone(modelRoot, "RightHand");
        if (rightHand == null)
        {
            GameLog.Warn(WeaponAdsLogCategory, "未找到 RightHand，WeaponSocket 建在 ModelRoot 下。");
            rightHand = modelRoot;
        }

        // 1) WeaponSocket
        Transform socket = FindDirectChild(rightHand, SocketName);
        if (socket == null)
        {
            socket = CreateChild(rightHand, SocketName).transform;
        }

        // 2) 枪模：优先已有 Mesh 子物体，否则确保 GunMesh 空节点
        Transform gunMesh = ResolveOrCreateGunMesh(socket);

        // 3) SightPoint（照门）
        Transform sightPoint = FindDirectChild(gunMesh, SightPointName);
        if (sightPoint == null)
        {
            sightPoint = CreateChild(gunMesh, SightPointName).transform;
            sightPoint.localPosition = DefaultSightPointLocal;
            sightPoint.localRotation = Quaternion.identity;
        }

        // 4) PlayerCamera + CameraSightTarget
        AttachCamera(player, out Camera camera, out AudioListener listener, out _);
        Transform playerCameraT = FindDirectChild(player.transform, CameraName);
        if (playerCameraT == null && camera != null)
        {
            playerCameraT = camera.transform;
        }

        if (playerCameraT == null)
        {
            GameLog.Error(WeaponAdsLogCategory, "找不到 PlayerCamera，无法创建 CameraSightTarget。");
            return false;
        }

        Transform cameraSightTarget = FindDirectChild(playerCameraT, CameraSightTargetName);
        if (cameraSightTarget == null)
        {
            cameraSightTarget = CreateChild(playerCameraT, CameraSightTargetName).transform;
            cameraSightTarget.localPosition = DefaultCameraSightTargetLocal;
            cameraSightTarget.localRotation = Quaternion.identity;
        }

        // 5) Layer + 销毁 Collider（框架硬性规范）
        SetLayerRecursively(gunMesh.gameObject, IgnoreRaycastLayer);
        SetLayerRecursively(socket.gameObject, IgnoreRaycastLayer);
        DestroyCollidersInHierarchy(gunMesh.gameObject);

        // 6) WeaponADS 挂在 Player 根（与 PlayerController / 现有预制体一致；勿挂到枪上以免双份）
        WeaponADS ads = GetOrAddComp<WeaponADS>(player);
        GetOrAddComp<ProceduralRecoil>(player);

        Transform muzzle = FindDirectChild(playerCameraT, MuzzleName);
        if (muzzle == null)
        {
            muzzle = CreateChild(playerCameraT, MuzzleName).transform;
        }

        WirePlayerReferences(player, camera, listener, muzzle, gunMesh, WeaponAdsLogCategory);

        // 显式再写一遍 ADS 关键引用，确保 SightPoint / AimAxis / AdsPivot / CameraSightTarget 优先生效。
        SetReference(ads, "weaponHolder", gunMesh, WeaponAdsLogCategory);
        SetReference(ads, "sightPoint", sightPoint, WeaponAdsLogCategory);
        SetReference(ads, "cameraSightTarget", cameraSightTarget, WeaponAdsLogCategory);
        SetReference(ads, "playerCamera", camera, WeaponAdsLogCategory);

        // AimAxis / AdsPivot 由 SetupHandIk 按握点与枪管轴向统一创建并对齐，避免重复写错误默认值。
        WeaponHandIkSetup.SetupHandIk(player);

        Transform aimAxis = FindDirectChild(gunMesh, WeaponHandIkSetup.AimAxisName);
        Transform adsPivot = FindDirectChild(gunMesh, WeaponHandIkSetup.AdsPivotName);
        SetReference(ads, "adsAimAxis", aimAxis, WeaponAdsLogCategory);
        SetReference(ads, "adsPivot", adsPivot, WeaponAdsLogCategory);

        EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(ads);
        return true;
    }

    /// <summary>
    /// 在 WeaponSocket 下找枪模：已有 GunMesh 优先；否则取带 MeshFilter/Renderer 的子物体；都没有则新建 GunMesh。
    /// </summary>
    static Transform ResolveOrCreateGunMesh(Transform socket)
    {
        Transform named = FindDirectChild(socket, GunMeshName);
        if (named != null)
        {
            return named;
        }

        for (int i = 0; i < socket.childCount; i++)
        {
            Transform child = socket.GetChild(i);
            if (child.GetComponentInChildren<MeshFilter>(true) != null
                || child.GetComponentInChildren<MeshRenderer>(true) != null
                || child.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
            {
                return child;
            }
        }

        return CreateChild(socket, GunMeshName).transform;
    }

    /// <summary>
    /// 校验：必须是场景里的 GameObject（不能是 Project 里的 Prefab 资产），否则弹窗提示。
    /// </summary>
    static bool ValidateSelection(GameObject player)
    {
        if (player == null)
        {
            EditorUtility.DisplayDialog("Attach Components", "请先在 Hierarchy 中选中一个 Player 对象。", "知道了");
            return false;
        }

        if (EditorUtility.IsPersistent(player) || !player.scene.IsValid())
        {
            EditorUtility.DisplayDialog("Attach Components",
                "选中的不是场景里的对象。请从 Hierarchy（而不是 Project 窗口）里选中 Player。", "知道了");
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------------
    // 1. 根节点
    // ------------------------------------------------------------------

    /// <summary>
    /// 设置 Tag / Layer，并按顺序补齐根节点组件。已存在的一律跳过，只修正关键参数。
    /// </summary>
    static void ConfigureRoot(GameObject player)
    {
        player.tag = "Player";
        player.layer = PlayerLayer;

        // NetworkObject / ClientNetworkTransform 先就位，后面挂 NetworkBehaviour 才有网络身份。
        GetOrAddComp<NetworkObject>(player);

        NetworkTransform existingTransform = player.GetComponent<NetworkTransform>();
        if (existingTransform != null && !(existingTransform is ClientNetworkTransform))
        {
            // 默认 NetworkTransform 是服务器权威，玩家移动会被回滚，必须换成 Owner 权威。
            GameLog.Warn(LogCategory, "检测到服务器权威 NetworkTransform，已替换为 ClientNetworkTransform。");
            Undo.DestroyObjectImmediate(existingTransform);
        }

        GetOrAddComp<ClientNetworkTransform>(player);

        CharacterController controller = GetOrAddComp<CharacterController>(player);
        controller.height = 1.6f;
        controller.center = Vector3.zero;

        GetOrAddComp<PlayerController>(player);
        GetOrAddComp<PlayerHealth>(player);
        GetOrAddComp<PlayerStatusController>(player);
        GetOrAddComp<PlayerWeapon>(player);
        GetOrAddComp<WeaponADS>(player);
        GetOrAddComp<ProceduralRecoil>(player);

        // CharacterMotor 是普通 C# 类（构造函数注入 CharacterController），不是 MonoBehaviour，
        // 由 PlayerController 在 Awake 里 new，因此这里无法也不应挂载成组件。
        GameLog.Info(LogCategory, "CharacterMotor 为纯逻辑类，由 PlayerController 内部 new，无需也无法挂载为组件。");
    }

    /// <summary>
    /// 找到角色模型根：优先取直接子节点 ModelRoot，否则回溯「Head」骨骼到 Player 的直接子级。
    /// </summary>
    static Transform FindModelRoot(Transform player)
    {
        Transform named = FindDirectChild(player, "ModelRoot");
        if (named != null)
        {
            return named;
        }

        Transform head = FindBone(player, "Head");
        if (head == null)
        {
            return null;
        }

        Transform current = head;
        while (current.parent != null && current.parent != player)
        {
            current = current.parent;
        }

        return current;
    }

    // ------------------------------------------------------------------
    // 2. 武器挂点
    // ------------------------------------------------------------------

    /// <summary>
    /// 确保 mixamorig:RightHand 下有 WeaponSocket，且其中放着 GunMesh；枪模禁用碰撞体并设 Ignore Raycast。
    /// </summary>
    static void AttachWeapon(Transform modelRoot)
    {
        Transform rightHand = FindBone(modelRoot, "RightHand");
        if (rightHand == null)
        {
            GameLog.Warn(LogCategory, "未找到 mixamorig:RightHand，武器挂点建在模型根节点下。");
            rightHand = modelRoot;
        }

        Transform socket = FindDirectChild(rightHand, SocketName);
        if (socket == null)
        {
            socket = CreateChild(rightHand, SocketName).transform;
        }

        socket.gameObject.layer = IgnoreRaycastLayer;

        Transform gunMesh = FindDirectChild(socket, GunMeshName);
        if (gunMesh == null)
        {
            gunMesh = CreateChild(socket, GunMeshName).transform;
        }

        gunMesh.gameObject.layer = IgnoreRaycastLayer;
        // 枪模绝不能挡子弹 / 误进占点 Trigger：整棵子树碰撞体一律禁用。
        RemoveOrDisableColliders(gunMesh.gameObject);

        // 挂点或枪模任一处已有 ShellEjector 就不重复挂，避免双份抛壳。
        if (socket.GetComponent<ShellEjector>() == null && gunMesh.GetComponent<ShellEjector>() == null)
        {
            AddComp<ShellEjector>(socket.gameObject);
        }
    }

    // ------------------------------------------------------------------
    // 3. 部位 Hitbox
    // ------------------------------------------------------------------

    /// <summary>
    /// 按部位挂碰撞体 + BodyPartHitbox。已存在则修正类型与参数，不会叠加重复碰撞体。
    /// 尺寸按「世界米」定义，写入时折算到骨骼本地空间，模型被缩放也能保持预期大小。
    /// </summary>
    static void AttachHitboxes(Transform modelRoot)
    {
        float height = Mathf.Max(0.01f, CalculateBounds(modelRoot.gameObject).size.y);

        ConfigureHitbox(modelRoot, "Head", DetailedBodyPart.Head, null, height);
        ConfigureHitbox(modelRoot, "Spine2", DetailedBodyPart.Torso, null, height);
        ConfigureHitbox(modelRoot, "LeftArm", DetailedBodyPart.Arms, "LeftForeArm", height);
        ConfigureHitbox(modelRoot, "RightArm", DetailedBodyPart.Arms, "RightForeArm", height);
        ConfigureHitbox(modelRoot, "LeftUpLeg", DetailedBodyPart.Legs, "LeftLeg", height);
        ConfigureHitbox(modelRoot, "RightUpLeg", DetailedBodyPart.Legs, "RightLeg", height);
    }

    static void ConfigureHitbox(Transform modelRoot, string boneName, DetailedBodyPart part, string limbChildName, float modelHeight)
    {
        Transform bone = FindBone(modelRoot, boneName);
        if (bone == null)
        {
            GameLog.Warn(LogCategory, $"未找到 {boneName}（{part}），跳过该 Hitbox。");
            return;
        }

        Vector3 scale = bone.lossyScale;

        if (part == DetailedBodyPart.Head || part == DetailedBodyPart.Torso)
        {
            BoxCollider box = EnsureCollider<BoxCollider>(bone.gameObject);
            box.isTrigger = false;
            // 头用固定尺寸（约 20cm 立方）；躯干按身高比例包裹胸腹。
            Vector3 worldCenter = part == DetailedBodyPart.Head ? new Vector3(0f, 0.1f, 0f) : Vector3.zero;
            Vector3 worldSize = part == DetailedBodyPart.Head
                ? new Vector3(0.2f, 0.2f, 0.2f)
                : new Vector3(modelHeight * 0.30f, modelHeight * 0.34f, modelHeight * 0.22f);
            box.center = DivideByScale(worldCenter, scale);
            box.size = DivideByScale(worldSize, scale);
        }
        else
        {
            CapsuleCollider capsule = EnsureCollider<CapsuleCollider>(bone.gameObject);
            capsule.isTrigger = false;
            capsule.center = Vector3.zero;
            // 胶囊轴对齐骨骼指向下一节骨骼的方向（上臂指向前臂、大腿指向小腿）。
            capsule.direction = ResolveCapsuleDirection(bone, limbChildName);

            float worldRadius = part == DetailedBodyPart.Arms ? modelHeight * 0.045f : modelHeight * 0.07f;
            // 大腿胶囊略加长，向下延伸盖住膝盖与小腿上段。
            float worldHeight = part == DetailedBodyPart.Arms ? modelHeight * 0.28f : modelHeight * 0.46f;
            capsule.radius = worldRadius / AverageScale(scale);
            float axisScale = SafeScale(scale[capsule.direction]);
            capsule.height = Mathf.Max(worldHeight / axisScale, capsule.radius * 2.01f);
        }

        BodyPartHitbox hitbox = GetOrAddComp<BodyPartHitbox>(bone.gameObject);
        hitbox.bodyPart = part;
    }

    /// <summary>
    /// 取得所需类型的碰撞体；若骨骼上已有别的类型，先移除，避免一骨两体造成重复判定。
    /// BodyPartHitbox 带 [RequireComponent(typeof(Collider))]，会「保护」住碰撞体不让删，
    /// 因此换类型前必须先把它摘掉，换完由调用方重新挂回。
    /// </summary>
    static T EnsureCollider<T>(GameObject target) where T : Collider
    {
        T existing = target.GetComponent<T>();
        if (existing != null)
        {
            return existing;
        }

        Collider[] others = target.GetComponents<Collider>();
        if (others.Length == 0)
        {
            return AddComp<T>(target);
        }

        BodyPartHitbox protectedHitbox = target.GetComponent<BodyPartHitbox>();
        if (protectedHitbox != null)
        {
            Undo.DestroyObjectImmediate(protectedHitbox);
        }

        foreach (Collider other in others)
        {
            Undo.DestroyObjectImmediate(other);
        }

        return AddComp<T>(target);
    }

    /// <summary>
    /// 用骨骼→下一节骨骼的方向判断胶囊应沿哪个本地轴（0=X / 1=Y / 2=Z），
    /// 这样不必手填方向也能贴合手臂与大腿。
    /// </summary>
    static int ResolveCapsuleDirection(Transform bone, string limbChildName)
    {
        if (string.IsNullOrEmpty(limbChildName))
        {
            return 1;
        }

        Transform limbChild = FindBone(bone, limbChildName);
        if (limbChild == null || limbChild == bone)
        {
            return 1;
        }

        Vector3 localDir = bone.InverseTransformDirection(limbChild.position - bone.position);
        Vector3 abs = new Vector3(Mathf.Abs(localDir.x), Mathf.Abs(localDir.y), Mathf.Abs(localDir.z));

        if (abs.x >= abs.y && abs.x >= abs.z)
        {
            return 0;
        }

        return abs.y >= abs.z ? 1 : 2;
    }

    // ------------------------------------------------------------------
    // 4. 相机与 muzzlePoint
    // ------------------------------------------------------------------

    /// <summary>
    /// 确保 Player 下有 PlayerCamera（Camera + AudioListener，默认关闭）及其子节点 muzzlePoint。
    /// </summary>
    static GameObject AttachCamera(GameObject player, out Camera camera, out AudioListener listener, out Transform gunMesh)
    {
        bool created;
        Transform cameraT = FindDirectChild(player.transform, CameraName);
        GameObject cameraGo;
        if (cameraT != null)
        {
            cameraGo = cameraT.gameObject;
            created = false;
        }
        else
        {
            cameraGo = CreateChild(player.transform, CameraName);
            created = true;
        }

        cameraGo.layer = 0;

        camera = GetOrAddComp<Camera>(cameraGo);
        camera.enabled = false;

        listener = GetOrAddComp<AudioListener>(cameraGo);
        listener.enabled = false;

        if (created)
        {
            // 首次创建时给个站立眼高，具体数值运行期由 PlayerController 按姿态接管。
            cameraGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        }

        Transform muzzleT = FindDirectChild(cameraGo.transform, MuzzleName);
        GameObject muzzle = muzzleT != null ? muzzleT.gameObject : CreateChild(cameraGo.transform, MuzzleName);
        muzzle.layer = 0;

        gunMesh = FindGunMesh(player.transform);
        return muzzle;
    }

    /// <summary>供引用写入复用：在整棵 Player 下找 GunMesh 作为枪模引用。</summary>
    static Transform FindGunMesh(Transform player)
    {
        Transform socket = FindDescendantByName(player, SocketName);
        if (socket != null)
        {
            Transform gun = FindDirectChild(socket, GunMeshName);
            if (gun != null)
            {
                return gun;
            }
        }

        return FindDescendantByName(player, GunMeshName);
    }
}
#endif
