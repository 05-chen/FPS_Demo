using Core;
using UnityEngine;
using UnityEngine.UI;
using World;

namespace UI
{
    /// <summary>
    /// 《人间地狱》风格顶栏：正中倒计时 + 五格战区链条（锁头 / 双色填充 / 徽章）。
    /// </summary>
    public class SectorHUDUI : MonoBehaviour
    {
        static readonly string[] SectorOrder =
        {
            "Sector_A", "Sector_B", "Sector_C", "Sector_D", "Sector_E"
        };

        static readonly string[] BadgeGlyphs = { "A", "B", "C", "D", "E" };

        static readonly string[] LegacyChildNames =
        {
            "RedBar", "BlueBar", "PointNameText", "OwnerText", "SectorStrip", "MatchTimer", "CaptureTrend"
        };

        [Header("阵营颜色")]
        [SerializeField] Color redFillColor = new Color(0.80f, 0.27f, 0.13f, 1f);
        [SerializeField] Color blueFillColor = new Color(0.00f, 0.53f, 1.00f, 1f);
        [SerializeField] Color grayFillColor = new Color(0.38f, 0.38f, 0.40f, 1f);
        [SerializeField] Color lockOverlayColor = new Color(0.05f, 0.07f, 0.10f, 0.88f);
        [SerializeField] Color lockIconColor = new Color(1f, 1f, 1f, 0.95f);

        Text _timerText;
        HudBlock[] _blocks;
        SectorManager[] _allManagers;
        Sprite _whiteSprite;
        bool _built;

        sealed class HudBlock
        {
            public Image GrayFill;
            public Image BlueFill;
            public Image RedFill;
            public GameObject LockOverlay;
            public Image LockDimmer;
            public Text Badge;
        }

        void Start()
        {
            HideLegacyChildren();
            EnsureChain();
        }

        void Update()
        {
            if (!_built)
            {
                EnsureChain();
            }

            RefreshManagerCache();
            RefreshTimer();
            RefreshBlocks();
        }

