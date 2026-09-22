using UnityEngine;
using Unity.Netcode;
using System;
using System.Collections;
using Core;
using Enemy;

public enum PlayerLifeState
{
    Alive = 0,
    Downed = 1,
    Dead = 2
}

public class PlayerHealth : NetworkBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private int maxHealth = 100;

    [Header("生命周期")]
    [SerializeField] float bleedOutSeconds = 15f;
    [SerializeField] float deadRespawnDelay = 5f;

    public NetworkVariable<int> currentHealth = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    readonly NetworkVariable<int> _lifeState = new NetworkVariable<int>(
        (int)PlayerLifeState.Alive,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>0 表示尚未选择（TeamId.None）。</summary>
    readonly NetworkVariable<int> _savedSpawnTeamId = new NetworkVariable<int>(
        (int)TeamId.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    readonly NetworkVariable<int> _networkInjury = new NetworkVariable<int>(
        (int)InjuryState.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public event Action<int, int> OnHealthChanged;
    public event Action OnDeath;

    public int MaxHealth => maxHealth;
    public PlayerLifeState LifeState => (PlayerLifeState)_lifeState.Value;
    public bool IsDead => LifeState == PlayerLifeState.Dead;
    public bool IsDowned => LifeState == PlayerLifeState.Downed;

    PlayerController _playerController;
    Player.PlayerStatusController _statusController;
    Coroutine _bleedRoutine;
    Coroutine _respawnRoutine;

    void Awake()
    {
        _playerController = GetComponent<PlayerController>();
        _statusController = GetComponent<Player.PlayerStatusController>();
    }

    public override void OnNetworkSpawn()
    {
        currentHealth.OnValueChanged += HandleHealthChanged;
        _lifeState.OnValueChanged += HandleLifeStateChanged;
        _networkInjury.OnValueChanged += HandleNetworkInjuryChanged;

        if (IsServer)
        {
            if (_lifeState.Value == (int)PlayerLifeState.Alive)
            {
                currentHealth.Value = maxHealth;
            }
        }

        HandleLifeStateChanged(_lifeState.Value, _lifeState.Value);
        HandleNetworkInjuryChanged(_networkInjury.Value, _networkInjury.Value);
        ApplyPresentation(IsDead);
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= HandleHealthChanged;
        _lifeState.OnValueChanged -= HandleLifeStateChanged;
        _networkInjury.OnValueChanged -= HandleNetworkInjuryChanged;
        StopBleedOut();
        StopRespawn();
    }

    void HandleHealthChanged(int previousValue, int newValue)
    {
        OnHealthChanged?.Invoke(newValue, maxHealth);
    }

    void HandleLifeStateChanged(int previousValue, int newValue)
    {
        PlayerLifeState state = (PlayerLifeState)newValue;
        ApplyPresentation(state == PlayerLifeState.Dead);

        if (state == PlayerLifeState.Dead && previousValue != (int)PlayerLifeState.Dead)
        {
            OnDeath?.Invoke();
        }

        if (!IsOwner && IsSpawned)
        {
            _statusController?.SetInjuryFromLifeState(ToInjury(state));
            return;
        }

        ApplyOwnerLifeUi(state);
    }

    void HandleNetworkInjuryChanged(int previousValue, int newValue)
    {
        InjuryState injury = (InjuryState)newValue;
        if (injury == InjuryState.None || LifeState != PlayerLifeState.Alive)
        {
            return;
        }

        _statusController?.SetInjuryFromLifeState(injury);
    }

    static InjuryState ToInjury(PlayerLifeState state)
    {
        switch (state)
        {
            case PlayerLifeState.Downed:
                return InjuryState.DBNO_Torso;
            case PlayerLifeState.Dead:
                return InjuryState.InstanceDeath_Head;
            default:
                return InjuryState.None;
        }
    }

    void ApplyOwnerLifeUi(PlayerLifeState state)
    {
        switch (state)
        {
            case PlayerLifeState.Downed:
                _statusController?.SetInjuryFromLifeState(InjuryState.DBNO_Torso);
                UI.CombatStatusUI.EnsureInstance().ShowDowned(bleedOutSeconds);
                GameplayGate.BlockInputOnly();
                break;
            case PlayerLifeState.Dead:
                _statusController?.SetInjuryFromLifeState(InjuryState.InstanceDeath_Head);
                UI.CombatStatusUI.EnsureInstance().ShowDead(deadRespawnDelay);
                GameplayGate.BlockInputOnly();
                break;
            default:
                // 复活黑屏必须等 SpawnManager.NotifyPlayerSpawnedClientRpc，避免延迟下提前关闭。
                _statusController?.ResetStatus();
                break;
        }
    }

    bool HasDamageAuthority =>
        NetworkManager.Singleton == null ||
        !NetworkManager.Singleton.IsListening ||
        IsServer;

    public void RememberSpawnTeam(TeamId team)
    {
        if (!HasDamageAuthority || !TeamIdUtil.IsPlayable(team))
        {
            return;
        }

        _savedSpawnTeamId.Value = (int)team;
    }

    public TeamId GetSavedSpawnTeam()
    {
        TeamId saved = TeamIdUtil.FromNetwork(_savedSpawnTeamId.Value);
        if (TeamIdUtil.IsPlayable(saved))
        {
            return saved;
        }

        return _playerController != null ? _playerController.ResolveTeam() : TeamId.None;
    }

    public void TakeDamage(int damageAmount, bool instantKill = false)
    {
        if (!HasDamageAuthority) return;
        if (IsDead) return;

        // 爆头：无视剩余 HP，立即进入 Dead（黑屏读秒）。
        // 旧逻辑先扣 hitscanDamage(20)，HP>0 就 return，导致「打中头却不秒杀」。
        if (instantKill)
        {
            currentHealth.Value = 0;
            EnterDeadState();
            return;
        }

        if (IsDowned)
        {
            return;
        }

        currentHealth.Value = Mathf.Max(0, currentHealth.Value - damageAmount);
        if (currentHealth.Value > 0)
        {
            return;
        }

        EnterDownedState();
    }

    /// <summary>
    /// 按命中部位处理玩家伤害。躯干命中直接进入倒地，和假人的躯干伤情规则保持一致；
    /// 服务器仍是唯一可以修改生命状态的一方。
    /// </summary>
    public void TakeDamage(int damageAmount, DetailedBodyPart hitPart)
    {
        if (!HasDamageAuthority) return;
        if (IsDead) return;

        if (hitPart == DetailedBodyPart.Head)
        {
            TakeDamage(damageAmount, true);
            return;
        }

        if (hitPart == DetailedBodyPart.Torso)
        {
            if (!IsDowned)
            {
                EnterDownedState();
            }

            return;
        }

        InjuryState injury = hitPart == DetailedBodyPart.Legs
            ? InjuryState.Crippled_Legs
            : hitPart == DetailedBodyPart.Arms
                ? InjuryState.Light_Arms
                : InjuryState.None;
        if (injury != InjuryState.None)
        {
            _networkInjury.Value = (int)injury;
            _statusController?.SetInjuryFromLifeState(injury);
        }

        TakeDamage(damageAmount);
    }

    public void KillFromBleedOut()
    {
        if (IsDead)
        {
            return;
        }

        if (!HasDamageAuthority)
        {
            if (IsSpawned && IsOwner)
            {
                GiveUpServerRpc();
            }

            return;
        }

        EnterDeadState();
    }

    [ServerRpc]
    void GiveUpServerRpc()
    {
        if (LifeState != PlayerLifeState.Downed)
        {
            return;
        }

        EnterDeadState();
    }

    public void RequestGiveUp()
    {
        if (!IsDowned || IsDead)
        {
            return;
        }

        KillFromBleedOut();
    }

    public void RequestEnterDowned()
    {
        if (IsDead || IsDowned)
        {
            return;
        }

        if (!HasDamageAuthority)
        {
            if (IsSpawned && IsOwner)
            {
                RequestDownedServerRpc();
            }

            return;
        }

        EnterDownedState();
    }

    [ServerRpc]
    void RequestDownedServerRpc()
    {
        if (IsDead || IsDowned)
        {
            return;
        }

        EnterDownedState();
    }

    /// <summary>
    /// 爆头 / 假人镜面反弹等正式即时死亡入口（不依赖 Alt+F9 调试门闩）。
    /// 会走 EnterDeadState → CombatStatusUI 黑屏读秒，与放弃救援一致。
    /// </summary>
    public void RequestInstantKill()
    {
        if (IsDead)
        {
            return;
        }

        if (!HasDamageAuthority)
        {
            if (IsSpawned && IsOwner)
            {
                RequestInstantKillServerRpc();
            }

            return;
        }

        currentHealth.Value = 0;
        EnterDeadState();
    }

    [ServerRpc]
    void RequestInstantKillServerRpc()
    {
        if (IsDead)
        {
            return;
        }

        currentHealth.Value = 0;
        EnterDeadState();
    }

    /// <summary>
    /// 服务器复活：回血、恢复控制。传送由 SpawnManager / Owner TeleportToSpawn 完成。
    /// </summary>
    public void Revive()
    {
        if (!HasDamageAuthority) return;

        StopBleedOut();
        StopRespawn();
        currentHealth.Value = maxHealth;
        _lifeState.Value = (int)PlayerLifeState.Alive;
        _networkInjury.Value = (int)InjuryState.None;
        ApplyPresentation(false);
        _statusController?.ResetStatus();
    }

    public void DebugForceKill()
    {
        if (!DebugCommandGate.IsEnabled)
        {
            return;
        }

        if (!HasDamageAuthority)
        {
            if (IsSpawned && IsOwner)
            {
                DebugForceKillServerRpc();
            }

            return;
        }

        currentHealth.Value = 0;
        EnterDeadState();
    }

    [ServerRpc]
    void DebugForceKillServerRpc()
    {
        currentHealth.Value = 0;
        EnterDeadState();
    }

    public void DebugForceDowned()
    {
        if (!DebugCommandGate.IsEnabled)
        {
            return;
        }

        if (!HasDamageAuthority)
        {
            if (IsSpawned && IsOwner)
            {
                DebugForceDownedServerRpc();
            }

            return;
        }

        EnterDownedState();
    }

    [ServerRpc]
    void DebugForceDownedServerRpc()
    {
        EnterDownedState();
    }

    public void DebugForceRevive()
    {
        if (!DebugCommandGate.IsEnabled)
        {
            return;
        }

        if (!HasDamageAuthority)
        {
            if (IsSpawned && IsOwner)
            {
                DebugForceReviveServerRpc();
            }

            return;
        }

        Revive();
        AutoRespawnAtFaction();
    }

    [ServerRpc]
    void DebugForceReviveServerRpc()
    {
        Revive();
        AutoRespawnAtFaction();
    }

    void EnterDownedState()
    {
        if (IsDead)
        {
            return;
        }

        StopBleedOut();
        StopRespawn();
        if (currentHealth.Value > 0)
        {
            currentHealth.Value = 0;
        }

        _lifeState.Value = (int)PlayerLifeState.Downed;
        if (HasDamageAuthority)
        {
            _bleedRoutine = StartCoroutine(ServerBleedOut());
        }
    }

    IEnumerator ServerBleedOut()
    {
        yield return new WaitForSecondsRealtime(bleedOutSeconds);
        _bleedRoutine = null;
        if (LifeState == PlayerLifeState.Downed)
        {
            EnterDeadState();
        }
    }

    void EnterDeadState()
    {
        StopBleedOut();
        StopRespawn();
        if (currentHealth.Value > 0)
        {
            currentHealth.Value = 0;
        }

        _lifeState.Value = (int)PlayerLifeState.Dead;
        ApplyPresentation(true);

        if (HasDamageAuthority)
        {
            _respawnRoutine = StartCoroutine(WaitThenAutoRespawn());
        }
    }

    IEnumerator WaitThenAutoRespawn()
    {
        yield return new WaitForSecondsRealtime(deadRespawnDelay);
        _respawnRoutine = null;
        if (LifeState != PlayerLifeState.Dead)
        {
            yield break;
        }

        AutoRespawnAtFaction();
    }

    void AutoRespawnAtFaction()
    {
        TeamId team = GetSavedSpawnTeam();
        // 先在服务端复活状态；黑屏必须等 NotifyPlayerSpawnedClientRpc 才关。
        Revive();

        if (Managers.SpawnManager.Instance != null && Managers.SpawnManager.Instance.IsSpawned)
        {
            ulong clientId = IsSpawned ? OwnerClientId : NetworkManager.Singleton.LocalClientId;
            if (Managers.SpawnManager.Instance.SpawnForClient(team, clientId))
            {
                return;
            }
        }

        NotifyOwnerTeleportFallback();
    }

    void NotifyOwnerTeleportFallback()
    {
        if (!IsSpawned)
        {
            TeleportOwnerToFallback();
            return;
        }

        ClientRpcParams targetOwner = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        TeleportOwnerFallbackClientRpc(targetOwner);
    }

    [ClientRpc]
    void TeleportOwnerFallbackClientRpc(ClientRpcParams rpcParams = default)
    {
        TeleportOwnerToFallback();
    }

    void TeleportOwnerToFallback()
    {
        if (IsSpawned && !IsOwner)
        {
            return;
        }

        UI.CombatStatusUI.Instance?.Hide();
        GameplayGate.Release();
        if (_playerController == null)
        {
            return;
        }

        TeamId team = GetSavedSpawnTeam();
        _playerController.TeleportToTeamSpawn(team);
    }

    void StopBleedOut()
    {
        if (_bleedRoutine == null)
        {
            return;
        }

        StopCoroutine(_bleedRoutine);
        _bleedRoutine = null;
    }

    void StopRespawn()
    {
        if (_respawnRoutine == null)
        {
            return;
        }

        StopCoroutine(_respawnRoutine);
        _respawnRoutine = null;
    }

    void ApplyPresentation(bool dead)
    {
        if (_playerController == null)
        {
            _playerController = GetComponent<PlayerController>();
        }

        _playerController?.SetDeadPresentation(dead);
    }
}
