using System.Text.RegularExpressions;

namespace MoveMentorChessServices;

public static partial class AdviceQualityValidator
{
    private static readonly string[] RequiredDetailedSections = ["What:", "Why:", "Better:", "Watch next time:"];
    private static readonly string[] GenericPhrases =
    [
        "be more careful",
        "think harder",
        "improve your position",
        "play better",
        "make better moves"
    ];

    public static bool IsUsable(
        MoveExplanation explanation,
        MistakeTag? tag,
        string? bestMoveUci,
        AdviceGenerationSettings settings,
        out string reason)
    {
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(explanation.ShortText)
            || string.IsNullOrWhiteSpace(explanation.DetailedText)
            || string.IsNullOrWhiteSpace(explanation.TrainingHint))
        {
            reason = "advice_empty_field";
            return false;
        }

        if (explanation.ShortText.Length > settings.MaxShortTextLength
            || explanation.DetailedText.Length > settings.MaxDetailedTextLength
            || explanation.TrainingHint.Length > settings.MaxTrainingHintLength)
        {
            reason = "advice_length_limit";
            return false;
        }

        foreach (string section in RequiredDetailedSections)
        {
            if (!explanation.DetailedText.Contains(section, StringComparison.OrdinalIgnoreCase))
            {
                reason = "advice_missing_structure";
                return false;
            }
        }

        string combined = $"{explanation.ShortText} {explanation.DetailedText} {explanation.TrainingHint}";
        if (GenericPhrases.Any(phrase => combined.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            reason = "advice_generic_text";
            return false;
        }

        string label = tag?.Label ?? "unclassified";
        bool mentionsMotif = !string.Equals(label, "unclassified", StringComparison.Ordinal)
            && combined.Contains(label.Replace('_', ' '), StringComparison.OrdinalIgnoreCase);
        bool mentionsBestMove = !string.IsNullOrWhiteSpace(bestMoveUci)
            && combined.Contains(bestMoveUci, StringComparison.OrdinalIgnoreCase);
        bool hasEvidenceSpecificity = tag?.Evidence.Any(e => ContainsAnyToken(combined, e)) == true;

        if (!mentionsMotif && !mentionsBestMove && !hasEvidenceSpecificity)
        {
            reason = "advice_lacks_specific_motif";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static bool HasOnlyAllowedReferencedMoves(
        string text,
        string? playedMoveUci,
        string? bestMoveUci,
        out string unexpectedMove)
    {
        unexpectedMove = string.Empty;
        HashSet<string> allowed = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(playedMoveUci))
        {
            allowed.Add(playedMoveUci);
        }

        if (!string.IsNullOrWhiteSpace(bestMoveUci))
        {
            allowed.Add(bestMoveUci);
        }

        foreach (Match match in UciMoveRegex().Matches(text))
        {
            string value = match.Value;
            if (!allowed.Contains(value))
            {
                unexpectedMove = value;
                return false;
            }
        }

        return true;
    }

    private static bool ContainsAnyToken(string text, string evidence)
    {
        foreach (string token in evidence.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length >= 5 && text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"\b[a-h][1-8][a-h][1-8][qrbn]?\b", RegexOptions.IgnoreCase)]
    private static partial Regex UciMoveRegex();
}
