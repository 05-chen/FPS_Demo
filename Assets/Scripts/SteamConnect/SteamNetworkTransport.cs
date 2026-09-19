using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// NGO 传输层：用 SteamNetworkingSockets 做 P2P，不再走 127.0.0.1。
/// 主机 CreateListenSocketP2P，客户端 ConnectP2P 到大厅房主 SteamID。
/// NGO 的 clientId 直接使用 Steam 连接句柄，避免再维护一张映射表。
/// </summary>
[DisallowMultipleComponent]
public sealed class SteamNetworkTransport : NetworkTransport
{
    public const ulong SteamServerClientId = 0;
    const int MessageBufferSize = 32;
    const int MaxClients = 1;
    const string LogCategory = "SteamTransport";

    public override ulong ServerClientId => SteamServerClientId;

    /// <summary>客户端在 StartClient 之前设置：要连接的房主 SteamID。</summary>
    public CSteamID ConnectToSteamId { get; set; }

    bool _initialized;
    bool _isServer;
    HSteamListenSocket _listenSocket = HSteamListenSocket.Invalid;
    HSteamNetPollGroup _pollGroup = HSteamNetPollGroup.Invalid;
    HSteamNetConnection _serverConnection = HSteamNetConnection.Invalid;
    readonly HashSet<HSteamNetConnection> _clientConnections = new HashSet<HSteamNetConnection>();
    readonly Queue<PendingEvent> _events = new Queue<PendingEvent>(32);
    readonly IntPtr[] _messageBuffer = new IntPtr[MessageBufferSize];
    Callback<SteamNetConnectionStatusChangedCallback_t> _statusChanged;
    byte[] _lastRentedPayload;

    struct PendingEvent
    {
        public NetworkEvent Type;
        public ulong ClientId;
        public byte[] Payload;
        public int PayloadLength;
        public bool PayloadRented;
    }

    public override void Initialize(NetworkManager networkManager = null)
    {
        if (_initialized)
        {
            return;
        }

        if (!SteamRuntime.IsInitialized)
        {
            GameLog.Error(LogCategory, "Steam 未初始化，无法使用 Steam 传输层。请先打开 Steam 客户端。");
            return;
        }

        SteamNetworkingUtils.InitRelayNetworkAccess();
        ApplyKeepAliveTimeout();
        _pollGroup = SteamNetworkingSockets.CreatePollGroup();
        _statusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
        _initialized = true;
    }

    public override bool StartServer()
    {
        if (!SteamRuntime.IsInitialized)
        {
            return false;
        }

        _isServer = true;
        _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
        if (_listenSocket == HSteamListenSocket.Invalid)
        {
            GameLog.Error(LogCategory, "CreateListenSocketP2P 失败。");
            return false;
        }

        GameLog.Info(LogCategory, "Steam P2P 主机已开始监听。");
        return true;
    }

    public override bool StartClient()
    {
        if (!SteamRuntime.IsInitialized)
        {
            return false;
        }

        if (!ConnectToSteamId.IsValid())
        {
            GameLog.Error(LogCategory, "没有要连接的房主 SteamID。请先加入 Steam 大厅。");
            return false;
        }

        _isServer = false;
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID(ConnectToSteamId);
        _serverConnection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null);
        if (_serverConnection == HSteamNetConnection.Invalid)
        {
            GameLog.Error(LogCategory, "ConnectP2P 失败。");
            return false;
        }

