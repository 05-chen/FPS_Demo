using System;
using Steamworks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Networking.Ngo
{
    /// <summary>
    /// NGO 会话：Host/Client/Offline 启动、关闭、传输切换与连接回调绑定。
    /// </summary>
    public sealed class NetworkSessionService
    {
        const string LogCategory = "SteamLobby";
        const float RelayTimeoutSeconds = 20f;

        readonly ISessionHost _host;
        readonly MonoBehaviour _runner;

        bool _callbacksBound;
        Coroutine _relayRoutine;

        public event Action NetworkStarted;
        public event Action<ulong> ClientConnected;
        public event Action<ulong> ClientDisconnected;

        public bool IsOfflineSession { get; private set; }

        /// <summary>主机新开一局标记，场景加载后由 Match 协调器消费。</summary>
        public bool HostOpenedNewRound { get; set; }

        public NetworkSessionService(ISessionHost host, MonoBehaviour runner)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public static bool IsHost()
        {
            return NetworkManager.Singleton != null
                && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer);
        }

        public static bool IsListening()
        {
            return NetworkManager.Singleton != null
                && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsHost);
        }

        public void BindCallbacks()
        {
            if (NetworkManager.Singleton == null)
            {
                return;
            }

            Managers.SpawnManager.ConfigureDelayedPlayerSpawn(NetworkManager.Singleton);

            if (_callbacksBound)
            {
                return;
            }

            NetworkManager.Singleton.OnClientConnectedCallback += OnNetClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnNetClientDisconnected;
            _callbacksBound = true;
        }

        public void UnbindCallbacks()
        {
            if (!_callbacksBound || NetworkManager.Singleton == null)
            {
                return;
            }

            NetworkManager.Singleton.OnClientConnectedCallback -= OnNetClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnNetClientDisconnected;
            _callbacksBound = false;
        }

        public void Shutdown()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        public bool StartNetworkHost()
        {
            if (NetworkManager.Singleton == null)
            {
                GameLog.Error(LogCategory, "场景里没有 NetworkManager。");
                return false;
            }

            DestroyOfflineScenePlayers();
            BindCallbacks();
            UseSteamTransport();
            IsOfflineSession = false;
            Managers.SpawnManager.ConfigureDelayedPlayerSpawn(NetworkManager.Singleton);
            bool started = NetworkManager.Singleton.StartHost();
            HostOpenedNewRound = started;
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
                _host.Notify("已进入单机练习。请选择红方或蓝方。");
                NetworkStarted?.Invoke();
                return true;
            }

            DestroyOfflineScenePlayers();
            BindCallbacks();
            UseUnityTransport();
            IsOfflineSession = true;
            Managers.SpawnManager.ConfigureDelayedPlayerSpawn(NetworkManager.Singleton);
            if (!NetworkManager.Singleton.StartHost())
            {
                HostOpenedNewRound = false;
                UseSteamTransport();
                IsOfflineSession = false;
                return false;
            }

            HostOpenedNewRound = true;
            _host.Notify("已进入单机练习。请选择红方或蓝方。");
            NetworkStarted?.Invoke();
            return true;
        }

        public void BeginClientAfterLobbyEnter(CSteamID owner)
        {
            _host.SetState(LobbySessionState.WaitingRelay);
            _host.Notify("已进入大厅，正在连接房主 " + owner + " ...");
            StopRelayWait();
            _relayRoutine = _runner.StartCoroutine(WaitRelayThenStartClient(owner));
        }

        public void StopRelayWait()
        {
            if (_relayRoutine == null)
            {
                return;
            }

            _runner.StopCoroutine(_relayRoutine);
            _relayRoutine = null;
        }

        public void ClearOfflineFlag()
        {
            IsOfflineSession = false;
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

        static void DestroyOfflineScenePlayers()
        {
            PlayerController[] players = UnityEngine.Object.FindObjectsByType<PlayerController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                PlayerController player = players[i];
                if (player != null && !player.IsSpawned)
                {
                    UnityEngine.Object.Destroy(player.gameObject);
                }
            }
        }

        System.Collections.IEnumerator WaitRelayThenStartClient(CSteamID owner)
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

                _host.Notify("正在准备 Steam 中继网络（" + availability + "）...");
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
                _host.Fail("场景里没有 NetworkManager。");
                return;
            }

            if (IsListening())
            {
                _host.Notify("已经在联机中。");
                return;
            }

            DestroyOfflineScenePlayers();
            BindCallbacks();
            UseSteamTransport();
            Managers.SpawnManager.ConfigureDelayedPlayerSpawn(NetworkManager.Singleton);
            var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as SteamNetworkTransport;
            if (transport == null)
            {
                _host.Fail("NetworkManager 没有挂 SteamNetworkTransport。");
                return;
            }

            transport.ConnectToSteamId = owner;
            _host.SetState(LobbySessionState.ConnectingToHost);
            if (!NetworkManager.Singleton.StartClient())
            {
                _host.Fail("NGO StartClient 失败。");
                return;
            }

            _host.Notify("正在通过 Steam 连接房主 " + owner + " ...");
        }

        void OnNetClientConnected(ulong clientId)
        {
            ClientConnected?.Invoke(clientId);
        }

        void OnNetClientDisconnected(ulong clientId)
        {
            ClientDisconnected?.Invoke(clientId);
        }
    }
}
