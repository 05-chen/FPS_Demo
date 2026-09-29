using System;
using System.Collections;
using System.Collections.Generic;
using Core;
using Networking;
using Networking.Ngo;
using Networking.Session;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using World;

namespace Match
{
    /// <summary>
    /// 比赛流程协调：选阵营后开局、场景加载生成、结算管线、下一局准备。
    /// </summary>
    public sealed class MatchSessionCoordinator
    {
        const string LogCategory = "SteamLobby";

        readonly ISessionHost _host;
        readonly MonoBehaviour _runner;
        readonly PlayerAdmissionService _admission;
        readonly NetworkSessionService _network;

        bool _matchLoadStarted;
        bool _gameplayStarted;
        bool _nextRoundPrepared;
        Coroutine _matchLoadRoutine;
        Coroutine _postMatchPipelineRoutine;

        public bool GameplayStarted => _gameplayStarted;
        public bool MatchLoadStarted => _matchLoadStarted;
        public bool NextRoundPrepared => _nextRoundPrepared;

        public MatchSessionCoordinator(
            ISessionHost host,
            MonoBehaviour runner,
            PlayerAdmissionService admission,
            NetworkSessionService network)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _admission = admission ?? throw new ArgumentNullException(nameof(admission));
            _network = network ?? throw new ArgumentNullException(nameof(network));
        }

        public void ResetMatchChoices()
        {
            _admission.Clear();
            _matchLoadStarted = false;
            _gameplayStarted = false;
            _network.HostOpenedNewRound = false;
            _nextRoundPrepared = false;
            StopMatchLoad();
            StopPostMatchPipeline();
        }

        public void StopAllRoutines()
        {
            StopMatchLoad();
            StopPostMatchPipeline();
        }

        public void OnClientChoseFaction(ulong clientId, TeamId team)
        {
            if (!TeamIdUtil.IsPlayable(team))
            {
                return;
            }

            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer)
            {
                return;
            }

            MatchRoundPhase phase = MatchGameManager.CurrentPhase;
            if (MatchGameManager.IsPostMatchBlocked)
            {
                GameLog.Warn(LogCategory, "结算/准备下一局期间拒绝阵营选择 clientId=" + clientId + " phase=" + phase);
                return;
            }

            if (_admission.GetStatus(clientId) == PlayerSessionStatus.None)
            {
                _admission.MarkConnected(clientId);
            }

            PlayerSessionStatus status = _admission.GetStatus(clientId);
            if (!PlayerSessionRules.CanSubmitFaction(status))
            {
                GameLog.Warn(LogCategory, "拒绝阵营选择：无资格 clientId="
                    + clientId + " status=" + status);
                return;
            }

            if (_admission.GetTeam(clientId) == team
                && status == PlayerSessionStatus.InMatch
                && HasSpawnedPlayer(clientId))
            {
                return;
            }

            _host.Notify("玩家 " + clientId + " 选择了" + TeamIdUtil.DisplayName(team) + "。");

            bool nextRoundReady = _nextRoundPrepared
                || MatchRules.AllowsIndependentFactionSpawn(phase);

            if (_gameplayStarted || nextRoundReady)
            {
                if (Managers.SpawnManager.Instance == null || !Managers.SpawnManager.Instance.IsSpawned)
                {
                    GameLog.Error(LogCategory, "对局场景未就绪，无法为 clientId=" + clientId + " 生成玩家。");
                    return;
                }

                _admission.MarkFactionChosen(clientId, team);
                bool spawned = Managers.SpawnManager.Instance.SpawnForClient(team, clientId);
                if (!spawned)
                {
                    _admission.MarkConnected(clientId);
                    GameLog.Error(LogCategory, "玩家生成失败，保持在选阵营阶段。clientId="
                        + clientId + " team=" + team);
                    MatchGameManager.Instance?.ServerSetRoundPhase(MatchRoundPhase.FactionSelection);
                    Managers.SpawnManager.Instance.RequestFactionSelectForClient(clientId);
                    return;
                }

                _admission.MarkInMatch(clientId, team);
                MatchGameManager.Instance?.ServerEnterPlayingIfSelecting();
                return;
            }

