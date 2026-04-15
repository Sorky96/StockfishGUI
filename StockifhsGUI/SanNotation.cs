using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StockifhsGUI;

public static class SanNotation
{
    private static readonly Regex SanCleanupRegex = new(@"[!?]+", RegexOptions.Compiled);
    private static readonly Regex SanTokenRegex = new(
        @"^(?:O-O-O|O-O|0-0-0|0-0|[KQRBN]?[a-h]?[1-8]?x?[a-h][1-8](?:=[QRBN])?[+#]?|[a-h]x[a-h][1-8](?:=[QRBN])?[+#]?|[a-h][1-8](?:=[QRBN])?[+#]?)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<string> ParsePgnMoves(string pgnText)
    {
        string text = Regex.Replace(pgnText, @"\[.*?\]", " ");
        text = Regex.Replace(text, @"\{.*?\}", " ");
        text = Regex.Replace(text, @"\(.*?\)", " ");
        text = Regex.Replace(text, @";[^\r\n]*", " ");
        text = text.Replace('\r', ' ').Replace('\n', ' ');

        List<string> moves = new();
        foreach (string rawToken in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string token = Regex.Replace(rawToken.Trim(), @"^\d+\.(\.\.)?", string.Empty);
            if (string.IsNullOrWhiteSpace(token) || token is "$" or "1-0" or "0-1" or "1/2-1/2" or "*")
            {
                continue;
            }

            if (token.StartsWith('$'))
            {
                continue;
            }

            if (SanTokenRegex.IsMatch(token))
            {
                moves.Add(token);
            }
        }

        return moves;
    }

    public static string NormalizeSan(string san)
    {
        string normalized = san.Trim();
        normalized = normalized.Replace('\u00A0', ' ');
        normalized = normalized.Replace("×", "x", StringComparison.Ordinal);
        normalized = normalized.Replace(":", "x", StringComparison.Ordinal);
        normalized = normalized.Replace("0-0-0", "O-O-O", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("0-0", "O-O", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("o-o-o", "O-O-O", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("o-o", "O-O", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("e.p.", string.Empty, StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("ep", string.Empty, StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace(" ", string.Empty);
        normalized = Regex.Replace(normalized, @"[^\w=+#\-xO]", string.Empty, RegexOptions.IgnoreCase);
        normalized = SanCleanupRegex.Replace(normalized, string.Empty);
        if (normalized.Length > 0 && ShouldUppercasePiecePrefix(normalized))
        {
            normalized = char.ToUpperInvariant(normalized[0]) + normalized[1..];
        }

        return normalized;
    }

    private static bool ShouldUppercasePiecePrefix(string normalized)
    {
        char first = normalized[0];
        if (!"kqrbn".Contains(first))
        {
            return false;
        }

        if (first == 'b' && normalized.Length > 1 && (normalized[1] == 'x' || char.IsDigit(normalized[1])))
        {
            return false;
        }

        return true;
    }
}
