/// <summary>
/// 1v1 阵营。用 byte 节省 NetworkVariable 带宽。
/// 全程使用本枚举，不要再用 0=红 / 1=蓝 的旧数组下标。
/// </summary>
namespace Core
{
    public enum TeamId : byte
    {
        None = 0,
        Red = 1,
        Blue = 2
    }

    public static class TeamIdUtil
    {
        public static bool IsPlayable(TeamId team)
        {
            return team == TeamId.Red || team == TeamId.Blue;
        }

        public static TeamId FromNetwork(int value)
        {
            if (value == (int)TeamId.Red)
            {
                return TeamId.Red;
            }

            if (value == (int)TeamId.Blue)
            {
                return TeamId.Blue;
            }

            return TeamId.None;
        }

        public static string DisplayName(TeamId team)
        {
            switch (team)
            {
                case TeamId.Red:
                    return "红队";
                case TeamId.Blue:
                    return "蓝队";
                default:
                    return "未选择";
            }
        }
    }
}
