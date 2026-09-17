using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Weapon
{
    /// <summary>
    /// 一次性武器层级诊断：校验 Player→右手→WeaponSocket→枪模链路，并打印 Transform / WeaponADS / Layer / Collider。
    /// 可挂在 Player 根或场景任意物体上，也可由 Editor 菜单 <c>Tools/Diagnose Player Weapon Hierarchy</c> 触发。
    /// </summary>
    public class WeaponHierarchyDiagnostic : MonoBehaviour
    {
        const string LogCategory = "WeaponHierarchyDiag";
        const string RightHandName = "RightHand";
        const string WeaponSocketName = "WeaponSocket";
        const string SightPointName = "SightPoint";
        const string CameraSightTargetName = "CameraSightTarget";
        const string ModelRootName = "ModelRoot";
        static readonly string[] GunMeshCandidateNames =
        {
            "M1 Garand Lowpoly",
            "GunMesh",
            "M1GarandLowpoly"
        };

        [Tooltip("留空则自动在自身子树 / 场景中寻找 Tag=Player 或名为 Player 的对象")]
        [SerializeField] GameObject playerOverride;

        /// <summary>
        /// Inspector 右键菜单：立即对绑定的 Player 跑一遍诊断。
        /// </summary>
        [ContextMenu("Diagnose Player Weapon Hierarchy")]
        public void DiagnoseFromComponent()
        {
            GameObject player = ResolvePlayer(playerOverride != null ? playerOverride : gameObject);
            if (player == null)
            {
                GameLog.Error(LogCategory, "未找到 Player，请把本脚本挂到 Player 上，或指定 playerOverride。");
                return;
            }

            DiagnosePlayer(player);
        }

        /// <summary>
        /// 对指定 Player 输出完整武器层级诊断日志。
        /// </summary>
        public static void DiagnosePlayer(GameObject player)
        {
            if (player == null)
            {
                GameLog.Error(LogCategory, "DiagnosePlayer 收到空 Player。");
                return;
            }

            var sb = new StringBuilder(4096);
            sb.AppendLine("========== Weapon Hierarchy Diagnostic ==========");
            sb.AppendLine($"Player: {GetHierarchyPath(player.transform)}");
            sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            Transform modelRoot = FindDescendantByName(player.transform, ModelRootName)
                                  ?? FindModelRootFallback(player.transform);
            Transform hips = FindBone(player.transform, "Hips");
            Transform rightHand = FindBone(player.transform, RightHandName);
            Transform weaponSocket = FindDescendantByName(player.transform, WeaponSocketName);
            Transform gunMesh = ResolveGunMesh(player.transform, weaponSocket);
            Transform sightPoint = FindDescendantByName(player.transform, SightPointName);
            Transform cameraSightTarget = FindDescendantByName(player.transform, CameraSightTargetName);

            AppendHierarchyChain(sb, player.transform, modelRoot, hips, rightHand, weaponSocket, gunMesh);
            AppendTransformSection(sb, rightHand, weaponSocket, gunMesh, sightPoint, cameraSightTarget);
            AppendWeaponAdsSection(sb, player, weaponSocket);
            AppendPhysicsLayerSection(sb, gunMesh);

            sb.AppendLine();
            sb.AppendLine("【诊断完成，请复制上方日志】");
            sb.AppendLine("=================================================");

            GameLog.Info(LogCategory, sb.ToString());
        }

        /// <summary>
        /// 解析诊断目标：优先 Tag=Player，其次名为 Player，再尝试向上找根。
        /// </summary>
        public static GameObject ResolvePlayer(GameObject start)
        {
            if (start == null)
            {
                return FindAnyPlayerInScene();
            }

            if (start.CompareTag("Player") || string.Equals(start.name, "Player", StringComparison.OrdinalIgnoreCase))
            {
                return start;
            }

            Transform t = start.transform;
            while (t != null)
            {
                if (t.CompareTag("Player") || string.Equals(t.name, "Player", StringComparison.OrdinalIgnoreCase))
                {
                    return t.gameObject;
                }

                t = t.parent;
            }

            Transform childPlayer = FindDescendantByName(start.transform, "Player");
            if (childPlayer != null)
            {
                return childPlayer.gameObject;
            }

            PlayerController controller = start.GetComponentInChildren<PlayerController>(true);
            if (controller != null)
            {
                return controller.gameObject;
            }

            return FindAnyPlayerInScene();
        }

        static GameObject FindAnyPlayerInScene()
        {
            GameObject tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null)
            {
                return tagged;
            }

            PlayerController[] controllers = UnityEngine.Object.FindObjectsOfType<PlayerController>(true);
            return controllers != null && controllers.Length > 0 ? controllers[0].gameObject : null;
        }

        static void AppendHierarchyChain(
            StringBuilder sb,
            Transform player,
            Transform modelRoot,
            Transform hips,
            Transform rightHand,
            Transform weaponSocket,
            Transform gunMesh)
        {
            sb.AppendLine("----- 1. Hierarchy Chain -----");

            if (rightHand != null)
            {
                sb.AppendLine($"RightHand full path: {GetHierarchyPath(rightHand)}");
            }
            else
            {
                sb.AppendLine("RightHand: [MISSING]");
            }

            if (weaponSocket != null)
            {
                sb.AppendLine($"WeaponSocket full path: {GetHierarchyPath(weaponSocket)}");
                bool parentIsRightHand = weaponSocket.parent != null
                    && NormalizeBoneName(weaponSocket.parent.name) == NormalizeBoneName(RightHandName);
                sb.AppendLine(
                    $"WeaponSocket.parent == mixamorig:RightHand ? {parentIsRightHand}" +
                    $"  (actual parent: {(weaponSocket.parent != null ? weaponSocket.parent.name : "null")})");
            }
            else
            {
                sb.AppendLine("WeaponSocket: [MISSING]");
            }

            if (gunMesh != null)
            {
                sb.AppendLine($"Gun mesh full path: {GetHierarchyPath(gunMesh)}");
            }
            else
            {
                sb.AppendLine("Gun mesh (M1 Garand Lowpoly / GunMesh): [MISSING]");
            }

            // 打印 Player → … → RightHand → WeaponSocket → Gun 的链路摘要
            sb.AppendLine("Chain summary:");
            AppendNodeStatus(sb, "Player", player);
            AppendNodeStatus(sb, "ModelRoot", modelRoot);
            AppendNodeStatus(sb, "mixamorig:Hips", hips);
            AppendNodeStatus(sb, "mixamorig:RightHand", rightHand);
            AppendNodeStatus(sb, "WeaponSocket", weaponSocket);
            AppendNodeStatus(sb, "GunMesh", gunMesh);
            sb.AppendLine();
        }

        static void AppendNodeStatus(StringBuilder sb, string label, Transform node)
        {
            sb.AppendLine(node != null
                ? $"  {label}: OK → {GetHierarchyPath(node)}"
                : $"  {label}: [MISSING]");
        }

        static void AppendTransformSection(
            StringBuilder sb,
            Transform rightHand,
            Transform weaponSocket,
            Transform gunMesh,
            Transform sightPoint,
            Transform cameraSightTarget)
        {
            sb.AppendLine("----- 2. Key Transform Data (Local / World) -----");

            if (rightHand != null)
            {
                sb.AppendLine("[mixamorig:RightHand]");
                sb.AppendLine($"  LocalPosition : {Fmt(rightHand.localPosition)}");
                sb.AppendLine($"  LocalRotation : {Fmt(rightHand.localRotation.eulerAngles)} (euler)");
                sb.AppendLine($"  LossyScale    : {Fmt(rightHand.lossyScale)}");
                sb.AppendLine($"  WorldPosition : {Fmt(rightHand.position)}");
            }
            else
            {
                sb.AppendLine("[mixamorig:RightHand] MISSING");
            }

            if (weaponSocket != null)
            {
                sb.AppendLine("[WeaponSocket]");
                sb.AppendLine($"  LocalPosition : {Fmt(weaponSocket.localPosition)}");
                sb.AppendLine($"  LocalRotation : {Fmt(weaponSocket.localRotation.eulerAngles)} (euler)");
                sb.AppendLine($"  LocalScale    : {Fmt(weaponSocket.localScale)}");
                sb.AppendLine($"  WorldPosition : {Fmt(weaponSocket.position)}");
            }
            else
            {
                sb.AppendLine("[WeaponSocket] MISSING");
            }

            if (gunMesh != null)
            {
                sb.AppendLine($"[GunMesh: {gunMesh.name}]");
                sb.AppendLine($"  LocalPosition : {Fmt(gunMesh.localPosition)}");
                sb.AppendLine($"  LocalRotation : {Fmt(gunMesh.localRotation.eulerAngles)} (euler)");
                sb.AppendLine($"  LocalScale    : {Fmt(gunMesh.localScale)}");
                sb.AppendLine($"  WorldPosition : {Fmt(gunMesh.position)}");
            }
            else
            {
                sb.AppendLine("[GunMesh] MISSING");
            }

            if (sightPoint != null)
            {
                sb.AppendLine("[SightPoint]");
                sb.AppendLine($"  LocalPosition : {Fmt(sightPoint.localPosition)}");
                sb.AppendLine($"  WorldPosition : {Fmt(sightPoint.position)}");
                sb.AppendLine($"  Path          : {GetHierarchyPath(sightPoint)}");
            }
            else
            {
                sb.AppendLine("[SightPoint] MISSING");
            }

            if (cameraSightTarget != null)
            {
                sb.AppendLine("[CameraSightTarget]");
                sb.AppendLine($"  LocalPosition : {Fmt(cameraSightTarget.localPosition)}");
                sb.AppendLine($"  WorldPosition : {Fmt(cameraSightTarget.position)}");
                sb.AppendLine($"  Path          : {GetHierarchyPath(cameraSightTarget)}");
            }
            else
            {
                sb.AppendLine("[CameraSightTarget] MISSING");
            }

            sb.AppendLine();
        }

        static void AppendWeaponAdsSection(StringBuilder sb, GameObject player, Transform weaponSocket)
        {
            sb.AppendLine("----- 3. WeaponADS Script / Data -----");

            var adsList = new List<WeaponADS>();
            adsList.AddRange(player.GetComponents<WeaponADS>());
            if (weaponSocket != null)
            {
                adsList.AddRange(weaponSocket.GetComponents<WeaponADS>());
            }

            // 去重（同一实例可能被两次收集）
            var unique = new List<WeaponADS>();
            foreach (WeaponADS ads in adsList)
            {
                if (ads != null && !unique.Contains(ads))
                {
                    unique.Add(ads);
                }
            }

            if (unique.Count == 0)
            {
                sb.AppendLine("未在 Player 根或 WeaponSocket 上找到 WeaponADS。");
                // 再扫整棵树，便于发现挂错位置的副本
                WeaponADS[] all = player.GetComponentsInChildren<WeaponADS>(true);
                sb.AppendLine($"整棵 Player 子树 WeaponADS 数量: {all.Length}");
                foreach (WeaponADS ads in all)
                {
                    AppendSingleWeaponAds(sb, ads);
                }

                sb.AppendLine();
                return;
            }

            sb.AppendLine($"Player 根 + WeaponSocket 上 WeaponADS 数量: {unique.Count}");
            foreach (WeaponADS ads in unique)
            {
                AppendSingleWeaponAds(sb, ads);
            }

            sb.AppendLine();
        }

        static void AppendSingleWeaponAds(StringBuilder sb, WeaponADS ads)
        {
            sb.AppendLine($"--- WeaponADS on [{GetHierarchyPath(ads.transform)}] ---");

            Transform weaponHolder = GetPrivateField<Transform>(ads, "weaponHolder");
            Transform sightPoint = GetPrivateField<Transform>(ads, "sightPoint");
            Transform cameraSightTarget = GetPrivateField<Transform>(ads, "cameraSightTarget");
            Camera playerCamera = GetPrivateField<Camera>(ads, "playerCamera");

            sb.AppendLine($"  weaponHolder      : {DescribeRef(weaponHolder)}");
            sb.AppendLine($"  sightPoint        : {DescribeRef(sightPoint)}");
            sb.AppendLine($"  cameraSightTarget : {DescribeRef(cameraSightTarget)}");
            sb.AppendLine($"  playerCamera      : {(playerCamera != null ? GetHierarchyPath(playerCamera.transform) : "null")}");

            // 当前源码腰射归零；仍反射打印可能残留的序列化字段（hipfire / initial）
            DumpNamedVectorFields(sb, ads,
                "hipfirePosition", "hipfireRotation",
                "initialPosition", "initialRotation",
                "_targetLocalPosition", "adsSpeed", "defaultFOV", "adsFOV");

            sb.AppendLine($"  IsAiming          : {ads.IsAiming}");
            sb.AppendLine($"  AdsProgress       : {ads.AdsProgress:F3}");

            if (weaponHolder != null)
            {
                sb.AppendLine($"  weaponHolder.localPosition (runtime): {Fmt(weaponHolder.localPosition)}");
                sb.AppendLine($"  weaponHolder.localRotation (runtime): {Fmt(weaponHolder.localRotation.eulerAngles)}");
            }
        }

        static void DumpNamedVectorFields(StringBuilder sb, object target, params string[] fieldNames)
        {
            foreach (string fieldName in fieldNames)
            {
                FieldInfo field = FindField(target.GetType(), fieldName);
                if (field == null)
                {
                    sb.AppendLine($"  {fieldName,-20}: [field not present in current WeaponADS]");
                    continue;
                }

                object value = field.GetValue(target);
                sb.AppendLine($"  {fieldName,-20}: {FormatFieldValue(value)}");
            }
        }

        static string FormatFieldValue(object value)
        {
            return value switch
            {
                null => "null",
                Vector3 v => Fmt(v),
                Quaternion q => Fmt(q.eulerAngles) + " (euler)",
                float f => f.ToString("F4"),
                bool b => b.ToString(),
                _ => value.ToString()
            };
        }

        static void AppendPhysicsLayerSection(StringBuilder sb, Transform gunMesh)
        {
            sb.AppendLine("----- 4. Physics / Layer (Gun Mesh Subtree) -----");

            if (gunMesh == null)
            {
                sb.AppendLine("枪模缺失，跳过 Layer / Collider 检查。");
                sb.AppendLine();
                return;
            }

            Transform[] nodes = gunMesh.GetComponentsInChildren<Transform>(true);
            int colliderCount = 0;

            foreach (Transform node in nodes)
            {
                int layer = node.gameObject.layer;
                string layerName = LayerMask.LayerToName(layer);
                if (string.IsNullOrEmpty(layerName))
                {
                    layerName = "(unnamed)";
                }

                Collider[] colliders = node.GetComponents<Collider>();
                string colliderInfo = colliders.Length == 0
                    ? "no Collider"
                    : DescribeColliders(colliders);

                if (colliders.Length > 0)
                {
                    colliderCount += colliders.Length;
                }

                sb.AppendLine(
                    $"  {GetRelativePath(gunMesh, node)} | Layer={layer} ({layerName}) | {colliderInfo}");
            }

            sb.AppendLine(colliderCount == 0
                ? "结论: 枪模子树无 Collider（符合预期）。"
                : $"结论: 枪模子树共发现 {colliderCount} 个 Collider（通常应清除，避免挡子弹 / 误 Trigger）。");
            sb.AppendLine();
        }

        static string DescribeColliders(Collider[] colliders)
        {
            var parts = new string[colliders.Length];
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                parts[i] = $"{c.GetType().Name}(enabled={c.enabled}, isTrigger={c.isTrigger})";
            }

            return string.Join(", ", parts);
        }

        static Transform ResolveGunMesh(Transform player, Transform weaponSocket)
        {
            foreach (string candidate in GunMeshCandidateNames)
            {
                Transform named = FindDescendantByName(player, candidate);
                if (named != null)
                {
                    return named;
                }
            }

            if (weaponSocket == null)
            {
                return null;
            }

            for (int i = 0; i < weaponSocket.childCount; i++)
            {
                Transform child = weaponSocket.GetChild(i);
                if (child.GetComponentInChildren<Renderer>(true) != null)
                {
                    return child;
                }
            }

            return weaponSocket.childCount > 0 ? weaponSocket.GetChild(0) : null;
        }

        static Transform FindModelRootFallback(Transform player)
        {
            // 无 ModelRoot 命名时：找带 Animator 的子物体
            Animator animator = player.GetComponentInChildren<Animator>(true);
            return animator != null ? animator.transform : null;
        }

        static Transform FindBone(Transform root, string boneName)
        {
            string target = NormalizeBoneName(boneName);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (NormalizeBoneName(t.name) == target)
                {
                    return t;
                }
            }

            return null;
        }

        static Transform FindDescendantByName(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }

            return null;
        }

        static string NormalizeBoneName(string raw)
        {
            string lower = raw.ToLowerInvariant();
            int colon = lower.LastIndexOf(':');
            if (colon >= 0)
            {
                lower = lower.Substring(colon + 1);
            }

            return lower.Replace("mixamorig", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
        }

        static string GetHierarchyPath(Transform t)
        {
            if (t == null)
            {
                return "null";
            }

            var stack = new Stack<string>();
            Transform cur = t;
            while (cur != null)
            {
                stack.Push(cur.name);
                cur = cur.parent;
            }

            return string.Join("/", stack);
        }

        static string GetRelativePath(Transform root, Transform node)
        {
            if (node == root)
            {
                return root.name;
            }

            var stack = new Stack<string>();
            Transform cur = node;
            while (cur != null && cur != root)
            {
                stack.Push(cur.name);
                cur = cur.parent;
            }

            return root.name + "/" + string.Join("/", stack);
        }

        static string DescribeRef(Transform t)
        {
            return t != null ? GetHierarchyPath(t) : "null";
        }

        static string Fmt(Vector3 v) => $"({v.x:F4}, {v.y:F4}, {v.z:F4})";

        static T GetPrivateField<T>(object target, string fieldName) where T : class
        {
            FieldInfo field = FindField(target.GetType(), fieldName);
            return field != null ? field.GetValue(target) as T : null;
        }

        static FieldInfo FindField(Type type, string fieldName)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }
    }
}
