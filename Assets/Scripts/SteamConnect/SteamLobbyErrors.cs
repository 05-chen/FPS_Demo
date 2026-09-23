using Steamworks;
using Core;

/// <summary>
/// 把 Steam 错误码翻译成玩家能看懂的中文。UI 只显示字符串，不关心枚举。
/// </summary>
public static class SteamLobbyErrors
{
    public static string DescribeCreateFail(bool ioFailure, EResult result)
    {
        if (ioFailure)
        {
            return "创建房间失败：Steam 回调超时。请确认 Steam 不是离线模式，并检查网络。";
        }

        switch (result)
        {
            case EResult.k_EResultNoConnection:
                return "创建房间失败：Steam 客户端连不上大厅服务器（k_EResultNoConnection）。\n"
                    + "这和游戏代码无关。请检查：\n"
                    + "1. Steam 右下角是否显示在线（不要离线模式）\n"
                    + "2. 关掉 VPN 或换一个 Steam 加速器后再试\n"
                    + "3. 防火墙不要拦截 steam.exe / Unity\n"
                    + "4. 在 Steam 里打开 Spacewar（AppID 480），确认能进这个测试游戏";
            case EResult.k_EResultTimeout:
                return "创建房间失败：连接 Steam 大厅超时。请换网络或开加速器后再试。";
            case EResult.k_EResultAccessDenied:
            case EResult.k_EResultLimitedUserAccount:
                return "创建房间失败：当前 Steam 账号没有权限（受限账号也可能失败）。";
            default:
                return "创建房间失败：" + result;
        }
    }

    public static string DescribeJoinFail(uint response)
    {
        switch ((EChatRoomEnterResponse)response)
        {
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist:
                return "加入失败：房间不存在。主机可能已经停止 Play，请让对方重新创建并复制新的大厅 ID。";
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed:
                return "加入失败：没有权限。请让主机点「邀请好友」，或确认两边都用 AppID 480。";
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseFull:
                return "加入失败：房间已满（最多" + MatchCapacity.MaxPlayers + "人）。";
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseLimited:
                return "加入失败：当前 Steam 账号是受限账号。";
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseBanned:
                return "加入失败：你被该房间禁止加入。";
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseRatelimitExceeded:
                return "加入失败：加入太频繁，请等几秒再试。";
            default:
                return "加入失败：Steam 错误码 " + response;
        }
    }
}
