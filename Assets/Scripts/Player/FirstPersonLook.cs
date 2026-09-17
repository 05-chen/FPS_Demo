using UnityEngine;

/// <summary>
/// 第一人称视角：身体左右转，摄像机上下看。
/// </summary>
public sealed class FirstPersonLook
{
    readonly Transform _body;
    readonly Transform _camera;
    float _pitch;

    public FirstPersonLook(Transform body, Transform camera)
    {
        _body = body;
        _camera = camera;
    }

    public void Tick(
        Vector2 lookDelta,
        float sensitivity,
        float minPitch,
        float maxPitch,
        float sensitivityMultiplier = 1f,
        Vector2 cameraRecoilOffset = default)
    {
        if (_body == null)
        {
            return;
        }

        float lookSensitivity = sensitivity * Mathf.Max(0.01f, sensitivityMultiplier);
        _body.Rotate(0f, lookDelta.x * lookSensitivity, 0f);
        _pitch = Mathf.Clamp(_pitch - lookDelta.y * lookSensitivity, minPitch, maxPitch);

        if (_camera != null)
        {
            // 后坐力是叠加层：鼠标只改 _pitch / 身体 Yaw，偏移独立回弹，不会被鼠标“写进”基础视角。
            _camera.localRotation = Quaternion.Euler(
                _pitch + cameraRecoilOffset.x,
                cameraRecoilOffset.y,
                0f);
        }
    }
}
