using Core;
using UnityEngine;

/// <summary>
/// 站立 / 蹲伏 / 趴下：输入状态、胶囊高度、摄像机高度。
/// </summary>
public sealed class PlayerStanceController
{
    public struct Settings
    {
        public float StandingHeight;
        public float CrouchingHeight;
        public float ProneHeight;
        public float StandingCameraHeight;
        public float CrouchingCameraHeight;
        public float ProneCameraHeight;
        public float StanceChangeSpeed;
    }

    readonly CharacterController _characterController;
    readonly Camera _playerCamera;
    Settings _settings;

    bool _sprintLatched;
    bool _wasMoving;

    public bool IsCrouching { get; private set; }
    public bool IsSprinting { get; private set; }

    public float StandingCameraHeight => _settings.StandingCameraHeight;
    public float StandingHeight => _settings.StandingHeight;

    public PlayerStanceController(
        CharacterController characterController,
        Camera playerCamera,
        Settings settings)
    {
        _characterController = characterController;
        _playerCamera = playerCamera;
        _settings = settings;
    }

    public void UpdateFromInput(in GameplayInputState input, InjuryState injury)
    {
        if (IsDowned(injury))
        {
            _sprintLatched = false;
            _wasMoving = false;
            IsCrouching = false;
            IsSprinting = false;
            return;
        }

        IsCrouching = input.CrouchHeld || injury == InjuryState.Crippled_Legs;

        if (IsCrouching)
        {
            _sprintLatched = false;
        }
        else if (input.SprintPressed)
        {
            _sprintLatched = true;
        }

        if (_wasMoving && !input.HasMoveInput)
        {
            _sprintLatched = false;
        }

        _wasMoving = input.HasMoveInput;
        IsSprinting = _sprintLatched && !IsCrouching && input.HasMoveInput;
    }

    public void TickCapsule(float deltaTime, InjuryState injury)
    {
        if (_characterController == null)
        {
            return;
        }

        float t = _settings.StanceChangeSpeed * deltaTime;
        ResolveTargets(injury, out float targetHeight, out _);
        ApplyCapsuleHeight(Mathf.Lerp(_characterController.height, targetHeight, t));
    }

    public void SyncCameraHeight(float deltaTime, InjuryState injury, bool cameraPositionControlledExternally)
    {
        if (_playerCamera == null || cameraPositionControlledExternally)
        {
            return;
        }

        ResolveTargets(injury, out _, out float cameraFromFeet);
        Vector3 localPos = _playerCamera.transform.localPosition;
        localPos.y = Mathf.Lerp(
            localPos.y,
            cameraFromFeet - _settings.StandingHeight * 0.5f,
            _settings.StanceChangeSpeed * deltaTime);
        _playerCamera.transform.localPosition = localPos;
    }

    public float DesiredCameraHeightFromFeet(InjuryState injury)
    {
        ResolveTargets(injury, out _, out float cameraFromFeet);
        return cameraFromFeet;
    }

    public void ResolveTargets(InjuryState injury, out float targetHeight, out float cameraFromFeet)
    {
        if (IsDowned(injury))
        {
            targetHeight = _settings.ProneHeight;
            cameraFromFeet = _settings.ProneCameraHeight;
            return;
        }

        if (IsCrouching || injury == InjuryState.Crippled_Legs)
        {
            targetHeight = _settings.CrouchingHeight;
            cameraFromFeet = _settings.CrouchingCameraHeight;
            return;
        }

        targetHeight = _settings.StandingHeight;
        cameraFromFeet = _settings.StandingCameraHeight;
    }

    public void ApplyCapsuleHeight(float height)
    {
        if (_characterController == null)
        {
            return;
        }

        const float defaultRadius = 0.5f;
        const float defaultStepOffset = 0.3f;

        _characterController.height = height;
        _characterController.radius = Mathf.Min(defaultRadius, height * 0.5f);
        _characterController.center = new Vector3(0f, (height - _settings.StandingHeight) * 0.5f, 0f);
        _characterController.stepOffset = Mathf.Min(defaultStepOffset, height * 0.5f);
    }

    static bool IsDowned(InjuryState injury)
    {
        return injury == InjuryState.DBNO_Torso || injury == InjuryState.InstanceDeath_Head;
    }
}
