using System.Text.RegularExpressions;

namespace StockifhsGUI;

public static class OpeningPgnMetadataParser
{
    private static readonly Regex HeaderRegex = new(@"^\[(?<key>[A-Za-z0-9_]+)\s+""(?<value>.*)""\]\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    public static OpeningGameMetadata Parse(string pgn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pgn);

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in HeaderRegex.Matches(pgn))
        {
            headers[match.Groups["key"].Value] = match.Groups["value"].Value;
        }

        string eco = GetValue(headers, "ECO");
        string opening = GetValue(headers, "Opening");
        string variation = GetValue(headers, "Variation");

        return new OpeningGameMetadata(eco, opening, variation);
    }

    private static string GetValue(IReadOnlyDictionary<string, string> headers, string key)
    {
        return headers.TryGetValue(key, out string? value) ? value : string.Empty;
    }
}
