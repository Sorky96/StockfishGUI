using System.Security.Cryptography;
using System.Text;

namespace MoveMentorChess.Opening;

internal static class OpeningGameFingerprint
{
    public static string Compute(string? pgnText)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(pgnText ?? string.Empty);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
