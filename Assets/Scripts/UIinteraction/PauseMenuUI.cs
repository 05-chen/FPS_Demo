using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Esc 暂停：继续游戏，或退出到大厅（不关整个游戏）。
/// </summary>
[DisallowMultipleComponent]
public sealed class PauseMenuUI : MonoBehaviour
{
    GameObject _canvasRoot;
    GameObject _panelRoot;
    System.Action _onQuitToLobby;

    public static PauseMenuUI Create(Transform parent, System.Action onQuitToLobby)
    {
        var holder = new GameObject("PauseMenu");
        holder.transform.SetParent(parent, false);
        var menu = holder.AddComponent<PauseMenuUI>();
        menu._onQuitToLobby = onQuitToLobby;
        return menu;
    }

    void Awake()
    {
        RuntimeUiFactory.EnsureEventSystem();
        BuildUi();
        SetVisible(false);
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape))
        {
            return;
        }

        if (PauseGate.IsPaused)
        {
            ResumeGame();
            return;
        }

        if (GameplayGate.IsBlocked)
        {
            return;
        }

        PauseGame();
    }

    void PauseGame()
    {
        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        PauseGate.Pause(freezeTime: !networked);
        SetVisible(true);
    }

    void ResumeGame()
    {
        PauseGate.Resume();
        SetVisible(false);
    }

    void QuitToLobby()
    {
        PauseGate.Resume();
        SetVisible(false);
        _onQuitToLobby?.Invoke();
    }

    void SetVisible(bool visible)
    {
        if (_canvasRoot != null)
        {
            _canvasRoot.SetActive(visible);
            return;
        }

        if (_panelRoot != null)
        {
            _panelRoot.SetActive(visible);
        }
    }

    void BuildUi()
    {
        var canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "PauseCanvas", RuntimeUiFactory.PauseSortingOrder);
        _canvasRoot = canvas.gameObject;
        _panelRoot = RuntimeUiFactory.CreatePanel(canvas.transform, "PausePanel", new Color(0f, 0f, 0f, 0.65f));

        RuntimeUiFactory.CreateText(
            _panelRoot.transform,
            "Title",
            "游戏暂停",
            56,
            new Vector2(0f, 160f),
            new Vector2(600f, 80f),
            FontStyle.Bold);

        RuntimeUiFactory.CreateText(
            _panelRoot.transform,
            "Hint",
            "再按 Esc 也可继续",
            24,
            new Vector2(0f, 90f),
            new Vector2(600f, 40f),
            FontStyle.Normal,
            new Color(0.85f, 0.85f, 0.85f));

        RuntimeUiFactory.CreateButton(
            _panelRoot.transform,
            "Resume",
            "继续游戏",
            new Vector2(0f, 0f),
            new Color(0.16f, 0.55f, 0.32f),
            ResumeGame);

        RuntimeUiFactory.CreateButton(
            _panelRoot.transform,
            "Quit",
            "退出到大厅",
            new Vector2(0f, -110f),
            new Color(0.65f, 0.22f, 0.2f),
            QuitToLobby);
    }
}
