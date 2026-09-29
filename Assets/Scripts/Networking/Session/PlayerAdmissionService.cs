using System;
using System.Collections.Generic;
using Core;
using Unity.Netcode;
using UnityEngine;
using World;

namespace Networking.Session
{
    /// <summary>
    /// 玩家准入：连接资格、断线移除、结算等待、选阵营资格、待处理加入队列。
    /// </summary>
    public sealed class PlayerAdmissionService
    {
        const string LogCategory = "SteamLobby";

        readonly ISessionHost _host;
        readonly PlayerSessionRoster _roster = new PlayerSessionRoster();
        readonly HashSet<ulong> _pendingJoinClientIds = new HashSet<ulong>();

        Func<bool> _isMatchLoadStarted;
        Func<bool> _isGameplayStarted;
        Func<bool> _isReturningToLobby;
        Action _onNetworkStartedForOpponent;

        public PlayerSessionRoster Roster => _roster;

        public PlayerAdmissionService(ISessionHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public void BindMatchFlags(
            Func<bool> isMatchLoadStarted,
            Func<bool> isGameplayStarted,
            Func<bool> isReturningToLobby,
            Action onNetworkStartedForOpponent)
        {
            _isMatchLoadStarted = isMatchLoadStarted;
            _isGameplayStarted = isGameplayStarted;
            _isReturningToLobby = isReturningToLobby;
            _onNetworkStartedForOpponent = onNetworkStartedForOpponent;
        }

        public void Clear()
        {
            _roster.Clear();
            _pendingJoinClientIds.Clear();
        }

        public bool AllowsSpawn(ulong clientId)
        {
            PlayerSessionStatus status = _roster.GetStatus(clientId);
            if (PlayerSessionRules.MustRefuseSpawn(status))
            {
                return false;
            }

            return _roster.HasChosenTeam(clientId);
        }

        public PlayerSessionStatus GetStatus(ulong clientId) => _roster.GetStatus(clientId);

        public TeamId GetTeam(ulong clientId) => _roster.GetTeam(clientId);

        public bool HasChosenTeam(ulong clientId) => _roster.HasChosenTeam(clientId);

        public void MarkConnected(ulong clientId) => _roster.MarkConnected(clientId);

        public void MarkFactionChosen(ulong clientId, TeamId team) => _roster.MarkFactionChosen(clientId, team);

        public void MarkInMatch(ulong clientId, TeamId team) => _roster.MarkInMatch(clientId, team);

        public void ResetConnectedPlayersForNextRound() => _roster.ResetConnectedPlayersForNextRound();

        public void TickPendingJoins()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsHost || _pendingJoinClientIds.Count == 0)
            {
                return;
            }

            TryProcessPendingJoins();
        }

