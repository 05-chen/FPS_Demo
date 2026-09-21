using Steamworks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Steam 联机测试界面：创建房间、邀请好友、粘贴大厅 ID 加入。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10)]
public sealed class SteamLobbyUI : MonoBehaviour
{
    public static SteamLobbyUI Instance { get; private set; }

    [SerializeField] FactionSelectUI offlineFactionUi;
    [SerializeField] GameObject redPlayer;
    [SerializeField] GameObject bluePlayer;

    /// <summary>大厅界面还在时挡住玩家输入，否则主机会锁鼠标，点不了邀请按钮。</summary>
    public static bool IsBlockingGameplay => GameplayGate.IsBlocked;

    Text _statusText;
    InputField _lobbyIdInput;
    GameObject _panelRoot;
    GameObject _lobbyCanvasRoot;
    Camera _overviewCamera;
    bool _sessionBound;

    static readonly Vector3 OverviewPosition = new Vector3(26f, 7f, 8f);
    static readonly Vector3 OverviewLookAt = new Vector3(26f, 3f, 15.2f);

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        RuntimeUiFactory.EnsureEventSystem();
        _overviewCamera = RuntimeUiFactory.CreateOverviewCamera(transform, "LobbyOverviewCamera", OverviewPosition, OverviewLookAt);
        _overviewCamera.tag = "MainCamera";
        BuildUi();
        DisableScenePlayers();
        GameplayGate.Block();
        PauseMenuUI.Create(transform, QuitToLobby);
        HideFactionSelect();
    }

    void OnEnable()
    {
        BindSession();
    }

    void Start()
    {
        BindSession();
    }

    void OnDisable()
    {
        UnbindSession();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void BindSession()
    {
        SteamLobbySession session = SteamLobbySession.Instance;
        if (session == null || _sessionBound)
        {
            return;
        }

        session.StatusChanged += OnStatusChanged;
        session.NetworkStarted += ShowOnlineFactionSelect;
        session.ReturnedToLobby += ShowLobby;
        _sessionBound = true;
    }

    void UnbindSession()
    {
        if (!_sessionBound || SteamLobbySession.Instance == null)
        {
            return;
        }

        SteamLobbySession.Instance.StatusChanged -= OnStatusChanged;
        SteamLobbySession.Instance.NetworkStarted -= ShowOnlineFactionSelect;
        SteamLobbySession.Instance.ReturnedToLobby -= ShowLobby;
        _sessionBound = false;
    }

    void DisableScenePlayers()
    {
        ResolveScenePlayers();

        if (redPlayer != null)
        {
            redPlayer.SetActive(false);
        }

        if (bluePlayer != null)
        {
            bluePlayer.SetActive(false);
        }
    }

    void ResolveScenePlayers()
    {
        if (redPlayer == null)
        {
            redPlayer = FindSceneObjectIncludingInactive("RED");
        }

        if (bluePlayer == null)
        {
            bluePlayer = FindSceneObjectIncludingInactive("BLUE");
        }
    }

    static GameObject FindSceneObjectIncludingInactive(string objectName)
    {
        GameObject active = GameObject.Find(objectName);
        if (active != null)
        {
            return active;
        }

        Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform t = transforms[i];
            if (t == null || t.name != objectName || t.gameObject.scene.name == null)
            {
                continue;
            }

            // 只要场景里的物体，不要预制体资源
            if (!t.gameObject.scene.IsValid() || !t.gameObject.scene.isLoaded)
            {
                continue;
            }

            return t.gameObject;
        }

        return null;
    }

    void HideLobbyVisuals(bool hideOverviewCamera)
    {
        if (_panelRoot != null)
        {
            _panelRoot.SetActive(false);
        }

        if (_lobbyCanvasRoot != null)
        {
            _lobbyCanvasRoot.SetActive(false);
        }

        if (hideOverviewCamera)
        {
            if (_overviewCamera != null)
            {
                _overviewCamera.tag = "Untagged";
            }

            RuntimeUiFactory.SetCameraActive(_overviewCamera, false);
        }
    }

    /// <summary>选完阵营、玩家相机开启后再关掉俯视相机。</summary>
    public static void HideOverviewForGameplay()
    {
        SteamLobbyUI lobbyUi = Instance;
        if (lobbyUi == null)
        {
            return;
        }

        if (lobbyUi._overviewCamera != null)
        {
            lobbyUi._overviewCamera.tag = "Untagged";
        }

        RuntimeUiFactory.SetCameraActive(lobbyUi._overviewCamera, false);
    }

    void BuildUi()
    {
        Canvas canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "SteamLobbyCanvas", RuntimeUiFactory.LobbySortingOrder);
        _lobbyCanvasRoot = canvas.gameObject;
        _panelRoot = RuntimeUiFactory.CreatePanel(canvas.transform, "Panel", new Color(0f, 0f, 0f, 0.55f));

        RuntimeUiFactory.CreateText(_panelRoot.transform, "Title", "Steam 1v1 联机测试", 56, new Vector2(0f, 280f), new Vector2(900f, 80f), FontStyle.Bold);
        _statusText = RuntimeUiFactory.CreateText(_panelRoot.transform, "Status", "正在连接 Steam...", 26, new Vector2(0f, 190f), new Vector2(1200f, 80f));

        RuntimeUiFactory.CreateButton(_panelRoot.transform, "Host", "创建房间（主机）", new Vector2(0f, 80f), new Color(0.16f, 0.55f, 0.32f), () =>
        {
            SteamLobbySession.Instance?.HostGame();
        });

        RuntimeUiFactory.CreateButton(_panelRoot.transform, "Invite", "邀请好友", new Vector2(0f, -20f), new Color(0.18f, 0.38f, 0.75f), () =>
        {
            SteamLobbySession.Instance?.InviteFriends();
        });

        RuntimeUiFactory.CreateButton(_panelRoot.transform, "CopyId", "复制邀请码", new Vector2(0f, -120f), new Color(0.35f, 0.35f, 0.4f), () =>
        {
            SteamLobbySession.Instance?.CopyLobbyId();
        });

        _lobbyIdInput = RuntimeUiFactory.CreateInput(_panelRoot.transform, "LobbyInput", "粘贴 6 位邀请码", new Vector2(-160f, -220f), new Vector2(420f, 56f));
        RuntimeUiFactory.CreateButton(_panelRoot.transform, "Join", "加入房间", new Vector2(220f, -220f), new Color(0.7f, 0.45f, 0.12f), JoinFromInput, new Vector2(240f, 56f));
        RuntimeUiFactory.CreateButton(_panelRoot.transform, "Offline", "单机练习（不联机）", new Vector2(0f, -320f), new Color(0.4f, 0.4f, 0.4f), StartOffline);
    }

    void JoinFromInput()
    {
        if (_lobbyIdInput == null || string.IsNullOrWhiteSpace(_lobbyIdInput.text))
        {
            OnStatusChanged("请先粘贴 6 位邀请码。");
            return;
        }

        string raw = JoinCodeUtility.Normalize(_lobbyIdInput.text);
        if (JoinCodeUtility.IsValid(raw))
        {
            SteamLobbySession.Instance?.JoinByCode(raw);
            return;
        }

        if (raw.Length >= 10 && ulong.TryParse(raw, out ulong lobbyValue))
        {
            SteamLobbySession.Instance?.JoinLobby(new CSteamID(lobbyValue));
            return;
        }

        OnStatusChanged("邀请码必须是 6 位，例如 K7M2QX。");
    }

    /// <summary>玩家退出房间：真正断开连接并回到开房大厅。只有这条路会断网。</summary>
    public void QuitToLobby()
    {
        if (SteamLobbySession.Instance != null)
        {
            SteamLobbySession.Instance.LeaveSession();
            return;
        }

        ShowLobby();
    }

    /// <summary>
    /// 结算后进入等待下一局：连接保持不变，直接在对局场景里弹出选阵营。
    /// 双方重新选完阵营后，TryStartMatch 会自动重载场景并开新一局。
    /// </summary>
    public void EnterPostMatchWaiting()
    {
        if (SteamLobbySession.Instance != null)
        {
            SteamLobbySession.Instance.PrepareNextRoundKeepingSession();
        }

        // 不能复用 ShowFactionSelect()：它会打开大厅俯视相机（坐标属于大厅场景）并抢走
        // 玩家相机的 AudioListener。结算时人还在对局场景，只需要把面板弹出来。
        // 大厅视觉已由 HideForMatchEnd 收起，这里不再碰相机。
        UI.FactionSelectUI factionUi = UI.FactionSelectUI.Instance;
        if (factionUi == null)
        {
            factionUi = FindFirstObjectByType<UI.FactionSelectUI>(FindObjectsInactive.Include);
        }

        if (factionUi == null)
        {
            OnStatusChanged("找不到选阵营界面，无法开始下一局。");
            return;
        }

        factionUi.gameObject.SetActive(true);
        factionUi.ShowUI(true); // ShowUI(true) 内部已做 GameplayGate.Block 与解锁鼠标

        // 结算时若玩家正处于死亡/倒地，伤情面板会留在屏幕上；下一局开始前先清掉。
        UI.CombatStatusUI.Instance?.Hide();
        OnStatusChanged("对局结束，请重新选择阵营开始下一局。");
    }

    /// <summary>结算播报显示期间先收起大厅，等玩家点击后再打开。</summary>
    public static void HideForMatchEnd()
    {
        if (Instance == null)
        {
            return;
        }

        Instance.HideLobbyVisuals(true);
    }

    void ShowLobby()
    {
        if (UI.MatchEndUI.IsAwaitingDismiss || UI.DisconnectNoticeUI.IsAwaitingDismiss)
        {
            return;
        }
        PauseGate.Resume();
        GameplayGate.Block();
        UI.CombatStatusUI.Instance?.Hide(); // 兜底：任何回到大厅的路径都不该带着死亡黑幕

        if (_lobbyCanvasRoot != null)
        {
            _lobbyCanvasRoot.SetActive(true);
        }

        if (_panelRoot != null)
        {
            _panelRoot.SetActive(true);
        }

        if (_overviewCamera != null)
        {
            _overviewCamera.tag = "MainCamera";
        }

        RuntimeUiFactory.SetCameraActive(_overviewCamera, true);
        HideFactionSelect();
        DisableScenePlayers();
        if (!SceneManager.GetActiveScene().name.Equals(GameScenes.OfflinePractice)
            && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
        {
            SceneManager.LoadScene(GameScenes.OfflinePractice);
        }

        OnStatusChanged("已返回大厅。可以重新创建房间或加入。");
    }

    void StartOffline()
    {
        DisableScenePlayers();
        if (SteamLobbySession.Instance == null)
        {
            OnStatusChanged("找不到会话组件。");
            return;
        }

        if (!SteamLobbySession.Instance.StartOfflineHost())
        {
            OnStatusChanged("单机练习启动失败。请确认场景里有 UnityTransport。");
            ShowLobby();
        }
    }

    /// <summary>
    /// 主机定向通知客户端弹选阵营。与本机 NetworkStarted 共用同一条路径，确保会关掉大厅面板。
    /// </summary>
    public void ShowFactionSelectRequestedByServer()
    {
        ShowFactionSelect();
        OnStatusChanged("请选择红方或蓝方。");
    }

    /// <summary>
    /// 主机侧需要选阵营时弹面板。客户端不再走这里（它拿不到权威的对局状态），
    /// 改由主机定向发 RequestFactionSelectForClient。
    /// </summary>
    void ShowOnlineFactionSelect()
    {
        ShowFactionSelectRequestedByServer();
    }

    void ShowFactionSelect()
    {
        HideLobbyVisuals(hideOverviewCamera: false);

        // 只有人还在大厅场景时才切俯视相机。结算后重新开局时人已经在对局场景，
        // 打开这台相机会把镜头拉到大厅坐标，还会通过 SetExclusiveAudioListener
        // 关掉玩家相机的 AudioListener（表现为对局中突然没有声音）。
        if (IsInLobbyScene() && _overviewCamera != null)
        {
            _overviewCamera.tag = "MainCamera";
            RuntimeUiFactory.SetCameraActive(_overviewCamera, true);
        }

        GameplayGate.Block();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        UI.FactionSelectUI factionUi = UI.FactionSelectUI.Instance;
        if (factionUi == null)
        {
            factionUi = FindFirstObjectByType<UI.FactionSelectUI>(FindObjectsInactive.Include);
        }

        if (factionUi == null)
        {
            OnStatusChanged("找不到选阵营界面。请在场景 Canvas 上挂 UI.FactionSelectUI。");
            ShowLobby();
            return;
        }

        factionUi.gameObject.SetActive(true);
        factionUi.ShowUI(true);
    }

    /// <summary>当前是否还在大厅（单机练习）场景。俯视相机只在大厅场景有意义。</summary>
    static bool IsInLobbyScene() =>
        SceneManager.GetActiveScene().name.Equals(GameScenes.OfflinePractice);

    void HideFactionSelect()
    {
        if (UI.FactionSelectUI.Instance != null)
        {
            UI.FactionSelectUI.Instance.ShowUI(false);
        }

        if (offlineFactionUi != null)
        {
            offlineFactionUi.ResetVisuals();
            offlineFactionUi.gameObject.SetActive(false);
        }
    }

    /// <summary>选完阵营后才真正开始操作角色。</summary>
    public static void ReleaseGameplay()
    {
        GameplayGate.Release();
    }

    void OnStatusChanged(string message)
    {
        if (_statusText != null)
        {
            _statusText.text = message;
        }
    }
}
