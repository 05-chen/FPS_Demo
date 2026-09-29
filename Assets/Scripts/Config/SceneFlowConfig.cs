using UnityEngine;

namespace Config
{
    /// <summary>
    /// 场景流程名。打包前须把同名场景勾进 Build Settings。
    /// </summary>
    [CreateAssetMenu(fileName = "SceneFlowConfig", menuName = "FPS/Config/Scene Flow Config", order = 13)]
    public sealed class SceneFlowConfig : ScriptableObject
    {
        [SerializeField] string offlinePracticeScene = "Testcene";
        [SerializeField] string onlineMatchScene = "Testcene_GamePlay";

        public string OfflinePracticeScene =>
            string.IsNullOrWhiteSpace(offlinePracticeScene) ? "Testcene" : offlinePracticeScene.Trim();

        public string OnlineMatchScene =>
            string.IsNullOrWhiteSpace(onlineMatchScene) ? "Testcene_GamePlay" : onlineMatchScene.Trim();

        public string ForMatch(bool isOffline) =>
            isOffline ? OfflinePracticeScene : OnlineMatchScene;
    }
}
