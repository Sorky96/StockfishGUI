using System.Text.RegularExpressions;

namespace MoveMentorChessServices;

public static partial class PlayerProfileFormattedOutputValidator
{
    private static readonly string[] DebugPhrases =
    [
        "frequency:",
        "cpl cost:",
        "trend:",
        "priority score",
        "json",
        "debug",
        "model",
        "prompt"
    ];

    public static bool IsValid(PlayerProfileFormattedOutput output, PlayerProfileReport report)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(report);

        string combined = string.Join(
            " ",
            output.ProfileSummary,
            output.StrengthsAndWeaknesses,
            output.WhatToFocusNext,
            output.ToneAdaptedVersion,
            output.DeepDive ?? string.Empty);

        if (string.IsNullOrWhiteSpace(output.ProfileSummary)
            || string.IsNullOrWhiteSpace(output.StrengthsAndWeaknesses)
            || string.IsNullOrWhiteSpace(output.WhatToFocusNext)
            || string.IsNullOrWhiteSpace(output.ToneAdaptedVersion))
        {
            return false;
        }

        if (DebugPhrases.Any(phrase => combined.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return UsesOnlyKnownEcoCodes(combined, report)
            && UsesOnlyKnownProfileNumbers(combined, report);
    }

    private static bool UsesOnlyKnownEcoCodes(string text, PlayerProfileReport report)
    {
        HashSet<string> allowed = report.MistakesByOpening
            .Select(item => item.Eco)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in EcoCodeRegex().Matches(text))
        {
            if (!allowed.Contains(match.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool UsesOnlyKnownProfileNumbers(string text, PlayerProfileReport report)
    {
        HashSet<int> allowed =
        [
            report.GamesAnalyzed,
            report.TotalAnalyzedMoves,
            report.HighlightedMistakes
        ];

        if (report.AverageCentipawnLoss.HasValue)
        {
            allowed.Add(report.AverageCentipawnLoss.Value);
        }

        foreach (ProfileLabelStat item in report.TopMistakeLabels)
        {
            allowed.Add(item.Count);
        }

        foreach (ProfileCostlyLabelStat item in report.CostliestMistakeLabels)
        {
            allowed.Add(item.Count);
            allowed.Add(item.TotalCentipawnLoss);
            if (item.AverageCentipawnLoss.HasValue)
            {
                allowed.Add(item.AverageCentipawnLoss.Value);
            }
        }

        foreach (ProfilePhaseStat item in report.MistakesByPhase)
        {
            allowed.Add(item.Count);
        }

        foreach (ProfileOpeningStat item in report.MistakesByOpening)
        {
            allowed.Add(item.Count);
        }

        foreach (Match match in NumberRegex().Matches(text))
        {
            if (int.TryParse(match.Value, out int number) && !allowed.Contains(number))
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"\b[A-E][0-9]{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EcoCodeRegex();

    [GeneratedRegex(@"\b\d+\b")]
    private static partial Regex NumberRegex();
}
