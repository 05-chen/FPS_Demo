using UnityEngine;

/// <summary>
/// 6 位房间邀请码。Steam 大厅 ID 太长，所以房间数据里另存一份短码，加入时按短码搜索大厅。
/// 去掉 0/O、1/I，减少看错。
/// </summary>
public static class JoinCodeUtility
{
    public const string LobbyDataKey = "join_code";
    public const int Length = 6;
    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generate()
    {
        var chars = new char[Length];
        for (int i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[Random.Range(0, Alphabet.Length)];
        }

        return new string(chars);
    }

    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return raw.Trim().Replace(" ", "").Replace("-", "").ToUpperInvariant();
    }

    public static bool IsValid(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != Length)
        {
            return false;
        }

        for (int i = 0; i < code.Length; i++)
        {
            if (Alphabet.IndexOf(code[i]) < 0)
            {
                return false;
            }
        }

        return true;
    }
}
