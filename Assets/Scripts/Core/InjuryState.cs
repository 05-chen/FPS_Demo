/// <summary>
/// 玩家 / 假人共用的伤情分级。放在 Core，避免玩法脚本依赖 Enemy。
/// </summary>
namespace Core
{
    public enum InjuryState
    {
        None,
        Light_Arms,
        Crippled_Legs,
        DBNO_Torso,
        InstanceDeath_Head
    }
}
