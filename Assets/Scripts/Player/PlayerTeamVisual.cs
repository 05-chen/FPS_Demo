using Core;
using UnityEngine;

/// <summary>
/// 阵营外观：名称推断与材质颜色。网络阵营 NV / RPC 仍由 PlayerController 持有。
/// </summary>
public sealed class PlayerTeamVisual
{
    static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    static readonly Color RedColor = new Color(0.85f, 0.15f, 0.15f);
    static readonly Color BlueColor = new Color(0.15f, 0.35f, 0.9f);

    readonly Renderer _meshRenderer;
    readonly MaterialPropertyBlock _propertyBlock;

    public PlayerTeamVisual(Renderer meshRenderer)
    {
        _meshRenderer = meshRenderer;
        _propertyBlock = new MaterialPropertyBlock();
    }

    public static TeamId DetectTeamByName(string objectName)
    {
        if (objectName.Equals("RED", System.StringComparison.OrdinalIgnoreCase))
        {
            return TeamId.Red;
        }

        if (objectName.Equals("BLUE", System.StringComparison.OrdinalIgnoreCase))
        {
            return TeamId.Blue;
        }

        return TeamId.None;
    }

    public static string DisplayObjectName(TeamId team)
    {
        return team == TeamId.Red ? "Player_Red" : team == TeamId.Blue ? "Player_Blue" : "Player";
    }

    public void ApplyTeamColor(TeamId team)
    {
        if (_meshRenderer == null || _propertyBlock == null)
        {
            return;
        }

        Color color = team == TeamId.Red
            ? RedColor
            : team == TeamId.Blue
                ? BlueColor
                : _meshRenderer.sharedMaterial != null
                    ? _meshRenderer.sharedMaterial.color
                    : Color.white;
        _meshRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(ColorPropertyId, color);
        _meshRenderer.SetPropertyBlock(_propertyBlock);
    }
}
