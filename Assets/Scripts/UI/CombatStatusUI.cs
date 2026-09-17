using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// 局内伤情 HUD：死亡黑屏 vs 倒地等待救援。运行时创建，不依赖场景物体。
    /// </summary>
    public class CombatStatusUI : MonoBehaviour
    {
        public static CombatStatusUI Instance { get; private set; }

        const float FadeDuration = 0.85f;

        enum PanelMode
        {
            Hidden,
            Downed,
            Dead
        }

        GameObject _canvasRoot;
        Image _blackout;
        Text _title;
        Text _subtitle;
        Text _giveUpHint;
        RectTransform _giveUpFillRect;
        GameObject _giveUpRoot;
        PanelMode _mode;
        float _deadlineUnscaled;
        Coroutine _fadeRoutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
        }

        public static CombatStatusUI EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var host = new GameObject("CombatStatusUI");
            DontDestroyOnLoad(host);
            Instance = host.AddComponent<CombatStatusUI>();
            Instance.Build();
            return Instance;
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
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void LateUpdate()
        {
            if (_mode == PanelMode.Hidden || _subtitle == null)
            {
                return;
            }

            float remain = Mathf.Max(0f, _deadlineUnscaled - Time.unscaledTime);
            int seconds = Mathf.CeilToInt(remain);
            if (_mode == PanelMode.Dead)
            {
                _subtitle.text = remain <= 0f
                    ? "等待服务器确认重生..."
                    : "将在 " + seconds + " 秒后返回重生点...";
            }
            else
            {
                _subtitle.text = "失血倒计时：" + seconds + " 秒";
            }
        }

        public void SetGiveUpProgress(float normalized)
        {
            if (_giveUpRoot == null)
            {
                return;
            }

            bool show = _mode == PanelMode.Downed;
            _giveUpRoot.SetActive(show);
            if (!show || _giveUpFillRect == null)
            {
                return;
            }

            float amount = Mathf.Clamp01(normalized);
            _giveUpFillRect.sizeDelta = new Vector2(420f * amount, 18f);
            if (_giveUpHint != null)
            {
                _giveUpHint.text = amount > 0.02f
                    ? "放弃中... " + Mathf.RoundToInt(amount * 100f) + "%"
                    : "长按空格放弃救援";
            }
        }

        public void ShowDead(float respawnDelaySeconds)
        {
            Build();
            _mode = PanelMode.Dead;
            _deadlineUnscaled = Time.unscaledTime + Mathf.Max(0.1f, respawnDelaySeconds);
            _title.text = "YOU ARE DEAD\n你已阵亡";
            _title.fontSize = 64;
            _title.rectTransform.anchoredPosition = new Vector2(0f, 40f);
            _subtitle.rectTransform.anchoredPosition = new Vector2(0f, -80f);
            _blackout.gameObject.SetActive(true);
            if (_giveUpRoot != null)
            {
                _giveUpRoot.SetActive(false);
            }
            _canvasRoot.SetActive(true);
            StartFade(0f, 0.92f);
        }

        public void ShowDowned(float bleedOutSeconds)
        {
            Build();
            _mode = PanelMode.Downed;
            _deadlineUnscaled = Time.unscaledTime + Mathf.Max(0.1f, bleedOutSeconds);
            _title.text = "正在等待救援中...";
            _title.fontSize = 40;
            _title.rectTransform.anchoredPosition = new Vector2(0f, -280f);
            _subtitle.rectTransform.anchoredPosition = new Vector2(0f, -340f);
            _blackout.gameObject.SetActive(false);
            SetBlackoutAlpha(0f);
            if (_giveUpRoot != null)
            {
                _giveUpRoot.SetActive(true);
            }

            SetGiveUpProgress(0f);
            _canvasRoot.SetActive(true);
        }

        public void Hide()
        {
            _mode = PanelMode.Hidden;
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            SetBlackoutAlpha(0f);
            if (_canvasRoot != null)
            {
                _canvasRoot.SetActive(false);
            }
        }

        void Build()
        {
            if (_canvasRoot != null)
            {
                return;
            }

            RuntimeUiFactory.EnsureEventSystem();
            Canvas canvas = RuntimeUiFactory.CreateOverlayCanvas(
                transform,
                "CombatStatusCanvas",
                RuntimeUiFactory.CombatStatusSortingOrder);
            _canvasRoot = canvas.gameObject;

            GameObject black = RuntimeUiFactory.CreatePanel(
                canvas.transform,
                "Blackout",
                new Color(0f, 0f, 0f, 0f));
            _blackout = black.GetComponent<Image>();
            _blackout.raycastTarget = false;

            _title = RuntimeUiFactory.CreateText(
                canvas.transform,
                "Title",
                "",
                48,
                Vector2.zero,
                new Vector2(1200f, 180f),
                FontStyle.Bold,
                Color.white);

            _subtitle = RuntimeUiFactory.CreateText(
                canvas.transform,
                "Subtitle",
                "",
                28,
                new Vector2(0f, -80f),
                new Vector2(1000f, 60f),
                FontStyle.Normal,
                new Color(0.85f, 0.85f, 0.85f));

            BuildGiveUpBar(canvas.transform);
            _canvasRoot.SetActive(false);
        }

        void BuildGiveUpBar(Transform parent)
        {
            _giveUpRoot = RuntimeUiFactory.CreateUiObject("GiveUpRoot", parent);
            RectTransform rootRect = _giveUpRoot.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(420f, 70f);
            rootRect.anchoredPosition = new Vector2(0f, -410f);

            _giveUpHint = RuntimeUiFactory.CreateText(
                _giveUpRoot.transform,
                "GiveUpHint",
                "长按空格放弃救援",
                22,
                new Vector2(0f, 22f),
                new Vector2(420f, 28f),
                FontStyle.Normal,
                new Color(1f, 0.82f, 0.35f));

            GameObject bg = RuntimeUiFactory.CreateUiObject("GiveUpBg", _giveUpRoot.transform);
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.sizeDelta = new Vector2(420f, 18f);
            bgRect.anchoredPosition = new Vector2(0f, -8f);
            Image bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.12f, 0.12f, 0.12f, 0.85f);
            bgImage.raycastTarget = false;

            GameObject fill = RuntimeUiFactory.CreateUiObject("GiveUpFill", bg.transform);
            _giveUpFillRect = fill.GetComponent<RectTransform>();
            _giveUpFillRect.anchorMin = new Vector2(0f, 0.5f);
            _giveUpFillRect.anchorMax = new Vector2(0f, 0.5f);
            _giveUpFillRect.pivot = new Vector2(0f, 0.5f);
            _giveUpFillRect.anchoredPosition = new Vector2(-210f, 0f);
            _giveUpFillRect.sizeDelta = new Vector2(0f, 18f);
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.85f, 0.2f, 0.18f, 1f);
            fillImage.raycastTarget = false;
        }

        void StartFade(float from, float to)
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            _fadeRoutine = StartCoroutine(FadeBlack(from, to));
        }

        IEnumerator FadeBlack(float from, float to)
        {
            SetBlackoutAlpha(from);
            float elapsed = 0f;
            while (elapsed < FadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetBlackoutAlpha(Mathf.Lerp(from, to, elapsed / FadeDuration));
                yield return null;
            }

            SetBlackoutAlpha(to);
            _fadeRoutine = null;
        }

        void SetBlackoutAlpha(float alpha)
        {
            if (_blackout == null)
            {
                return;
            }

            Color color = _blackout.color;
            color.a = alpha;
            _blackout.color = color;
        }
    }
}
