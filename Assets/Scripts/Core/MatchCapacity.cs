using Config;

namespace Core
{
    /// <summary>
    /// 当前房间容量。Steam 大厅人数含主机；NGO 远程连接数不含主机。
    /// 数值来自 <see cref="MatchConfig"/>，无资源时回退默认。
    /// </summary>
    public static class MatchCapacity
    {
        public const int DefaultMaxPlayers = 4;

        public static int MaxPlayers =>
            GameConfigCatalog.Match != null
                ? GameConfigCatalog.Match.MaxPlayers
                : DefaultMaxPlayers;

        public static int MaxRemoteClients
        {
            get
            {
                int value = MaxPlayers - 1;
                return value < 1 ? 1 : value;
            }
        }
    }
}
