using UnityEngine;

/// <summary>
/// 一帧的玩法输入快照。把旧 Input Manager 关在这里，以后换新输入系统只改这一处。
/// </summary>
public readonly struct GameplayInputState
{
    public readonly Vector2 Move;
    public readonly Vector2 Look;

    public readonly bool JumpPressed;
    public readonly bool UnlockCursor;
    public readonly bool RelockCursor;
    public readonly bool FirePressed;
    public readonly bool CrouchHeld;
    public readonly bool SprintPressed;
    public readonly bool HasMoveInput;

    public GameplayInputState(
        Vector2 move,
        Vector2 look,
        bool jumpPressed,
        bool unlockCursor,
        bool relockCursor,
        bool firePressed,
        bool crouchHeld,
        bool sprintPressed,
        bool hasMoveInput)
    {
        Move = move;
        Look = look;
        JumpPressed = jumpPressed;
        UnlockCursor = unlockCursor;
        RelockCursor = relockCursor;
        FirePressed = firePressed;
        CrouchHeld = crouchHeld;
        SprintPressed = sprintPressed;
        HasMoveInput = hasMoveInput;
    }

    public static GameplayInputState Read()
    {
        bool isCursorLocked = Cursor.lockState == CursorLockMode.Locked;
        // 只认左键开火；右键留给机瞄（WeaponADS），不要并进 FirePressed。
        bool fire = isCursorLocked && Input.GetMouseButtonDown(0);
        Vector2 rawMove = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

        return new GameplayInputState(
            new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")),
            new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")),
            Input.GetButtonDown("Jump"),
            Input.GetKeyDown(KeyCode.Escape),
            Input.GetMouseButtonDown(0),
            fire,
            Input.GetKey(KeyCode.LeftControl),
            Input.GetKeyDown(KeyCode.LeftShift),
            rawMove.sqrMagnitude > 0f);
    }
}
