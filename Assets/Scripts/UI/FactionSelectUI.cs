using System.Collections;
using Core;
using Managers;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// 选阵营面板。大厅按钮能点，是因为它们挂在 sortingOrder=20 的 Overlay 上。
    /// 场景 Canvas 默认 sortingOrder=0，会被暂停菜单等高层 Canvas 挡住点击。
    /// 所以选阵营也用同一套 RuntimeUiFactory Overlay（sortingOrder=100）。
    /// </summary>
    public class FactionSelectUI : MonoBehaviour
    {
        public static FactionSelectUI Instance { get; private set; }

        [Header("场景里的旧面板（可留空，运行时会再做一套能点的按钮）")]
        [SerializeField] GameObject panel;
        [SerializeField] Button joinRedButton;
        [SerializeField] Button joinBlueButton;

        bool _spawnRequested;
        bool _isShowing;
        GameObject _runtimeCanvas;

        void Awake()
        {
            Instance = this;
            BindSceneButtons();
            ShowUI(false);
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_runtimeCanvas != null)
            {
                Destroy(_runtimeCanvas);
            }
        }

        void Start()
        {
            BindSceneButtons();
        }

        void LateUpdate()
        {
            if (!_isShowing)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void BindSceneButtons()
        {
            if (joinRedButton != null)
            {
                joinRedButton.onClick.RemoveAllListeners();
                joinRedButton.onClick.AddListener(() => OnSelectFaction(TeamId.Red));
            }

            if (joinBlueButton != null)
            {
                joinBlueButton.onClick.RemoveAllListeners();
                joinBlueButton.onClick.AddListener(() => OnSelectFaction(TeamId.Blue));
            }
        }

        public void ShowUI(bool show)
        {
            _isShowing = show;
            _spawnRequested = false;

            if (panel != null)
            {
                panel.SetActive(false);
            }

            EnsureRuntimeUi();
            if (_runtimeCanvas != null)
            {
                _runtimeCanvas.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            RaiseSceneCanvasIfPresent();
            GameplayGate.Block();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void RaiseSceneCanvasIfPresent()
        {
            Canvas sceneCanvas = GetComponent<Canvas>();
            if (sceneCanvas == null)
            {
                sceneCanvas = GetComponentInParent<Canvas>();
            }

            if (sceneCanvas == null)
            {
                return;
            }

            sceneCanvas.overrideSorting = true;
            sceneCanvas.sortingOrder = RuntimeUiFactory.FactionSortingOrder;
        }

        void EnsureRuntimeUi()
        {
            if (_runtimeCanvas != null)
            {
                return;
            }

            RuntimeUiFactory.EnsureEventSystem();
            Transform parent = SteamLobbyUI.Instance != null ? SteamLobbyUI.Instance.transform : null;
            Canvas canvas = RuntimeUiFactory.CreateOverlayCanvas(
                parent,
                "FactionSelectCanvas",
                RuntimeUiFactory.FactionSortingOrder);
            _runtimeCanvas = canvas.gameObject;

            GameObject runtimePanel = RuntimeUiFactory.CreatePanel(
                canvas.transform,
                "Panel",
                new Color(0f, 0f, 0f, 0.45f));

            RuntimeUiFactory.CreateText(
                runtimePanel.transform,
                "Title",
                "请选择阵营",
                48,
                new Vector2(0f, 160f),
                new Vector2(800f, 70f),
                FontStyle.Bold);

            RuntimeUiFactory.CreateButton(
                runtimePanel.transform,
                "JoinRed",
                "Join Red / 红队",
                new Vector2(-240f, 0f),
                new Color(0.75f, 0.16f, 0.16f),
                () => OnSelectFaction(TeamId.Red));

            RuntimeUiFactory.CreateButton(
                runtimePanel.transform,
                "JoinBlue",
                "Join Blue / 蓝队",
                new Vector2(240f, 0f),
                new Color(0.16f, 0.32f, 0.75f),
                () => OnSelectFaction(TeamId.Blue));

            _runtimeCanvas.SetActive(false);
        }

        void OnSelectFaction(TeamId team)
        {
            if (_spawnRequested)
            {
                return;
            }

            if (!TeamIdUtil.IsPlayable(team))
            {
                return;
            }

            GameLog.Info("Faction", TeamIdUtil.DisplayName(team));

            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsListening)
            {
                GameLog.Warn("Faction", "尚未进入网络会话，无法选阵营。请先创建/加入房间或进入单机练习。");
                return;
            }

            if (!network.IsServer && SpawnManager.Instance == null)
            {
                GameLog.Warn("Faction", "SpawnManager 未就绪，无法申请出生。");
                return;
            }

            if (SteamLobbySession.Instance == null)
            {
                GameLog.Warn("Faction", "找不到会话组件。");
                return;
            }

            _spawnRequested = true;
            if (network.IsServer)
            {
                SteamLobbySession.Instance.OnClientChoseFaction(network.LocalClientId, team);
            }
            else
            {
                if (SpawnManager.Instance == null)
                {
                    _spawnRequested = false;
                    GameLog.Warn("Faction", "SpawnManager 未就绪，无法申请出生。");
                    return;
                }

                SpawnManager.Instance.SubmitFactionServerRpc((int)team);
            }

            StartCoroutine(ResetSpawnFlagIfStuck());
        }

        public void OnSpawnSuccess(TeamId team, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            ShowUI(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            StopAllCoroutines();
            StartCoroutine(CompleteSpawn(team, spawnPosition, spawnRotation));
        }

        IEnumerator CompleteSpawn(TeamId team, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            NetworkObject playerObject = null;
            float deadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < deadline)
            {
                playerObject = NetworkManager.Singleton != null
                    ? NetworkManager.Singleton.LocalClient?.PlayerObject
                    : null;
                if (playerObject != null)
                {
                    break;
                }

                yield return null;
            }

            if (playerObject != null)
            {
                PlayerController controller = playerObject.GetComponent<PlayerController>();
                if (controller != null)
                {
                    controller.enabled = true;
                    controller.SetTeam(team);
                    controller.TeleportToSpawn(spawnPosition, spawnRotation);
                    if (!controller.IsSpawned)
                    {
                        controller.SetControlled(true);
                    }
                }

                GameplayGate.Release();
                SteamLobbyUI.HideOverviewForGameplay();
                yield break;
            }

            _spawnRequested = false;
            PlayerController localPlayer = PlayerController.FindLocalOwnedPlayer();
            if (localPlayer != null && localPlayer.HasChosenFaction())
            {
                GameLog.Warn("Faction", "复活传送等待超时，但阵营已记录，不再弹出选阵营。");
                yield break;
            }

            ShowUI(true);
            GameLog.Warn("Faction", "选阵营成功，但玩家物体还没同步过来。");
        }

        IEnumerator ResetSpawnFlagIfStuck()
        {
            yield return new WaitForSeconds(3f);
            if (_isShowing)
            {
                _spawnRequested = false;
            }
        }
    }
}
