using UnityEngine;

namespace Config
{
    /// <summary>
    /// 比赛规则配置：时长、人数上限等。
    /// </summary>
    [CreateAssetMenu(fileName = "MatchConfig", menuName = "FPS/Config/Match Config", order = 10)]
    public sealed class MatchConfig : ScriptableObject
    {
        [Header("回合")]
        [Min(1f)]
        [SerializeField] float matchDurationSeconds = 900f;

        [Header("房间")]
        [Range(2, 16)]
        [SerializeField] int maxPlayers = 4;

        [Header("默认战区占领时长（秒，无 SectorData 时兜底）")]
        [Min(1f)]
        [SerializeField] float defaultCaptureDurationSeconds = 15f;

        public float MatchDurationSeconds => Mathf.Max(1f, matchDurationSeconds);
        public int MaxPlayers => Mathf.Max(2, maxPlayers);
        public int MaxRemoteClients => Mathf.Max(1, MaxPlayers - 1);
        public float DefaultCaptureDurationSeconds => Mathf.Max(1f, defaultCaptureDurationSeconds);
    }
}
