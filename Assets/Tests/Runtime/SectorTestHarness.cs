using System.Collections;
using Core;
using Unity.Netcode;
using UnityEngine;

namespace World
{
    /// <summary>
    /// 灰盒测试台：Editor 下未开房时自动 StartHost，让 SectorManager 完成 NetworkSpawn。
    /// 进 Play 后若场景里有 SectorManager，会自动挂一个本组件。
    /// </summary>
    public sealed class SectorTestHarness : MonoBehaviour
    {
        const int MaxSimulatedPerTeam = 16;
        const ulong MockRedIdBase = 9100;
        const ulong MockBlueIdBase = 9200;

        [Header("绑定")]
        [SerializeField] string targetSectorId = "Sector_C";
        [SerializeField] SectorManager targetManager;

        [Header("Mock 生命状态")]
        [SerializeField] PlayerLifeState mockLifeState = PlayerLifeState.Alive;

        [Header("快捷键")]
        [SerializeField] bool enableHotkeys = true;

        bool _redInside;
        bool _blueInside;
        bool _autoHostAttempted;
        string _hostStatus = "等待网络...";

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoPlace()
        {
            if (!Application.isPlaying || FindFirstObjectByType<SectorManager>() == null)
            {
                return;
            }

            if (FindFirstObjectByType<SectorTestHarness>() != null)
            {
                return;
            }

            var host = new GameObject("[SectorTest]");
            host.AddComponent<SectorTestHarness>();
            host.AddComponent<SectorPhysicsTest>();
        }
#endif

        IEnumerator Start()
        {
            yield return EnsureAutoHost();
            TryBind();
        }

        /// <summary>
        /// Editor 开发模式：未监听时自动切 UnityTransport 并 StartHost，无需点大厅开房。
        /// </summary>
        IEnumerator EnsureAutoHost()
        {
            if (_autoHostAttempted)
            {
                yield break;
            }

            _autoHostAttempted = true;

#if !UNITY_EDITOR
            _hostStatus = "仅 Editor 启用 Auto-Host";
            yield break;
#else
            for (int i = 0; i < 30 && NetworkManager.Singleton == null; i++)
            {
                yield return null;
            }

            NetworkManager network = NetworkManager.Singleton;
            if (network == null)
            {
                _hostStatus = "场景没有 NetworkManager";
                SectorTestReport.Fail("AutoHost", "场景里没有 NetworkManager，无法自动开房");
                yield break;
            }

            if (network.IsListening)
            {
                _hostStatus = "已在监听，跳过 Auto-Host";
                GameLog.Info("SectorTest", "网络已在监听，跳过 Auto-Host");
                yield break;
            }

            _hostStatus = "正在 Auto-Host...";
            GameLog.Info("SectorTest", "Editor 未开房，正在自动 StartHost（UnityTransport）");

            bool started = TryStartHost(network);
            if (!started)
            {
                _hostStatus = "StartHost 失败";
                SectorTestReport.Fail("AutoHost", "StartHost 失败，请确认 NetworkManager 挂了 UnityTransport");
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline && (network == null || !network.IsListening))
            {
                yield return null;
                network = NetworkManager.Singleton;
            }

            yield return null;
            yield return null;

            if (network != null && network.IsListening && network.IsHost)
            {
                _hostStatus = "Auto-Host 成功（IsHost）";
                SectorTestReport.Pass("AutoHost 已监听，SectorManager 可 NetworkSpawn");
            }
            else
            {
                _hostStatus = "Auto-Host 超时";
                SectorTestReport.Fail("AutoHost", "StartHost 后仍未进入 IsListening");
            }
#endif
        }

        static bool TryStartHost(NetworkManager network)
        {
            if (SteamLobbySession.Instance != null)
            {
                return SteamLobbySession.Instance.StartOfflineHost();
            }

            Managers.SpawnManager.ConfigureDelayedPlayerSpawn(network);
            SteamLobbySession.UseUnityTransport();
            return network.StartHost();
        }

        void Update()
        {
            if (targetManager == null)
            {
                TryBind();
            }

            if (!enableHotkeys)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Keypad1))
            {
                SelectSector("Sector_A");
            }

            if (Input.GetKeyDown(KeyCode.Keypad2))
            {
                SelectSector("Sector_B");
            }

            if (Input.GetKeyDown(KeyCode.Keypad3))
            {
                SelectSector("Sector_C");
            }

            if (Input.GetKeyDown(KeyCode.Keypad4))
            {
                SelectSector("Sector_D");
            }

            if (Input.GetKeyDown(KeyCode.Keypad5))
            {
                SelectSector("Sector_E");
            }

