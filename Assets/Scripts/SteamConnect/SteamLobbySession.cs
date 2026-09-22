using System.Collections;
using System.Collections.Generic;
using Core;
using Steamworks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using World;

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
    /// <summary>
    /// 对局场景是否已加载。**仅主机侧可靠**；玩家生成资格按 clientId 独立处理。
    /// </summary>
    public bool GameplayStarted => _gameplayStarted;

    readonly Dictionary<ulong, TeamId> _chosenTeams = new Dictionary<ulong, TeamId>();

    readonly HashSet<ulong> _pendingJoinClientIds = new HashSet<ulong>();
    /// <summary>结算等待期间已收到等待提示的 clientId；下一局就绪后定向发选阵营。</summary>
    readonly HashSet<ulong> _postMatchWaitingClientIds = new HashSet<ulong>();

    bool _matchLoadStarted;
    bool _gameplayStarted;
    bool _hostOpenedNewRound;
    /// <summary>本轮结算后的下一局准备是否已执行（防重复）。</summary>
    bool _nextRoundPrepared;
    Coroutine _matchLoadRoutine;
    Coroutine _postMatchPipelineRoutine;

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

    void Update()
    {
        if (NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsHost
            && _pendingJoinClientIds.Count > 0)
        {
            TryProcessPendingJoins();
        }
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
        StopPostMatchPipeline();
        ResetMatchChoices();
        IsOfflineSession = false;

        // 死亡黑幕是 DontDestroyOnLoad 且层级(90)高于大厅(20)，不清掉会盖死大厅界面。
        UI.CombatStatusUI.Instance?.Hide();

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
        bool started = NetworkManager.Singleton.StartHost();
        _hostOpenedNewRound = started;
        return started;
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
            _hostOpenedNewRound = false;
            UseSteamTransport();
            IsOfflineSession = false;
            return false;
        }

        _hostOpenedNewRound = true;

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

        NetworkManager network = NetworkManager.Singleton;
        if (network == null || !network.IsServer)
        {
            return;
        }

        MatchRoundPhase phase = MatchGameManager.CurrentPhase;
        if (MatchGameManager.IsPostMatchBlocked)
        {
            GameLog.Warn(LogCategory, "结算/准备下一局期间拒绝阵营选择 clientId=" + clientId + " phase=" + phase);
            return;
        }

        if (_chosenTeams.TryGetValue(clientId, out TeamId existing)
            && existing == team
            && HasSpawnedPlayer(clientId))
        {
            return;
        }

        _postMatchWaitingClientIds.Remove(clientId);
        Notify("玩家 " + clientId + " 选择了" + TeamIdUtil.DisplayName(team) + "。");

        // 下一局已在服务器准备完成：只为该 clientId 生成，不等待其他人，不重载场景。
        bool nextRoundReady = _nextRoundPrepared
            || MatchRoundPhaseRules.AllowsIndependentFactionSpawn(phase);

        if (_gameplayStarted || nextRoundReady)
        {
            if (Managers.SpawnManager.Instance == null || !Managers.SpawnManager.Instance.IsSpawned)
            {
                GameLog.Error(LogCategory, "对局场景未就绪，无法为 clientId=" + clientId + " 生成玩家。");
                return;
            }

            _chosenTeams[clientId] = team;
            bool spawned = Managers.SpawnManager.Instance.SpawnForClient(team, clientId);
            if (!spawned)
            {
                _chosenTeams.Remove(clientId);
                GameLog.Error(LogCategory, "玩家生成失败，保持在选阵营阶段。clientId="
                    + clientId + " team=" + team);
                MatchGameManager.Instance?.ServerSetRoundPhase(MatchRoundPhase.FactionSelection);
                Managers.SpawnManager.Instance.RequestFactionSelectForClient(clientId);
                return;
            }

            MatchGameManager.Instance?.ServerEnterPlayingIfSelecting();
            return;
        }

        _chosenTeams[clientId] = team;
        TryStartMatch();
    }

    void TryStartMatch()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (_gameplayStarted || _matchLoadStarted || MatchGameManager.IsPostMatchBlocked
            || _nextRoundPrepared
            || network == null || !network.IsServer)
        {
            return;
        }

        // 已在 FactionSelection 且场景就绪时不应走全局加载。
        if (MatchGameManager.CurrentPhase == MatchRoundPhase.FactionSelection && _gameplayStarted)
        {
            return;
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

        // 主机新开一局时即使人还停在对局场景，也必须再 LoadScene 一次。
        // 否则场景里的 NetworkObject 带着上一局的 Owner/Progress，客户端顶栏不会更新。
        bool forceReloadForNewRound = _hostOpenedNewRound;

        // 联机对局禁止 Skip：走 else 分支时场景里的 NetworkObject 会继续带着上一局的
        // 身份，客户端顶栏读到脏数据。单机练习没有需要同步的对端，才允许 Skip。
        bool networkedMatch = !IsOfflineSession;
        string current = SceneManager.GetActiveScene().name;
        if (current != sceneName || forceReloadForNewRound || networkedMatch)
        {
            if (forceReloadForNewRound && current == sceneName)
            {
                Notify("主机新开一局，重新加载对局场景以同步双方顶栏。");
            }

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
            Notify("单机练习已在对局场景，跳过二次 LoadScene。");
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

        Notify("已进入对局场景。");
        if (network.IsServer)
        {
            bool openedNewRound = _hostOpenedNewRound;
            _hostOpenedNewRound = false;
            MatchGameManager.ServerHandleMatchEntry(openedNewRound);
        }

        int spawnedCount = 0;
        List<ulong> failedClientIds = new List<ulong>();
        foreach (KeyValuePair<ulong, TeamId> pair in _chosenTeams)
        {
            if (!network.ConnectedClients.ContainsKey(pair.Key))
            {
                failedClientIds.Add(pair.Key);
                continue;
            }

            if (Managers.SpawnManager.Instance.SpawnForClient(pair.Value, pair.Key))
            {
                spawnedCount++;
            }
            else
            {
                failedClientIds.Add(pair.Key);
            }
        }

        for (int i = 0; i < failedClientIds.Count; i++)
        {
            _chosenTeams.Remove(failedClientIds[i]);
        }

        _gameplayStarted = spawnedCount > 0;
        _matchLoadStarted = false;
        _matchLoadRoutine = null;
        _nextRoundPrepared = false;

        if (network.IsServer && MatchGameManager.Instance != null && MatchGameManager.Instance.IsSpawned)
        {
            if (spawnedCount > 0)
            {
                MatchGameManager.Instance.ServerEnterPlayingIfSelecting();
            }
            else if (MatchGameManager.CurrentPhase == MatchRoundPhase.None
                     || MatchGameManager.CurrentPhase == MatchRoundPhase.FactionSelection)
            {
                MatchGameManager.Instance.ServerSetRoundPhase(MatchRoundPhase.FactionSelection);
            }
        }

        // 场景由 NGO 全体同步切换，但未选阵营的客户端仍停留在自己的选阵营界面。
        foreach (ulong clientId in network.ConnectedClientsIds)
        {
            if (!_chosenTeams.ContainsKey(clientId))
            {
                Managers.SpawnManager.Instance.RequestFactionSelectForClient(clientId);
            }
        }

        TryProcessPendingJoins();
    }

    void ResetMatchChoices()
    {
        _chosenTeams.Clear();
        _pendingJoinClientIds.Clear();
        _postMatchWaitingClientIds.Clear();
        _matchLoadStarted = false;
        _gameplayStarted = false;
        _hostOpenedNewRound = false;
        _nextRoundPrepared = false;
        StopMatchLoad();
        StopPostMatchPipeline();
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

    void StopPostMatchPipeline()
    {
        if (_postMatchPipelineRoutine == null)
        {
            return;
        }

        StopCoroutine(_postMatchPipelineRoutine);
        _postMatchPipelineRoutine = null;
    }

    static bool HasSpawnedPlayer(ulong clientId)
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null
            || !network.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
            || client.PlayerObject == null)
        {
            return false;
        }

        return client.PlayerObject.IsSpawned;
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

            MatchRoundPhase phase = MatchGameManager.CurrentPhase;

            // 结算中 / 等待计时 / 准备下一局：只准入等待，不进旧局、不选阵营、不生成。
            if (MatchGameManager.IsPostMatchBlocked || MatchGameManager.IsPostMatchWaitingPhase)
            {
                Notify("对局已结束，已连接服务器；请等待当前结算结束。");
                _pendingJoinClientIds.Remove(clientId);
                _postMatchWaitingClientIds.Add(clientId);
                Managers.SpawnManager.Instance?.RequestPostMatchWaitingForClient(clientId);
                return;
            }

            if (phase == MatchRoundPhase.FactionSelection)
            {
                Notify("下一局选阵营中，通知新客户端选阵营。");
                _pendingJoinClientIds.Remove(clientId);
                Managers.SpawnManager.Instance?.RequestPostMatchFactionSelectForClient(clientId);
                return;
            }

            if (_matchLoadStarted)
            {
                _pendingJoinClientIds.Add(clientId);
                Notify("对手在场景加载期间加入，等待场景就绪后处理。");
                return;
            }

            if (_gameplayStarted || phase == MatchRoundPhase.Playing)
            {
                // 对局还在。重连只复活，进度由主机上的对局状态决定，这里不能清。
                if (Managers.SpawnManager.Instance != null && Managers.SpawnManager.Instance.IsSpawned)
                {
                    ProcessClientJoin(clientId);
                }
                else
                {
                    _pendingJoinClientIds.Add(clientId);
                    GameLog.Warn(LogCategory, "对局中重连，但 SpawnManager 未就绪，已加入等待队列。");
                }

                return;
            }

            SetState(LobbySessionState.InSession);
            Notify("对手已加入，进入选阵营。");
            NetworkStarted?.Invoke();

            // 客户端拿不到权威的对局状态，无法自己判断该不该弹面板；
            // 由主机在这里定向通知，避免重连时两端各弹一次。
            if (Managers.SpawnManager.Instance != null && Managers.SpawnManager.Instance.IsSpawned)
            {
                ProcessClientJoin(clientId);
            }
            else
            {
                _pendingJoinClientIds.Add(clientId);
                GameLog.Warn(LogCategory, "SpawnManager 未就绪，新玩家已加入等待队列。");
            }

            return;
        }

        SetState(LobbySessionState.InSession);
        Notify("已连接到主机。");
        // 客户端本机的 _gameplayStarted 恒为 false（只有主机跑 LoadMatchAndSpawn 会置 true），
        // 用它判断会误弹面板。这里什么都不做，完全等主机的定向通知。
    }

    /// <summary>
    /// 找回这位客户端本局的阵营。先查 clientId（同一次连接内有效），
    /// 再查 SteamID（重连后 clientId 换号，但人还是同一个人）。
    /// </summary>
    /// <summary>当前 NetworkManager 上的 Steam 传输层。单机练习时是 UnityTransport，会返回 null。</summary>
    static SteamNetworkTransport ActiveTransport =>
        NetworkManager.Singleton != null
            ? NetworkManager.Singleton.NetworkConfig.NetworkTransport as SteamNetworkTransport
            : null;

    void OnNetClientDisconnected(ulong clientId)
    {
        if (_returningToLobby)
        {
            return;
        }

        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.IsHost)
        {
            _pendingJoinClientIds.Remove(clientId);
            _postMatchWaitingClientIds.Remove(clientId);
            _chosenTeams.Remove(clientId);
            if (clientId != network.LocalClientId)
            {
                Notify("对手已断开：" + SteamNetworkTransport.ConsumeDisconnectNotice());
            }

            return;
        }

        // 先停在提示上，等玩家点击再回大厅，避免和大厅界面叠在一起。
        UI.DisconnectNoticeUI.EnsureInstance().Show(SteamNetworkTransport.ConsumeDisconnectNotice());
    }

    void TryProcessPendingJoins()
    {
        if (_matchLoadStarted || Managers.SpawnManager.Instance == null
            || !Managers.SpawnManager.Instance.IsSpawned)
        {
            return;
        }

        if (MatchGameManager.IsPostMatchBlocked)
        {
            return;
        }

        NetworkManager network = NetworkManager.Singleton;
        if (network == null || !network.IsHost)
        {
            return;
        }

        ulong[] pending = new ulong[_pendingJoinClientIds.Count];
        _pendingJoinClientIds.CopyTo(pending);
        for (int i = 0; i < pending.Length; i++)
        {
            ulong clientId = pending[i];
            if (!network.ConnectedClients.ContainsKey(clientId))
            {
                _pendingJoinClientIds.Remove(clientId);
                continue;
            }

            if (ProcessClientJoin(clientId))
            {
                _pendingJoinClientIds.Remove(clientId);
            }
        }
    }

    bool ProcessClientJoin(ulong clientId)
    {
        MatchRoundPhase phase = MatchGameManager.CurrentPhase;

        if (MatchGameManager.IsPostMatchBlocked || MatchGameManager.IsPostMatchWaitingPhase)
        {
            _postMatchWaitingClientIds.Add(clientId);
            Managers.SpawnManager.Instance?.RequestPostMatchWaitingForClient(clientId);
            return true;
        }

        if (phase == MatchRoundPhase.FactionSelection)
        {
            Managers.SpawnManager.Instance?.RequestPostMatchFactionSelectForClient(clientId);
            return true;
        }

        if (phase == MatchRoundPhase.PreparingNextRound)
        {
            _pendingJoinClientIds.Add(clientId);
            return false;
        }

        if (_gameplayStarted || phase == MatchRoundPhase.Playing)
        {
            Notify("对手重新加入，请重新选择阵营。");
        }

        Managers.SpawnManager.Instance.RequestFactionSelectForClient(clientId);
        return true;
    }

    /// <summary>
    /// 对局 Concude 后由 MatchGameManager 调用：启动唯一服务端结算等待管线。
    /// 可重复调用，但管线只跑一次。
    /// </summary>
    public void ServerOnMatchEnded()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null || !network.IsServer)
        {
            return;
        }

        _nextRoundPrepared = false;
        if (_postMatchPipelineRoutine != null)
        {
            return;
        }

        _postMatchPipelineRoutine = StartCoroutine(ServerPostMatchPipeline());
    }

    /// <summary>服务端权威：MatchEnded → PostMatchWaiting(5s) → PreparingNextRound → FactionSelection。</summary>
    IEnumerator ServerPostMatchPipeline()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null || !network.IsServer)
        {
            _postMatchPipelineRoutine = null;
            yield break;
        }

        MatchGameManager match = MatchGameManager.Instance;
        if (match != null && match.IsSpawned)
        {
            match.ServerSetRoundPhase(MatchRoundPhase.PostMatchWaiting);
        }

        Notify("结算等待计时开始（约 5 秒）。");
        yield return new WaitForSecondsRealtime(5f);

        network = NetworkManager.Singleton;
        if (network == null || !network.IsServer)
        {
            _postMatchPipelineRoutine = null;
            yield break;
        }

        ServerPrepareNextRoundKeepingSession();
        ServerNotifyFactionSelectAfterPrepare();
        TryProcessPendingJoins();
        _postMatchPipelineRoutine = null;
    }

    void ServerNotifyFactionSelectAfterPrepare()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null || !network.IsServer
            || Managers.SpawnManager.Instance == null
            || !Managers.SpawnManager.Instance.IsSpawned)
        {
            return;
        }

        // 等待中的新加入者 + 仍连接且尚未选阵营的客户端。
        HashSet<ulong> targets = new HashSet<ulong>(_postMatchWaitingClientIds);
        foreach (ulong clientId in network.ConnectedClientsIds)
        {
            if (!_chosenTeams.ContainsKey(clientId) || !HasSpawnedPlayer(clientId))
            {
                targets.Add(clientId);
            }
        }

        foreach (ulong clientId in targets)
        {
            if (!network.ConnectedClients.ContainsKey(clientId))
            {
                continue;
            }

            Managers.SpawnManager.Instance.RequestPostMatchFactionSelectForClient(clientId);
        }

        _postMatchWaitingClientIds.Clear();
    }

    /// <summary>
    /// 清理上一局并初始化下一局（只执行一次）。不因新客户端加入而重复创建 MatchGameManager。
    /// </summary>
    public void ServerPrepareNextRoundKeepingSession()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null || !network.IsServer || !network.IsListening)
        {
            return;
        }

        if (_nextRoundPrepared && MatchGameManager.CurrentPhase == MatchRoundPhase.FactionSelection)
        {
            GameLog.Info(LogCategory, "下一局已准备过，跳过重复 Prepare。");
            return;
        }

        MatchGameManager match = MatchGameManager.Instance;
        if (match != null && match.IsSpawned)
        {
            match.ServerSetRoundPhase(MatchRoundPhase.PreparingNextRound);
        }

        StopMatchLoad();
        _matchLoadStarted = false;
        _hostOpenedNewRound = false;
        _chosenTeams.Clear();

        ServerDespawnAllPlayerObjects();

        if (UI.FactionSelectUI.Instance != null)
        {
            UI.FactionSelectUI.Instance.ShowUI(false);
        }

        // 原地重置占点与 MatchGameManager，不重新加载场景、不 Instantiate 新的 MatchGameManager。
        SectorManager.ServerResetAllForNewMatch();
        if (match != null && match.IsSpawned)
        {
            match.ServerBeginNewRound();
            match.NotifyMatchResetClientRpc();
        }
        else
        {
            GameLog.Warn(LogCategory, "PrepareNextRound 时 MatchGameManager 未就绪。");
        }

        // 场景已在对局中：选阵营后直接 Spawn，禁止再走 TryStartMatch 全局加载。
        _gameplayStarted = true;
        _nextRoundPrepared = true;
        Notify("下一局已准备完成，开放阵营选择。");
    }

    /// <summary>兼容旧调用名：转交服务端准备下一局。</summary>
    public void PrepareNextRoundKeepingSession()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            ServerPrepareNextRoundKeepingSession();
        }
    }

    static void ServerDespawnAllPlayerObjects()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null || !network.IsServer)
        {
            return;
        }

        List<NetworkObject> toDespawn = new List<NetworkObject>();
        foreach (ulong clientId in network.ConnectedClientsIds)
        {
            if (!network.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                || client.PlayerObject == null)
            {
                continue;
            }

            toDespawn.Add(client.PlayerObject);
        }

        PlayerController[] players = Object.FindObjectsByType<PlayerController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            PlayerController player = players[i];
            if (player == null)
            {
                continue;
            }

            NetworkObject netObj = player.NetworkObject;
            if (netObj != null && netObj.IsSpawned && !toDespawn.Contains(netObj))
            {
                toDespawn.Add(netObj);
            }
        }

        for (int i = 0; i < toDespawn.Count; i++)
        {
            NetworkObject netObj = toDespawn[i];
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(true);
            }
        }
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
        StopMatchLoad();
        StopPostMatchPipeline();
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
