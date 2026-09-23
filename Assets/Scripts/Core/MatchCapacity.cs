namespace Core
{
    ///<summary>
    /// 当前房间容量。Steam大厅人数含主机：NGO远程连接数不含主机
    ///</summary>
    public static class MatchCapacity
    {
        public const int MaxPlayers = 4;
        public const int MaxRemoteClients = MaxPlayers - 1;
    }
}