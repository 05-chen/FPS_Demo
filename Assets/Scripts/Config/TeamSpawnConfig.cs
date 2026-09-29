using UnityEngine;

namespace Config
{
    /// <summary>
    /// 阵营出生兜底坐标（SpawnManager 区域缺失时使用）。
    /// </summary>
    [CreateAssetMenu(fileName = "TeamSpawnConfig", menuName = "FPS/Config/Team Spawn Config", order = 11)]
    public sealed class TeamSpawnConfig : ScriptableObject
    {
        [SerializeField] Vector3 redSpawnPosition = new Vector3(22f, 2.92f, 15.21f);
        [SerializeField] Vector3 blueSpawnPosition = new Vector3(30f, 2.92f, 15.21f);
        [SerializeField] float voidY = -15f;
        [SerializeField] float groundProbeUp = 4f;
        [SerializeField] float groundProbeDown = 30f;

        public Vector3 RedSpawnPosition => redSpawnPosition;
        public Vector3 BlueSpawnPosition => blueSpawnPosition;
        public float VoidY => voidY;
        public float GroundProbeUp => groundProbeUp;
        public float GroundProbeDown => groundProbeDown;

        public Vector3 GetSpawnPosition(Core.TeamId team)
        {
            return team == Core.TeamId.Blue ? blueSpawnPosition : redSpawnPosition;
        }
    }
}