        void HideLegacyChildren()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                for (int n = 0; n < LegacyChildNames.Length; n++)
                {
                    if (child.name == LegacyChildNames[n])
                    {
                        child.gameObject.SetActive(false);
                        break;
                    }
                }
            }
        }

        void EnsureChain()
        {
            if (_built)
            {
                return;
            }

            RectTransform root = transform as RectTransform;
            if (root != null)
            {
                root.anchorMin = new Vector2(0.5f, 1f);
                root.anchorMax = new Vector2(0.5f, 1f);
                root.pivot = new Vector2(0.5f, 1f);
                root.anchoredPosition = new Vector2(0f, -12f);
                root.sizeDelta = new Vector2(640f, 88f);
            }

            _whiteSprite = CreateWhiteSprite();
            Font font = GetHudFont();

            _timerText = CreateTimer(font);
            Transform chain = CreateChain();
            _blocks = new HudBlock[SectorOrder.Length];
            for (int i = 0; i < SectorOrder.Length; i++)
            {
                _blocks[i] = CreateBlock(chain, SectorOrder[i], BadgeGlyphs[i], font);
            }

            _built = true;
            GameLog.Info("SectorHUD", "已生成五格战区链条");
        }

        Text CreateTimer(Font font)
        {
            GameObject go = new GameObject("MatchTimerText", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 28f);

            Text text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = 22;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = "15:00";
            return text;
        }

        Transform CreateChain()
        {
            GameObject go = new GameObject("SectorChain", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(0f, 0f);
            rect.offsetMax = new Vector2(0f, -30f);

            HorizontalLayoutGroup layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 3f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.padding = new RectOffset(0, 0, 0, 0);
            return go.transform;
        }

        HudBlock CreateBlock(Transform chain, string sectorId, string badge, Font font)
        {
            GameObject blockGo = new GameObject("Block_" + sectorId[^1], typeof(RectTransform));
            blockGo.transform.SetParent(chain, false);
            blockGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Image gray = CreateSolid(blockGo.transform, "GrayFill", grayFillColor);
            Image red = CreateFill(blockGo.transform, "RedFill", redFillColor, Image.OriginHorizontal.Left);
            Image blue = CreateFill(blockGo.transform, "BlueFill", blueFillColor, Image.OriginHorizontal.Right);

            GameObject overlayGo = new GameObject("LockOverlay", typeof(RectTransform));
            overlayGo.transform.SetParent(blockGo.transform, false);
            Stretch(overlayGo.GetComponent<RectTransform>());
            Image dimmer = overlayGo.AddComponent<Image>();
            dimmer.sprite = _whiteSprite;
            dimmer.color = Color.clear;
            dimmer.raycastTarget = false;
            dimmer.enabled = false;

            Text lockText = CreateCenteredText(overlayGo.transform, "LockIcon", "锁", font, 18);
            lockText.color = lockIconColor;

            Text badgeText = CreateCenteredText(blockGo.transform, "BadgeIcon", badge, font, 22);
            badgeText.fontStyle = FontStyle.Bold;

            return new HudBlock
            {
                GrayFill = gray,
                BlueFill = blue,
                RedFill = red,
                LockOverlay = overlayGo,
                LockDimmer = dimmer,
                Badge = badgeText
            };
        }

        Image CreateSolid(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());
            Image image = go.AddComponent<Image>();
            image.sprite = _whiteSprite;
            image.color = color;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        Image CreateFill(Transform parent, string name, Color color, Image.OriginHorizontal origin)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());
            Image image = go.AddComponent<Image>();
            image.sprite = _whiteSprite;
            image.color = color;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)origin;
            image.fillAmount = 0f;
            image.raycastTarget = false;
            return image;
        }

        static Text CreateCenteredText(Transform parent, string name, string content, Font font, int size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());
            Text text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = content;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static Sprite CreateWhiteSprite()
        {
            Texture2D texture = Texture2D.whiteTexture;
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                4f);
        }

        static Font GetHudFont()
        {
            Font osFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Arial" },
                22);
            return osFont != null ? osFont : RuntimeUiFactory.GetUiFont();
        }

        void RefreshManagerCache()
        {
            if (_allManagers != null && _allManagers.Length >= 5 && ManagersStillValid(_allManagers))
            {
                return;
            }

            _allManagers = SectorManager.FindAll();
        }

        /// <summary>开新局或场景重载后，丢掉旧的 SectorManager 引用。</summary>
        public static void InvalidateManagerCache()
        {
            SectorHUDUI[] huds = FindObjectsByType<SectorHUDUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < huds.Length; i++)
            {
                if (huds[i] != null)
                {
                    huds[i]._allManagers = null;
                }
            }
        }

        static bool ManagersStillValid(SectorManager[] managers)
        {
            for (int i = 0; i < managers.Length; i++)
            {
                if (managers[i] == null || !managers[i].IsSpawned)
                {
                    return false;
                }
            }

            return true;
        }

        void RefreshTimer()
        {
            if (_timerText == null)
            {
                return;
            }

            MatchGameManager match = MatchGameManager.Instance;
            if (match == null || !match.IsSpawned)
            {
                _timerText.text = "--:--";
                return;
            }

            int total = Mathf.Max(0, Mathf.CeilToInt(match.MatchTimer.Value));
            int hours = total / 3600;
            int minutes = total % 3600 / 60;
            int seconds = total % 60;
            _timerText.text = hours > 0
                ? hours + ":" + minutes.ToString("00") + ":" + seconds.ToString("00")
                : minutes.ToString("00") + ":" + seconds.ToString("00");
        }

        void RefreshBlocks()
        {
            if (_blocks == null)
            {
                return;
            }

            for (int i = 0; i < SectorOrder.Length; i++)
            {
                ApplyBlock(_blocks[i], FindCached(SectorOrder[i]));
            }
        }

        static void ApplyBlock(HudBlock block, SectorManager manager)
        {
            bool spawned = manager != null && manager.IsSpawned;
            TeamId owner = spawned ? manager.OwnerTeam.Value : TeamId.None;
            bool capturable = spawned && manager.IsCapturable;
            bool rearGuarded = spawned && manager.IsRearGuarded;
            bool isLocked = SectorCaptureRules.IsHudLocked(owner, capturable, rearGuarded);
            float progress = spawned ? manager.CaptureProgress.Value : 0f;
            bool hasBeenCaptured = spawned && manager.HasBeenCaptured.Value;
            bool hasBeenContested = spawned && manager.HasBeenContested.Value;
            int redCount = spawned ? manager.OccupantRedCount.Value : 0;
            int blueCount = spawned ? manager.OccupantBlueCount.Value : 0;
            SectorCaptureRules.ToHudBarFills(
                owner,
                progress,
                hasBeenCaptured,
                isLocked,
                hasBeenContested,
                redCount,
                blueCount,
                out float redFill,
                out float blueFill,
                out bool showGray);

            ApplyBarImages(block, redFill, blueFill, showGray);

            // 上锁只显示锁标，禁用深色遮罩，避免 D 等后方区残留未填满灰缝
            if (block.LockDimmer != null)
            {
                block.LockDimmer.enabled = false;
                block.LockDimmer.color = Color.clear;
            }

            block.LockOverlay.SetActive(isLocked);
            block.Badge.gameObject.SetActive(!isLocked);
        }

        /// <summary>
        /// 中立未完成占领时显示灰底和实际填充；已占领或争夺时显示从进度映射出的双色分界。
        /// </summary>
        static void ApplyBarImages(HudBlock block, float redFill, float blueFill, bool showGray)
        {
            block.GrayFill.gameObject.SetActive(showGray);
            if (showGray)
            {
                SetHorizontalFill(block.RedFill, Image.OriginHorizontal.Left, redFill);
                SetHorizontalFill(block.BlueFill, Image.OriginHorizontal.Right, blueFill);
                return;
            }

            // 已占领或争夺：用红底承载整格，再由蓝条从右侧覆盖到真实分界。
            block.RedFill.type = Image.Type.Simple;
            block.RedFill.fillAmount = 1f;
            block.RedFill.enabled = true;
            SetHorizontalFill(block.BlueFill, Image.OriginHorizontal.Right, blueFill);
        }

        static void SetHorizontalFill(Image image, Image.OriginHorizontal origin, float amount)
        {
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)origin;
            image.fillAmount = Mathf.Clamp01(amount);
            image.enabled = true;
        }

        SectorManager FindCached(string sectorId)
        {
            if (_allManagers == null)
            {
                return SectorManager.FindBySectorId(sectorId);
            }

            for (int i = 0; i < _allManagers.Length; i++)
            {
                if (_allManagers[i] != null && _allManagers[i].SectorId == sectorId)
                {
                    return _allManagers[i];
                }
            }

            return null;
        }
    }
}
