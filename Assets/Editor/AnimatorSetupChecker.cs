#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 菜单工具：全面检查 PlayerAnimatorController.controller 的状态机配置是否与代码约定一致。
/// 纯只读校验，不会修改任何资产；用法：菜单 <b>Tools/Check Player Animator Setup</b>。
///
/// 校验范围：Parameters 类型、Base Layer 的 Locomotion 混合树、UpperBody 层的权重 /
/// 混合模式 / AvatarMask 以及 Fire、Reload 两条往返连线。
/// </summary>
public static class AnimatorSetupChecker
{
    const string MenuPath = "Tools/Check Player Animator Setup";
    const string ControllerFileName = "PlayerAnimatorController.controller";
    const string LogCategory = "AnimatorCheck";

    const string BaseLayerName = "Base Layer";
    const string UpperBodyLayerName = "UpperBody";
    const string LocomotionStateName = "Locomotion";
    const string EmptyStateName = "Empty";
    const string FireStateName = "Fire";
    const string ReloadStateName = "Reload";

    /// <summary>退出型连线要求的最短 ExitTime，低于该值动画会被过早打断。</summary>
    const float MinExitTime = 0.8f;

    /// <summary>浮点比较容差：手拖节点很难刚好落在整点，ExitTime 也常写成 0.799999。</summary>
    const float ComparisonEpsilon = 0.001f;

    /// <summary>参数约定：名称 + 必须匹配的类型。</summary>
    struct ParameterSpec
    {
        public string Name;
        public AnimatorControllerParameterType Type;

        public ParameterSpec(string name, AnimatorControllerParameterType type)
        {
            Name = name;
            Type = type;
        }
    }

    static readonly ParameterSpec[] ExpectedParameters =
    {
        new ParameterSpec("InputX", AnimatorControllerParameterType.Float),
        new ParameterSpec("InputY", AnimatorControllerParameterType.Float),
        new ParameterSpec("IsGround", AnimatorControllerParameterType.Bool),
        new ParameterSpec("IsADS", AnimatorControllerParameterType.Bool),
        new ParameterSpec("Fire", AnimatorControllerParameterType.Trigger),
        new ParameterSpec("Reload", AnimatorControllerParameterType.Trigger),
        new ParameterSpec("Jump", AnimatorControllerParameterType.Trigger),
    };

    /// <summary>Locomotion 混合树必须覆盖的四方向 + 待机共 5 个槽位坐标。</summary>
    static readonly Vector2[] ExpectedBlendPositions =
    {
        new Vector2(0f, 0f),
        new Vector2(0f, 1f),
        new Vector2(0f, -1f),
        new Vector2(-1f, 0f),
        new Vector2(1f, 0f),
    };

    [MenuItem(MenuPath)]
    static void CheckPlayerAnimatorSetup()
    {
        AnimatorController controller = FindController();
        if (controller == null)
        {
            GameLog.Error(LogCategory, $"❌ 在工程中找不到 {ControllerFileName}，请确认资产已导入。");
            return;
        }

        List<string> issues = new List<string>();
        CheckParameters(controller, issues);
        CheckBaseLayer(controller, issues);
        CheckUpperBodyLayer(controller, issues);

        if (issues.Count == 0)
        {
            // Console 支持富文本标签，加 <color> 才能显示成绿色。
            GameLog.Info(LogCategory, "<color=green>✅ PlayerAnimatorController 状态机配置完美！</color>");
            return;
        }

        StringBuilder report = new StringBuilder();
        report.AppendLine($"❌ {ControllerFileName} 检查未通过，共 {issues.Count} 项不合规：");
        for (int i = 0; i < issues.Count; i++)
        {
            report.AppendLine($"  {i + 1}. {issues[i]}");
        }

        GameLog.Error(LogCategory, report.ToString().TrimEnd());
    }

