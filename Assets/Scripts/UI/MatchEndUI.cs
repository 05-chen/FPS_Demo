using Core;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// 结算弹窗：VICTORY / DEFEAT / DRAW。由 MatchGameManager ClientRpc 唤起。
    /// </summary>
    public sealed class MatchEndUI : MonoBehaviour
    {
        public static MatchEndUI Instance { get; private set; }

        GameObject _canvasRoot;
        Text _title;
        Text _subtitle;
        bool _awaitingDismiss;

        /// <summary>播报还在等点击时，大厅不能同时打开。</summary>
        public static bool IsAwaitingDismiss => Instance != null && Instance._awaitingDismiss;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public static MatchEndUI EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var host = new GameObject("MatchEndUI");
            Instance = host.AddComponent<MatchEndUI>();
            Instance.Build();
            return Instance;
        }

        void Build()
        {
            RuntimeUiFactory.EnsureEventSystem();
            Canvas canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "MatchEndCanvas", 120);
            _canvasRoot = canvas.gameObject;
            RuntimeUiFactory.CreatePanel(_canvasRoot.transform, "Dim", new Color(0f, 0f, 0f, 0.72f));
            _title = RuntimeUiFactory.CreateText(
                _canvasRoot.transform,
                "Title",
                "",
                72,
                Vector2.zero,
                new Vector2(900f, 120f),
                FontStyle.Bold,
                Color.white);
            _title.alignment = TextAnchor.MiddleCenter;
            _subtitle = RuntimeUiFactory.CreateText(
                _canvasRoot.transform,
                "Subtitle",
                "",
                28,
                new Vector2(0f, -90f),
                new Vector2(900f, 60f),
                FontStyle.Normal,
                Color.white);
            _subtitle.alignment = TextAnchor.MiddleCenter;
            RuntimeUiFactory.CreateText(
                _canvasRoot.transform,
                "Hint",
                "点击任意位置返回大厅",
                22,
                new Vector2(0f, -160f),
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

        /// <summary>先关掉播报，再打开大厅，避免两层 UI 叠在同一屏。</summary>
        void Dismiss()
        {
            _awaitingDismiss = false;
            if (_canvasRoot != null)
            {
                _canvasRoot.SetActive(false);
            }

            World.MatchGameManager.ContinueAfterMatchEnd();
        }

        /// <summary>winner=None 为平局；按本地玩家阵营显示胜/负。</summary>
        public void Show(TeamId winner, bool isSweep)
        {
            if (_canvasRoot == null)
            {
                Build();
            }

            TeamId localTeam = TeamId.None;
            PlayerController local = PlayerRegistry.FindLocalOwned();
            if (local != null)
            {
                localTeam = local.ResolveTeam();
            }

            if (!TeamIdUtil.IsPlayable(winner))
            {
                _title.text = "DRAW";
                _title.color = Color.yellow;
                _subtitle.text = isSweep ? "对局结束" : "时间到 · 占领数相同";
            }
            else if (localTeam == winner)
            {
                _title.text = "VICTORY";
                _title.color = new Color(0.35f, 0.95f, 0.4f);
                _subtitle.text = isSweep ? "已占领对方指挥部" : "时间到 · 占领战区更多";
            }
            else
            {
                _title.text = "DEFEAT";
                _title.color = new Color(0.95f, 0.3f, 0.3f);
                _subtitle.text = isSweep ? "对方已占领我方指挥部" : "时间到 · 占领战区更少";
            }

            SteamLobbyUI.HideForMatchEnd();
            _awaitingDismiss = true;
            _canvasRoot.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
