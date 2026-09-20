using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// 非主机掉线提示。先挡住大厅，点屏幕任意位置再退出回大厅。
    /// </summary>
    public sealed class DisconnectNoticeUI : MonoBehaviour
    {
        public static DisconnectNoticeUI Instance { get; private set; }

        GameObject _canvasRoot;
        Text _body;
        bool _awaitingDismiss;

        /// <summary>提示还在等点击时，大厅不能同时打开。</summary>
        public static bool IsAwaitingDismiss => Instance != null && Instance._awaitingDismiss;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public static DisconnectNoticeUI EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var host = new GameObject("DisconnectNoticeUI");
            DontDestroyOnLoad(host);
            Instance = host.AddComponent<DisconnectNoticeUI>();
            Instance.Build();
            return Instance;
        }

        void Build()
        {
            RuntimeUiFactory.EnsureEventSystem();
            Canvas canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "DisconnectNoticeCanvas", 130);
            _canvasRoot = canvas.gameObject;
            RuntimeUiFactory.CreatePanel(_canvasRoot.transform, "Dim", new Color(0f, 0f, 0f, 0.78f));
            Text title = RuntimeUiFactory.CreateText(
                _canvasRoot.transform,
                "Title",
                "连接已断开",
                64,
                new Vector2(0f, 80f),
                new Vector2(1100f, 100f),
                FontStyle.Bold,
                new Color(0.95f, 0.35f, 0.3f));
            title.alignment = TextAnchor.MiddleCenter;
            _body = RuntimeUiFactory.CreateText(
                _canvasRoot.transform,
                "Body",
                "",
                28,
                new Vector2(0f, -40f),
                new Vector2(1100f, 180f),
                FontStyle.Normal,
                Color.white);
            _body.alignment = TextAnchor.MiddleCenter;
            RuntimeUiFactory.CreateText(
                _canvasRoot.transform,
                "Hint",
                "点击任意位置退出",
                22,
                new Vector2(0f, -180f),
                new Vector2(900f, 40f),
                FontStyle.Normal,
                new Color(1f, 1f, 1f, 0.85f)).alignment = TextAnchor.MiddleCenter;
            _canvasRoot.SetActive(false);
        }

        void Update()
        {
            if (!_awaitingDismiss || _canvasRoot == null || !_canvasRoot.activeSelf)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (Input.GetMouseButtonDown(0))
            {
                Dismiss();
            }
        }

        /// <summary>先关掉提示，再回大厅。</summary>
        void Dismiss()
        {
            _awaitingDismiss = false;
            if (_canvasRoot != null)
            {
                _canvasRoot.SetActive(false);
            }

            if (SteamLobbySession.Instance != null)
            {
                SteamLobbySession.Instance.LeaveSession();
                return;
            }

            SteamLobbyUI.Instance?.QuitToLobby();
        }

        /// <summary>弹出掉线原因。reason 由主机侧的 Steam 断开原因翻译而来。</summary>
        public void Show(string reason)
        {
            if (_canvasRoot == null)
            {
                Build();
            }

            _body.text = string.IsNullOrEmpty(reason)
                ? "与主机失去连接。"
                : reason;
            SteamLobbyUI.HideForMatchEnd();
            GameplayGate.Block();
            _awaitingDismiss = true;
            _canvasRoot.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
