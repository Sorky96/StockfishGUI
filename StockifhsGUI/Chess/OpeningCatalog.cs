namespace StockifhsGUI;

public static class OpeningCatalog
{
    private static readonly Dictionary<string, string> ExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A00"] = "Uncommon Opening",
        ["B00"] = "Uncommon King's Pawn Opening",
        ["B01"] = "Scandinavian Defense",
        ["C20"] = "King's Pawn Game",
        ["C23"] = "Bishop's Opening",
        ["C24"] = "Bishop's Opening: Berlin Defense",
        ["C42"] = "Petrov's Defense",
        ["D00"] = "Queen's Pawn Game"
    };

    public static string Describe(string? eco)
    {
        if (string.IsNullOrWhiteSpace(eco))
        {
            return "Unknown opening";
        }

        string normalized = eco.Trim().ToUpperInvariant();
        string name = GetName(normalized);
        return string.Equals(name, normalized, StringComparison.Ordinal)
            ? normalized
            : $"{name} ({normalized})";
    }

    public static string GetName(string? eco)
    {
        if (string.IsNullOrWhiteSpace(eco))
        {
            return "Unknown opening";
        }

        string normalized = eco.Trim().ToUpperInvariant();
        if (ExactNames.TryGetValue(normalized, out string? exactName))
        {
            return exactName;
        }

        if (normalized.Length != 3 || !char.IsLetter(normalized[0]) || !char.IsDigit(normalized[1]) || !char.IsDigit(normalized[2]))
        {
            return normalized;
        }

        return normalized[0] switch
        {
            'A' => "Flank Opening",
            'B' => "Semi-Open Game",
            'C' => "Open Game",
            'D' => "Closed Game",
            'E' => "Indian Defense",
            _ => normalized
        };
    }
}
