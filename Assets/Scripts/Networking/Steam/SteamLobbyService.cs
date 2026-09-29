using System;
using Core;
using Steamworks;
using UnityEngine;

namespace Networking.Steam
{
    /// <summary>
    /// Steam 大厅：创建、按邀请码搜索、加入、退出、邀请好友、复制邀请码。
    /// Steam 回调转为事件，不直接操作 NGO 或 UI。
    /// </summary>
    public sealed class SteamLobbyService
    {
        public const string GameKey = "game";
        public const string GameValue = "fps1v1";

        readonly ISessionHost _host;

        CallResult<LobbyCreated_t> _lobbyCreated;
        CallResult<LobbyEnter_t> _lobbyJoin;
        CallResult<LobbyMatchList_t> _lobbyList;
        Callback<GameLobbyJoinRequested_t> _lobbyJoinRequested;
        Callback<LobbyEnter_t> _lobbyEntered;
        bool _bound;
        ulong _handledEnterLobbyId;
        string _pendingJoinCode;

        public CSteamID CurrentLobby { get; private set; }
        public string CurrentJoinCode { get; private set; }
        public bool IsLobbyHost { get; private set; }

        /// <summary>大厅创建成功且本机是房主。</summary>
        public event Action LobbyCreatedAsHost;

        /// <summary>作为客户端进入他人大厅，参数为房主 SteamID。</summary>
        public event Action<CSteamID> LobbyEnteredAsClient;

        /// <summary>进入自己创建的大厅但 NGO 尚未 Host（提示用户）。</summary>
        public event Action OwnLobbyEnteredWithoutHost;

        /// <summary>Steam 邀请 overlay 请求加入大厅。</summary>
        public event Action<CSteamID> LobbyJoinRequested;

        public SteamLobbyService(ISessionHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public void BindCallbacks()
        {
            if (_bound)
            {
                return;
            }

            _lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyJoin = CallResult<LobbyEnter_t>.Create(OnLobbyJoinCallResult);
            _lobbyList = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
            _lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);
            _lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            _bound = true;
        }

        public void DisposeCallbacks()
        {
            if (!_bound)
            {
                return;
            }

            _lobbyCreated?.Dispose();
            _lobbyJoin?.Dispose();
            _lobbyList?.Dispose();
            _lobbyJoinRequested?.Dispose();
            _lobbyEntered?.Dispose();
            _lobbyCreated = null;
            _lobbyJoin = null;
            _lobbyList = null;
            _lobbyJoinRequested = null;
            _lobbyEntered = null;
            _bound = false;
        }

        public void CreateLobby()
        {
            BindCallbacks();
            SteamAPICall_t call = SteamMatchmaking.CreateLobby(
                ELobbyType.k_ELobbyTypePublic,
                MatchCapacity.MaxPlayers);
            _lobbyCreated.Set(call);
        }

        public void JoinByCode(string rawCode)
        {
            string code = JoinCodeUtility.Normalize(rawCode);
            if (!JoinCodeUtility.IsValid(code))
            {
                _host.Notify("邀请码必须是 6 位，例如 K7M2QX。不要用 0/O、1/I。");
                return;
            }

            BindCallbacks();
            _host.SetState(LobbySessionState.JoiningLobby);
            _pendingJoinCode = code;
            _host.Notify("正在查找邀请码 " + code + " ...");

            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListStringFilter(
                JoinCodeUtility.LobbyDataKey,
                code,
                ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(
                GameKey,
                GameValue,
                ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(16);
            _lobbyList.Set(SteamMatchmaking.RequestLobbyList());
        }

        public void JoinLobby(CSteamID lobbyId)
        {
            if (!lobbyId.IsValid())
            {
                _host.Notify("大厅 ID 无效。");
                return;
            }

            _host.SetState(LobbySessionState.JoiningLobby);
            SubmitJoinLobby(lobbyId);
        }

        public void LeaveLobby()
        {
            CurrentJoinCode = null;
            _pendingJoinCode = null;
            IsLobbyHost = false;
            _handledEnterLobbyId = 0;

            if (!CurrentLobby.IsValid() || !SteamRuntime.IsInitialized)
            {
                CurrentLobby = default;
                return;
            }

            SteamMatchmaking.LeaveLobby(CurrentLobby);
            CurrentLobby = default;
        }

        public void InviteFriends()
        {
            if (!CurrentLobby.IsValid())
            {
                _host.Notify("还没有房间，请先创建。");
                return;
            }

            SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobby);
            _host.Notify("已打开 Steam 邀请窗口。");
        }

        public void CopyJoinCode()
        {
            if (string.IsNullOrEmpty(CurrentJoinCode))
            {
                _host.Notify("还没有房间。");
                return;
            }

            GUIUtility.systemCopyBuffer = CurrentJoinCode;
            _host.Notify("邀请码已复制：" + CurrentJoinCode);
        }

        public static CSteamID ResolveLobbyOwner(CSteamID lobby)
        {
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobby);
            if (owner.IsValid() && owner.BIndividualAccount())
            {
                return owner;
            }

            string hostData = SteamMatchmaking.GetLobbyData(lobby, "host_steamid");
            if (ulong.TryParse(hostData, out ulong hostValue))
            {
                return new CSteamID(hostValue);
            }

            return owner;
        }

