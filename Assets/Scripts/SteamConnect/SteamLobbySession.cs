using System.Collections;
using System.Collections.Generic;
using Core;
using Steamworks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Steam 大厅会话 + NGO 开房/加入。
/// 主机：创建大厅 -> StartHost（Steam P2P 监听）
/// 客户端：加入大厅 -> 拿到房主 SteamID -> 等中继就绪 -> StartClient
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class SteamLobbySession : MonoBehaviour
{
    public const string GameKey = "game";
    public const string GameValue = "fps1v1";
    const string LogCategory = "SteamLobby";
    const float RelayTimeoutSeconds = 20f;

    public static SteamLobbySession Instance { get; private set; }

    public CSteamID CurrentLobby { get; private set; }
    public string CurrentJoinCode { get; private set; }
    public bool IsLobbyHost { get; private set; }
    public LobbySessionState State { get; private set; } = LobbySessionState.Idle;

    public event System.Action<string> StatusChanged;
    public event System.Action NetworkStarted;
    public event System.Action ReturnedToLobby;

    public bool IsOfflineSession { get; private set; }
    public bool GameplayStarted => _gameplayStarted;

    readonly Dictionary<ulong, TeamId> _chosenTeams = new Dictionary<ulong, TeamId>();
    bool _matchLoadStarted;
    bool _gameplayStarted;
    Coroutine _matchLoadRoutine;

    CallResult<LobbyCreated_t> _lobbyCreated;
    CallResult<LobbyEnter_t> _lobbyJoin;
    CallResult<LobbyMatchList_t> _lobbyList;
    Callback<GameLobbyJoinRequested_t> _lobbyJoinRequested;
    Callback<LobbyEnter_t> _lobbyEntered;
    Coroutine _relayRoutine;
    bool _networkCallbacksBound;
    bool _steamCallbacksBound;
    bool _returningToLobby;
    ulong _handledEnterLobbyId;
    string _pendingJoinCode;

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
    }

    void Start()
    {
        BindNetworkCallbacks();

        if (!SteamRuntime.IsInitialized)
        {
            Notify("Steam 未初始化。联机不可用，仍可点「单机练习」。请先打开并登录 Steam 客户端。");
            return;
        }

        BindSteamCallbacks();
        string onlineLabel = SteamRuntime.IsOnline ? "（在线）" : "（离线，无法创建房间）";
        Notify("Steam 已连接：" + SteamRuntime.PersonaName + onlineLabel);
        TryJoinFromCommandLine();
    }

    public void HostGame()
    {
        _returningToLobby = false;
        if (!CanStartNewSession())
        {
            return;
        }

        if (IsNetworkHost())
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
        BindSteamCallbacks();
        SteamAPICall_t call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 2);
        _lobbyCreated.Set(call);
    }

    public void JoinByCode(string rawCode)
    {
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

        if (IsNetworkListening())
        {
            Notify("已经在联机中，不能再加入。");
            return;
        }

        BindSteamCallbacks();
        SetState(LobbySessionState.JoiningLobby);
        _pendingJoinCode = code;
        Notify("正在查找邀请码 " + code + " ...");

        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListStringFilter(JoinCodeUtility.LobbyDataKey, code, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(GameKey, GameValue, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(16);
        _lobbyList.Set(SteamMatchmaking.RequestLobbyList());
    }

    public void JoinLobby(CSteamID lobbyId)
    {
        if (!lobbyId.IsValid())
        {
            Notify("大厅 ID 无效。");
            return;
        }

        if (!CanStartNewSession())
        {
            return;
        }

        if (IsNetworkListening())
        {
            Notify("已经在联机中，不能再加入。");
            return;
        }

        SetState(LobbySessionState.JoiningLobby);
        SubmitJoinLobby(lobbyId);
    }

    public void LeaveSession()
    {
        if (_returningToLobby)
        {
            return;
        }

        _returningToLobby = true;
        StopRelayWait();
        StopMatchLoad();
        ResetMatchChoices();
        IsOfflineSession = false;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        LeaveCurrentLobby();
        SetState(LobbySessionState.Idle);
        Notify("已返回大厅。");
        ReturnedToLobby?.Invoke();
        _returningToLobby = false;
    }

    public void InviteFriends()
    {
        if (!CurrentLobby.IsValid())
        {
            Notify("还没有房间，请先创建。");
            return;
        }

        SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobby);
        Notify("已打开 Steam 邀请窗口。");
    }

    public void CopyLobbyId()
    {
        if (string.IsNullOrEmpty(CurrentJoinCode))
        {
            Notify("还没有房间。");
            return;
        }

        GUIUtility.systemCopyBuffer = CurrentJoinCode;
        Notify("邀请码已复制：" + CurrentJoinCode);
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

    void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
    {
        if (ioFailure || result.m_eResult != EResult.k_EResultOK)
        {
            Fail(SteamLobbyErrors.DescribeCreateFail(ioFailure, result.m_eResult));
            return;
        }

        CurrentLobby = new CSteamID(result.m_ulSteamIDLobby);
        IsLobbyHost = true;
        SteamMatchmaking.SetLobbyJoinable(CurrentLobby, true);
        CurrentJoinCode = JoinCodeUtility.Generate();
        SteamMatchmaking.SetLobbyData(CurrentLobby, GameKey, GameValue);
        SteamMatchmaking.SetLobbyData(CurrentLobby, JoinCodeUtility.LobbyDataKey, CurrentJoinCode);
        SteamMatchmaking.SetLobbyData(CurrentLobby, "name", SteamRuntime.PersonaName + " 的房间");
        SteamMatchmaking.SetLobbyData(CurrentLobby, "host_steamid", SteamRuntime.LocalSteamId.m_SteamID.ToString());

        if (!StartNetworkHost())
        {
            LeaveCurrentLobby();
            Fail("NGO StartHost 失败。请确认场景里的 NetworkManager 已挂 SteamNetworkTransport。");
            return;
        }

        SetState(LobbySessionState.Hosting);
        GUIUtility.systemCopyBuffer = CurrentJoinCode;
        Notify("房间已创建。把邀请码发给对方，双方到齐后会自动进入选阵营。\n邀请码: " + CurrentJoinCode + "（已复制）");
    }

    void OnLobbyJoinRequested(GameLobbyJoinRequested_t request)
    {
        JoinLobby(request.m_steamIDLobby);
    }

    void OnLobbyJoinCallResult(LobbyEnter_t result, bool ioFailure)
    {
        if (ioFailure)
        {
            Fail("加入大厅失败：Steam 回调超时。请确认网络/加速器，并让主机保持 Play。");
            return;
        }

        HandleLobbyEntered(result);
    }

    void OnLobbyEntered(LobbyEnter_t result)
    {
        HandleLobbyEntered(result);
    }

    void HandleLobbyEntered(LobbyEnter_t result)
    {
        if (result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            Fail(SteamLobbyErrors.DescribeJoinFail(result.m_EChatRoomEnterResponse));
            return;
        }

        if (_handledEnterLobbyId == result.m_ulSteamIDLobby && CurrentLobby.m_SteamID == result.m_ulSteamIDLobby)
        {
            return;
        }

        _handledEnterLobbyId = result.m_ulSteamIDLobby;
        CurrentLobby = new CSteamID(result.m_ulSteamIDLobby);
        string lobbyCode = JoinCodeUtility.Normalize(SteamMatchmaking.GetLobbyData(CurrentLobby, JoinCodeUtility.LobbyDataKey));
        if (JoinCodeUtility.IsValid(lobbyCode))
        {
            CurrentJoinCode = lobbyCode;
        }

        if (!SteamRuntime.TryGetLocalSteamId(out CSteamID me))
        {
            Fail("已进入大厅，但拿不到本机 SteamID。");
            return;
        }

        CSteamID owner = ResolveLobbyOwner(CurrentLobby);
        if (!owner.IsValid())
        {
            Fail("已进入大厅，但拿不到房主 SteamID。请让主机重新创建房间后再加入。");
            return;
        }

        if (owner == me)
        {
            IsLobbyHost = true;
            if (IsNetworkHost())
            {
                SetState(LobbySessionState.Hosting);
                return;
            }

            Notify("这是你自己创建的房间。请把邀请码发给另一台电脑上的另一个 Steam 账号。");
            return;
        }

        IsLobbyHost = false;
        SetState(LobbySessionState.WaitingRelay);
        Notify("已进入大厅，正在连接房主 " + owner + " ...");
        StopRelayWait();
        _relayRoutine = StartCoroutine(WaitRelayThenStartClient(owner));
    }

    static CSteamID ResolveLobbyOwner(CSteamID lobby)
    {
        CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobby);
        if (owner.IsValid() && owner.BIndividualAccount())
        {
            return owner;
        }

        string hostData = SteamMatchmaking.GetLobbyData(lobby, "host_steamid");
        if (ulong.TryParse(hostData, out ulong hostValue))
        {
            return new CSteamID(hostValue);
        }

        return owner;
    }

    IEnumerator WaitRelayThenStartClient(CSteamID owner)
    {
        SteamNetworkingUtils.InitRelayNetworkAccess();

        float timeout = RelayTimeoutSeconds;
        while (timeout > 0f)
        {
            ESteamNetworkingAvailability availability = SteamNetworkingUtils.GetRelayNetworkStatus(out _);
            if (availability == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current)
            {
                break;
            }

            Notify("正在准备 Steam 中继网络（" + availability + "）...");
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        _relayRoutine = null;
        StartNetworkClient(owner);
    }

    void StartNetworkClient(CSteamID owner)
    {
        if (NetworkManager.Singleton == null)
        {
            Fail("场景里没有 NetworkManager。");
            return;
        }

        if (IsNetworkListening())
        {
            Notify("已经在联机中。");
            return;
        }

        DestroyOfflineScenePlayers();
        BindNetworkCallbacks();
        UseSteamTransport();
        Managers.SpawnManager.ConfigureDelayedPlayerSpawn(NetworkManager.Singleton);
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as SteamNetworkTransport;
        if (transport == null)
        {
            Fail("NetworkManager 没有挂 SteamNetworkTransport。");
            return;
        }

        transport.ConnectToSteamId = owner;
        SetState(LobbySessionState.ConnectingToHost);
        if (!NetworkManager.Singleton.StartClient())
        {
            Fail("NGO StartClient 失败。");
            return;
        }

        Notify("正在通过 Steam 连接房主 " + owner + " ...");
    }

    bool StartNetworkHost()
    {
        if (NetworkManager.Singleton == null)
        {
            GameLog.Error(LogCategory, "场景里没有 NetworkManager。");
            return false;
        }

        DestroyOfflineScenePlayers();
        BindNetworkCallbacks();
        UseSteamTransport();
        IsOfflineSession = false;
        ResetMatchChoices();
        Managers.SpawnManager.ConfigureDelayedPlayerSpawn(NetworkManager.Singleton);
        return NetworkManager.Singleton.StartHost();
    }

    public bool StartOfflineHost()
    {
        if (NetworkManager.Singleton == null)
        {
            GameLog.Error(LogCategory, "场景里没有 NetworkManager。");
            return false;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            IsOfflineSession = true;
            Notify("已进入单机练习。请选择红方或蓝方。");
            NetworkStarted?.Invoke();
            return true;
        }

        DestroyOfflineScenePlayers();
        BindNetworkCallbacks();
        UseUnityTransport();
        IsOfflineSession = true;
        ResetMatchChoices();
        Managers.SpawnManager.ConfigureDelayedPlayerSpawn(NetworkManager.Singleton);
        if (!NetworkManager.Singleton.StartHost())
        {
            UseSteamTransport();
            IsOfflineSession = false;
            return false;
        }

        Notify("已进入单机练习。请选择红方或蓝方。");
        NetworkStarted?.Invoke();
        return true;
    }

    public void OnClientChoseFaction(ulong clientId, TeamId team)
    {
        if (!TeamIdUtil.IsPlayable(team))
        {
            return;
        }

        _chosenTeams[clientId] = team;
        Notify("玩家 " + clientId + " 选择了" + TeamIdUtil.DisplayName(team) + "。");

        if (_gameplayStarted)
        {
            if (Managers.SpawnManager.Instance == null || !Managers.SpawnManager.Instance.IsSpawned)
            {
                GameLog.Error(LogCategory, "对局已开始，但 SpawnManager 未就绪，无法复活。");
                return;
            }

            Managers.SpawnManager.Instance.SpawnForClient(team, clientId);
            return;
        }

        TryStartMatch();
    }

    void TryStartMatch()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (_gameplayStarted || _matchLoadStarted || network == null || !network.IsServer)
        {
            return;
        }

        foreach (ulong clientId in network.ConnectedClientsIds)
        {
            if (!_chosenTeams.ContainsKey(clientId))
            {
                Notify("已记录阵营，等待所有玩家选完。");
                return;
            }
        }

        _matchLoadStarted = true;
        if (UI.FactionSelectUI.Instance != null)
        {
            UI.FactionSelectUI.Instance.ShowUI(false);
        }

        StopMatchLoad();
        _matchLoadRoutine = StartCoroutine(LoadMatchAndSpawn());
    }

    IEnumerator LoadMatchAndSpawn()
    {
        string sceneName = GameScenes.ForMatch(IsOfflineSession);
        Notify("正在进入场景：" + sceneName);

        NetworkManager network = NetworkManager.Singleton;
        if (network == null)
        {
            yield break;
        }

        string current = SceneManager.GetActiveScene().name;
        if (current != sceneName)
        {
            bool loaded = false;
            void OnLoaded(string loadedName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
            {
                if (loadedName == sceneName)
                {
                    loaded = true;
                }
            }

            network.SceneManager.OnLoadEventCompleted += OnLoaded;
            SceneEventProgressStatus status = network.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                network.SceneManager.OnLoadEventCompleted -= OnLoaded;
                GameLog.Error(LogCategory, "加载场景失败：" + sceneName + "，状态=" + status + "。请确认该场景已勾进 Build Settings。");
                _matchLoadStarted = false;
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 30f;
            while (!loaded && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            network.SceneManager.OnLoadEventCompleted -= OnLoaded;
            if (!loaded)
            {
                GameLog.Error(LogCategory, "加载场景超时：" + sceneName);
                _matchLoadStarted = false;
                yield break;
            }
        }
        else
        {
            Notify("已在对局场景，跳过二次 LoadScene。");
        }

        float spawnDeadline = Time.realtimeSinceStartup + 5f;
        while ((Managers.SpawnManager.Instance == null || !Managers.SpawnManager.Instance.IsSpawned)
               && Time.realtimeSinceStartup < spawnDeadline)
        {
            yield return null;
        }

        if (Managers.SpawnManager.Instance == null || !Managers.SpawnManager.Instance.IsSpawned)
        {
            GameLog.Error(LogCategory, "场景已加载，但 SpawnManager 未就绪。");
            _matchLoadStarted = false;
            yield break;
        }

        foreach (KeyValuePair<ulong, TeamId> pair in _chosenTeams)
        {
            Managers.SpawnManager.Instance.SpawnForClient(pair.Value, pair.Key);
        }

        Notify("已进入对局场景。");
        _gameplayStarted = true;
        _matchLoadStarted = false;
        _matchLoadRoutine = null;
    }

    void ResetMatchChoices()
    {
        _chosenTeams.Clear();
        _matchLoadStarted = false;
        _gameplayStarted = false;
        StopMatchLoad();
    }

    void StopMatchLoad()
    {
        if (_matchLoadRoutine == null)
        {
            return;
        }

        StopCoroutine(_matchLoadRoutine);
        _matchLoadRoutine = null;
    }

    public static void UseSteamTransport()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null)
        {
            return;
        }

        SteamNetworkTransport steam = network.GetComponent<SteamNetworkTransport>();
        UnityTransport unity = network.GetComponent<UnityTransport>();
        if (steam == null)
        {
            return;
        }

        if (unity != null)
        {
            unity.enabled = false;
        }

        steam.enabled = true;
        network.NetworkConfig.NetworkTransport = steam;
    }

    public static void UseUnityTransport()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null)
        {
            return;
        }

        SteamNetworkTransport steam = network.GetComponent<SteamNetworkTransport>();
        UnityTransport unity = network.GetComponent<UnityTransport>();
        if (unity == null)
        {
            GameLog.Error(LogCategory, "NetworkManager 上没有 UnityTransport，单机无法在没有 Steam 时启动。");
            return;
        }

        if (steam != null)
        {
            steam.enabled = false;
        }

        unity.enabled = true;
        network.NetworkConfig.NetworkTransport = unity;
    }

    void BindSteamCallbacks()
    {
        if (_steamCallbacksBound)
        {
            return;
        }

        _lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
        _lobbyJoin = CallResult<LobbyEnter_t>.Create(OnLobbyJoinCallResult);
        _lobbyList = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
        _lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);
        _lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
        _steamCallbacksBound = true;
    }

    void BindNetworkCallbacks()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        Managers.SpawnManager.ConfigureDelayedPlayerSpawn(NetworkManager.Singleton);

        if (_networkCallbacksBound)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnNetClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnNetClientDisconnected;
        _networkCallbacksBound = true;
    }

    void OnNetClientConnected(ulong clientId)
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.IsHost)
        {
            if (clientId == network.LocalClientId)
            {
                Notify("主机已就绪。等待对手加入，双方到齐后进入选阵营。");
                return;
            }

            if (_gameplayStarted)
            {
                if (Managers.SpawnManager.Instance != null && Managers.SpawnManager.Instance.IsSpawned)
                {
                    if (_chosenTeams.TryGetValue(clientId, out TeamId savedTeam))
                    {
                        Notify("对手已重连，按已记录阵营自动复活。");
                        Managers.SpawnManager.Instance.SpawnForClient(savedTeam, clientId);
                    }
                    else
                    {
                        Notify("对手已重连。仅通知该玩家选择阵营，不会把主机拉回大厅。");
                        Managers.SpawnManager.Instance.RequestFactionSelectForClient(clientId);
                    }
                }
                else
                {
                    GameLog.Warn(LogCategory, "对局中重连，但 SpawnManager 未就绪。");
                }

                return;
            }

            SetState(LobbySessionState.InSession);
            Notify("对手已加入，进入选阵营。");
            NetworkStarted?.Invoke();
            return;
        }

        SetState(LobbySessionState.InSession);
        Notify("已连接到主机。");
        if (!_gameplayStarted)
        {
            NetworkStarted?.Invoke();
        }
    }

    void OnNetClientDisconnected(ulong clientId)
    {
        if (_returningToLobby)
        {
            return;
        }

        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.IsHost)
        {
            if (clientId != network.LocalClientId)
            {
                Notify("对手已断开。");
            }

            return;
        }

        Notify("与主机断开，已返回大厅。");
        LeaveSession();
    }

    void OnLobbyList(LobbyMatchList_t result, bool ioFailure)
    {
        if (ioFailure)
        {
            Fail("查找房间失败：Steam 回调超时。请检查网络后再试。");
            return;
        }

        for (int i = 0; i < result.m_nLobbiesMatching; i++)
        {
            CSteamID lobby = SteamMatchmaking.GetLobbyByIndex(i);
            string code = JoinCodeUtility.Normalize(SteamMatchmaking.GetLobbyData(lobby, JoinCodeUtility.LobbyDataKey));
            if (code == _pendingJoinCode)
            {
                SubmitJoinLobby(lobby);
                return;
            }
        }

        Fail("找不到邀请码 " + _pendingJoinCode + " 的房间。请确认主机仍在 Play，码没有输错。");
    }

    void SubmitJoinLobby(CSteamID lobbyId)
    {
        Notify("正在加入大厅...");
        BindSteamCallbacks();
        SteamAPICall_t call = SteamMatchmaking.JoinLobby(lobbyId);
        _lobbyJoin.Set(call);
    }

    static void DestroyOfflineScenePlayers()
    {
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            PlayerController player = players[i];
            if (player != null && !player.IsSpawned)
            {
                Destroy(player.gameObject);
            }
        }
    }

    void Notify(string message)
    {
        GameLog.Info(LogCategory, message);
        StatusChanged?.Invoke(message);
    }

    void Fail(string message)
    {
        SetState(LobbySessionState.Failed);
        Notify(message);
        SetState(LobbySessionState.Idle);
    }

    void SetState(LobbySessionState state)
    {
        State = state;
    }

    static bool IsNetworkHost()
    {
        return NetworkManager.Singleton != null && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer);
    }

    static bool IsNetworkListening()
    {
        return NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsHost);
    }

    void OnDisable()
    {
        UnbindNetworkCallbacks();
    }

    void OnDestroy()
    {
        StopRelayWait();
        LeaveCurrentLobby();
        DisposeSteamCallbacks();
        UnbindNetworkCallbacks();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    void StopRelayWait()
    {
        if (_relayRoutine == null)
        {
            return;
        }

        StopCoroutine(_relayRoutine);
        _relayRoutine = null;
    }

    void LeaveCurrentLobby()
    {
        CurrentJoinCode = null;
        _pendingJoinCode = null;
        IsLobbyHost = false;
        _handledEnterLobbyId = 0;

        if (!CurrentLobby.IsValid() || !SteamRuntime.IsInitialized)
        {
            CurrentLobby = default;
            return;
        }

        SteamMatchmaking.LeaveLobby(CurrentLobby);
        CurrentLobby = default;
    }

    void DisposeSteamCallbacks()
    {
        if (!_steamCallbacksBound)
        {
            return;
        }

        _lobbyCreated?.Dispose();
        _lobbyJoin?.Dispose();
        _lobbyList?.Dispose();
        _lobbyJoinRequested?.Dispose();
        _lobbyEntered?.Dispose();
        _lobbyCreated = null;
        _lobbyJoin = null;
        _lobbyList = null;
        _lobbyJoinRequested = null;
        _lobbyEntered = null;
        _steamCallbacksBound = false;
    }

    void UnbindNetworkCallbacks()
    {
        if (!_networkCallbacksBound || NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback -= OnNetClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnNetClientDisconnected;
        _networkCallbacksBound = false;
    }
}
