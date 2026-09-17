using UnityEngine;

namespace Weapon
{
    /// <summary>
    /// 机瞄 / 腰射位姿。
    /// 腰射：在「预制体里调好的枪本地位姿」之上叠加 hip 偏移，每帧写回（供后坐力复位）。
    /// 开镜：在父空间内计算 SightPoint→CameraSightTarget 的平移，平滑叠到枪上；
    /// 不再先清零再按世界坐标硬拽，避免把枪从右手骨骼上撕开、看起来像锁死在相机上。
    /// </summary>
    public class WeaponADS : MonoBehaviour
    {
        [Header("核心组件引用")]
        [SerializeField] Transform weaponHolder;
        [SerializeField] Transform sightPoint;
        [Tooltip("独立 ADS 瞄准轴：forward 必须沿枪管，up 指向枪械顶部，且仅负责朝向，不再混用 SightPoint 旋转")]
        [SerializeField] Transform adsAimAxis;
        [Tooltip("ADS 位置/旋转补偿支点：通常放在右手握把/枪托附近，负责反解 holder 位置与补偿角度")]
        [SerializeField] Transform adsPivot;
        [Tooltip("相机前方准星对齐点（PlayerCamera/CameraSightTarget）")]
        [SerializeField] Transform cameraSightTarget;
        [SerializeField] Camera playerCamera;

        [Header("腰射偏移（相对预制体初始位姿，不是世界坐标）")]
        [Tooltip("加在 Awake 时缓存的枪 localPosition 上；默认 0 表示完全沿用你在层级里调好的位置")]
        public Vector3 hipLocalPosition = Vector3.zero;
        [Tooltip("加在 Awake 时缓存的枪 local 欧拉角上")]
        public Vector3 hipLocalRotation = Vector3.zero;
        [Tooltip("腰射时把枪管摆平（绕握把旋转），避免枪竖直导致左手明显高于右手")]
        [SerializeField] bool levelBarrelInHipfire = true;

        [Header("ADS 瞄准参数")]
        [SerializeField] float adsSpeed = 12f;
        [SerializeField] float defaultFOV = 70f;
        [SerializeField] float adsFOV = 50f;
        [Tooltip("关闭后开镜只缩 FOV、不移动枪，便于单独调握姿/IK")]
        [SerializeField] bool moveWeaponOnAds = true;

        [Header("双手 IK 预留接口")]
        public Transform leftHandIKTarget;
        public Transform rightHandIKTarget;
        public bool isLeftHandAttached = true;

        /// <summary>预制体 / 场景里调好的枪 local 位姿，Awake 缓存后不再被 (0,0,0) 覆盖。</summary>
        Vector3 _authoredLocalPosition;
        Vector3 _authoredLocalEuler;
        bool _authoredPoseCached;

        bool _isAiming;
        float _adsBlend;

        public bool IsAiming => _isAiming;
        public float AdsProgress { get; private set; }

        /// <summary>枪模 Transform（weaponHolder），供 HandIK 对齐握把使用。</summary>
        public Transform WeaponHolder => weaponHolder;

        /// <summary>当前腰射目标 local 位姿（authored + hip 偏移）。</summary>
        public Vector3 HipTargetLocalPosition => _authoredLocalPosition + hipLocalPosition;
        public Vector3 HipTargetLocalEuler => _authoredLocalEuler + hipLocalRotation;

        void Awake()
        {
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>(true);
            }

            ResolveAimReferences();
            CacheAuthoredPose();
        }

        /// <summary>
        /// 由 PlayerController 在有操作权的 Update 里驱动。
        /// </summary>
        public void OnUpdate(bool canAimInput)
        {
            if (!_authoredPoseCached)
            {
                ResolveAimReferences();
                CacheAuthoredPose();
            }

            HandleInput(canAimInput);
            UpdateAdsBlendAndFov();
            UpdateWeaponPose();
        }

        /// <summary>
        /// 换弹开始传 false（松开左手 IK），换弹结束传 true（重新附着）。
        /// </summary>
        public void SetLeftHandIK(bool enabled)
        {
            isLeftHandAttached = enabled;
        }

