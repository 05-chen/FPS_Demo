using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Core;
using Player;
using UI.Runtime;
using Weapon;

/// <summary>
/// 1v1 FPS 玩家控制协调器。
/// 联网后只有 Owner 读输入、开摄像机；阵营由服务器写入 NetworkVariable。
/// 移动 / 视角 / 姿态 / 出生 / 动画同步委托子模块，本类保留权限判断与对外 API。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerController : NetworkBehaviour
{
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

    public bool IsCrouching => _stance != null && _stance.IsCrouching;
    public bool IsSprinting => _stance != null && _stance.IsSprinting;

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
    public float DesiredCameraHeightFromFeet =>
        _stance != null ? _stance.DesiredCameraHeightFromFeet(CurrentInjury) : standingCameraHeight;

    /// <summary>站姿基准摄像机高度，用于计算相对站立的姿态偏移量。</summary>
    public float StandingCameraHeight => standingCameraHeight;

    public event System.Action<TeamId> TeamConfirmed;
    public event System.Action<TeamId> TeamRejected;

    readonly NetworkVariable<int> _syncedTeam = new NetworkVariable<int>(
        (int)TeamId.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

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

    CharacterController _characterController;
    CharacterMotor _motor;
    FirstPersonLook _look;
    PlayerStanceController _stance;
    PlayerSpawnState _spawn;
    PlayerTeamVisual _teamVisual;
    PlayerAnimationSync _animSync;
    NetworkTransform _networkTransform;
    ClientNetworkTransform _clientNetworkTransform;
    PlayerStatusController _statusController;
    PlayerHealth _playerHealth;
    bool _deadPresentation;
    bool _waitingForTeamAck;
    Vector2 _moveInput;
    bool _jumpPressedThisFrame;

    public static PlayerController FindLocalOwnedPlayer()
    {
        return PlayerRegistry.FindLocalOwned();
    }

    void Awake()
    {
        SetupPhysics();
        CacheViewComponents();
        _networkTransform = GetComponent<NetworkTransform>();
        _clientNetworkTransform = GetComponent<ClientNetworkTransform>();
        Renderer meshRenderer = GetComponent<Renderer>();
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
        _stance = new PlayerStanceController(
            _characterController,
            playerCamera,
            new PlayerStanceController.Settings
            {
                StandingHeight = standingHeight,
                CrouchingHeight = crouchingHeight,
                ProneHeight = proneHeight,
                StandingCameraHeight = standingCameraHeight,
                CrouchingCameraHeight = crouchingCameraHeight,
                ProneCameraHeight = proneCameraHeight,
                StanceChangeSpeed = stanceChangeSpeed
            });
        _spawn = new PlayerSpawnState(
            transform,
            _characterController,
            _motor,
            _networkTransform,
            _clientNetworkTransform,
            voidY,
            groundProbeUp,
            groundProbeDown,
            redSpawnPosition,
            blueSpawnPosition);
        _teamVisual = new PlayerTeamVisual(meshRenderer);
        _animSync = new PlayerAnimationSync(
            _syncedAnimMove,
            _syncedAnimGrounded,
            _syncedAnimAds,
            _syncedFireSeq,
            _syncedJumpSeq,
            _syncedReloadSeq);

        _stance.ApplyCapsuleHeight(standingHeight);

        TeamId detected = PlayerTeamVisual.DetectTeamByName(name);
        if (detected != TeamId.None)
        {
            Team = detected;
        }

        _teamVisual.ApplyTeamColor(Team);
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

        if (_characterController != null)
        {
            _characterController.enabled = ShouldEnableCharacterController();
        }

        if (IsOwner)
        {
            GameplayGate.Changed += OnGameplayGateChanged;
            SetViewEnabled(!GameplayGate.SuppressPlayerView);
            StartCoroutine(SnapToGroundWhenReady());
        }
        else
        {
            SetViewEnabled(false);
        }
    }

    System.Collections.IEnumerator SnapToGroundWhenReady()
    {
        yield return null;
        if (!IsSpawned || !IsOwner || GameplayGate.IsBlocked || _spawn == null)
        {
            yield break;
        }

        _spawn.TeleportCharacter(_spawn.SnapToGround(transform.position), IsSpawned);
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
        GameLog.Warn("Player", "非法阵营，已拒绝。");
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
        TeamConfirmed?.Invoke(Team);
    }

    void ApplyTeamFromNet(int teamValue)
    {
        SetTeam(TeamIdUtil.FromNetwork(teamValue));
        gameObject.name = PlayerTeamVisual.DisplayObjectName(Team);
    }

    void Update()
    {
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
                    _stance?.TickCapsule(Time.deltaTime, CurrentInjury);
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
                _stance?.TickCapsule(Time.deltaTime, CurrentInjury);
                TickVerticalOnly();
            }

            return;
        }

        GameplayInputState input = PlayerInputReader.Read();
        _stance?.UpdateFromInput(input, CurrentInjury);
        _stance?.TickCapsule(Time.deltaTime, CurrentInjury);

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

        TickWeaponAds();

        Vector2 move = lockMove ? Vector2.zero : input.Move;
        bool jumpPressed = !lockMove && input.JumpPressed;
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

    void LateUpdate()
    {
        _stance?.SyncCameraHeight(Time.deltaTime, CurrentInjury, CameraPositionControlledExternally);
    }

    public void TeleportToSpawn(Vector3 position, Quaternion rotation)
    {
        _spawn?.TeleportToSpawn(position, rotation, IsSpawned);
    }

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
        else
        {
            _teamVisual?.ApplyTeamColor(Team);
        }
    }

    void RecoverIfInVoid()
    {
        bool isDead = _deadPresentation || (_playerHealth != null && _playerHealth.IsDead);
        if (_spawn == null || !_spawn.ShouldRecoverFromVoid(isDead))
        {
            return;
        }

        GameLog.Warn("Player", "检测到掉入虚空，拉回出生点。");
        _spawn.ResolveVoidRecoveryPose(Team, ResolveTeam(), out Vector3 spawn, out Quaternion rotation);
        TeleportToSpawn(spawn, rotation);
    }

    public void SetTeam(TeamId team)
    {
        Team = team;
        _teamVisual?.ApplyTeamColor(team);
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
        if (_spawn == null
            || !_spawn.TryResolveTeamSpawnPose(resolved, ResolveTeam(), out Vector3 spawn, out Quaternion rotation))
        {
            if (_spawn != null)
            {
                _spawn.GetPrefabFallbackSpawn(resolved, out spawn, out rotation);
            }
            else
            {
                spawn = resolved == TeamId.Blue ? blueSpawnPosition : redSpawnPosition;
                rotation = transform.rotation;
            }
        }

        TeleportToSpawn(spawn, rotation);
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
        _animSync?.PublishState(IsSpawned, IsOwner, move, grounded, isAds);
    }

    public void PublishFireAnimation()
    {
        _animSync?.PublishFire(IsSpawned, IsOwner);
    }

    public void PublishJumpAnimation()
    {
        _animSync?.PublishJump(IsSpawned, IsOwner);
    }

    public void PublishReloadAnimation()
    {
        _animSync?.PublishReload(IsSpawned, IsOwner);
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
        // 临时高度；Awake 后 _stance.ApplyCapsuleHeight 会再设一次
        _characterController.height = standingHeight;
        _characterController.center = Vector3.zero;
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
