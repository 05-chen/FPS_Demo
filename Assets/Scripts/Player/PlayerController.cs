using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Core;
using Player;
using Weapon;

/// <summary>
/// 1v1 FPS 玩家控制。
/// 联网后只有 Owner 读输入、开摄像机；阵营由服务器写入NetworkVariable。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerController : NetworkBehaviour
{
    static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    static readonly Color RedColor = new Color(0.85f, 0.15f, 0.15f);
    static readonly Color BlueColor = new Color(0.15f, 0.35f, 0.9f);

    [Header("视角组件（拖 PlayerCamera 上的组件）")]
    [SerializeField] Camera playerCamera;
    [SerializeField] AudioListener audioListener;

    [Header("移动")]
    [SerializeField] float walkSpeed = 4f;
    [SerializeField] float sprintSpeed = 7f;
    [SerializeField] float crouchSpeed = 2f;
    [SerializeField] [Range(0.5f, 1f)] float lightArmsSpeedMultiplier = 0.8f;
    [SerializeField] float jumpHeight = 1.2f;
    [SerializeField] float gravity = -20f;

    [Header("姿态")]
    // 必须与 CharacterController 的高度、以及 ModelRoot 的 -0.8 偏移保持一致（1.6）。
    // 写成 2 会把胶囊撑到 2m：底部落到 -1，而模型脚底固定在 -0.8，导致模型悬空 0.2m、摄像机扎进头部网格。
    [SerializeField] float standingHeight = 1.6f;
    [SerializeField] float crouchingHeight = 1f;
    [SerializeField] float standingCameraHeight = 1.6f;
    [SerializeField] float crouchingCameraHeight = 0.8f;
    [SerializeField] float proneHeight = 0.4f;
    [SerializeField] float proneCameraHeight = 0.2f;
    [SerializeField] float stanceChangeSpeed = 10f;

    [Header("视角")]
    [SerializeField] float mouseSensitivity = 2f;
    [SerializeField] [Range(0.1f, 1f)] float adsLookSensitivityMultiplier = 0.6f;
    [SerializeField] float minPitch = -80f;
    [SerializeField] float maxPitch = 80f;

    [Header("出生点")]
    [SerializeField] Vector3 redSpawnPosition = new Vector3(22f, 2.92f, 15.21f);
    [SerializeField] Vector3 blueSpawnPosition = new Vector3(30f, 2.92f, 15.21f);

    [Header("落地与虚空")]
    [SerializeField] float voidY = -15f;
    [SerializeField] float groundProbeUp = 4f;
    [SerializeField] float groundProbeDown = 30f;

    [SerializeField] PlayerWeapon playerWeapon;
    [SerializeField] WeaponADS weaponADS;

    public TeamId Team { get; private set; } = TeamId.None;

    public bool OccupiesTeam(TeamId team)
    {
        if (team == TeamId.None)
        {
            return false;
        }

        return Team == team || (IsSpawned && _syncedTeam.Value == (int)team);
    }

    /// <summary>
    /// 离线选阵营时使用。一旦物体被 NetworkObject.Spawn，就改看 IsOwner。
    /// </summary>
    public bool IsControlled { get; private set; }

    public bool IsCrouching { get; private set; }
    public bool IsSprinting { get; private set; }

    /// <summary>本帧本地平面移动输入（X=左右，Y=前后），供动画混合树读取。</summary>
    public Vector2 MoveInput => _moveInput;

    /// <summary>本帧是否按下跳跃，供动画 Trigger 使用；读完不会被消费。</summary>
    public bool JumpPressedThisFrame => _jumpPressedThisFrame;

    /// <summary>CharacterController 接地状态；控制器未就绪时视为未接地。</summary>
    public bool IsGrounded => _characterController != null && _characterController.isGrounded;

    /// <summary>本机第一人称摄像机；供动画管理器做仅位置跟随。</summary>
    public Camera PlayerCamera => playerCamera;

    /// <summary>由 PlayerAnimationManager 接管摄像机位置后置为 true，本类便不再写摄像机 Transform。</summary>
    public bool CameraPositionControlledExternally { get; set; }

    /// <summary>站姿对应的摄像机离脚底高度，供跟随脚本换算下蹲 / 趴下的高度补偿。</summary>
    public float DesiredCameraHeightFromFeet
    {
        get
        {
            ResolveStanceTargets(out _, out float cameraFromFeet);
            return cameraFromFeet;
        }
    }

    /// <summary>站姿基准摄像机高度，用于计算相对站立的姿态偏移量。</summary>
    public float StandingCameraHeight => standingCameraHeight;

    public event System.Action<TeamId> TeamConfirmed;
    public event System.Action<TeamId> TeamRejected;

    readonly NetworkVariable<int> _syncedTeam = new NetworkVariable<int>(
        (int)TeamId.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Owner 写入，供远端驱动 Animator（移动混合树）。</summary>
    readonly NetworkVariable<Vector2> _syncedAnimMove = new NetworkVariable<Vector2>(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    readonly NetworkVariable<bool> _syncedAnimGrounded = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    readonly NetworkVariable<bool> _syncedAnimAds = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    readonly NetworkVariable<byte> _syncedFireSeq = new NetworkVariable<byte>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    readonly NetworkVariable<byte> _syncedJumpSeq = new NetworkVariable<byte>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    readonly NetworkVariable<byte> _syncedReloadSeq = new NetworkVariable<byte>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public Vector2 SyncedAnimMove => IsOwner || !IsSpawned ? _moveInput : _syncedAnimMove.Value;
    public bool SyncedAnimGrounded => IsOwner || !IsSpawned ? IsGrounded : _syncedAnimGrounded.Value;
    public bool SyncedAnimAds => IsOwner || !IsSpawned
        ? (weaponADS != null && weaponADS.IsAiming)
        : _syncedAnimAds.Value;

    public NetworkVariable<byte> FireAnimSeq => _syncedFireSeq;
    public NetworkVariable<byte> JumpAnimSeq => _syncedJumpSeq;
    public NetworkVariable<byte> ReloadAnimSeq => _syncedReloadSeq;

    MaterialPropertyBlock _propertyBlock;

    CharacterController _characterController;
    CharacterMotor _motor;
    FirstPersonLook _look;
    NetworkTransform _networkTransform;
    ClientNetworkTransform _clientNetworkTransform;
    Renderer _meshRenderer;
    PlayerStatusController _statusController;
    PlayerHealth _playerHealth;
    bool _deadPresentation;
    bool _waitingForTeamAck;
    bool _sprintLatched;
    bool _wasMoving;
    Vector2 _moveInput;
    bool _jumpPressedThisFrame;
    /// <summary>最近一次由 SpawnManager / 选边 RPC 写入的出生位姿；虚空回收优先用它。</summary>
    Vector3 _lastSpawnPosition;
    Quaternion _lastSpawnRotation = Quaternion.identity;
    bool _hasLastSpawnPose;

    public static PlayerController FindLocalOwnedPlayer()
    {
        return PlayerRegistry.FindLocalOwned();
    }

    void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();
        SetupPhysics();
        CacheViewComponents();
        _networkTransform = GetComponent<NetworkTransform>();
        _clientNetworkTransform = GetComponent<ClientNetworkTransform>();
        _meshRenderer = GetComponent<Renderer>();
        _statusController = GetComponent<PlayerStatusController>();
        _playerHealth = GetComponent<PlayerHealth>();
        if (playerWeapon == null)
        {
            playerWeapon = GetComponent<PlayerWeapon>();
        }

        if (weaponADS == null)
        {
            weaponADS = GetComponent<WeaponADS>();
            if (weaponADS == null)
            {
                weaponADS = GetComponentInChildren<WeaponADS>(true);
            }
        }

        _motor = new CharacterMotor(_characterController);
        _look = new FirstPersonLook(transform, playerCamera != null ? playerCamera.transform : null);
        DetectTeamByName();
        ApplyTeamColor();
        SetViewEnabled(false);
    }

    void OnEnable()
    {
        PlayerRegistry.Register(this);
    }

    void OnDisable()
    {
        PlayerRegistry.Unregister(this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _syncedTeam.OnValueChanged += OnSyncedTeamChanged;
        ApplyTeamFromNet(_syncedTeam.Value);

        bool isLocalPlayer = IsOwner;
        if (_characterController != null)
        {
            // 战区只认 CharacterController。客户端在主机上 IsOwner=false，若关掉胶囊，蓝方进圈永远不计人数。
            _characterController.enabled = ShouldEnableCharacterController();
        }

        if (isLocalPlayer)
        {
            GameplayGate.Changed += OnGameplayGateChanged;
            SetViewEnabled(!GameplayGate.SuppressPlayerView);
            // OnNetworkSpawn 当下 NetworkTransform 可能还没进入可提交状态，延后一帧贴地
            StartCoroutine(SnapToGroundWhenReady());
        }
        else
        {
            SetViewEnabled(false);
        }
    }

    System.Collections.IEnumerator SnapToGroundWhenReady()
    {
        // 等 NetworkTransform 权威侧就绪，避免 Teleport 抛异常
        yield return null;
        if (!IsSpawned || !IsOwner || GameplayGate.IsBlocked)
        {
            yield break;
        }

        TeleportCharacter(SnapToGround(transform.position));
    }

    public override void OnNetworkDespawn()
    {
        _syncedTeam.OnValueChanged -= OnSyncedTeamChanged;
        GameplayGate.Changed -= OnGameplayGateChanged;
        SetViewEnabled(false);
        ResetWeaponAdsToHipfire();
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// 选阵营。联网时 Owner 发 ServerRpc，由服务器校验后再同步。
    /// </summary>
    public void ChooseTeam(TeamId team)
    {
        if (team == TeamId.None)
        {
            return;
        }

        if (!IsSpawned)
        {
            SetTeam(team);
            return;
        }

        if (!IsOwner || _waitingForTeamAck)
        {
            return;
        }

        _waitingForTeamAck = true;
        SetTeam(team);
        RequestTeamServerRpc((int)team);
    }

    [ServerRpc(RequireOwnership = true)]
    void RequestTeamServerRpc(int teamValue)
    {
        TeamId requested = TeamIdUtil.FromNetwork(teamValue);
        if (!TeamIdUtil.IsPlayable(requested))
        {
            RejectTeamClientRpc(teamValue, TargetOwner());
            return;
        }

        _syncedTeam.Value = (int)requested;
    }

    [ClientRpc]
    void RejectTeamClientRpc(int teamValue, ClientRpcParams clientRpcParams = default)
    {
        _waitingForTeamAck = false;
        SetTeam(TeamId.None);
        GameLog.Warn("Player", "阵营已被占用，请选另一方。");
        TeamRejected?.Invoke(TeamIdUtil.FromNetwork(teamValue));
    }

    void OnSyncedTeamChanged(int previous, int current)
    {
        ApplyTeamFromNet(current);

        if (!IsOwner || current == (int)TeamId.None)
        {
            return;
        }

        _waitingForTeamAck = false;
        // 出生点由 SpawnManager 区域 + NotifyPlayerSpawnedClientRpc 决定。
        // 禁止在此调用 MoveToSpawnPoint：预制体里的 red/blueSpawnPosition 是旧练习坐标，
        // 会在阵营 NV 晚于出生 RPC 到达时把双方都拽回红方一侧。
        TeamConfirmed?.Invoke(Team);
    }

    void ApplyTeamFromNet(int teamValue)
    {
        SetTeam(TeamIdUtil.FromNetwork(teamValue));
        gameObject.name = Team == TeamId.Red ? "Player_Red" : Team == TeamId.Blue ? "Player_Blue" : "Player";
    }

    /// <summary>
    /// 仅用于无 SpawnManager 的兜底（旧练习点）。正式对局请走 SpawnManager 区域。
    /// </summary>
    void MoveToSpawnPoint()
    {
        if (!TryResolveTeamSpawnPose(Team, out Vector3 spawn, out Quaternion rotation))
        {
            spawn = Team == TeamId.Blue ? blueSpawnPosition : redSpawnPosition;
            rotation = transform.rotation;
        }

        TeleportToSpawn(spawn, rotation);
    }

    void Update()
    {
        // 默认清零：任何提前 return 的分支都会让动画回到 Idle，避免卡在上一帧输入。
        _moveInput = Vector2.zero;
        _jumpPressedThisFrame = false;

        if (_deadPresentation || (_playerHealth != null && _playerHealth.IsDead))
        {
            ResetWeaponAdsToHipfire();
            return;
        }

        if (IsSpawned)
        {
            if (!IsOwner)
            {
                ResetWeaponAdsToHipfire();
                return;
            }

            if (GameplayGate.IsBlocked || PauseGate.IsPaused)
            {
                ResetWeaponAdsToHipfire();
                if (!GameplayGate.SuppressPlayerView)
                {
                    TickStance(Time.deltaTime);
                    TickVerticalOnly();
                    RecoverIfInVoid();
                }

                return;
            }
        }
        else if (!IsControlled || PauseGate.IsPaused)
        {
            ResetWeaponAdsToHipfire();
            if (PauseGate.IsPaused)
            {
                TickStance(Time.deltaTime);
                TickVerticalOnly();
            }

            return;
        }

        GameplayInputState input = GameplayInputState.Read();
        UpdateStanceState(input);
        TickStance(Time.deltaTime);

        bool lockMove = IsDownedInjury();
        if (!lockMove)
        {
            Vector2 cameraRecoil = playerWeapon != null ? playerWeapon.CameraRecoilOffset : Vector2.zero;
            _look.Tick(
                input.Look,
                mouseSensitivity,
                minPitch,
                maxPitch,
                ResolveLookSensitivityMultiplier(),
                cameraRecoil);
        }

        // 先更新相机，再计算 ADS 枪械姿态，避免瞄准点使用上一帧的视角。
        TickWeaponAds();

        Vector2 move = lockMove ? Vector2.zero : input.Move;
        bool jumpPressed = !lockMove && input.JumpPressed;
        // 缓存给 PlayerAnimationManager，不改变 Motor 原有调用方式。
        // Jump 触发器必须复用 Motor 的起跳条件（着地瞬间），否则空中连按会反复重播起跳动画。
        _moveInput = move;
        _jumpPressedThisFrame = jumpPressed && IsGrounded;
        _motor.Tick(move, jumpPressed, ResolveMoveSpeed(), jumpHeight, gravity, Time.deltaTime);
        HandleCursor(input);
        if (playerWeapon != null && !lockMove)
        {
            playerWeapon.PerformShoot(input);
        }

        RecoverIfInVoid();
    }

    void TickWeaponAds()
    {
        if (weaponADS == null)
        {
            return;
        }

        bool canAimInput = !IsSprinting && !IsDownedInjury();
        weaponADS.OnUpdate(canAimInput);
    }

    void ResetWeaponAdsToHipfire()
    {
        if (weaponADS != null)
        {
            weaponADS.ResetToHipfire();
        }
    }

    float ResolveLookSensitivityMultiplier()
    {
        if (weaponADS != null && weaponADS.IsAiming)
        {
            return adsLookSensitivityMultiplier;
        }

        return 1f;
    }

    void TickVerticalOnly()
    {
        if (_motor == null)
        {
            return;
        }

        _motor.Tick(Vector2.zero, false, ResolveMoveSpeed(), jumpHeight, gravity, Time.deltaTime);
    }

    void UpdateStanceState(in GameplayInputState input)
    {
        if (IsDownedInjury())
        {
            _sprintLatched = false;
            _wasMoving = false;
            IsCrouching = false;
            IsSprinting = false;
            return;
        }

        IsCrouching = input.CrouchHeld || IsCrippledInjury();

        if (IsCrouching)
        {
            _sprintLatched = false;
        }
        else if (input.SprintPressed)
        {
            _sprintLatched = true;
        }

        if (_wasMoving && !input.HasMoveInput)
        {
            _sprintLatched = false;
        }

        _wasMoving = input.HasMoveInput;
        IsSprinting = _sprintLatched && !IsCrouching && input.HasMoveInput;
    }

    InjuryState CurrentInjury =>
        StatusController != null ? StatusController.currentInjury : InjuryState.None;

    PlayerStatusController StatusController
    {
        get
        {
            if (_statusController == null)
            {
                _statusController = GetComponent<PlayerStatusController>();
            }

            return _statusController;
        }
    }

    bool IsCrippledInjury()
    {
        return CurrentInjury == InjuryState.Crippled_Legs;
    }

    bool IsDownedInjury()
    {
        InjuryState injury = CurrentInjury;
        return injury == InjuryState.DBNO_Torso || injury == InjuryState.InstanceDeath_Head;
    }

    float ResolveMoveSpeed()
    {
        float speed = walkSpeed;
        if (IsCrouching)
        {
            speed = crouchSpeed;
        }
        else if (IsSprinting)
        {
            speed = sprintSpeed;
        }

        if (CurrentInjury == InjuryState.Light_Arms)
        {
            speed *= lightArmsSpeedMultiplier;
        }

        return speed;
    }

    void TickStance(float deltaTime)
    {
        if (_characterController == null)
        {
            return;
        }

        float t = stanceChangeSpeed * deltaTime;
        ResolveStanceTargets(out float targetHeight, out _);
        ApplyCapsuleHeight(Mathf.Lerp(_characterController.height, targetHeight, t));
    }

    void LateUpdate()
    {
        SyncCameraHeight(Time.deltaTime);
    }

    /// <summary>
    /// 摄像机高度只在 LateUpdate 同步：此时 Update 里的 CharacterController.Move 已经执行完，
    /// 视角严格跟随胶囊体当帧的最终位置，不会出现「胶囊已移动、摄像机还在上一帧」的错位。
    /// 摄像机保持挂在 Player 根节点下作为子物体（不是 Head 骨骼的子物体），
    /// 只同步高度、不参与骨骼旋转，因此不会与头骨骼的动画轴向打架。
    ///
    /// 若 PlayerAnimationManager 正在做 Head 位置跟随，位置改由它统一接管，这里直接让位，避免两边互相覆盖。
    /// </summary>
    void SyncCameraHeight(float deltaTime)
    {
        if (playerCamera == null || CameraPositionControlledExternally)
        {
            return;
        }

        ResolveStanceTargets(out _, out float cameraFromFeet);
        Vector3 localPos = playerCamera.transform.localPosition;
        localPos.y = Mathf.Lerp(localPos.y, cameraFromFeet - standingHeight * 0.5f, stanceChangeSpeed * deltaTime);
        playerCamera.transform.localPosition = localPos;
    }

    void ResolveStanceTargets(out float targetHeight, out float cameraFromFeet)
    {
        if (IsDownedInjury())
        {
            targetHeight = proneHeight;
            cameraFromFeet = proneCameraHeight;
            return;
        }

        if (IsCrouching || IsCrippledInjury())
        {
            targetHeight = crouchingHeight;
            cameraFromFeet = crouchingCameraHeight;
            return;
        }

        targetHeight = standingHeight;
        cameraFromFeet = standingCameraHeight;
    }

    /// <summary>
    /// Transform 在胶囊中心，不是脚底。高度变化时只改 center，让底部始终停在 -standingHeight/2。
    /// 若写成 center.y = height/2，碰撞体会整体上移半个身高。
    /// </summary>
    void ApplyCapsuleHeight(float height)
    {
        const float defaultRadius = 0.5f;
        const float defaultStepOffset = 0.3f;

        _characterController.height = height;
        _characterController.radius = Mathf.Min(defaultRadius, height * 0.5f);
        _characterController.center = new Vector3(0f, (height - standingHeight) * 0.5f, 0f);
        _characterController.stepOffset = Mathf.Min(defaultStepOffset, height * 0.5f);
    }

    public void TeleportToSpawn(Vector3 position, Quaternion rotation)
    {
        Vector3 grounded = SnapToGround(position);
        _lastSpawnPosition = grounded;
        _lastSpawnRotation = rotation;
        _hasLastSpawnPose = true;

        bool wasEnabled = _characterController != null && _characterController.enabled;
        if (_characterController != null)
        {
            _characterController.enabled = false;
        }

        if (_clientNetworkTransform != null)
        {
            _clientNetworkTransform.TeleportToSpawn(grounded, rotation);
        }
        else
        {
            transform.SetPositionAndRotation(grounded, rotation);
            if (_networkTransform != null && IsSpawned && _networkTransform.CanCommitToTransform)
            {
                _networkTransform.Teleport(grounded, rotation, transform.localScale);
            }
        }

        _motor?.ResetVertical();
        if (_characterController != null)
        {
            _characterController.enabled = wasEnabled;
        }
    }

    /// <summary>
    /// Owner 用胶囊走路；Server 上也要开着所有人的胶囊，占领圈才能数到远端玩家。
    /// 纯客户端上的远端玩家关掉，避免本地物理乱推。
    /// </summary>
    bool ShouldEnableCharacterController()
    {
        if (_deadPresentation || (_playerHealth != null && _playerHealth.IsDead))
        {
            return false;
        }

        if (!IsSpawned)
        {
            return true;
        }

        return IsOwner || IsServer;
    }

    public void SetDeadPresentation(bool dead)
    {
        _deadPresentation = dead;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = !dead;
            }
        }

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                colliders[i].enabled = !dead;
            }
        }

        if (_characterController != null)
        {
            _characterController.enabled = ShouldEnableCharacterController();
        }

        if (dead)
        {
            ResetWeaponAdsToHipfire();
        }
        else if (_meshRenderer != null)
        {
            ApplyTeamColor();
        }
    }

    void RecoverIfInVoid()
    {
        if (_deadPresentation || (_playerHealth != null && _playerHealth.IsDead))
        {
            return;
        }

        if (transform.position.y > voidY)
        {
            return;
        }

        GameLog.Warn("Player", "检测到掉入虚空，拉回出生点。");
        if (!TryResolveTeamSpawnPose(ResolveTeam(), out Vector3 spawn, out Quaternion rotation))
        {
            spawn = _hasLastSpawnPose
                ? _lastSpawnPosition
                : (Team == TeamId.Blue ? blueSpawnPosition : redSpawnPosition);
            rotation = _hasLastSpawnPose ? _lastSpawnRotation : transform.rotation;
        }

        TeleportToSpawn(spawn, rotation);
    }

    void TeleportCharacter(Vector3 position)
    {
        bool wasEnabled = _characterController != null && _characterController.enabled;
        if (_characterController != null)
        {
            _characterController.enabled = false;
        }

        transform.position = position;
        _motor?.ResetVertical();

        if (_characterController != null)
        {
            _characterController.enabled = wasEnabled;
        }

        // ClientNetworkTransform 是拥有者权威：只能在可提交的一侧调用 Teleport
        if (_networkTransform == null || !IsSpawned || !_networkTransform.CanCommitToTransform)
        {
            return;
        }

        _networkTransform.Teleport(position, transform.rotation, transform.localScale);
    }

    Vector3 SnapToGround(Vector3 position)
    {
        Vector3 origin = position + Vector3.up * groundProbeUp;
        float distance = groundProbeUp + groundProbeDown;
        int mask = ~(1 << gameObject.layer);

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
        {
            return position;
        }

        float extra = 1f;
        if (_characterController != null)
        {
            float scaleY = transform.lossyScale.y;
            float bottomOffset =
                (_characterController.center.y - _characterController.height * 0.5f) * scaleY;
            extra = -bottomOffset + _characterController.skinWidth * scaleY;
        }

        return new Vector3(position.x, hit.point.y + extra, position.z);
    }

    public void SetTeam(TeamId team)
    {
        Team = team;
        ApplyTeamColor();
    }

    /// <summary>
    /// 服务器写入网络阵营，供死亡后自动复活读取。
    /// </summary>
    public void PersistTeam(TeamId team)
    {
        SetTeam(team);
        if (IsSpawned && IsServer)
        {
            _syncedTeam.Value = (int)team;
        }
    }

    /// <summary>
    /// 解析已同步的可玩阵营。未选择时返回 None。
    /// </summary>
    public TeamId ResolveTeam()
    {
        if (TeamIdUtil.IsPlayable(Team))
        {
            return Team;
        }

        if (IsSpawned)
        {
            return TeamIdUtil.FromNetwork(_syncedTeam.Value);
        }

        return TeamId.None;
    }

    public bool HasChosenFaction()
    {
        return TeamIdUtil.IsPlayable(ResolveTeam());
    }

    public void TeleportToTeamSpawn(TeamId team)
    {
        TeamId resolved = TeamIdUtil.IsPlayable(team) ? team : ResolveTeam();
        if (!TryResolveTeamSpawnPose(resolved, out Vector3 spawn, out Quaternion rotation))
        {
            spawn = resolved == TeamId.Blue ? blueSpawnPosition : redSpawnPosition;
            rotation = transform.rotation;
        }

        TeleportToSpawn(spawn, rotation);
    }

    /// <summary>
    /// 优先向场景 SpawnManager 要阵营区域点；没有区域时再退回缓存 / 预制体旧坐标。
    /// </summary>
    bool TryResolveTeamSpawnPose(TeamId team, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (!TeamIdUtil.IsPlayable(team))
        {
            return false;
        }

        if (Managers.SpawnManager.Instance != null &&
            Managers.SpawnManager.Instance.TryGetSpawnPose(team, out position, out rotation))
        {
            return true;
        }

        if (_hasLastSpawnPose && ResolveTeam() == team)
        {
            position = _lastSpawnPosition;
            rotation = _lastSpawnRotation;
            return true;
        }

        return false;
    }

    public void SetControlled(bool controlled)
    {
        IsControlled = controlled;

        if (IsSpawned)
        {
            return;
        }

        SetViewEnabled(controlled);

        if (controlled)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            ResetWeaponAdsToHipfire();
        }
    }

    /// <summary>Owner 每帧把动画相关状态写入 NetworkVariable，远端据此播动作。</summary>
    public void PublishAnimationState(Vector2 move, bool grounded, bool isAds)
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        if (_syncedAnimMove.Value != move)
        {
            _syncedAnimMove.Value = move;
        }

        if (_syncedAnimGrounded.Value != grounded)
        {
            _syncedAnimGrounded.Value = grounded;
        }

        if (_syncedAnimAds.Value != isAds)
        {
            _syncedAnimAds.Value = isAds;
        }
    }

    public void PublishFireAnimation()
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        _syncedFireSeq.Value++;
    }

    public void PublishJumpAnimation()
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        _syncedJumpSeq.Value++;
    }

    public void PublishReloadAnimation()
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        _syncedReloadSeq.Value++;
    }

    void OnGameplayGateChanged(bool blocked)
    {
        if (IsSpawned && IsOwner)
        {
            SetViewEnabled(!GameplayGate.SuppressPlayerView);
        }
    }

    void SetupPhysics()
    {
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            capsule.enabled = false;
        }

        var body = GetComponent<Rigidbody>();
        if (body != null)
        {
            Destroy(body);
        }

        _characterController = GetComponent<CharacterController>();
        if (_characterController == null)
        {
            _characterController = gameObject.AddComponent<CharacterController>();
        }

        _characterController.radius = 0.5f;
        ApplyCapsuleHeight(standingHeight);
        _characterController.slopeLimit = 45f;
        _characterController.stepOffset = 0.3f;
    }

    void CacheViewComponents()
    {
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }

        if (audioListener == null && playerCamera != null)
        {
            audioListener = playerCamera.GetComponent<AudioListener>();
        }

        if (playerCamera != null)
        {
            playerCamera.nearClipPlane = 0.1f;
            playerCamera.fieldOfView = 70f;
            Vector3 localPos = playerCamera.transform.localPosition;
            localPos.y = standingCameraHeight - standingHeight * 0.5f;
            playerCamera.transform.localPosition = localPos;
        }
    }

    void SetViewEnabled(bool enabled)
    {
        bool isLocalView = enabled && (!IsSpawned || IsOwner);

        if (playerCamera != null)
        {
            playerCamera.enabled = isLocalView;
            if (isLocalView)
            {
                AudioSource muzzleSource = playerCamera.GetComponent<AudioSource>();
                if (muzzleSource == null)
                {
                    muzzleSource = playerCamera.gameObject.AddComponent<AudioSource>();
                }

                muzzleSource.playOnAwake = false;
                muzzleSource.mute = false;
                muzzleSource.loop = false;
                muzzleSource.spatialBlend = 0f;
                muzzleSource.volume = 1f;
                muzzleSource.dopplerLevel = 0f;
                muzzleSource.ignoreListenerPause = true;
            }
        }

        if (audioListener == null && playerCamera != null)
        {
            audioListener = playerCamera.GetComponent<AudioListener>();
        }

        AudioListener[] childListeners = GetComponentsInChildren<AudioListener>(true);
        if (!isLocalView)
        {
            if (audioListener != null)
            {
                audioListener.enabled = false;
            }

            for (int i = 0; i < childListeners.Length; i++)
            {
                if (childListeners[i] != null)
                {
                    childListeners[i].enabled = false;
                }
            }

            return;
        }

        if (audioListener == null)
        {
            return;
        }

        RuntimeUiFactory.SetExclusiveAudioListener(audioListener);
    }

    void DetectTeamByName()
    {
        if (name.Equals("RED", System.StringComparison.OrdinalIgnoreCase))
        {
            Team = TeamId.Red;
        }
        else if (name.Equals("BLUE", System.StringComparison.OrdinalIgnoreCase))
        {
            Team = TeamId.Blue;
        }
    }

    void ApplyTeamColor()
    {
        if (_meshRenderer == null || _propertyBlock == null)
        {
            return;
        }

        Color color = Team == TeamId.Red ? RedColor : Team == TeamId.Blue ? BlueColor : _meshRenderer.sharedMaterial != null ? _meshRenderer.sharedMaterial.color : Color.white;
        _meshRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(ColorPropertyId, color);
        _meshRenderer.SetPropertyBlock(_propertyBlock);
    }

    void HandleCursor(in GameplayInputState input)
    {
        if (PauseGate.IsPaused)
        {
            return;
        }

        if (input.RelockCursor && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    ClientRpcParams TargetOwner()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };
    }
}
