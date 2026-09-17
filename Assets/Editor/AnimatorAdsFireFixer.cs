#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 菜单工具：补全 / 修正 PlayerAnimatorController 中 ADS 与 Fire 的互通连线。
/// 机瞄时左键能进 Fire，开火结束后若仍按住右键则回到 ADS，而不是掉回腰射 Empty。
/// 用法：菜单 <b>Tools/Fix ADS Fire Transitions</b>。
///
/// 注意：AnimatorControllerLayer 是 struct，项目未开 nullable，不能用 Layer? / .Value。
/// </summary>
public static class AnimatorAdsFireFixer
{
    const string MenuPath = "Tools/Fix ADS Fire Transitions";
    const string LogCategory = "AnimBuilder";
    const string ControllerPath = "Assets/Animations/PlayerAnimatorController.controller";
    const string UpperBodyLayerName = "UpperBody";
    const string AimClipPath = "Assets/Model/Soldier/Animations/Rifle Aiming Idle.fbx";

    const float TriggerDuration = 0.1f;
    const float ExitDuration = 0.2f;

    [MenuItem(MenuPath)]
    static void Fix()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Fix ADS Fire Transitions", "请先退出 Play 模式再执行。", "知道了");
            return;
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            GameLog.Error(LogCategory, $"❌ 找不到 {ControllerPath}。");
            return;
        }

        if (!TryFindLayer(controller, UpperBodyLayerName, out AnimatorControllerLayer layer))
        {
            GameLog.Error(LogCategory, $"❌ 找不到 {UpperBodyLayerName} 层。");
            return;
        }

        AnimatorStateMachine stateMachine = layer.stateMachine;
        AnimatorState empty = FindState(stateMachine, "Empty");
        AnimatorState fire = FindState(stateMachine, "Fire");
        if (empty == null || fire == null)
        {
            GameLog.Error(LogCategory,
                $"❌ UpperBody 层缺少状态：Empty={(empty != null)}, Fire={(fire != null)}。");
            return;
        }

        List<string> report = new List<string>();

        AnimatorState ads = FindState(stateMachine, "ADS");
        if (ads == null)
        {
            AnimationClip aimClip = LoadClip(AimClipPath);
            if (aimClip == null)
            {
                GameLog.Error(LogCategory, $"❌ 读不到 {AimClipPath}，无法自动创建 ADS 状态。");
                return;
            }

            ads = stateMachine.AddState("ADS", new Vector3(460f, 230f, 0f));
            ads.motion = aimClip;
            ads.writeDefaultValues = true;
            report.Add($"新增状态 ADS（{aimClip.name}）");
        }

        // Empty ↔ ADS：保证右键瞄准进出灵敏。
        ConfigureBoolTransition(GetOrAddTransition(empty, ads), "IsADS", true);
        report.Add("Empty → ADS（IsADS == true，无 ExitTime）");

        ConfigureBoolTransition(GetOrAddTransition(ads, empty), "IsADS", false);
        report.Add("ADS → Empty（IsADS == false，无 ExitTime）");

        // ADS → Fire：机瞄状态下左键仍能开火。
        ConfigureTriggerTransition(GetOrAddTransition(ads, fire), "Fire");
        report.Add("ADS → Fire（Trigger Fire，无 ExitTime）");

        // Fire → ADS：仍按着右键时回瞄准；必须排在 Fire → Empty 之前，否则会被无条件那条抢先吃掉。
        AnimatorStateTransition fireToAds = GetOrAddTransition(fire, ads);
        ConfigureExitWithBool(fireToAds, "IsADS", true);
        MoveTransitionToFirst(fire, fireToAds);
        report.Add("Fire → ADS（IsADS == true，ExitTime 播完，且排在 Fire → Empty 之前）");

        // Fire → Empty：腰射开火后的回落，保持已有行为。
        AnimatorStateTransition fireToEmpty = GetOrAddTransition(fire, empty);
        ConfigureExitPlain(fireToEmpty);
        report.Add("Fire → Empty（无条件，ExitTime 播完）");

        // stateMachine 是引用类型子资产，直接改状态/连线即可持久化，不必回写 layers 数组。
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(stateMachine);
        AssetDatabase.SaveAssets();

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"ADS / Fire 连线已同步，共 {report.Count} 条：");
        foreach (string line in report)
        {
            sb.AppendLine("  - " + line);
        }

        GameLog.Info(LogCategory, sb.ToString().TrimEnd());
    }

    /// <summary>按名字找层；用 out + bool，避免对 struct 使用可空注解。</summary>
    static bool TryFindLayer(AnimatorController controller, string layerName, out AnimatorControllerLayer layer)
    {
        foreach (AnimatorControllerLayer candidate in controller.layers)
        {
            if (candidate.name == layerName)
            {
                layer = candidate;
                return true;
            }
        }

        layer = default;
        return false;
    }

    static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
    {
        foreach (ChildAnimatorState child in stateMachine.states)
        {
            if (child.state != null && child.state.name == stateName)
            {
                return child.state;
            }
        }

        return null;
    }

    static AnimatorStateTransition GetOrAddTransition(AnimatorState from, AnimatorState to)
    {
        foreach (AnimatorStateTransition transition in from.transitions)
        {
            if (transition.destinationState == to)
            {
                return transition;
            }
        }

        return from.AddTransition(to);
    }

    static void ConfigureBoolTransition(AnimatorStateTransition transition, string parameter, bool expectedTrue)
    {
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.duration = TriggerDuration;
        transition.hasFixedDuration = true;
        ClearConditions(transition);
        transition.AddCondition(
            expectedTrue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
            0f,
            parameter);
    }

    static void ConfigureTriggerTransition(AnimatorStateTransition transition, string parameter)
    {
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.duration = TriggerDuration;
        transition.hasFixedDuration = true;
        ClearConditions(transition);
        transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
    }

    static void ConfigureExitWithBool(AnimatorStateTransition transition, string parameter, bool expectedTrue)
    {
        transition.hasExitTime = true;
        transition.exitTime = 1f;
        transition.duration = ExitDuration;
        transition.hasFixedDuration = true;
        ClearConditions(transition);
        transition.AddCondition(
            expectedTrue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
            0f,
            parameter);
    }

    static void ConfigureExitPlain(AnimatorStateTransition transition)
    {
        transition.hasExitTime = true;
        transition.exitTime = 1f;
        transition.duration = ExitDuration;
        transition.hasFixedDuration = true;
        ClearConditions(transition);
    }

    static void ClearConditions(AnimatorStateTransition transition)
    {
        for (int i = transition.conditions.Length - 1; i >= 0; i--)
        {
            transition.RemoveCondition(transition.conditions[i]);
        }
    }

    /// <summary>
    /// 把指定连线挪到列表首位。Unity 按顺序取第一个条件成立的转移。
    /// </summary>
    static void MoveTransitionToFirst(AnimatorState state, AnimatorStateTransition transition)
    {
        List<AnimatorStateTransition> list = new List<AnimatorStateTransition>(state.transitions);
        if (!list.Remove(transition))
        {
            return;
        }

        list.Insert(0, transition);
        state.transitions = list.ToArray();
    }

    static AnimationClip LoadClip(string assetPath)
    {
        AnimationClip clip = FindClipIn(AssetDatabase.LoadAllAssetsAtPath(assetPath));
        return clip != null
            ? clip
            : FindClipIn(AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath));
    }

    static AnimationClip FindClipIn(UnityEngine.Object[] assets)
    {
        if (assets == null)
        {
            return null;
        }

        foreach (UnityEngine.Object asset in assets)
        {
            if (asset is AnimationClip clip
                && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            {
                return clip;
            }
        }

        return null;
    }
}
#endif
