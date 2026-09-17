#if UNITY_EDITOR
using Core;
using UnityEditor;
using UnityEngine;
using World;

/// <summary>
/// Inspector 快捷按钮：邻接链 Debug 易主、Mock 占点、结算。
/// </summary>
[CustomEditor(typeof(SectorTestHarness))]
public class SectorTestHarnessEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Play GamePlay 会 Auto-Host。开局红打 B、蓝打 D。C 锁定。\nF6/F7 增减模拟人数。F8/F9 半进度与临界（须已解锁）。F10 打满才易主并触发后方上锁纯色。",
            MessageType.Info);

        SectorTestHarness harness = (SectorTestHarness)target;
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("选 A"))
            {
                harness.SelectSector("Sector_A");
            }

            if (GUILayout.Button("选 B"))
            {
                harness.SelectSector("Sector_B");
            }

            if (GUILayout.Button("选 C"))
            {
                harness.SelectSector("Sector_C");
            }

            if (GUILayout.Button("选 D"))
            {
                harness.SelectSector("Sector_D");
            }

            if (GUILayout.Button("选 E"))
            {
                harness.SelectSector("Sector_E");
            }

            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("运行黑盒物理 Trigger 测试 (F5)"))
            {
                harness.RunPhysicsTest();
            }

            if (GUILayout.Button("红方模拟人数 +1 (F6)"))
            {
                harness.AdjustSimulatedOccupants(TeamId.Red, 1);
            }

            if (GUILayout.Button("红方模拟人数 -1 (Shift+F6)"))
            {
                harness.AdjustSimulatedOccupants(TeamId.Red, -1);
            }

            if (GUILayout.Button("蓝方模拟人数 +1 (F7)"))
            {
                harness.AdjustSimulatedOccupants(TeamId.Blue, 1);
            }

            if (GUILayout.Button("蓝方模拟人数 -1 (Shift+F7)"))
            {
                harness.AdjustSimulatedOccupants(TeamId.Blue, -1);
            }

            if (GUILayout.Button("模拟死亡/离开"))
            {
                harness.SimulateLeaveOrDeath();
            }

            if (GUILayout.Button("红 50% (F8)"))
            {
                harness.SetPartialProgress(TeamId.Red, 0.5f);
            }

            if (GUILayout.Button("蓝 50% (Shift+F8)"))
            {
                harness.SetPartialProgress(TeamId.Blue, -0.5f);
            }

            if (GUILayout.Button("红 95% (F9)"))
            {
                harness.SetPartialProgress(TeamId.Red, 0.95f);
            }

            if (GUILayout.Button("蓝 95% (Shift+F9)"))
            {
                harness.SetPartialProgress(TeamId.Blue, -0.95f);
            }

            if (GUILayout.Button("强制当前区归红 +1 (F10)"))
            {
                harness.ForceCaptureRed();
            }

            if (GUILayout.Button("强制当前区归蓝 -1"))
            {
                harness.ForceCaptureBlue();
            }

            if (GUILayout.Button("进度归零保持归属 (F11)"))
            {
                harness.ForceClearProgress();
            }

            if (GUILayout.Button("限时立刻结算 (F12)"))
            {
                harness.DebugExpireMatch();
            }

            if (GUILayout.Button("红方推平（占 E）"))
            {
                harness.DebugForceSweep(TeamId.Red);
            }

            if (GUILayout.Button("蓝方推平（占 A）"))
            {
                harness.DebugForceSweep(TeamId.Blue);
            }
        }
    }
}

[CustomEditor(typeof(SectorPhysicsTest))]
public class SectorPhysicsTestEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("运行黑盒物理 Trigger 测试 (F5)"))
            {
                ((SectorPhysicsTest)target).RunFromHarness();
            }
        }
    }
}
#endif
