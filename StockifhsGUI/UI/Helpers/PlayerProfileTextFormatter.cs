using System.Globalization;

namespace StockifhsGUI;

internal static class PlayerProfileTextFormatter
{
    public static string FormatMistakeLabel(string label)
    {
        return label switch
        {
            "hanging_piece" => "Loose pieces",
            "missed_tactic" => "Missed tactics",
            "opening_principles" => "Opening discipline",
            "king_safety" => "King safety",
            "endgame_technique" => "Endgame technique",
            "material_loss" => "Material losses",
            "piece_activity" => "Passive pieces",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase((label ?? string.Empty).Replace('_', ' ').ToLowerInvariant())
        };
    }

    public static string FormatPhase(GamePhase phase)
    {
        return phase switch
        {
            GamePhase.Opening => "Opening",
            GamePhase.Middlegame => "Middlegame",
            GamePhase.Endgame => "Endgame",
            _ => phase.ToString()
        };
    }

    public static string FormatOpening(string eco)
    {
        string description = OpeningCatalog.Describe(eco);
        return string.IsNullOrWhiteSpace(description)
            ? "Mixed openings"
            : description;
    }

    public static string FormatTrendHeadline(ProfileProgressDirection direction)
    {
        return direction switch
        {
            ProfileProgressDirection.Improving => "Improving lately",
            ProfileProgressDirection.Stable => "Mostly stable",
            ProfileProgressDirection.Regressing => "Results slipped recently",
            _ => "Need more games"
        };
    }

    public static string FormatTimes(int count)
    {
        return count == 1 ? "1 time" : $"{count} times";
    }

    public static string FormatMistakeCount(int count)
    {
        return count == 1 ? "1 mistake" : $"{count} mistakes";
    }

    public static string TrimSentence(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Trim().TrimEnd('.', ';', ':', '!');
    }
}
