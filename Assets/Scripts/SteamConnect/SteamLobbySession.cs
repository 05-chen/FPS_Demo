using System.Collections;
using Core;
using Match;
using Networking;
using Networking.Ngo;
using Networking.Session;
using Networking.Steam;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Steam 大厅会话兼容入口。具体逻辑已拆到 Steam / NGO / 准入 / 比赛协调服务。
/// 对外 public API 与事件保持不变，调用方无需立刻迁移。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class SteamLobbySession : MonoBehaviour, ISessionHost
{
    public const string GameKey = SteamLobbyService.GameKey;
    public const string GameValue = SteamLobbyService.GameValue;
    const string LogCategory = "SteamLobby";

    public static SteamLobbySession Instance { get; private set; }

    SteamLobbyService _steam;
    NetworkSessionService _network;
    PlayerAdmissionService _admission;
    MatchSessionCoordinator _match;

    bool _returningToLobby;
    bool _servicesWired;

    public CSteamID CurrentLobby => _steam != null ? _steam.CurrentLobby : default;
    public string CurrentJoinCode => _steam != null ? _steam.CurrentJoinCode : null;
    public bool IsLobbyHost => _steam != null && _steam.IsLobbyHost;
    public LobbySessionState State { get; private set; } = LobbySessionState.Idle;

    public event System.Action<string> StatusChanged;
    public event System.Action NetworkStarted;
    public event System.Action ReturnedToLobby;

    public bool IsOfflineSession => _network != null && _network.IsOfflineSession;

    /// <summary>
    /// 对局场景是否已加载。**仅主机侧可靠**；玩家生成资格按 clientId 独立处理。
    /// </summary>
    public bool GameplayStarted => _match != null && _match.GameplayStarted;

    LobbySessionState ISessionHost.State => State;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureServices();
    }

    void EnsureServices()
    {
        if (_servicesWired)
        {
            return;
        }

        _steam = new SteamLobbyService(this);
        _network = new NetworkSessionService(this, this);
        _admission = new PlayerAdmissionService(this);
        _match = new MatchSessionCoordinator(this, this, _admission, _network);

        _admission.BindMatchFlags(
            () => _match.MatchLoadStarted,
            () => _match.GameplayStarted,
            () => _returningToLobby,
            () => NetworkStarted?.Invoke());

        _steam.LobbyCreatedAsHost += OnSteamLobbyCreatedAsHost;
        _steam.LobbyEnteredAsClient += OnSteamLobbyEnteredAsClient;
        _steam.OwnLobbyEnteredWithoutHost += OnOwnLobbyEnteredWithoutHost;
        _steam.LobbyJoinRequested += JoinLobby;

        _network.NetworkStarted += () => NetworkStarted?.Invoke();
        _network.ClientConnected += clientId => _admission.OnClientConnected(clientId);
        _network.ClientDisconnected += clientId => _admission.OnClientDisconnected(clientId);

        _servicesWired = true;
    }

    void Start()
    {
        EnsureServices();
        _network.BindCallbacks();

        if (!SteamRuntime.IsInitialized)
        {
            Notify("Steam 未初始化。联机不可用，仍可点「单机练习」。请先打开并登录 Steam 客户端。");
            return;
        }

        _steam.BindCallbacks();
        string onlineLabel = SteamRuntime.IsOnline ? "（在线）" : "（离线，无法创建房间）";
        Notify("Steam 已连接：" + SteamRuntime.PersonaName + onlineLabel);
        TryJoinFromCommandLine();
    }

    void TryJoinFromCommandLine()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong lobbyValue))
            {
                JoinLobby(new CSteamID(lobbyValue));
                return;
            }
        }
    }

    void Update()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            _admission?.TickPendingJoins();
        }
    }

    public void HostGame()
    {
        EnsureServices();
        _returningToLobby = false;
        if (!CanStartNewSession())
        {
            return;
        }

        if (NetworkSessionService.IsHost())
        {
            Notify("已经是主机了。");
            return;
        }

        if (!SteamRuntime.IsOnline)
        {
            Notify("Steam 是离线模式，无法创建房间。请在 Steam 左上角菜单关掉离线模式，等它显示在线后再点创建。");
            return;
        }

        SetState(LobbySessionState.CreatingLobby);
        Notify("正在创建 Steam 房间...");
        _steam.CreateLobby();
    }

    public void JoinByCode(string rawCode)
    {
        EnsureServices();
        string code = JoinCodeUtility.Normalize(rawCode);
        if (!JoinCodeUtility.IsValid(code))
        {
            Notify("邀请码必须是 6 位，例如 K7M2QX。不要用 0/O、1/I。");
            return;
        }

        if (!CanStartNewSession())
        {
            return;
        }

        if (NetworkSessionService.IsListening())
        {
            Notify("已经在联机中，不能再加入。");
            return;
        }

        _steam.JoinByCode(rawCode);
    }

    public void JoinLobby(CSteamID lobbyId)
    {
        EnsureServices();
        if (!CanStartNewSession())
        {
            return;
        }

        if (NetworkSessionService.IsListening())
        {
            Notify("已经在联机中，不能再加入。");
            return;
        }

        _steam.JoinLobby(lobbyId);
    }

    public void LeaveSession()
    {
        EnsureServices();
        if (_returningToLobby)
        {
            return;
        }

        _returningToLobby = true;
        _network.StopRelayWait();
        _match.StopAllRoutines();
        _match.ResetMatchChoices();
        _network.ClearOfflineFlag();

        UI.CombatStatusUI.Instance?.Hide();

        _network.Shutdown();
        _steam.LeaveLobby();
        SetState(LobbySessionState.Idle);
        Notify("已返回大厅。");
        ReturnedToLobby?.Invoke();
        _returningToLobby = false;
    }

    public void InviteFriends()
    {
        EnsureServices();
        _steam.InviteFriends();
    }

    public void CopyLobbyId()
    {
        EnsureServices();
        _steam.CopyJoinCode();
    }

    public bool StartOfflineHost()
    {
        EnsureServices();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            return _network.StartOfflineHost();
        }

        _match.ResetMatchChoices();
        return _network.StartOfflineHost();
    }

    public void OnClientChoseFaction(ulong clientId, TeamId team)
    {
        EnsureServices();
        _match.OnClientChoseFaction(clientId, team);
    }

    public bool AllowsSpawn(ulong clientId)
    {
        EnsureServices();
        return _admission.AllowsSpawn(clientId);
    }

    public static bool CanCountClientForCapture(ulong clientId)
    {
        if (Instance == null || Instance._admission == null)
        {
            return true;
        }

        return PlayerAdmissionService.CanCountClientForCapture(clientId, Instance._admission.Roster);
    }

    public void ServerOnMatchEnded()
    {
        EnsureServices();
        _match.ServerOnMatchEnded();
    }

    public void ServerPrepareNextRoundKeepingSession()
    {
        EnsureServices();
        _match.ServerPrepareNextRoundKeepingSession();
    }

    public void PrepareNextRoundKeepingSession()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            ServerPrepareNextRoundKeepingSession();
        }
    }

    public static void UseSteamTransport() => NetworkSessionService.UseSteamTransport();

    public static void UseUnityTransport() => NetworkSessionService.UseUnityTransport();

    void OnSteamLobbyCreatedAsHost()
    {
        _match.ResetMatchChoices();
        if (!_network.StartNetworkHost())
        {
            _steam.LeaveLobby();
            Fail("NGO StartHost 失败。请确认场景里的 NetworkManager 已挂 SteamNetworkTransport。");
            return;
        }

        SetState(LobbySessionState.Hosting);
        GUIUtility.systemCopyBuffer = _steam.CurrentJoinCode;
        Notify("房间已创建。把邀请码发给对方，双方到齐后会自动进入选阵营。\n邀请码: "
            + _steam.CurrentJoinCode + "（已复制）");
    }

    void OnSteamLobbyEnteredAsClient(CSteamID owner)
    {
        _network.BeginClientAfterLobbyEnter(owner);
    }

    void OnOwnLobbyEnteredWithoutHost()
    {
        if (NetworkSessionService.IsHost())
        {
            SetState(LobbySessionState.Hosting);
            return;
        }

        Notify("这是你自己创建的房间。请把邀请码发给另一台电脑上的另一个 Steam 账号。");
    }

    bool CanStartNewSession()
    {
        if (!SteamRuntime.IsInitialized)
        {
            Notify("Steam 未就绪，无法创建或加入房间。");
            return false;
        }

        if (State == LobbySessionState.CreatingLobby
            || State == LobbySessionState.JoiningLobby
            || State == LobbySessionState.WaitingRelay
            || State == LobbySessionState.ConnectingToHost)
        {
            Notify("正在处理上一次请求，请稍候。");
            return false;
        }

        return true;
    }

    public void Notify(string message)
    {
        GameLog.Info(LogCategory, message);
        StatusChanged?.Invoke(message);
    }

    public void Fail(string message)
    {
        SetState(LobbySessionState.Failed);
        Notify(message);
        SetState(LobbySessionState.Idle);
    }

    public void SetState(LobbySessionState state)
    {
        State = state;
    }

    public Coroutine Run(IEnumerator routine) => StartCoroutine(routine);

    public void Halt(Coroutine routine)
    {
        if (routine != null)
        {
            StopCoroutine(routine);
        }
    }

    void OnDisable()
    {
        _network?.UnbindCallbacks();
    }

    void OnDestroy()
    {
        if (_network != null)
        {
            _network.StopRelayWait();
            _network.UnbindCallbacks();
        }

        _match?.StopAllRoutines();
        _steam?.LeaveLobby();
        _steam?.DisposeCallbacks();

        if (_steam != null)
        {
            _steam.LobbyCreatedAsHost -= OnSteamLobbyCreatedAsHost;
            _steam.LobbyEnteredAsClient -= OnSteamLobbyEnteredAsClient;
            _steam.OwnLobbyEnteredWithoutHost -= OnOwnLobbyEnteredWithoutHost;
            _steam.LobbyJoinRequested -= JoinLobby;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