        /// <summary>
        /// 强制回到腰射位姿，供倒地/死亡等状态重置。
        /// </summary>
        public void ResetToHipfire()
        {
            _isAiming = false;
            _adsBlend = 0f;
            AdsProgress = 0f;
            ApplyHipPose();

            if (playerCamera != null)
            {
                playerCamera.fieldOfView = defaultFOV;
            }
        }

        /// <summary>
        /// 把当前 weaponHolder 本地位姿重新记为「预制体基准」（Scene 里调完枪后可在 Inspector 右键调用）。
        /// </summary>
        [ContextMenu("Recache Authored Pose From WeaponHolder")]
        public void RecacheAuthoredPoseFromWeaponHolder()
        {
            if (weaponHolder == null)
            {
                return;
            }

            _authoredLocalPosition = weaponHolder.localPosition;
            _authoredLocalEuler = weaponHolder.localRotation.eulerAngles;
            _authoredPoseCached = true;
            hipLocalPosition = Vector3.zero;
            hipLocalRotation = Vector3.zero;
            GameLog.Info("WeaponADS", $"已缓存枪基准 localPos={_authoredLocalPosition}, localEuler={_authoredLocalEuler}");
        }

        void CacheAuthoredPose()
        {
            if (weaponHolder == null)
            {
                return;
            }

            // 关键：以层级/预制体上的实际 local 为基准，而不是用 hip 默认 (0,0,0) 把枪每帧拽回原点。
            _authoredLocalPosition = weaponHolder.localPosition;
            _authoredLocalEuler = weaponHolder.localRotation.eulerAngles;
            _authoredPoseCached = true;
        }

        void HandleInput(bool canAimInput)
        {
            _isAiming = canAimInput && Input.GetMouseButton(1);
        }

        void UpdateWeaponPose()
        {
            if (weaponHolder == null)
            {
                return;
            }

            if (!_isAiming || !moveWeaponOnAds || _adsBlend <= 0.001f)
            {
                ApplyHipPose();
                return;
            }

            ApplyAdsPose();
        }

        void ApplyHipPose()
        {
            ApplyAuthoredHipLocalPose();
            if (levelBarrelInHipfire)
            {
                LevelBarrelAroundGrip();
            }
        }

        /// <summary>只写回预制体腰射 local，不做枪管找平。</summary>
        void ApplyAuthoredHipLocalPose()
        {
            weaponHolder.localPosition = HipTargetLocalPosition;
            weaponHolder.localRotation = Quaternion.Euler(HipTargetLocalEuler);
        }

        /// <summary>
        /// 腰射找平：以 adsPivot（握把）为支点，把 AimAxis 转到水平朝向相机前方。
        /// 这样左右手握点都在近似水平的枪管上，高度接近，而不会左手高高举起。
        /// </summary>
        void LevelBarrelAroundGrip()
        {
            ResolveAimReferences();
            if (adsAimAxis == null || weaponHolder.parent == null)
            {
                return;
            }

            Vector3 desiredFwd = playerCamera != null
                ? playerCamera.transform.forward
                : weaponHolder.parent.forward;
            desiredFwd = Vector3.ProjectOnPlane(desiredFwd, Vector3.up);
            if (desiredFwd.sqrMagnitude < 1e-4f)
            {
                desiredFwd = Vector3.ProjectOnPlane(weaponHolder.parent.forward, Vector3.up);
            }

            if (desiredFwd.sqrMagnitude < 1e-4f)
            {
                return;
            }

            desiredFwd.Normalize();

            Transform pivot = adsPivot != null ? adsPivot : weaponHolder;
            Vector3 pivotWorld = pivot.position;
            Vector3 holderToPivotLocal = weaponHolder.InverseTransformPoint(pivotWorld);

            Quaternion aimRelativeToHolder = Quaternion.Inverse(weaponHolder.rotation) * adsAimAxis.rotation;
            Quaternion desiredAimWorld = Quaternion.LookRotation(desiredFwd, Vector3.up);
            Quaternion desiredHolderWorld = desiredAimWorld * Quaternion.Inverse(aimRelativeToHolder);

            weaponHolder.rotation = desiredHolderWorld;
            // 握把世界坐标保持不动，整枪绕握把转动找平。
            weaponHolder.position = pivotWorld - weaponHolder.rotation * holderToPivotLocal;
        }

