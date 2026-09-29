using UnityEngine;

namespace UI.Runtime
{
    /// <summary>
    /// 旧选阵营入口（场景里可能仍挂着）。不再查找 RED/BLUE，也不再自建一套 UI。
    /// 全部转给 <see cref="FactionSelectionPanel"/> + SpawnManager。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LegacyFactionSelectAdapter : MonoBehaviour
    {
        public void OpenOnline()
        {
            ShowNewFactionUi();
        }

        public void OpenOffline()
        {
            ShowNewFactionUi();
        }

        public void OpenOffline(PlayerController red, PlayerController blue)
        {
            _ = red;
            _ = blue;
            ShowNewFactionUi();
        }

        public void ResetVisuals()
        {
            if (FactionSelectionPanel.Instance != null)
            {
                FactionSelectionPanel.Instance.ShowUI(false);
            }
        }

        static void ShowNewFactionUi()
        {
            if (FactionSelectionPanel.Instance != null)
            {
                FactionSelectionPanel.Instance.ShowUI(true);
                return;
            }

            GameLog.Warn("Faction", "找不到 UI.FactionSelectionPanel。请在场景 Canvas 上挂选阵营面板脚本。");
        }
    }
}
