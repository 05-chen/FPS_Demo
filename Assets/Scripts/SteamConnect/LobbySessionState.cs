/// <summary>
/// 大厅会话状态机。用状态挡住非法操作，例如正在创建时再点一次「创建房间」。
/// </summary>
public enum LobbySessionState
{
    Idle = 0,
    CreatingLobby,
    JoiningLobby,
    WaitingRelay,
    Hosting,
    ConnectingToHost,
    InSession,
    Failed
}