        void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                _host.Fail(SteamLobbyErrors.DescribeCreateFail(ioFailure, result.m_eResult));
                return;
            }

            CurrentLobby = new CSteamID(result.m_ulSteamIDLobby);
            IsLobbyHost = true;
            SteamMatchmaking.SetLobbyJoinable(CurrentLobby, true);
            CurrentJoinCode = JoinCodeUtility.Generate();
            SteamMatchmaking.SetLobbyData(CurrentLobby, GameKey, GameValue);
            SteamMatchmaking.SetLobbyData(CurrentLobby, JoinCodeUtility.LobbyDataKey, CurrentJoinCode);
            SteamMatchmaking.SetLobbyData(CurrentLobby, "name", SteamRuntime.PersonaName + " 的房间");
            SteamMatchmaking.SetLobbyData(
                CurrentLobby,
                "host_steamid",
                SteamRuntime.LocalSteamId.m_SteamID.ToString());

            LobbyCreatedAsHost?.Invoke();
        }

        void OnLobbyJoinRequested(GameLobbyJoinRequested_t request)
        {
            LobbyJoinRequested?.Invoke(request.m_steamIDLobby);
        }

        void OnLobbyJoinCallResult(LobbyEnter_t result, bool ioFailure)
        {
            if (ioFailure)
            {
                _host.Fail("加入大厅失败：Steam 回调超时。请确认网络/加速器，并让主机保持 Play。");
                return;
            }

            HandleLobbyEntered(result);
        }

        void OnLobbyEntered(LobbyEnter_t result)
        {
            HandleLobbyEntered(result);
        }

        void HandleLobbyEntered(LobbyEnter_t result)
        {
            if (result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                _host.Fail(SteamLobbyErrors.DescribeJoinFail(result.m_EChatRoomEnterResponse));
                return;
            }

            if (_handledEnterLobbyId == result.m_ulSteamIDLobby
                && CurrentLobby.m_SteamID == result.m_ulSteamIDLobby)
            {
                return;
            }

            _handledEnterLobbyId = result.m_ulSteamIDLobby;
            CurrentLobby = new CSteamID(result.m_ulSteamIDLobby);
            string lobbyCode = JoinCodeUtility.Normalize(
                SteamMatchmaking.GetLobbyData(CurrentLobby, JoinCodeUtility.LobbyDataKey));
            if (JoinCodeUtility.IsValid(lobbyCode))
            {
                CurrentJoinCode = lobbyCode;
            }

            if (!SteamRuntime.TryGetLocalSteamId(out CSteamID me))
            {
                _host.Fail("已进入大厅，但拿不到本机 SteamID。");
                return;
            }

            CSteamID owner = ResolveLobbyOwner(CurrentLobby);
            if (!owner.IsValid())
            {
                _host.Fail("已进入大厅，但拿不到房主 SteamID。请让主机重新创建房间后再加入。");
                return;
            }

            if (owner == me)
            {
                IsLobbyHost = true;
                OwnLobbyEnteredWithoutHost?.Invoke();
                return;
            }

            IsLobbyHost = false;
            LobbyEnteredAsClient?.Invoke(owner);
        }

        void OnLobbyList(LobbyMatchList_t result, bool ioFailure)
        {
            if (ioFailure)
            {
                _host.Fail("查找房间失败：Steam 回调超时。请检查网络后再试。");
                return;
            }

            for (int i = 0; i < result.m_nLobbiesMatching; i++)
            {
                CSteamID lobby = SteamMatchmaking.GetLobbyByIndex(i);
                string code = JoinCodeUtility.Normalize(
                    SteamMatchmaking.GetLobbyData(lobby, JoinCodeUtility.LobbyDataKey));
                if (code == _pendingJoinCode)
                {
                    SubmitJoinLobby(lobby);
                    return;
                }
            }

            _host.Fail("找不到邀请码 " + _pendingJoinCode + " 的房间。请确认主机仍在 Play，码没有输错。");
        }

        void SubmitJoinLobby(CSteamID lobbyId)
        {
            _host.Notify("正在加入大厅...");
            BindCallbacks();
            SteamAPICall_t call = SteamMatchmaking.JoinLobby(lobbyId);
            _lobbyJoin.Set(call);
        }
    }
}