        /// <summary>
        /// 开镜：用独立 ADS 轴的 forward/up 定义枪管朝向，并以 adsPivot 反解 holder 位置。
        /// SightPoint 只用作瞄准位置点，不再单独承载旋转职责；这样可保留枪身 authored roll，
        /// 并避免瞄准轴因复杂本地旋转而绕到左侧象限。
        /// </summary>
        void ApplyAdsPose()
        {
            // 开镜混合以「未找平」的 authored 腰射为起点，避免 LevelBarrel 与 ADS 抢旋转。
            ApplyAuthoredHipLocalPose();

            ResolveAimReferences();
            if (sightPoint == null
                || adsAimAxis == null
                || adsPivot == null
                || cameraSightTarget == null
                || weaponHolder.parent == null)
            {
                if (adsAimAxis == null)
                {
                    GameLog.Warn("WeaponADS", "adsAimAxis 缺失，ADS 期间无法计算枪管朝向。请在枪械子树中绑定独立 AimAxis。");
                }

                if (adsPivot == null)
                {
                    GameLog.Warn("WeaponADS", "adsPivot 缺失，ADS 位置无法由右手握把支点反解。请在枪械子树中绑定独立 AdsPivot。 ");
                }

                if (sightPoint == null)
                {
                    GameLog.Warn("WeaponADS", "sightPoint 缺失，ADS 无法对齐屏幕中心点。请保留其作为瞄准位置点。 ");
                }

                return;
            }

            // 关键约束：射击方向必须跟随相机视线；ADS 仅修正枪械相对握点的朝向和位置，
            // 不直接把持枪根节点扯离 RightHand / WeaponSocket。
            Vector3 cameraForward = playerCamera != null ? playerCamera.transform.forward : cameraSightTarget.forward;
            if (cameraForward.sqrMagnitude < 1e-6f)
            {
                cameraForward = cameraSightTarget.forward;
            }

            cameraForward.Normalize();

            Vector3 cameraUp = playerCamera != null ? playerCamera.transform.up : Vector3.up;
            if (cameraUp.sqrMagnitude < 1e-6f)
            {
                cameraUp = Vector3.up;
            }

            // AimAxis 相对 gun 的本地旋转是恒定的；用 LookRotation 同时对齐 forward 与 roll，
            // 避免旧版 FromToRotation 只拧枪管、把枪身拧成竖直贴脸。
            Quaternion aimRelativeToHolder = Quaternion.Inverse(weaponHolder.rotation) * adsAimAxis.rotation;
            Quaternion desiredAimWorld = Quaternion.LookRotation(cameraForward, cameraUp);
            Quaternion desiredHolderWorldRotation = desiredAimWorld * Quaternion.Inverse(aimRelativeToHolder);
            Quaternion desiredHolderLocalRotation =
                Quaternion.Inverse(weaponHolder.parent.rotation) * desiredHolderWorldRotation;

            // 以 adsPivot 作为枪托/握把支点，使用 SightPoint 的偏移量反解 holder 的目标位置。
            // 这里仍然保持枪为本地挂点子树，避免在 ADS 时把模型拉到相机左侧象限。
            Vector3 sightFromPivot = adsPivot.InverseTransformPoint(sightPoint.position);
            Vector3 holderToPivot = weaponHolder.InverseTransformPoint(adsPivot.position);
            Vector3 desiredPivotWorldPosition =
                cameraSightTarget.position - desiredHolderWorldRotation * sightFromPivot;
            Vector3 desiredHolderWorldPosition =
                desiredPivotWorldPosition - desiredHolderWorldRotation * holderToPivot;
            Vector3 desiredHolderLocalPosition =
                weaponHolder.parent.InverseTransformPoint(desiredHolderWorldPosition);

            weaponHolder.localPosition = Vector3.Lerp(
                HipTargetLocalPosition,
                desiredHolderLocalPosition,
                _adsBlend);
            weaponHolder.localRotation = Quaternion.Slerp(
                Quaternion.Euler(HipTargetLocalEuler),
                desiredHolderLocalRotation,
                _adsBlend);
        }