            if (Input.GetKeyDown(KeyCode.F5))
            {
                RunPhysicsTest();
            }

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.F6))
            {
                AdjustSimulatedOccupants(TeamId.Red, shift ? -1 : 1);
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                AdjustSimulatedOccupants(TeamId.Blue, shift ? -1 : 1);
            }

            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (shift)
                {
                    SetPartialProgress(TeamId.Blue, -0.5f);
                }
                else
                {
                    SetPartialProgress(TeamId.Red, 0.5f);
                }
            }

            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (shift)
                {
                    SetPartialProgress(TeamId.Blue, -0.95f);
                }
                else
                {
                    SetPartialProgress(TeamId.Red, 0.95f);
                }
            }

            if (Input.GetKeyDown(KeyCode.F10))
            {
                ForceCaptureRed();
            }

            if (Input.GetKeyDown(KeyCode.F11))
            {
                ForceClearProgress();
            }

            if (Input.GetKeyDown(KeyCode.F12))
            {
                DebugExpireMatch();
            }
        }

        void OnGUI()
        {
            const float width = 300f;
            GUILayout.BeginArea(new Rect(12f, 12f, width, 520f), GUI.skin.box);
            GUILayout.Label("战区测试台 当前=" + targetSectorId);
            GUILayout.Label(_hostStatus);
            GUILayout.Label(DescribeBoundManager());

            if (GUILayout.Button("运行黑盒物理 Trigger 测试 (F5)"))
            {
                RunPhysicsTest();
            }

            if (GUILayout.Button("红方模拟人数 +1 (F6)"))
            {
                AdjustSimulatedOccupants(TeamId.Red, 1);
            }

            if (GUILayout.Button("红方模拟人数 -1 (Shift+F6)"))
            {
                AdjustSimulatedOccupants(TeamId.Red, -1);
            }

            if (GUILayout.Button("蓝方模拟人数 +1 (F7)"))
            {
                AdjustSimulatedOccupants(TeamId.Blue, 1);
            }

            if (GUILayout.Button("蓝方模拟人数 -1 (Shift+F7)"))
            {
                AdjustSimulatedOccupants(TeamId.Blue, -1);
            }

            if (GUILayout.Button("模拟玩家死亡/传送离开"))
            {
                SimulateLeaveOrDeath();
            }

            if (GUILayout.Button("红方推进 50% (F8)"))
            {
                SetPartialProgress(TeamId.Red, 0.5f);
            }

            if (GUILayout.Button("蓝方推进 50% (Shift+F8)"))
            {
                SetPartialProgress(TeamId.Blue, -0.5f);
            }

            if (GUILayout.Button("红方临界 95% (F9)"))
            {
                SetPartialProgress(TeamId.Red, 0.95f);
            }

            if (GUILayout.Button("蓝方临界 95% (Shift+F9)"))
            {
                SetPartialProgress(TeamId.Blue, -0.95f);
            }

            if (GUILayout.Button("强行拉满（红方 +1）(F10)"))
            {
                ForceCaptureRed();
            }

            if (GUILayout.Button("强行拉满（蓝方 -1）"))
            {
                ForceCaptureBlue();
            }

            if (GUILayout.Button("清空占领进度（保持归属）(F11)"))
            {
                ForceClearProgress();
            }

            if (GUILayout.Button("Debug 限时立刻结算 (F12)"))
            {
                DebugExpireMatch();
            }

            if (GUILayout.Button("Debug 红方推平（占 E）"))
            {
                DebugForceSweep(TeamId.Red);
            }

            if (GUILayout.Button("Debug 蓝方推平（占 A）"))
            {
                DebugForceSweep(TeamId.Blue);
            }

            GUILayout.Label("F6/F7 增减红/蓝模拟人数  Shift 为减");
            GUILayout.Label("F8 红50%  Shift+F8 蓝50%  F9 红95%  Shift+F9 蓝95%");
            GUILayout.Label("小键盘 1-5 选区  F10 红满  F11 清进度  F12 限时");
            GUILayout.EndArea();
        }

        string DescribeBoundManager()
        {
            if (targetManager == null)
            {
                return "未绑定 " + targetSectorId;
            }

            string spawn = targetManager.IsSpawned ? "已 Spawn" : "未 Spawn";
            return spawn + " 进度=" + targetManager.CaptureProgress.Value.ToString("F2") +
                   " 归属=" + targetManager.OwnerTeam.Value +
                   " 战力 R/B=" + targetManager.DebugActiveRedWeight + "/" +
                   targetManager.DebugActiveBlueWeight;
        }

        void TryBind()
        {
            SectorManager match = SectorManager.FindBySectorId(targetSectorId);
            if (match == null)
            {
                return;
            }

            targetManager = match;
            GameLog.Info("SectorTest", "Harness 已绑定 " + targetSectorId);
        }

        public void SelectSector(string sectorId)
        {
            targetSectorId = sectorId;
            targetManager = null;
            TryBind();
            GameLog.Info("SectorTest", "Debug 目标战区切换为 " + sectorId);
        }

        bool EnsureReady()
        {
            if (targetManager == null)
            {
                TryBind();
            }

            if (targetManager == null)
            {
                SectorTestReport.Fail("Harness", "找不到 " + targetSectorId + "，请打开 Testcene_GamePlay 再 Play");
                return false;
            }

            if (!targetManager.IsSpawned || !targetManager.IsServer)
            {
                SectorTestReport.Fail("Harness", "SectorManager 尚未 Spawn。Auto-Host 状态：" + _hostStatus);
                return false;
            }

            if (targetManager.DebugStrongpoint == null)
            {
                SectorTestReport.Fail("Harness", targetSectorId + " 未挂 StrongpointArea");
                return false;
            }

            targetManager.DebugSetIgnoreFrontline(false);
            return true;
        }

        /// <summary>一键跑黑盒：等 Auto-Host 完成后再生成 Dummy / GunMesh 穿 Trigger。</summary>
        [ContextMenu("运行黑盒物理 Trigger 测试")]
        public void RunPhysicsTest()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsListening)
            {
                SectorTestReport.Fail("黑盒", "网络尚未监听，请等 Auto-Host 完成后再按 F5");
                return;
            }

            SectorPhysicsTest runner = GetComponent<SectorPhysicsTest>();
            if (runner == null)
            {
                runner = gameObject.AddComponent<SectorPhysicsTest>();
            }

            GameLog.Info("SectorTest", "开始黑盒物理 Trigger 测试");
            runner.RunFromHarness();
        }

        [ContextMenu("模拟红方人数 +1")]
        public void SimulateRedEnter()
        {
            AdjustSimulatedOccupants(TeamId.Red, 1);
        }

        [ContextMenu("模拟蓝方人数 +1")]
        public void SimulateBlueEnter()
        {
            AdjustSimulatedOccupants(TeamId.Blue, 1);
        }

        /// <summary>增减当前战区核心圈模拟人数。己方占领区可防守；对非己方区须已进攻解锁。</summary>
        public void AdjustSimulatedOccupants(TeamId team, int delta)
        {
            if (!EnsureReady() || delta == 0)
            {
                return;
            }

            if (!targetManager.CanOccupyOrDefend(team))
            {
                GameLog.Warn(
                    "SectorTest",
                    targetSectorId + " 对" + TeamIdUtil.DisplayName(team) +
                    "既非己方占领（防守）也未进攻解锁，忽略模拟人数热键。");
                return;
            }

            int before = targetManager.DebugStrongpoint.DebugCountMocks(team);
            targetManager.DebugStrongpoint.DebugAdjustMockCount(team, delta, mockLifeState);
            _redInside = targetManager.DebugStrongpoint.DebugCountMocks(TeamId.Red) > 0;
            _blueInside = targetManager.DebugStrongpoint.DebugCountMocks(TeamId.Blue) > 0;
            SyncGridMocks();
            int after = targetManager.DebugStrongpoint.DebugCountMocks(team);
            targetManager.DebugStrongpoint.EvaluateActivePlayers(out int red, out int blue);
            GameLog.Info(
                "SectorTest",
                targetSectorId + " 模拟" + TeamIdUtil.DisplayName(team) +
                "人数 " + before + "→" + after + " 战力 R/B=" + red + "/" + blue);
        }

        void SyncGridMocks()
        {
            if (targetManager.DebugGrid == null)
            {
                return;
            }

            for (int i = 0; i < MaxSimulatedPerTeam; i++)
            {
                targetManager.DebugGrid.DebugRemovePlayer(MockRedIdBase + (ulong)i);
                targetManager.DebugGrid.DebugRemovePlayer(MockBlueIdBase + (ulong)i);
            }

            int redN = targetManager.DebugStrongpoint.DebugCountMocks(TeamId.Red);
            int blueN = targetManager.DebugStrongpoint.DebugCountMocks(TeamId.Blue);
            for (int i = 0; i < redN; i++)
            {
                targetManager.DebugGrid.DebugInjectPlayer(MockRedIdBase + (ulong)i);
            }

            for (int i = 0; i < blueN; i++)
            {
                targetManager.DebugGrid.DebugInjectPlayer(MockBlueIdBase + (ulong)i);
            }
        }

        [ContextMenu("模拟玩家死亡/传送离开")]
        public void SimulateLeaveOrDeath()
        {
            if (!EnsureReady())
            {
                return;
            }

            bool hadMocks = _redInside || _blueInside;
            if (hadMocks)
            {
                targetManager.DebugStrongpoint.DebugMarkMocksDead();
                targetManager.DebugStrongpoint.EvaluateActivePlayers(out int redDead, out int blueDead);
                SectorTestReport.Check(
                    "灰盒-死亡后不计分",
                    redDead == 0 && blueDead == 0,
                    "Dead 仍产生战力 Red=" + redDead + " Blue=" + blueDead);
            }

            targetManager.DebugStrongpoint.DebugClearMocks();
            SyncGridMocks();
            _redInside = false;
            _blueInside = false;

            targetManager.DebugStrongpoint.EvaluateActivePlayers(out int red, out int blue);
            SectorTestReport.Check(
                "灰盒-传送离开后圈内清理",
                red == 0 && blue == 0,
                "清理后仍有战力 Red=" + red + " Blue=" + blue);
        }

        [ContextMenu("强行拉满红方占领")]
        public void ForceCaptureRed()
        {
            TryForceCapture(TeamId.Red, 1f);
        }

        /// <summary>半进度/临界：只改进度，打满 ±1 才易主。未解锁则 Warn 并拒绝。</summary>
        public void SetPartialProgress(TeamId team, float progress)
        {
            if (!EnsureReady())
            {
                return;
            }

            if (!targetManager.IsUnlockedFor(team))
            {
                GameLog.Warn(
                    "SectorTest",
                    targetSectorId + " 对" + TeamIdUtil.DisplayName(team) +
                    "未解锁，忽略半进度/临界热键。");
                return;
            }

            bool ok = targetManager.DebugSetCaptureProgress(progress, team);
            if (ok)
            {
                GameLog.Info(
                    "SectorTest",
                    targetSectorId + " 进度=" + targetManager.CaptureProgress.Value.ToString("F2") +
                    " 归属=" + targetManager.OwnerTeam.Value);
            }
        }

        [ContextMenu("强行拉满蓝方占领")]
        public void ForceCaptureBlue()
        {
            TryForceCapture(TeamId.Blue, -1f);
        }

        void TryForceCapture(TeamId team, float progress)
        {
            if (!EnsureReady())
            {
                return;
            }

            if (!targetManager.IsUnlockedFor(team))
            {
                GameLog.Error(
                    "SectorTest",
                    targetSectorId + " 对" + TeamIdUtil.DisplayName(team) +
                    "未解锁（无己方邻接领地），忽略占领热键。先占领相邻区。");
                SectorTestReport.Fail("灰盒-越界占领拦截", targetSectorId + " 对 " + team + " 锁定，未写入进度");
                return;
            }

            bool ok = targetManager.DebugSetCaptureState(progress, team);
            SectorTestReport.Check(
                "灰盒-拉满" + team,
                ok && targetManager.OwnerTeam.Value == team &&
                Mathf.Approximately(targetManager.CaptureProgress.Value, progress),
                "写入失败或 NetworkVariable 未同步");
        }

        [ContextMenu("清空占领进度（保持归属）")]
        public void ForceClearProgress()
        {
            if (!EnsureReady())
            {
                return;
            }

            if (!targetManager.IsCapturable)
            {
                GameLog.Error("SectorTest", targetSectorId + " 对双方锁定，忽略 F11。");
                SectorTestReport.Fail("灰盒-锁定区清进度拦截", targetSectorId + " 未解锁");
                return;
            }

            TeamId ownerBefore = targetManager.OwnerTeam.Value;
            bool ok = targetManager.DebugSetCaptureState(0f, ownerBefore);
            bool ownerKept = targetManager.OwnerTeam.Value == ownerBefore &&
                             SectorCaptureRules.ResolveOwnerAfterProgress(ownerBefore, 0f) == ownerBefore;
            SectorTestReport.Check(
                "灰盒-进度归零且归属不变（0 不清成 None）",
                ok && ownerKept && Mathf.Approximately(targetManager.CaptureProgress.Value, 0f),
                "进度或归属不符合易主规则 owner=" + targetManager.OwnerTeam.Value);
        }

        public void DebugExpireMatch()
        {
            MatchGameManager match = MatchGameManager.Instance;
            if (match == null || !match.IsServer)
            {
                SectorTestReport.Fail("MatchDebug", "找不到 MatchGameManager 或当前不是 Host");
                return;
            }

            match.DebugExpireTimer();
            SectorTestReport.Pass("Debug 已触发限时结算");
        }

        public void DebugForceSweep(TeamId winner)
        {
            MatchGameManager match = MatchGameManager.Instance;
            if (match == null || !match.IsServer)
            {
                SectorTestReport.Fail("MatchDebug", "找不到 MatchGameManager 或当前不是 Host");
                return;
            }

            match.DebugForceSweep(winner);
            SectorTestReport.Pass("Debug 已触发推平 winner=" + winner);
        }
    }
}
