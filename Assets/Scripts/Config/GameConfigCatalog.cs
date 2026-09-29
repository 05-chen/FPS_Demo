using UnityEngine;

namespace Config
{
    /// <summary>
    /// 运行时配置目录。优先从 Resources/Configs 加载；缺失则使用内存默认实例。
    /// </summary>
    public static class GameConfigCatalog
    {
        const string MatchResourcePath = "Configs/MatchConfig";
        const string TeamSpawnResourcePath = "Configs/TeamSpawnConfig";
        const string WeaponResourcePath = "Configs/WeaponConfig";
        const string SceneFlowResourcePath = "Configs/SceneFlowConfig";

        static MatchConfig _match;
        static TeamSpawnConfig _teamSpawn;
        static WeaponConfig _weapon;
        static SceneFlowConfig _sceneFlow;
        static bool _loaded;

        public static MatchConfig Match
        {
            get
            {
                EnsureLoaded();
                return _match;
            }
        }

        public static TeamSpawnConfig TeamSpawn
        {
            get
            {
                EnsureLoaded();
                return _teamSpawn;
            }
        }

        public static WeaponConfig Weapon
        {
            get
            {
                EnsureLoaded();
                return _weapon;
            }
        }

        public static SceneFlowConfig SceneFlow
        {
            get
            {
                EnsureLoaded();
                return _sceneFlow;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _match = null;
            _teamSpawn = null;
            _weapon = null;
            _sceneFlow = null;
            _loaded = false;
        }

        static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _match = Resources.Load<MatchConfig>(MatchResourcePath);
            _teamSpawn = Resources.Load<TeamSpawnConfig>(TeamSpawnResourcePath);
            _weapon = Resources.Load<WeaponConfig>(WeaponResourcePath);
            _sceneFlow = Resources.Load<SceneFlowConfig>(SceneFlowResourcePath);

            if (_match == null)
            {
                _match = ScriptableObject.CreateInstance<MatchConfig>();
                GameLog.Warn("Config", "未找到 Resources/Configs/MatchConfig，使用内存默认值。");
            }

            if (_teamSpawn == null)
            {
                _teamSpawn = ScriptableObject.CreateInstance<TeamSpawnConfig>();
                GameLog.Warn("Config", "未找到 Resources/Configs/TeamSpawnConfig，使用内存默认值。");
            }

            if (_weapon == null)
            {
                _weapon = ScriptableObject.CreateInstance<WeaponConfig>();
                GameLog.Warn("Config", "未找到 Resources/Configs/WeaponConfig，使用内存默认值。");
            }

            if (_sceneFlow == null)
            {
                _sceneFlow = ScriptableObject.CreateInstance<SceneFlowConfig>();
                GameLog.Warn("Config", "未找到 Resources/Configs/SceneFlowConfig，使用内存默认值。");
            }
        }
    }
}