        public void OnClientConnected(ulong clientId)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsHost)
            {
                if (clientId == network.LocalClientId)
                {
                    _roster.MarkConnected(clientId);
                    _host.Notify("主机已就绪。等待对手加入，双方到齐后进入选阵营。");
                    return;
                }

                MatchRoundPhase phase = MatchGameManager.CurrentPhase;

                if (MatchGameManager.IsPostMatchBlocked || MatchGameManager.IsPostMatchWaitingPhase)
                {
                    _host.Notify("对局已结束，已连接服务器；请等待当前结算结束。");
                    _pendingJoinClientIds.Remove(clientId);
                    _roster.MarkPostMatchWaiting(clientId);
                    Managers.SpawnManager.Instance?.RequestPostMatchWaitingForClient(clientId);
                    return;
                }

                if (phase == MatchRoundPhase.FactionSelection)
                {
                    _host.Notify("下一局选阵营中，通知新客户端选阵营。");
                    _pendingJoinClientIds.Remove(clientId);
                    _roster.MarkConnected(clientId);
                    Managers.SpawnManager.Instance?.RequestPostMatchFactionSelectForClient(clientId);
                    return;
                }

                if (_isMatchLoadStarted != null && _isMatchLoadStarted())
                {
                    _roster.MarkConnected(clientId);
                    _pendingJoinClientIds.Add(clientId);
                    _host.Notify("对手在场景加载期间加入，等待场景就绪后处理。");
                    return;
                }

                if ((_isGameplayStarted != null && _isGameplayStarted()) || phase == MatchRoundPhase.Playing)
                {
                    _roster.MarkConnected(clientId);
                    if (Managers.SpawnManager.Instance != null && Managers.SpawnManager.Instance.IsSpawned)
                    {
                        ProcessClientJoin(clientId);
                    }
                    else
                    {
                        _pendingJoinClientIds.Add(clientId);
                        GameLog.Warn(LogCategory, "对局中重连，但 SpawnManager 未就绪，已加入等待队列。");
                    }

                    return;
                }

                _host.SetState(LobbySessionState.InSession);
                _host.Notify("对手已加入，进入选阵营。");
                _onNetworkStartedForOpponent?.Invoke();

                _roster.MarkConnected(clientId);
                if (Managers.SpawnManager.Instance != null && Managers.SpawnManager.Instance.IsSpawned)
                {
                    ProcessClientJoin(clientId);
                }
                else
                {
                    _pendingJoinClientIds.Add(clientId);
                    GameLog.Warn(LogCategory, "SpawnManager 未就绪，新玩家已加入等待队列。");
                }

                return;
            }

            _host.SetState(LobbySessionState.InSession);
            _host.Notify("已连接到主机。");
        }

        public void OnClientDisconnected(ulong clientId)
        {
            if (_isReturningToLobby != null && _isReturningToLobby())
            {
                return;
            }

            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsHost)
            {
                _pendingJoinClientIds.Remove(clientId);
                _roster.Remove(clientId);
                if (clientId != network.LocalClientId)
                {
                    _host.Notify("对手已断开：" + SteamNetworkTransport.ConsumeDisconnectNotice());
                }

                return;
            }

            UI.DisconnectNoticeUI.EnsureInstance().Show(SteamNetworkTransport.ConsumeDisconnectNotice());
        }

        public void TryProcessPendingJoins()
        {
            if ((_isMatchLoadStarted != null && _isMatchLoadStarted())
                || Managers.SpawnManager.Instance == null
                || !Managers.SpawnManager.Instance.IsSpawned)
            {
                return;
            }

            if (MatchGameManager.IsPostMatchBlocked)
            {
                return;
            }

            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsHost)
            {
                return;
            }

            ulong[] pending = new ulong[_pendingJoinClientIds.Count];
            _pendingJoinClientIds.CopyTo(pending);
            for (int i = 0; i < pending.Length; i++)
            {
                ulong clientId = pending[i];
                if (!network.ConnectedClients.ContainsKey(clientId))
                {
                    _pendingJoinClientIds.Remove(clientId);
                    continue;
                }

                if (ProcessClientJoin(clientId))
                {
                    _pendingJoinClientIds.Remove(clientId);
                }
            }
        }

        public bool ProcessClientJoin(ulong clientId)
        {
            MatchRoundPhase phase = MatchGameManager.CurrentPhase;

            if (MatchGameManager.IsPostMatchBlocked || MatchGameManager.IsPostMatchWaitingPhase)
            {
                _roster.MarkPostMatchWaiting(clientId);
                Managers.SpawnManager.Instance?.RequestPostMatchWaitingForClient(clientId);
                return true;
            }

            if (phase == MatchRoundPhase.FactionSelection)
            {
                if (_roster.GetStatus(clientId) == PlayerSessionStatus.None)
                {
                    _roster.MarkConnected(clientId);
                }

                Managers.SpawnManager.Instance?.RequestPostMatchFactionSelectForClient(clientId);
                return true;
            }

            if (phase == MatchRoundPhase.PreparingNextRound)
            {
                _pendingJoinClientIds.Add(clientId);
                return false;
            }

            if (_roster.GetStatus(clientId) == PlayerSessionStatus.None)
            {
                _roster.MarkConnected(clientId);
            }

            if ((_isGameplayStarted != null && _isGameplayStarted()) || phase == MatchRoundPhase.Playing)
            {
                _host.Notify("对手重新加入，请重新选择阵营。");
            }

            Managers.SpawnManager.Instance.RequestFactionSelectForClient(clientId);
            return true;
        }

        public static bool CanCountClientForCapture(ulong clientId, PlayerSessionRoster roster)
        {
            return PlayerSessionRules.CanCountForCapture(roster.GetStatus(clientId));
        }
    }
}