        SteamNetworkingSockets.SetConnectionPollGroup(_serverConnection, _pollGroup);
        _lastDisconnectNotice = null;
        GameLog.Info(LogCategory, "正在通过 Steam P2P 连接房主: " + ConnectToSteamId);
        return true;
    }

    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
    {
        HSteamNetConnection connection = ResolveConnection(clientId);
        if (connection == HSteamNetConnection.Invalid || payload.Array == null || payload.Count <= 0)
        {
            return;
        }

        int flags = IsUnreliable(networkDelivery)
            ? Constants.k_nSteamNetworkingSend_UnreliableNoNagle
            : Constants.k_nSteamNetworkingSend_ReliableNoNagle;

        GCHandle handle = GCHandle.Alloc(payload.Array, GCHandleType.Pinned);
        try
        {
            IntPtr pointer = IntPtr.Add(handle.AddrOfPinnedObject(), payload.Offset);
            EResult result = SteamNetworkingSockets.SendMessageToConnection(
                connection,
                pointer,
                (uint)payload.Count,
                flags,
                out _);

            if (result != EResult.k_EResultOK && result != EResult.k_EResultIgnored)
            {
                GameLog.Warn(LogCategory, "SendMessageToConnection 失败: " + result);
            }
        }
        finally
        {
            handle.Free();
        }
    }

    public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
    {
        ReturnLastPayload();
        PumpIncomingMessages();

        receiveTime = Time.realtimeSinceStartup;
        if (_events.Count == 0)
        {
            clientId = 0;
            payload = default;
            return NetworkEvent.Nothing;
        }

        PendingEvent pending = _events.Dequeue();
        clientId = pending.ClientId;
        if (pending.Payload != null && pending.PayloadLength > 0)
        {
            if (pending.PayloadRented)
            {
                _lastRentedPayload = pending.Payload;
            }

            payload = new ArraySegment<byte>(pending.Payload, 0, pending.PayloadLength);
        }
        else
        {
            payload = default;
        }

        return pending.Type;
    }

    public override void DisconnectRemoteClient(ulong clientId)
    {
        HSteamNetConnection connection = ResolveConnection(clientId);
        if (connection == HSteamNetConnection.Invalid)
        {
            return;
        }

        SteamNetworkingSockets.CloseConnection(connection, 0, "DisconnectRemoteClient", false);
        _clientConnections.Remove(connection);
    }

    public override void DisconnectLocalClient()
    {
        if (_serverConnection == HSteamNetConnection.Invalid)
        {
            return;
        }

        SteamNetworkingSockets.CloseConnection(_serverConnection, 0, "DisconnectLocalClient", false);
        _serverConnection = HSteamNetConnection.Invalid;
    }

    public override ulong GetCurrentRtt(ulong clientId)
    {
        HSteamNetConnection connection = ResolveConnection(clientId);
        if (connection == HSteamNetConnection.Invalid)
        {
            return 0;
        }

        var status = new SteamNetConnectionRealTimeStatus_t();
        var lane = new SteamNetConnectionRealTimeLaneStatus_t();
        if (SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lane) == EResult.k_EResultOK)
        {
            return (ulong)Mathf.Max(0, status.m_nPing);
        }

        return 0;
    }

    public override void Shutdown()
    {
        foreach (HSteamNetConnection connection in _clientConnections)
        {
            SteamNetworkingSockets.CloseConnection(connection, 0, "Shutdown", false);
        }

        _clientConnections.Clear();

        if (_serverConnection != HSteamNetConnection.Invalid)
        {
            SteamNetworkingSockets.CloseConnection(_serverConnection, 0, "Shutdown", false);
            _serverConnection = HSteamNetConnection.Invalid;
        }

        if (_listenSocket != HSteamListenSocket.Invalid)
        {
            SteamNetworkingSockets.CloseListenSocket(_listenSocket);
            _listenSocket = HSteamListenSocket.Invalid;
        }

        if (_pollGroup != HSteamNetPollGroup.Invalid)
        {
            SteamNetworkingSockets.DestroyPollGroup(_pollGroup);
            _pollGroup = HSteamNetPollGroup.Invalid;
        }

        while (_events.Count > 0)
        {
            PendingEvent pending = _events.Dequeue();
            if (pending.PayloadRented && pending.Payload != null)
            {
                ArrayPool<byte>.Shared.Return(pending.Payload);
            }
        }

        ReturnLastPayload();

        if (_statusChanged != null)
        {
            _statusChanged.Dispose();
            _statusChanged = null;
        }

        ConnectToSteamId = default;
        _isServer = false;
        _initialized = false;
    }

    void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t data)
    {
        HSteamNetConnection connection = data.m_hConn;
        ESteamNetworkingConnectionState state = data.m_info.m_eState;

        switch (state)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                HandleConnecting(connection, data);
                break;
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                HandleConnected(connection);
                break;
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                HandleDisconnected(connection, data);
                break;
        }
    }

    void HandleConnecting(HSteamNetConnection connection, SteamNetConnectionStatusChangedCallback_t data)
    {
        if (!_isServer || data.m_info.m_hListenSocket == HSteamListenSocket.Invalid)
        {
            return;
        }

        if (_clientConnections.Count >= MaxClients)
        {
            SteamNetworkingSockets.CloseConnection(connection, 0, "Lobby full", false);
            GameLog.Warn(LogCategory, "拒绝多余连接：1v1 房间已满。");
            return;
        }

        if (SteamNetworkingSockets.AcceptConnection(connection) == EResult.k_EResultOK)
        {
            SteamNetworkingSockets.SetConnectionPollGroup(connection, _pollGroup);
            return;
        }

        SteamNetworkingSockets.CloseConnection(connection, 0, "Accept failed", false);
    }

    void HandleConnected(HSteamNetConnection connection)
    {
        if (_isServer)
        {
            _clientConnections.Add(connection);
            Enqueue(NetworkEvent.Connect, ToClientId(connection), null, 0, false);
            return;
        }

        Enqueue(NetworkEvent.Connect, ServerClientId, null, 0, false);
    }

    /// <summary>最近一次 Steam 断开的中文说明。给掉线提示界面用，用完即清。</summary>
    public static string ConsumeDisconnectNotice()
    {
        string notice = string.IsNullOrEmpty(_lastDisconnectNotice)
            ? "与主机失去连接。可能是网络中断，或主机已经离开。"
            : _lastDisconnectNotice;
        _lastDisconnectNotice = null;
        return notice;
    }

    static string _lastDisconnectNotice;

    /// <summary>
    /// Steam 默认大约 10 秒没数据就掐连接，比场景里 NGO 的 30 秒短。
    /// 进场景卡一下，或国内中继抖一下，客户端就会先被 Steam 踢掉。
    /// </summary>
    static void ApplyKeepAliveTimeout()
    {
        const int timeoutMs = 30000;
        SetGlobalInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutInitial, timeoutMs);
        SetGlobalInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutConnected, timeoutMs);
    }

    static void SetGlobalInt(ESteamNetworkingConfigValue value, int number)
    {
        GCHandle handle = GCHandle.Alloc(number, GCHandleType.Pinned);
        try
        {
            SteamNetworkingUtils.SetConfigValue(
                value,
                ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                IntPtr.Zero,
                ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    static string DescribeEndReason(ESteamNetConnectionEnd reason, string debug)
    {
        string extra = string.IsNullOrEmpty(debug) ? "" : "（" + debug + "）";
        if (debug == "Shutdown" || debug == "DisconnectLocalClient")
        {
            return "主机离开了房间。" + extra;
        }

        switch (reason)
        {
            case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Remote_Timeout:
            case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Misc_Timeout:
                return "网络超时。和主机之间太久没有收到数据，连接被断开。" + extra;
            case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Misc_SteamConnectivity:
            case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Misc_NoRelaySessionsToClient:
                return "Steam 中继不通。国内网络经常这样，不是游戏把你踢出房间。" + extra;
            case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Misc_P2P_NAT_Firewall:
            case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Local_P2P_ICE_NoPublicAddresses:
            case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Remote_P2P_ICE_NoPublicAddresses:
                return "防火墙或路由器拦住了连接，Steam 也没找到可用线路。" + extra;
            case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Local_OfflineMode:
                return "Steam 处于离线模式，联机会被断开。" + extra;
            case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Misc_P2P_Rendezvous:
                return "没能和主机完成握手。多半是网络或 Steam 中继的问题。" + extra;
            default:
                int code = (int)reason;
                if (code >= (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_App_Min
                    && code <= (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_App_Max)
                {
                    return "对方主动断开了连接。" + extra;
                }

                return "连接中断。Steam 原因码 " + code + "。" + extra;
        }
    }

    void HandleDisconnected(HSteamNetConnection connection, SteamNetConnectionStatusChangedCallback_t data)
    {
        string debug = data.m_info.m_szEndDebug;
        // CloseConnection 收尾时会再来一次 "Closed"，不能把真正的原因盖掉。
        if (debug != "Closed" || string.IsNullOrEmpty(_lastDisconnectNotice))
        {
            _lastDisconnectNotice = DescribeEndReason((ESteamNetConnectionEnd)data.m_info.m_eEndReason, debug);
        }

        GameLog.Warn(LogCategory, "Steam P2P 断开: " + data.m_info.m_eEndReason + " " + debug);

        if (_isServer)
        {
            _clientConnections.Remove(connection);
            Enqueue(NetworkEvent.Disconnect, ToClientId(connection), null, 0, false);
        }
        else
        {
            Enqueue(NetworkEvent.Disconnect, ServerClientId, null, 0, false);
            _serverConnection = HSteamNetConnection.Invalid;
        }

        SteamNetworkingSockets.CloseConnection(connection, 0, "Closed", false);
    }

    void PumpIncomingMessages()
    {
        if (_pollGroup == HSteamNetPollGroup.Invalid)
        {
            return;
        }

        int count = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(_pollGroup, _messageBuffer, _messageBuffer.Length);
        for (int i = 0; i < count; i++)
        {
            SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(_messageBuffer[i]);
            int size = message.m_cbSize;
            byte[] payload = null;
            if (size > 0 && message.m_pData != IntPtr.Zero)
            {
                payload = ArrayPool<byte>.Shared.Rent(size);
                Marshal.Copy(message.m_pData, payload, 0, size);
            }

            ulong clientId = _isServer ? ToClientId(message.m_conn) : ServerClientId;
            Enqueue(NetworkEvent.Data, clientId, payload, size, payload != null);
            SteamNetworkingMessage_t.Release(_messageBuffer[i]);
        }
    }

    void Enqueue(NetworkEvent type, ulong clientId, byte[] payload, int payloadLength, bool payloadRented)
    {
        _events.Enqueue(new PendingEvent
        {
            Type = type,
            ClientId = clientId,
            Payload = payload,
            PayloadLength = payloadLength,
            PayloadRented = payloadRented
        });
    }

    void ReturnLastPayload()
    {
        if (_lastRentedPayload == null)
        {
            return;
        }

        ArrayPool<byte>.Shared.Return(_lastRentedPayload);
        _lastRentedPayload = null;
    }

    HSteamNetConnection ResolveConnection(ulong clientId)
    {
        if (!_isServer)
        {
            return _serverConnection;
        }

        if (clientId == ServerClientId)
        {
            return HSteamNetConnection.Invalid;
        }

        return new HSteamNetConnection((uint)clientId);
    }

    static ulong ToClientId(HSteamNetConnection connection)
    {
        return connection.m_HSteamNetConnection;
    }

    static bool IsUnreliable(NetworkDelivery delivery)
    {
        return delivery == NetworkDelivery.Unreliable || delivery == NetworkDelivery.UnreliableSequenced;
    }
}