            _admission.MarkFactionChosen(clientId, team);
            TryStartMatch();
        }

        public void TryStartMatch()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (_gameplayStarted || _matchLoadStarted || !MatchRules.AllowsPlayerSpawn(MatchGameManager.CurrentPhase)
                || _nextRoundPrepared
                || network == null || !network.IsServer)
            {
                return;
            }

            if (MatchGameManager.CurrentPhase == MatchRoundPhase.FactionSelection && _gameplayStarted)
            {
                return;
            }

            _matchLoadStarted = true;
            if (UI.FactionSelectionPanel.Instance != null)
            {
                UI.FactionSelectionPanel.Instance.ShowUI(false);
            }

            StopMatchLoad();
            _matchLoadRoutine = _runner.StartCoroutine(LoadMatchAndSpawn());
        }

        public void ServerOnMatchEnded()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer)
            {
                return;
            }

            _nextRoundPrepared = false;
            if (_postMatchPipelineRoutine != null)
            {
                return;
            }

            _postMatchPipelineRoutine = _runner.StartCoroutine(ServerPostMatchPipeline());
        }

        public void ServerPrepareNextRoundKeepingSession()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer || !network.IsListening)
            {
                return;
            }

            if (_nextRoundPrepared && MatchGameManager.CurrentPhase == MatchRoundPhase.FactionSelection)
            {
                GameLog.Info(LogCategory, "下一局已准备过，跳过重复 Prepare。");
                return;
            }

            MatchGameManager match = MatchGameManager.Instance;
            if (match != null && match.IsSpawned)
            {
                match.ServerSetRoundPhase(MatchRoundPhase.PreparingNextRound);
            }

            StopMatchLoad();
            _matchLoadStarted = false;
            _network.HostOpenedNewRound = false;
            _admission.ResetConnectedPlayersForNextRound();

            ServerDespawnAllPlayerObjects();

            if (UI.FactionSelectionPanel.Instance != null)
            {
                UI.FactionSelectionPanel.Instance.ShowUI(false);
            }

            SectorManager.ServerResetAllForNewMatch();
            if (match != null && match.IsSpawned)
            {
                match.ServerBeginNewRound();
                match.NotifyMatchResetClientRpc();
            }
            else
            {
                GameLog.Warn(LogCategory, "PrepareNextRound 时 MatchGameManager 未就绪。");
            }

            _gameplayStarted = true;
            _nextRoundPrepared = true;
            _host.Notify("下一局已准备完成，开放阵营选择。");
        }

        IEnumerator LoadMatchAndSpawn()
        {
            string sceneName = GameScenes.ForMatch(_network.IsOfflineSession);
            _host.Notify("正在进入场景：" + sceneName);

            NetworkManager network = NetworkManager.Singleton;
            if (network == null)
            {
                yield break;
            }

            bool forceReloadForNewRound = _network.HostOpenedNewRound;
            bool networkedMatch = !_network.IsOfflineSession;
            string current = SceneManager.GetActiveScene().name;
            if (current != sceneName || forceReloadForNewRound || networkedMatch)
            {
                if (forceReloadForNewRound && current == sceneName)
                {
                    _host.Notify("主机新开一局，重新加载对局场景以同步双方顶栏。");
                }

                bool loaded = false;
                void OnLoaded(string loadedName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
                {
                    if (loadedName == sceneName)
                    {
                        loaded = true;
                    }
                }

                network.SceneManager.OnLoadEventCompleted += OnLoaded;
                SceneEventProgressStatus status = network.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
                if (status != SceneEventProgressStatus.Started)
                {
                    network.SceneManager.OnLoadEventCompleted -= OnLoaded;
                    GameLog.Error(LogCategory, "加载场景失败：" + sceneName + "，状态=" + status + "。请确认该场景已勾进 Build Settings。");
                    _matchLoadStarted = false;
                    yield break;
                }

                float deadline = Time.realtimeSinceStartup + 30f;
                while (!loaded && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                network.SceneManager.OnLoadEventCompleted -= OnLoaded;
                if (!loaded)
                {
                    GameLog.Error(LogCategory, "加载场景超时：" + sceneName);
                    _matchLoadStarted = false;
                    yield break;
                }
            }
            else
            {
                _host.Notify("单机练习已在对局场景，跳过二次 LoadScene。");
            }

            float spawnDeadline = Time.realtimeSinceStartup + 5f;
            while ((Managers.SpawnManager.Instance == null || !Managers.SpawnManager.Instance.IsSpawned)
                   && Time.realtimeSinceStartup < spawnDeadline)
            {
                yield return null;
            }

            if (Managers.SpawnManager.Instance == null || !Managers.SpawnManager.Instance.IsSpawned)
            {
                GameLog.Error(LogCategory, "场景已加载，但 SpawnManager 未就绪。");
                _matchLoadStarted = false;
                yield break;
            }

            _host.Notify("已进入对局场景。");
            if (network.IsServer)
            {
                bool openedNewRound = _network.HostOpenedNewRound;
                _network.HostOpenedNewRound = false;
                MatchGameManager.ServerHandleMatchEntry(openedNewRound);
            }

            int spawnedCount = 0;
            foreach (ulong clientId in network.ConnectedClientsIds)
            {
                if (!_admission.HasChosenTeam(clientId))
                {
                    continue;
                }

                TeamId team = _admission.GetTeam(clientId);
                if (Managers.SpawnManager.Instance.SpawnForClient(team, clientId))
                {
                    _admission.MarkInMatch(clientId, team);
                    spawnedCount++;
                }
                else
                {
                    _admission.MarkConnected(clientId);
                }
            }

            _gameplayStarted = spawnedCount > 0;
            _matchLoadStarted = false;
            _matchLoadRoutine = null;
            _nextRoundPrepared = false;

            if (network.IsServer && MatchGameManager.Instance != null && MatchGameManager.Instance.IsSpawned)
            {
                if (spawnedCount > 0)
                {
                    MatchGameManager.Instance.ServerEnterPlayingIfSelecting();
                }
                else if (MatchGameManager.CurrentPhase == MatchRoundPhase.None
                         || MatchGameManager.CurrentPhase == MatchRoundPhase.FactionSelection)
                {
                    MatchGameManager.Instance.ServerSetRoundPhase(MatchRoundPhase.FactionSelection);
                }
            }

            foreach (ulong clientId in network.ConnectedClientsIds)
            {
                if (!_admission.HasChosenTeam(clientId))
                {
                    Managers.SpawnManager.Instance.RequestFactionSelectForClient(clientId);
                }
            }

            _admission.TryProcessPendingJoins();
        }

        IEnumerator ServerPostMatchPipeline()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer)
            {
                _postMatchPipelineRoutine = null;
                yield break;
            }

            MatchGameManager match = MatchGameManager.Instance;
            if (match != null && match.IsSpawned)
            {
                match.ServerSetRoundPhase(MatchRoundPhase.PostMatchWaiting);
            }

            _host.Notify("结算等待计时开始（约 5 秒）。");
            yield return new WaitForSecondsRealtime(5f);

            network = NetworkManager.Singleton;
            if (network == null || !network.IsServer)
            {
                _postMatchPipelineRoutine = null;
                yield break;
            }

            ServerPrepareNextRoundKeepingSession();
            ServerNotifyFactionSelectAfterPrepare();
            _admission.TryProcessPendingJoins();
            _postMatchPipelineRoutine = null;
        }

        void ServerNotifyFactionSelectAfterPrepare()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer
                || Managers.SpawnManager.Instance == null
                || !Managers.SpawnManager.Instance.IsSpawned)
            {
                return;
            }

            foreach (ulong clientId in network.ConnectedClientsIds)
            {
                if (_admission.GetStatus(clientId) == PlayerSessionStatus.None)
                {
                    _admission.MarkConnected(clientId);
                }

                Managers.SpawnManager.Instance.RequestPostMatchFactionSelectForClient(clientId);
            }
        }

        static void ServerDespawnAllPlayerObjects()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer)
            {
                return;
            }

            List<NetworkObject> toDespawn = new List<NetworkObject>();
            foreach (ulong clientId in network.ConnectedClientsIds)
            {
                if (!network.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                    || client.PlayerObject == null)
                {
                    continue;
                }

                toDespawn.Add(client.PlayerObject);
            }

            PlayerController[] players = UnityEngine.Object.FindObjectsByType<PlayerController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                PlayerController player = players[i];
                if (player == null)
                {
                    continue;
                }

                NetworkObject netObj = player.NetworkObject;
                if (netObj != null && netObj.IsSpawned && !toDespawn.Contains(netObj))
                {
                    toDespawn.Add(netObj);
                }
            }

            for (int i = 0; i < toDespawn.Count; i++)
            {
                NetworkObject netObj = toDespawn[i];
                if (netObj != null && netObj.IsSpawned)
                {
                    netObj.Despawn(true);
                }
            }
        }

        static bool HasSpawnedPlayer(ulong clientId)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null
                || !network.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                || client.PlayerObject == null)
            {
                return false;
            }

            return client.PlayerObject.IsSpawned;
        }

        void StopMatchLoad()
        {
            if (_matchLoadRoutine == null)
            {
                return;
            }

            _runner.StopCoroutine(_matchLoadRoutine);
            _matchLoadRoutine = null;
        }

        void StopPostMatchPipeline()
        {
            if (_postMatchPipelineRoutine == null)
            {
                return;
            }

            _runner.StopCoroutine(_postMatchPipelineRoutine);
            _postMatchPipelineRoutine = null;
        }
    }
}
