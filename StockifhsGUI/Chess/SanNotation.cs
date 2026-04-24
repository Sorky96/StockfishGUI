using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace StockifhsGUI;

public static class SanNotation
{
    private static readonly Regex SanCleanupRegex = new(@"[!?]+", RegexOptions.Compiled);
    private static readonly Regex MoveNumberPrefixRegex = new(@"^\d+\.(\.\.)?", RegexOptions.Compiled);
    private static readonly Regex SanTokenRegex = new(
        @"^(?:(?:O-O-O|O-O|0-0-0|0-0)[+#]?|[KQRBN]?[a-h]?[1-8]?x?[a-h][1-8](?:=[QRBN])?[+#]?|[a-h]x[a-h][1-8](?:=[QRBN])?[+#]?|[a-h][1-8](?:=[QRBN])?[+#]?)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<string> ParsePgnMoves(string pgnText)
    {
        return ParsePgnMoves(pgnText, maxMoves: null);
    }

    public static List<string> ParsePgnMoves(string pgnText, int? maxMoves)
    {
        if (maxMoves.HasValue)
        {
            return ParsePgnMovesWithLimit(pgnText, Math.Max(0, maxMoves.Value));
        }

        string text = Regex.Replace(pgnText, @"\[.*?\]", " ");
        text = Regex.Replace(text, @"\{.*?\}", " ");
        text = Regex.Replace(text, @"\(.*?\)", " ");
        text = Regex.Replace(text, @";[^\r\n]*", " ");
        text = text.Replace('\r', ' ').Replace('\n', ' ');

        List<string> moves = new();
        foreach (string rawToken in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryReadSanToken(rawToken, out string token))
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

    public static string RemoveCheckSuffix(string san)
    {
        string normalized = NormalizeSan(san);
        return Regex.Replace(normalized, @"[+#]+$", string.Empty);
    }

    public static bool HasExplicitPiecePrefix(string normalizedSan)
    {
        if (string.IsNullOrEmpty(normalizedSan))
        {
            return false;
        }

        char first = normalizedSan[0];
        if ("KQRBN".Contains(first))
        {
            return true;
        }

        return "kqrbn".Contains(first) && ShouldUppercasePiecePrefix(normalizedSan);
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

    private static List<string> ParsePgnMovesWithLimit(string pgnText, int maxMoves)
    {
        List<string> moves = new(maxMoves);
        if (maxMoves == 0)
        {
            return moves;
        }

        bool inHeader = false;
        bool inComment = false;
        int variationDepth = 0;
        bool inLineComment = false;
        StringBuilder tokenBuilder = new(32);

        for (int i = 0; i < pgnText.Length; i++)
        {
            char c = pgnText[i];
            if (inLineComment)
            {
                if (c is '\r' or '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inHeader)
            {
                if (c == ']')
                {
                    inHeader = false;
                }

                continue;
            }

            if (inComment)
            {
                if (c == '}')
                {
                    inComment = false;
                }

                continue;
            }

            if (variationDepth > 0)
            {
                if (c == '(')
                {
                    variationDepth++;
                }
                else if (c == ')')
                {
                    variationDepth--;
                }

                continue;
            }

            if (c == '[')
            {
                FlushToken();
                inHeader = true;
                continue;
            }

            if (c == '{')
            {
                FlushToken();
                inComment = true;
                continue;
            }

            if (c == '(')
            {
                FlushToken();
                variationDepth = 1;
                continue;
            }

            if (c == ';')
            {
                FlushToken();
                inLineComment = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                FlushToken();
                if (moves.Count >= maxMoves)
                {
                    break;
                }

                continue;
            }

            tokenBuilder.Append(c);

            if (i == pgnText.Length - 1)
            {
                FlushToken();
            }
        }

        return moves;

        void FlushToken()
        {
            if (tokenBuilder.Length == 0 || moves.Count >= maxMoves)
            {
                tokenBuilder.Clear();
                return;
            }

            string rawToken = tokenBuilder.ToString();
            tokenBuilder.Clear();
            if (TryReadSanToken(rawToken, out string token))
            {
                moves.Add(token);
            }
        }
    }

    private static bool TryReadSanToken(string rawToken, out string token)
    {
        token = MoveNumberPrefixRegex.Replace(rawToken.Trim(), string.Empty);
        if (string.IsNullOrWhiteSpace(token) || token is "$" or "1-0" or "0-1" or "1/2-1/2" or "*")
        {
            return false;
        }

        if (token.StartsWith('$'))
        {
            return false;
        }

        return SanTokenRegex.IsMatch(token);
    }
}
