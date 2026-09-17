using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 运行时 UI 工厂。大厅和选阵营共用同一套创建规则，避免两份复制粘贴。
/// </summary>
public static class RuntimeUiFactory
{
    public const int LobbySortingOrder = 20;
    public const int CombatStatusSortingOrder = 90;
    public const int FactionSortingOrder = 100;
    public const int PauseSortingOrder = 110;

    public static Canvas CreateOverlayCanvas(Transform parent, string name, int sortingOrder)
    {
        var canvasObject = new GameObject(name);
        canvasObject.transform.SetParent(parent, false);

        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    public static GameObject CreatePanel(Transform parent, string name, Color background)
    {
        GameObject panel = CreateUiObject(name, parent);
        StretchFull(panel.GetComponent<RectTransform>());
        var image = panel.AddComponent<Image>();
        image.color = background;
        return panel;
    }

    public static Text CreateText(Transform parent, string name, string content, int fontSize, Vector2 position, Vector2 size, FontStyle style = FontStyle.Normal, Color? color = null)
    {
        GameObject textObject = CreateUiObject(name, parent);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        var text = textObject.AddComponent<Text>();
        text.text = content;
        text.font = GetUiFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color ?? Color.white;
        text.raycastTarget = false;
        return text;
    }

    public static Button CreateButton(Transform parent, string name, string label, Vector2 position, Color color, UnityEngine.Events.UnityAction onClick, Vector2? size = null, int fontSize = 28)
    {
        Vector2 buttonSize = size ?? new Vector2(420f, 70f);
        GameObject buttonObject = CreateUiObject(name, parent);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.sizeDelta = buttonSize;
        rect.anchoredPosition = position;

        var image = buttonObject.AddComponent<Image>();
        image.color = color;

        var button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = color * 1.15f;
        colors.pressedColor = color * 0.8f;
        colors.selectedColor = color;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        CreateText(buttonObject.transform, "Label", label, fontSize, Vector2.zero, buttonSize, FontStyle.Bold);
        return button;
    }

    public static InputField CreateInput(Transform parent, string name, string placeholder, Vector2 position, Vector2 size)
    {
        GameObject inputObject = CreateUiObject(name, parent);
        RectTransform rect = inputObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        var image = inputObject.AddComponent<Image>();
        image.color = Color.white;

        GameObject placeholderObject = CreateUiObject("Placeholder", inputObject.transform);
        StretchFull(placeholderObject.GetComponent<RectTransform>());
        var placeholderText = placeholderObject.AddComponent<Text>();
        placeholderText.font = GetUiFont();
        placeholderText.fontSize = 22;
        placeholderText.color = new Color(0.4f, 0.4f, 0.4f);
        placeholderText.alignment = TextAnchor.MiddleCenter;
        placeholderText.text = placeholder;

        GameObject textObject = CreateUiObject("Text", inputObject.transform);
        StretchFull(textObject.GetComponent<RectTransform>());
        var text = textObject.AddComponent<Text>();
        text.font = GetUiFont();
        text.fontSize = 22;
        text.color = Color.black;
        text.alignment = TextAnchor.MiddleCenter;
        text.supportRichText = false;

        var input = inputObject.AddComponent<InputField>();
        input.textComponent = text;
        input.placeholder = placeholderText;
        return input;
    }

    public static Camera CreateOverviewCamera(Transform parent, string name, Vector3 position, Vector3 lookAt, int depth = -1)
    {
        var overviewObject = new GameObject(name);
        overviewObject.transform.SetParent(parent, false);
        overviewObject.transform.position = position;
        overviewObject.transform.LookAt(lookAt);

        var camera = overviewObject.AddComponent<Camera>();
        camera.depth = depth;
        var listener = overviewObject.AddComponent<AudioListener>();
        listener.enabled = false;
        return camera;
    }

    public static void SetCameraActive(Camera camera, bool enabled)
    {
        if (camera == null)
        {
            return;
        }

        camera.enabled = enabled;
        var listener = camera.GetComponent<AudioListener>();
        if (listener == null)
        {
            return;
        }

        if (enabled)
        {
            SetExclusiveAudioListener(listener);
        }
        else
        {
            listener.enabled = false;
        }
    }

    /// <summary>
    /// 场景里同时只能有一个启用的 AudioListener，否则 Unity 会每帧刷警告。
    /// </summary>
    public static void SetExclusiveAudioListener(AudioListener keepEnabled)
    {
        AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener == null)
            {
                continue;
            }

            listener.enabled = listener == keepEnabled;
        }
    }

    public static void DisableAllAudioListeners()
    {
        AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] != null)
            {
                listeners[i].enabled = false;
            }
        }
    }

    public static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        var eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
    }

    public static GameObject CreateUiObject(string name, Transform parent)
    {
        var uiObject = new GameObject(name, typeof(RectTransform));
        uiObject.transform.SetParent(parent, false);
        return uiObject;
    }

    public static void StretchFull(RectTransform rect)
    {
        if (rect == null)
        {
            return;
        }

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public static Font GetUiFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        if (font == null)
        {
            font = Font.CreateDynamicFontFromOSFont("Arial", 16);
        }

        return font;
    }
}
