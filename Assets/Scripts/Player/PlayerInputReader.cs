using UnityEngine;

/// <summary>
/// 玩法输入读取入口。当前委托 <see cref="GameplayInputState"/>，便于日后换新输入系统。
/// </summary>
public static class PlayerInputReader
{
    public static GameplayInputState Read() => GameplayInputState.Read();
}
