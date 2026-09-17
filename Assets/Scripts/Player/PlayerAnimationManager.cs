using UnityEngine;
using Weapon;

/// <summary>
/// 独立驱动 Animator，并在 LateUpdate 做「仅位置」的头部摄像机跟随。
/// 摄像机保持挂在 Player 根节点下：旋转完全交给 FirstPersonLook（鼠标），
/// 位置贴到 Head 骨骼，隔离跑动时头部骨骼的 3D 晃动，避免看到后脑勺。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerAnimationManager : MonoBehaviour
{
    static readonly int InputXHash = Animator.StringToHash("InputX");
    static readonly int InputYHash = Animator.StringToHash("InputY");
    static readonly int IsGroundHash = Animator.StringToHash("IsGround");
    static readonly int IsAdsHash = Animator.StringToHash("IsADS");
    static readonly int FireHash = Animator.StringToHash("Fire");
    static readonly int ReloadHash = Animator.StringToHash("Reload");
    static readonly int JumpHash = Animator.StringToHash("Jump");

    const float MoveDampTime = 0.1f;
    const int MaxBoneSearchAttempts = 30;

    [Header("核心引用（留空自动查找）")]
    [SerializeField] Animator animator;
    [SerializeField] PlayerController playerController;
    [SerializeField] PlayerWeapon playerWeapon;
    [SerializeField] WeaponADS weaponADS;
    [SerializeField] Transform playerCamera;
    [SerializeField] Transform headBone;

    [Header("摄像机仅位置跟随")]
    [Tooltip("开启后用当前摄像机站位反推相对 Head 的偏移，保留预制体里调好的眼位")]
    [SerializeField] bool autoCalibrateEyeOffset = true;
    [SerializeField] Vector3 eyeOffset = new Vector3(0f, 0.08f, 0.1f);
    [Tooltip("0 = 刚性跟随。略大于 0 可抹平头部高频抖动，但过大又会在急跑时短暂露后脑")]
    [SerializeField] float positionDamping;

    bool _subscribed;
    bool _eyeOffsetReady;
    Vector3 _calibratedEyeOffset;
    int _boneSearchAttempts;
    bool _cameraParentWarned;

    void Reset()
    {
        AutoBind();
    }

    void Awake()
    {
        AutoBind();
        EnforceNoRootMotion();
        EnsureCameraUnderPlayerRoot();
    }

    void OnEnable()
    {
        SubscribeWeapon();
        EnforceNoRootMotion();
        if (playerController != null)
        {
            // 明确接管摄像机位置，避免与 PlayerController.SyncCameraHeight 互相覆盖。
            playerController.CameraPositionControlledExternally = true;
        }
    }

    void OnDisable()
    {
        UnsubscribeWeapon();
        if (playerController != null)
        {
            playerController.CameraPositionControlledExternally = false;
        }
    }

    void Start()
    {
        ResolveHeadBone();
        CalibrateEyeOffset();
    }

    /// <summary>
    /// 运行时兜底关闭根位移：Animator 上的 Apply Root Motion 可能因 Prefab Override 未 Apply 而在实例上残留为 true，
    /// 一旦开启，动画自身位移会把渲染模型从 CharacterController 胶囊上拽走。
    /// </summary>
    void EnforceNoRootMotion()
    {
        if (animator == null || !animator.applyRootMotion)
        {
            return;
        }

        animator.applyRootMotion = false;
        GameLog.Warn("Anim", $"{animator.gameObject.name} 的 Apply Root Motion 被强制关闭：根位移会与 CharacterController 位置冲突。");
    }

    void LateUpdate()
    {
        if (playerController == null || !ShouldDriveLocalPresentation())
        {
            return;
        }

        SyncAnimatorParameters();
        SyncCameraPositionToHead();
    }

    /// <summary>
    /// 把移动 / 接地 / ADS / Jump 写进 Animator。
    /// IsADS 只读 WeaponADS 的右键状态，开火路径绝不改写它，保证 Fire 与 ADS 可并行。
    /// </summary>
    void SyncAnimatorParameters()
    {
        if (animator == null)
        {
            return;
        }

        Vector2 move = playerController.MoveInput;
        if (move.sqrMagnitude > 1f)
        {
            move.Normalize();
        }

        animator.SetFloat(InputXHash, move.x, MoveDampTime, Time.deltaTime);
        animator.SetFloat(InputYHash, move.y, MoveDampTime, Time.deltaTime);
        animator.SetBool(IsGroundHash, playerController.IsGrounded);

        // 显式写 false，避免 weaponADS 空引用时参数残留上一帧的 true。
        bool isAiming = weaponADS != null && weaponADS.IsAiming;
        animator.SetBool(IsAdsHash, isAiming);

        if (playerController.JumpPressedThisFrame)
        {
            animator.SetTrigger(JumpHash);
        }
    }

    /// <summary>
    /// 仅同步摄像机世界坐标到 Head；旋转完全不动，留给 FirstPersonLook。
    /// </summary>
    void SyncCameraPositionToHead()
    {
        if (playerCamera == null)
        {
            return;
        }

        EnsureCameraUnderPlayerRoot();
        ResolveHeadBone();
        if (headBone == null)
        {
            return;
        }

        if (!_eyeOffsetReady)
        {
            CalibrateEyeOffset();
            if (!_eyeOffsetReady)
            {
                return;
            }
        }

        Transform root = playerController.transform;
        Vector3 headLocal = root.InverseTransformPoint(headBone.position);
        Vector3 targetLocal = headLocal + _calibratedEyeOffset;

        // 下蹲 / 趴下没有对应骨骼动画，用站姿高度差补摄像机下沉。
        float stanceDelta = playerController.DesiredCameraHeightFromFeet
            - playerController.StandingCameraHeight;
        targetLocal += Vector3.up * stanceDelta;

        Vector3 targetWorld = root.TransformPoint(targetLocal);
        playerCamera.position = positionDamping > 0f
            ? Vector3.Lerp(playerCamera.position, targetWorld, 1f - Mathf.Exp(-positionDamping * Time.deltaTime))
            : targetWorld;
    }

    /// <summary>
    /// 供外部轻量调用：只拉 Fire Trigger，绝不读写 IsADS。
    /// 机瞄开火依赖 Controller 里的 ADS → Fire → ADS 连线，代码侧保持解耦即可。
    /// </summary>
    public void OnFireTriggered()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetTrigger(FireHash);
    }

    /// <summary>供外部轻量调用：拉起 UpperBody 层的 Reload 触发器。</summary>
    public void OnReloadTriggered()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetTrigger(ReloadHash);
    }

    void AutoBind()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }

        if (playerWeapon == null)
        {
            playerWeapon = GetComponent<PlayerWeapon>();
            if (playerWeapon == null)
            {
                playerWeapon = GetComponentInChildren<PlayerWeapon>(true);
            }
        }

        if (weaponADS == null)
        {
            weaponADS = GetComponent<WeaponADS>();
            if (weaponADS == null)
            {
                weaponADS = GetComponentInChildren<WeaponADS>(true);
            }
        }

        if (playerCamera == null && playerController != null)
        {
            Camera cam = playerController.PlayerCamera;
            if (cam != null)
            {
                playerCamera = cam.transform;
            }
        }

        if (playerCamera == null)
        {
            Transform named = transform.Find("PlayerCamera");
            if (named != null)
            {
                playerCamera = named;
            }
            else
            {
                Camera fallback = GetComponentInChildren<Camera>(true);
                if (fallback != null)
                {
                    playerCamera = fallback.transform;
                }
            }
        }
    }

    /// <summary>
    /// 强制摄像机挂在 Player 根下。若被人误挂到 Head 骨骼，会继承骨骼旋转导致镜头剧烈晃动，这里纠正回来。
    /// </summary>
    void EnsureCameraUnderPlayerRoot()
    {
        if (playerCamera == null || playerController == null)
        {
            return;
        }

        Transform root = playerController.transform;
        if (playerCamera.parent == root)
        {
            return;
        }

        Transform previousParent = playerCamera.parent;
        Vector3 worldPos = playerCamera.position;
        Quaternion worldRot = playerCamera.rotation;
        playerCamera.SetParent(root, true);
        playerCamera.SetPositionAndRotation(worldPos, worldRot);

        if (!_cameraParentWarned)
        {
            _cameraParentWarned = true;
            GameLog.Warn("Anim",
                $"{playerCamera.name} 已从 {(previousParent != null ? previousParent.name : "null")} 挪回 Player 根节点：摄像机只跟位置，不能挂在 Head 骨骼下。");
        }

        // 父级变了，相对 Head 的偏移需要重新标定。
        _eyeOffsetReady = false;
    }

    void ResolveHeadBone()
    {
        if (headBone != null)
        {
            return;
        }

        if (animator != null && animator.isHuman)
        {
            headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headBone != null)
            {
                return;
            }
        }

        if (_boneSearchAttempts >= MaxBoneSearchAttempts)
        {
            return;
        }

        _boneSearchAttempts++;
        Transform best = null;
        foreach (Transform candidate in transform.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name.IndexOf("head", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            // 避开 HeadTop_End：真头骨名字更短（mixamorig:Head）。
            if (best == null || candidate.name.Length < best.name.Length)
            {
                best = candidate;
            }
        }

        headBone = best;
        if (headBone == null && _boneSearchAttempts >= MaxBoneSearchAttempts)
        {
            GameLog.Warn("Anim", "找不到 Head 骨骼，摄像机位置跟随失效。请确认模型是 Humanoid，或手动指定 Head Bone。");
        }
    }

    /// <summary>
    /// 用当前摄像机站位反推相对 Head 的本地偏移，保留预制体里调好的眼位。
    /// </summary>
    void CalibrateEyeOffset()
    {
        if (!autoCalibrateEyeOffset)
        {
            _calibratedEyeOffset = eyeOffset;
            _eyeOffsetReady = true;
            return;
        }

        if (playerController == null || playerCamera == null || headBone == null)
        {
            return;
        }

        Transform root = playerController.transform;
        _calibratedEyeOffset = root.InverseTransformPoint(playerCamera.position)
            - root.InverseTransformPoint(headBone.position);
        _eyeOffsetReady = true;
    }

    /// <summary>仅本机操控端驱动动画与摄像机，避免远端抢写。</summary>
    bool ShouldDriveLocalPresentation()
    {
        if (playerController.IsSpawned)
        {
            return playerController.IsOwner;
        }

        return playerController.IsControlled;
    }

    void SubscribeWeapon()
    {
        if (_subscribed || playerWeapon == null)
        {
            return;
        }

        playerWeapon.Fired += OnFireTriggered;
        playerWeapon.Reloaded += OnReloadTriggered;
        _subscribed = true;
    }

    void UnsubscribeWeapon()
    {
        if (!_subscribed || playerWeapon == null)
        {
            return;
        }

        playerWeapon.Fired -= OnFireTriggered;
        playerWeapon.Reloaded -= OnReloadTriggered;
        _subscribed = false;
    }
}
