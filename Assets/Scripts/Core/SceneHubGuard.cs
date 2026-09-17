using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 切场景时，新场景里会再带一份 NetworkManager / SteamLobby。
/// 旧的那份已经 DontDestroyOnLoad，这份必须立刻删掉，否则会有两个网络中枢。
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class SceneHubGuard : MonoBehaviour
{
    [SerializeField] bool isNetworkManagerHub;
    [SerializeField] bool isLobbyHub;

    void Awake()
    {
        if (isNetworkManagerHub)
        {
            NetworkManager existing = NetworkManager.Singleton;
            if (existing != null && existing.gameObject != gameObject)
            {
                DestroyImmediate(gameObject);
                return;
            }
        }

        if (isLobbyHub)
        {
            SteamLobbySession existing = SteamLobbySession.Instance;
            if (existing != null && existing.gameObject != gameObject)
            {
                DestroyImmediate(gameObject);
            }
        }
    }
}
