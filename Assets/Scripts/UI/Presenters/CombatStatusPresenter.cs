using UnityEngine;

namespace UI.Presenters
{
    /// <summary>
    /// 伤情/死亡 HUD 与输入门闩。面板只负责画面。
    /// </summary>
    public static class CombatStatusPresenter
    {
        public static void ShowDowned(float bleedOutSeconds)
        {
            GameplayGate.BlockInputOnly(GameplayGate.Reason.Downed);
            CombatStatusUI.EnsureInstance().ShowDowned(bleedOutSeconds);
        }

        public static void ShowDead(float deadRespawnDelay)
        {
            GameplayGate.BlockInputOnly(GameplayGate.Reason.Downed);
            CombatStatusUI.EnsureInstance().ShowDead(deadRespawnDelay);
        }

        public static void Hide()
        {
            CombatStatusUI.Instance?.Hide();
            GameplayGate.Release(GameplayGate.Reason.Downed);
            GameplayGate.Release(GameplayGate.Reason.Respawn);
        }
    }
}