    /// <summary>按文件名精确定位目标 Controller，避免选中同类型的其它状态机。</summary>
    static AnimatorController FindController()
    {
        string[] guids = AssetDatabase.FindAssets("t:AnimatorController");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.Equals(Path.GetFileName(path), ControllerFileName, StringComparison.OrdinalIgnoreCase))
            {
                return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Parameters
    // ------------------------------------------------------------------

    static void CheckParameters(AnimatorController controller, List<string> issues)
    {
        foreach (ParameterSpec spec in ExpectedParameters)
        {
            AnimatorControllerParameter found = null;
            foreach (AnimatorControllerParameter parameter in controller.parameters)
            {
                if (parameter.name == spec.Name)
                {
                    found = parameter;
                    break;
                }
            }

            if (found == null)
            {
                issues.Add($"Parameters 缺少参数 \"{spec.Name}\"（类型应为 {spec.Type}）。");
                continue;
            }

            if (found.type != spec.Type)
            {
                issues.Add($"参数 \"{spec.Name}\" 类型错误：当前为 {found.type}，应为 {spec.Type}。");
            }
        }
    }

    // ------------------------------------------------------------------
    // Base Layer
    // ------------------------------------------------------------------

    static void CheckBaseLayer(AnimatorController controller, List<string> issues)
    {
        AnimatorControllerLayer layer = FindLayer(controller, BaseLayerName);
        if (layer == null)
        {
            issues.Add($"缺少 \"{BaseLayerName}\" 层。");
            return;
        }

        AnimatorState defaultState = layer.stateMachine != null ? layer.stateMachine.defaultState : null;
        if (defaultState == null)
        {
            issues.Add($"{BaseLayerName} 未设置默认 State。");
            return;
        }

        if (defaultState.name != LocomotionStateName)
        {
            issues.Add($"{BaseLayerName} 的默认 State 为 \"{defaultState.name}\"，应为 \"{LocomotionStateName}\"。");
        }

        if (!(defaultState.motion is BlendTree tree))
        {
            issues.Add($"{LocomotionStateName} 的 Motion 不是 Blend Tree，无法用 InputX/InputY 混合位移。");
            return;
        }

        if (tree.blendType != BlendTreeType.FreeformDirectional2D)
        {
            issues.Add($"{LocomotionStateName} 的 BlendType 为 {tree.blendType}，应为 FreeformDirectional2D（2D Freeform Directional）。");
        }

        if (tree.blendParameter != "InputX" || tree.blendParameterY != "InputY")
        {
            issues.Add($"{LocomotionStateName} 的混合参数为 (\"{tree.blendParameter}\", \"{tree.blendParameterY}\")，应为 (InputX, InputY)。");
        }

        CheckBlendTreeChildren(tree, issues);
    }

    /// <summary>校验混合树是否为 5 槽位、坐标覆盖四方向 + 待机、且每个槽位都挂了动画。</summary>
    static void CheckBlendTreeChildren(BlendTree tree, List<string> issues)
    {
        ChildMotion[] children = tree.children;
        if (children == null || children.Length != ExpectedBlendPositions.Length)
        {
            int count = children == null ? 0 : children.Length;
            issues.Add($"{LocomotionStateName} 混合树的动作槽位为 {count} 个，应为 {ExpectedBlendPositions.Length} 个。");
        }

        if (children == null)
        {
            return;
        }

        foreach (Vector2 expected in ExpectedBlendPositions)
        {
            bool matched = false;
            foreach (ChildMotion child in children)
            {
                if (Mathf.Abs(child.position.x - expected.x) <= ComparisonEpsilon
                    && Mathf.Abs(child.position.y - expected.y) <= ComparisonEpsilon)
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                issues.Add($"{LocomotionStateName} 混合树缺少坐标 ({expected.x}, {expected.y}) 的动作槽位。");
            }
        }

        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].motion == null)
            {
                issues.Add($"{LocomotionStateName} 混合树第 {i + 1} 个槽位没有挂 Motion。");
            }
        }
    }

    // ------------------------------------------------------------------
    // UpperBody Layer
    // ------------------------------------------------------------------

    static void CheckUpperBodyLayer(AnimatorController controller, List<string> issues)
    {
        AnimatorControllerLayer layer = FindLayer(controller, UpperBodyLayerName);
        if (layer == null)
        {
            issues.Add($"缺少 \"{UpperBodyLayerName}\" 层。");
            return;
        }

        // 权重为 0 时整层被完全屏蔽，开火 / 换弹动画看起来「没做」。
        if (!Mathf.Approximately(layer.defaultWeight, 1f))
        {
            issues.Add($"{UpperBodyLayerName} 层的 Weight 为 {layer.defaultWeight}，应为 1，否则该层不生效。");
        }

        if (layer.blendingMode != AnimatorLayerBlendingMode.Override)
        {
            issues.Add($"{UpperBodyLayerName} 层的 BlendingMode 为 {layer.blendingMode}，应为 Override。");
        }

        if (layer.avatarMask == null)
        {
            issues.Add($"{UpperBodyLayerName} 层未关联 AvatarMask，应指定 UpperBodyMask 以只覆盖上半身。");
        }

        AnimatorStateMachine stateMachine = layer.stateMachine;
        AnimatorState empty = FindState(stateMachine, EmptyStateName);
        AnimatorState fire = FindState(stateMachine, FireStateName);
        AnimatorState reload = FindState(stateMachine, ReloadStateName);

        ValidateTriggerTransition(issues, empty, fire, "Fire", $"{EmptyStateName} -> {FireStateName}");
        ValidateExitTransition(issues, fire, empty, $"{FireStateName} -> {EmptyStateName}");
        ValidateTriggerTransition(issues, empty, reload, "Reload", $"{EmptyStateName} -> {ReloadStateName}");
        ValidateExitTransition(issues, reload, empty, $"{ReloadStateName} -> {EmptyStateName}");
    }

    /// <summary>进入型连线：不应有 HasExitTime，且必须带对应 Trigger 条件。</summary>
    static void ValidateTriggerTransition(List<string> issues, AnimatorState from, AnimatorState to,
        string triggerName, string label)
    {
        if (from == null || to == null)
        {
            issues.Add($"{UpperBodyLayerName} 层缺少状态，无法校验 {label} 连线。");
            return;
        }

        AnimatorStateTransition transition = FindTransition(from, to);
        if (transition == null)
        {
            issues.Add($"缺少 {label} 连线（HasExitTime 应为 false，条件为 Trigger \"{triggerName}\"）。");
            return;
        }

        if (transition.hasExitTime)
        {
            issues.Add($"{label} 的 HasExitTime 应为 false，否则无法立即响应 Trigger。");
        }

        bool hasTrigger = false;
        foreach (AnimatorCondition condition in transition.conditions)
        {
            if (condition.mode == AnimatorConditionMode.If && condition.parameter == triggerName)
            {
                hasTrigger = true;
                break;
            }
        }

        if (!hasTrigger)
        {
            issues.Add($"{label} 缺少 Trigger \"{triggerName}\" 条件。");
        }
    }

    /// <summary>退出型连线：必须等动画播完（HasExitTime 且 ExitTime 达标），且不能带任何条件。</summary>
    static void ValidateExitTransition(List<string> issues, AnimatorState from, AnimatorState to, string label)
    {
        if (from == null || to == null)
        {
            issues.Add($"{UpperBodyLayerName} 层缺少状态，无法校验 {label} 连线。");
            return;
        }

        AnimatorStateTransition transition = FindTransition(from, to);
        if (transition == null)
        {
            issues.Add($"缺少 {label} 连线（HasExitTime 应为 true 且 ExitTime >= {MinExitTime}，且无条件）。");
            return;
        }

        if (!transition.hasExitTime)
        {
            issues.Add($"{label} 缺乏 HasExitTime，单次动作会被立刻切走。");
        }

        if (transition.exitTime < MinExitTime - ComparisonEpsilon)
        {
            issues.Add($"{label} 的 ExitTime 为 {transition.exitTime}，应 >= {MinExitTime}。");
        }

        if (transition.conditions.Length > 0)
        {
            issues.Add($"{label} 不应带条件，当前有 {transition.conditions.Length} 个，动画会无法播完。");
        }
    }

    // ------------------------------------------------------------------
    // 通用查找
    // ------------------------------------------------------------------

    static AnimatorControllerLayer FindLayer(AnimatorController controller, string layerName)
    {
        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            if (layer.name == layerName)
            {
                return layer;
            }
        }

        return null;
    }

    static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
    {
        if (stateMachine == null)
        {
            return null;
        }

        foreach (ChildAnimatorState child in stateMachine.states)
        {
            if (child.state != null && child.state.name == stateName)
            {
                return child.state;
            }
        }

        return null;
    }

    /// <summary>在起始状态的出线里找出指向目标状态的那条连线。</summary>
    static AnimatorStateTransition FindTransition(AnimatorState from, AnimatorState to)
    {
        foreach (AnimatorStateTransition transition in from.transitions)
        {
            if (transition.destinationState == to)
            {
                return transition;
            }
        }

        return null;
    }
}
#endif
