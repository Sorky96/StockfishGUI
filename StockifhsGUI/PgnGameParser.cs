using System.Text.RegularExpressions;

namespace StockifhsGUI;

public static class PgnGameParser
{
    private static readonly Regex HeaderRegex = new(@"^\[(?<key>[A-Za-z0-9_]+)\s+""(?<value>.*)""\]\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    public static ImportedGame Parse(string pgn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pgn);

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in HeaderRegex.Matches(pgn))
        {
            string key = match.Groups["key"].Value;
            string value = match.Groups["value"].Value;
            headers[key] = value;
        }

        List<string> sanMoves = ChessGame.ParsePgnMoves(pgn);

        return new ImportedGame(
            pgn,
            sanMoves,
            GetValue(headers, "White"),
            GetValue(headers, "Black"),
            ParseNullableInt(headers, "WhiteElo"),
            ParseNullableInt(headers, "BlackElo"),
            GetValue(headers, "Date"),
            GetValue(headers, "Result"),
            GetValue(headers, "ECO"),
            GetValue(headers, "Site"));
    }

    private static string? GetValue(IReadOnlyDictionary<string, string> headers, string key)
    {
        return headers.TryGetValue(key, out string? value) ? value : null;
    }

    private static int? ParseNullableInt(IReadOnlyDictionary<string, string> headers, string key)
    {
        return headers.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed)
            ? parsed
            : null;
    }
}
