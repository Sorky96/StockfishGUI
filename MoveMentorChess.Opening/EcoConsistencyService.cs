using System.Globalization;

namespace MoveMentorChess.Opening;

public static class EcoConsistencyService
{
    public static bool IsConsistentWithMoves(string eco, IReadOnlyList<string> moves)
    {
        if (string.IsNullOrWhiteSpace(eco) || moves.Count < 2 || eco.Length < 3)
        {
            return true;
        }

        char family = char.ToUpperInvariant(eco[0]);
        if (!int.TryParse(eco.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int code))
        {
            return true;
        }

        string firstMove = NormalizeSan(moves[0]);
        string firstBlackMove = NormalizeSan(moves[1]);

        return family switch
        {
            'B' when code == 22 => firstMove == "e4"
                && firstBlackMove == "c5"
                && moves.Count >= 3
                && NormalizeSan(moves[2]) == "c3",
            'B' when code >= 20 => firstMove == "e4" && firstBlackMove == "c5",
            'B' => firstMove == "e4" && firstBlackMove != "e5",
            'C' when code < 20 => firstMove == "e4" && firstBlackMove == "e6",
            'C' => firstMove == "e4" && firstBlackMove == "e5",
            _ => true
        };
    }

    private static string NormalizeSan(string san)
    {
        return san
            .Replace("+", string.Empty, StringComparison.Ordinal)
            .Replace("#", string.Empty, StringComparison.Ordinal)
            .Replace("!", string.Empty, StringComparison.Ordinal)
            .Replace("?", string.Empty, StringComparison.Ordinal)
            .Trim();
    }
}
