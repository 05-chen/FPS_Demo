using UnityEngine;

/// <summary>
/// CharacterController 移动。纯逻辑，不读输入、不管网络。
/// </summary>
public sealed class CharacterMotor
{
    readonly CharacterController _controller;
    float _verticalVelocity;

    public CharacterMotor(CharacterController controller)
    {
        _controller = controller;
    }

    public void ResetVertical()
    {
        _verticalVelocity = -2f;
    }

    public void Tick(Vector2 moveInput, bool jumpPressed, float moveSpeed, float jumpHeight, float gravity, float deltaTime)
    {
        if (_controller == null || !_controller.enabled)
        {
            return;
        }

        Vector3 input = new Vector3(moveInput.x, 0f, moveInput.y);
        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        Vector3 worldMove = _controller.transform.TransformDirection(input) * moveSpeed;

        if (_controller.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f;
        }

        if (_controller.isGrounded && jumpPressed)
        {
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        _verticalVelocity += gravity * deltaTime;
        worldMove.y = _verticalVelocity;
        _controller.Move(worldMove * deltaTime);
    }
}
