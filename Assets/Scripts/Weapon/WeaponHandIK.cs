using UnityEngine;

namespace Weapon
{
    /// <summary>
    /// Generic / Humanoid 骨骼持枪方案（不依赖 Animator IK Pass）。
    /// 优先按 Mixamo 节点名解析，Humanoid Avatar 再用 GetBoneTransform 兜底，
    /// 这样换模型导入类型时仍能保持同一套双手握枪逻辑。
    /// <list type="bullet">
    /// <item>枪挂在 mixamorig:RightHand → WeaponSocket 下，右手靠父子约束握枪</item>
    /// <item>左手用解析双骨 IK（上臂→前臂→手）够向 LeftHandIK，不拆断骨骼长度</item>
    /// <item>握点与骨骼一律按名字查找（mixamorig:*）</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(500)]
    public sealed class WeaponHandIK : MonoBehaviour
    {
        [Header("引用（留空自动找）")]
        [SerializeField] Animator animator;
        [SerializeField] WeaponADS weaponADS;

        [Header("右手：枪对进掌心（只平移）")]
        [Tooltip("Start 时把枪平移到 RightHandIK 对齐 RightHand，并写回 WeaponADS 腰射基准")]
        [SerializeField] bool autoAlignGunToRightHandOnStart = true;

        [Header("左手：双骨 IK")]
        [Tooltip("会直接改 LeftArm/LeftForeArm/LeftHand 旋转。轴向不对时会出现面条手臂，务必先关")]
        [SerializeField] bool enableLeftArmTwoBoneIk = true;
        [Range(0f, 1f)] [SerializeField] float leftArmIkWeight = 1f;
        [Tooltip("手掌相对 LeftHandIK 的额外旋转（欧拉）")]
        [SerializeField] Vector3 leftHandRotationOffsetEuler = new Vector3(0f, 90f, 90f);
        [Tooltip("是否把左手旋转也贴到握点；持枪时建议开启，否则掌心常呈摊开悬空")]
        [SerializeField] bool applyLeftHandRotation = true;

        [Header("调试")]
        [SerializeField] bool drawGizmos = true;

        Transform _leftUpperArm;
        Transform _leftForeArm;
        Transform _leftHand;
        Transform _rightHand;
        bool _loggedResolve;
        float _upperLen;
        float _foreLen;

        void Awake()
        {
            AutoBind();
            ResolveBones();
        }

        void Start()
        {
            AutoBind();
            EnsureIkTargets();
            ResolveBones();
            CacheBoneLengths();
            ValidateSetup();

            // 右手：把枪握把对进掌心（只改枪 local 平移），避免枪浮在手旁边。
            if (autoAlignGunToRightHandOnStart)
            {
                AlignGunToRightHand();
            }
        }

        void OnValidate() => AutoBind();

        void LateUpdate()
        {
            if (weaponADS == null)
            {
                return;
            }

            if (_leftUpperArm == null || _leftForeArm == null || _leftHand == null)
            {
                ResolveBones();
                CacheBoneLengths();
            }

            if (!enableLeftArmTwoBoneIk
                || !weaponADS.isLeftHandAttached
                || leftArmIkWeight <= 0.001f
                || weaponADS.leftHandIKTarget == null
                || _leftUpperArm == null
                || _leftForeArm == null
                || _leftHand == null)
            {
                return;
            }

            // Animator 已播完本帧动画，这里覆盖左臂，把左手够到护木。
            SolveLeftArmTwoBoneIk(weaponADS.leftHandIKTarget, leftArmIkWeight);
        }

        /// <summary>
        /// 在进入运行态时报告关键引用缺失；不自动改写已在 Prefab 中调好的枪姿态。
        /// </summary>
        void ValidateSetup()
        {
            if (weaponADS == null || weaponADS.WeaponHolder == null)
            {
                GameLog.Error("WeaponHandIK", "WeaponADS 或 weaponHolder 未绑定，右手枪械挂点不会生效。");
                return;
            }

            if (weaponADS.rightHandIKTarget == null)
            {
                GameLog.Warn("WeaponHandIK", "rightHandIKTarget 未绑定；枪仍会挂在 RightHand，但无法执行手动握把对齐。");
            }

            if (weaponADS.leftHandIKTarget == null)
            {
                GameLog.Error("WeaponHandIK", "leftHandIKTarget 未绑定，左手双骨 IK 不会执行。");
            }

            if (_leftUpperArm == null || _leftForeArm == null || _leftHand == null || _rightHand == null)
            {
                GameLog.Error("WeaponHandIK", "左右手骨骼引用不完整，无法可靠地执行双手握枪。");
            }
        }