        /// <summary>
        /// 解析显式 ADS 轴和支点。缺失时必须显式记录，不要静默回到 SightPoint / weaponHolder 继续误用混合旋转。
        /// 并对「枪管沿 Y、AimAxis 却沿 Z」的旧错误装配做一次运行时纠正。
        /// </summary>
        void ResolveAimReferences()
        {
            if (adsAimAxis == null)
            {
                GameLog.Warn("WeaponADS", "adsAimAxis 未绑定，正在回退到 SightPoint 作为临时兼容；请在枪械层级中创建独立 AimAxis 节点。 ");
                adsAimAxis = sightPoint;
            }

            if (adsPivot == null)
            {
                GameLog.Warn("WeaponADS", "adsPivot 未绑定，正在回退到 weaponHolder 作为临时兼容；请在枪械层级中创建独立 AdsPivot 节点。 ");
                adsPivot = weaponHolder;
            }

            TryRepairLegacyAimAxis();
        }

        /// <summary>
        /// 旧 Setup 把 AimAxis 放在 local Z 且 rotation=identity，而本枪枪管实际沿 local Y。
        /// 运行时按握点重算一次，避免必须手动跑 Editor 菜单才能开镜。
        /// </summary>
        void TryRepairLegacyAimAxis()
        {
            if (adsAimAxis == null || weaponHolder == null || adsAimAxis.parent != weaponHolder)
            {
                return;
            }

            bool looksLegacy =
                adsAimAxis.localRotation == Quaternion.identity
                && Mathf.Abs(adsAimAxis.localPosition.z) > 0.2f
                && Mathf.Abs(adsAimAxis.localPosition.y) < 0.2f;
            if (!looksLegacy)
            {
                return;
            }

            Transform leftIk = leftHandIKTarget != null && leftHandIKTarget.parent == weaponHolder
                ? leftHandIKTarget
                : null;
            Transform rightIk = rightHandIKTarget != null && rightHandIKTarget.parent == weaponHolder
                ? rightHandIKTarget
                : null;

            Vector3 barrelDir = Vector3.up;
            Vector3 pivotLocal = adsPivot != null ? weaponHolder.InverseTransformPoint(adsPivot.position) : Vector3.zero;
            if (leftIk != null && rightIk != null)
            {
                Vector3 delta = leftIk.localPosition - rightIk.localPosition;
                if (delta.sqrMagnitude > 1e-6f)
                {
                    barrelDir = delta.normalized;
                }

                pivotLocal = rightIk.localPosition;
            }

            Vector3 gunUp = Vector3.forward;
            if (sightPoint != null && sightPoint.parent == weaponHolder)
            {
                Vector3 toSight = sightPoint.localPosition - pivotLocal;
                Vector3 projected = toSight - Vector3.Dot(toSight, barrelDir) * barrelDir;
                if (projected.sqrMagnitude > 1e-6f)
                {
                    gunUp = projected.normalized;
                }
            }

            adsAimAxis.localRotation = Quaternion.LookRotation(barrelDir, gunUp);
            adsAimAxis.localPosition = pivotLocal + barrelDir * 0.55f;
            GameLog.Warn(
                "WeaponADS",
                $"检测到旧版 AimAxis（沿 local Z），已按握点重定向到枪管方向 localPos={adsAimAxis.localPosition}。请重新 Apply Prefab 以固化。");
        }

        void UpdateAdsBlendAndFov()
        {
            float blendTarget = _isAiming ? 1f : 0f;
            _adsBlend = Mathf.MoveTowards(_adsBlend, blendTarget, adsSpeed * Time.deltaTime);
            AdsProgress = _adsBlend;

            if (playerCamera != null)
            {
                float targetFOV = _isAiming ? adsFOV : defaultFOV;
                playerCamera.fieldOfView = Mathf.Lerp(
                    playerCamera.fieldOfView,
                    targetFOV,
                    Time.deltaTime * adsSpeed);
            }
        }
    }
}
