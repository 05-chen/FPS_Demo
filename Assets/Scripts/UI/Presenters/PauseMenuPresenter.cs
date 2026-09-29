using Unity.Netcode;
using UnityEngine;

namespace UI.Presenters
{
    /// <summary>
    /// 暂停菜单意图：是否允许打开、退出大厅回调由外部注入。
    /// 暂停状态仍由 <see cref="PauseGate"/> 管理（与 FullBlock 分离，保留摄像机）。
    /// </summary>
    public static class PauseMenuPresenter
    {
        public static bool CanOpenPauseMenu()
        {
            return GameplayGate.CurrentMode != GameplayGate.Mode.FullBlock;
        }

        public static void Pause(bool networked)
        {
            if (!CanOpenPauseMenu())
            {
                return;
            }

            PauseGate.Pause(freezeTime: !networked);
        }

        public static void Resume()
        {
            PauseGate.Resume();
        }

        public static void RequestQuitToLobby(System.Action onQuit)
        {
            PauseGate.Resume();
            onQuit?.Invoke();
        }
    }
}