        /// <summary>
        /// 编辑器手动校正枪根，使 RightHandIK 落到 RightHand 上，并写回 WeaponADS 腰射基准。
        /// 运行时不会调用此方法，避免启动时改写 authored pose 或影响 Animator 驱动的角色。
        /// </summary>
        [ContextMenu("Align Gun To Right Hand")]
        public void AlignGunToRightHand()
        {
            AutoBind();
            EnsureIkTargets();
            ResolveBones();

            if (weaponADS == null || weaponADS.WeaponHolder == null)
            {
                GameLog.Warn("WeaponHandIK", "无法对齐：缺少 WeaponADS / weaponHolder。");
                return;
            }

            Transform grip = weaponADS.rightHandIKTarget;
            if (grip == null || _rightHand == null)
            {
                GameLog.Warn(
                    "WeaponHandIK",
                    $"无法对齐：grip={(grip != null ? grip.name : "null")}, rightHand={NameOf(_rightHand)}");
                return;
            }

            Transform gun = weaponADS.WeaponHolder;
            // 只改 WeaponHolder 自身的 localPosition；不触碰右手、WeaponSocket 父链或 ModelRoot。
            Vector3 localDelta = gun.parent.InverseTransformVector(_rightHand.position - grip.position);
            gun.localPosition += localDelta;
            weaponADS.RecacheAuthoredPoseFromWeaponHolder();

            GameLog.Info("WeaponHandIK", $"右手握把对齐完成 localDelta={localDelta}, gun.local={gun.localPosition}");
        }

        /// <summary>
        /// 解析双骨 IK：上臂 + 前臂，末端为手骨；保持骨长，弯折平面用「肩→目标」与动画残留侧向。
        /// </summary>
        void SolveLeftArmTwoBoneIk(Transform target, float weight)
        {
            Vector3 rootPos = _leftUpperArm.position;
            Vector3 targetPos = target.position;

            float maxReach = _upperLen + _foreLen;
            float minReach = Mathf.Abs(_upperLen - _foreLen);
            Vector3 toTarget = targetPos - rootPos;
            float dist = Mathf.Clamp(toTarget.magnitude, minReach + 0.001f, maxReach - 0.001f);
            if (toTarget.sqrMagnitude < 1e-8f)
            {
                return;
            }

            toTarget = toTarget.normalized * dist;

            // 弯折轴向：用当前肘相对根→手方向的侧向，避免肘翻到怪方向。
            Vector3 currentMid = _leftForeArm.position - rootPos;
            Vector3 bendNormal = Vector3.Cross(toTarget, Vector3.Cross(currentMid, toTarget));
            if (bendNormal.sqrMagnitude < 1e-6f)
            {
                bendNormal = Vector3.Cross(toTarget, _leftUpperArm.up);
            }

            if (bendNormal.sqrMagnitude < 1e-6f)
            {
                bendNormal = Vector3.up;
            }

            bendNormal.Normalize();

            // 余弦定理求上臂相对「根→目标」的夹角。
            float upperLenSq = _upperLen * _upperLen;
            float foreLenSq = _foreLen * _foreLen;
            float distSq = dist * dist;
            float cosAngle = (upperLenSq + distSq - foreLenSq) / (2f * _upperLen * dist);
            cosAngle = Mathf.Clamp(cosAngle, -1f, 1f);
            float angle = Mathf.Acos(cosAngle);

            Vector3 upperDir = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, bendNormal) * (toTarget / dist);
            Vector3 midPos = rootPos + upperDir * _upperLen;
            Vector3 lowerDir = (rootPos + toTarget - midPos).normalized;

            // 把「骨轴（指向子骨骼）」旋到目标方向；Mixamo 通骨轴为子物体方向而非一定是 forward。
            Quaternion upperRot = RotationFromChildAxis(_leftUpperArm, _leftForeArm, upperDir);
            Quaternion lowerRot = RotationFromChildAxis(_leftForeArm, _leftHand, lowerDir);

            _leftUpperArm.rotation = Quaternion.Slerp(_leftUpperArm.rotation, upperRot, weight);
            _leftForeArm.rotation = Quaternion.Slerp(_leftForeArm.rotation, lowerRot, weight);

            // 腕骨朝向握点：否则只拧到前臂，手掌仍保持动画里的“摊开”姿势，看起来像没握枪。
            if (applyLeftHandRotation)
            {
                Quaternion handRot = target.rotation * Quaternion.Euler(leftHandRotationOffsetEuler);
                _leftHand.rotation = Quaternion.Slerp(_leftHand.rotation, handRot, weight);
            }
            else
            {
                // 即使不锁死掌心旋转，也把腕骨“骨轴”继续对准握点，减少手悬在护木旁的空档。
                Transform leftHandChild = _leftHand.childCount > 0 ? _leftHand.GetChild(0) : null;
                if (leftHandChild != null)
                {
                    Vector3 handDir = (target.position - _leftHand.position);
                    if (handDir.sqrMagnitude > 1e-6f)
                    {
                        Quaternion tipRot = RotationFromChildAxis(_leftHand, leftHandChild, handDir.normalized);
                        _leftHand.rotation = Quaternion.Slerp(_leftHand.rotation, tipRot, weight * 0.5f);
                    }
                }
            }
        }

        /// <summary>
        /// 生成旋转：把「当前骨骼指向子骨骼」的轴对齐到 desiredWorldDir，并尽量保留原 twist。
        /// </summary>
        static Quaternion RotationFromChildAxis(Transform bone, Transform child, Vector3 desiredWorldDir)
        {
            Vector3 currentAxis = child.position - bone.position;
            if (currentAxis.sqrMagnitude < 1e-8f || desiredWorldDir.sqrMagnitude < 1e-8f)
            {
                return bone.rotation;
            }

            Quaternion delta = Quaternion.FromToRotation(currentAxis.normalized, desiredWorldDir.normalized);
            return delta * bone.rotation;
        }

        void ResolveBones()
        {
            Transform root = animator != null ? animator.transform : transform;

            _leftUpperArm = FindBoneByNames(root, "LeftArm", "mixamorig:LeftArm");
            _leftForeArm = FindBoneByNames(root, "LeftForeArm", "mixamorig:LeftForeArm");
            _leftHand = FindBoneByNames(root, "LeftHand", "mixamorig:LeftHand");
            _rightHand = FindBoneByNames(root, "RightHand", "mixamorig:RightHand");

            // Humanoid 模型可能重命名骨骼；名称解析失败时使用 Avatar 映射。
            if (animator != null && animator.isHuman)
            {
                _leftUpperArm ??= animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                _leftForeArm ??= animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                _leftHand ??= animator.GetBoneTransform(HumanBodyBones.LeftHand);
                _rightHand ??= animator.GetBoneTransform(HumanBodyBones.RightHand);
            }

            if (_loggedResolve)
            {
                return;
            }

            _loggedResolve = true;
            GameLog.Info(
                "WeaponHandIK",
                $"骨骼解析 | Upper={NameOf(_leftUpperArm)} Fore={NameOf(_leftForeArm)} " +
                $"LHand={NameOf(_leftHand)} RHand={NameOf(_rightHand)} | AvatarHuman={animator != null && animator.isHuman}");

            if (_leftUpperArm == null || _leftForeArm == null || _leftHand == null || _rightHand == null)
            {
                GameLog.Error("WeaponHandIK", "手臂骨未找全，请确认 ModelRoot 下有 LeftArm/LeftForeArm/LeftHand/RightHand，或提供有效 Humanoid Avatar。");
            }
        }

        void CacheBoneLengths()
        {
            if (_leftUpperArm == null || _leftForeArm == null || _leftHand == null)
            {
                return;
            }

            _upperLen = Vector3.Distance(_leftUpperArm.position, _leftForeArm.position);
            _foreLen = Vector3.Distance(_leftForeArm.position, _leftHand.position);
            if (_upperLen < 1e-4f)
            {
                _upperLen = 0.25f;
            }

            if (_foreLen < 1e-4f)
            {
                _foreLen = 0.25f;
            }
        }

        void EnsureIkTargets()
        {
            if (weaponADS == null)
            {
                return;
            }

            Transform searchRoot = weaponADS.WeaponHolder != null ? weaponADS.WeaponHolder : transform;

            if (weaponADS.leftHandIKTarget == null)
            {
                weaponADS.leftHandIKTarget = FindBoneByNames(searchRoot, "LeftHandIK", "Left_IK", "Letf_IK");
            }

            if (weaponADS.rightHandIKTarget == null)
            {
                weaponADS.rightHandIKTarget = FindBoneByNames(searchRoot, "RightHandIK", "Right_IK");
            }
        }

        void AutoBind()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (weaponADS == null)
            {
                weaponADS = GetComponentInParent<WeaponADS>();
                if (weaponADS == null)
                {
                    weaponADS = GetComponentInChildren<WeaponADS>(true);
                }
            }
        }

        static Transform FindBoneByNames(Transform root, params string[] names)
        {
            if (root == null)
            {
                return null;
            }

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            foreach (string name in names)
            {
                string target = NormalizeBoneName(name);
                foreach (Transform t in all)
                {
                    if (NormalizeBoneName(t.name) == target)
                    {
                        return t;
                    }
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

        static string NameOf(Transform t) => t != null ? t.name : "null";

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!drawGizmos || weaponADS == null)
            {
                return;
            }

            if (weaponADS.leftHandIKTarget != null)
            {
                Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
                Gizmos.DrawWireSphere(weaponADS.leftHandIKTarget.position, 0.025f);
            }

            if (weaponADS.rightHandIKTarget != null)
            {
                Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.9f);
                Gizmos.DrawWireSphere(weaponADS.rightHandIKTarget.position, 0.025f);
            }

            if (_leftUpperArm != null && _leftForeArm != null && _leftHand != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(_leftUpperArm.position, _leftForeArm.position);
                Gizmos.DrawLine(_leftForeArm.position, _leftHand.position);
            }
        }
#endif
    }
}
