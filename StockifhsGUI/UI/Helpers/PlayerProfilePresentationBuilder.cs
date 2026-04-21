namespace StockifhsGUI;

internal static class PlayerProfilePresentationBuilder
{
    public static PlayerProfilePresentationViewModel Build(PlayerProfileReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new PlayerProfilePresentationViewModel(
            BuildSnapshotCaption(report),
            BuildSummaryItems(report),
            BuildFixFirstItems(report),
            BuildKeyMistakes(report),
            BuildCostliestMistakes(report),
            BuildWorkOnItems(report),
            BuildTrend(report.ProgressSignal));
    }

    private static string BuildSnapshotCaption(PlayerProfileReport report)
    {
        return $"Based on {report.GamesAnalyzed} games and {report.TotalAnalyzedMoves} analyzed moves.";
    }

    private static IReadOnlyList<PlayerProfileSummaryItem> BuildSummaryItems(PlayerProfileReport report)
    {
        return
        [
            new PlayerProfileSummaryItem("Biggest problem", BuildProblemSummary(report.TopMistakeLabels, 0)),
            new PlayerProfileSummaryItem("Second problem", BuildProblemSummary(report.TopMistakeLabels, 1)),
            new PlayerProfileSummaryItem("Weakest phase", BuildWeakestPhaseSummary(report)),
            new PlayerProfileSummaryItem("Most problematic opening", BuildOpeningSummary(report)),
            new PlayerProfileSummaryItem("Recent trend", PlayerProfileTextFormatter.FormatTrendHeadline(report.ProgressSignal.Direction))
        ];
    }

    private static string BuildProblemSummary(IReadOnlyList<ProfileLabelStat> labels, int index)
    {
        if (labels.Count <= index)
        {
            return "Not enough data yet";
        }

        ProfileLabelStat item = labels[index];
        return $"{PlayerProfileTextFormatter.FormatMistakeLabel(item.Label)} ({PlayerProfileTextFormatter.FormatTimes(item.Count)})";
    }

    private static string BuildWeakestPhaseSummary(PlayerProfileReport report)
    {
        if (report.MistakesByPhase.Count == 0)
        {
            return "Not enough data yet";
        }

        ProfilePhaseStat phase = report.MistakesByPhase[0];
        return $"{PlayerProfileTextFormatter.FormatPhase(phase.Phase)} ({PlayerProfileTextFormatter.FormatMistakeCount(phase.Count)})";
    }

    private static string BuildOpeningSummary(PlayerProfileReport report)
    {
        if (report.MistakesByOpening.Count == 0)
        {
            return "Not enough data yet";
        }

        ProfileOpeningStat opening = report.MistakesByOpening[0];
        return $"{PlayerProfileTextFormatter.FormatOpening(opening.Eco)} ({PlayerProfileTextFormatter.FormatMistakeCount(opening.Count)})";
    }

    private static IReadOnlyList<string> BuildFixFirstItems(PlayerProfileReport report)
    {
        List<string> items = [];

        if (report.Recommendations.Count > 0)
        {
            TrainingRecommendation primary = report.Recommendations[0];
            TryAddFixFirst(items, primary.Checklist, 0);
            TryAddFixFirst(items, primary.Checklist, 1);
        }

        if (report.Recommendations.Count > 1)
        {
            TryAddFixFirst(items, report.Recommendations[1].Checklist, 0);
        }

        if (items.Count < 3 && report.MistakesByOpening.Count > 0)
        {
            string opening = PlayerProfileTextFormatter.FormatOpening(report.MistakesByOpening[0].Eco);
            items.Add($"Review two recent positions from {opening} where this pattern showed up.");
        }

        if (items.Count < 3 && report.MistakesByPhase.Count > 0)
        {
            string phase = PlayerProfileTextFormatter.FormatPhase(report.MistakesByPhase[0].Phase).ToLowerInvariant();
            items.Add($"Slow down in the {phase} and do a full safety check before moving.");
        }

        if (items.Count == 0)
        {
            items.Add("Pause at every big evaluation swing and ask what had to be checked first.");
            items.Add("Review two recent mistakes from your own games before the next training session.");
        }

        return items.Take(3).ToList();
    }

    private static void TryAddFixFirst(List<string> items, IReadOnlyList<string> checklist, int index)
    {
        if (checklist.Count <= index)
        {
            return;
        }

        string action = PlayerProfileTextFormatter.TrimSentence(checklist[index]);
        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        if (items.Any(existing => string.Equals(existing, action, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        items.Add(action + ".");
    }

    private static IReadOnlyList<PlayerProfileStatItem> BuildKeyMistakes(PlayerProfileReport report)
    {
        if (report.TopMistakeLabels.Count == 0)
        {
            return [new PlayerProfileStatItem("No clear pattern yet", "Analyze more games to surface recurring mistakes.")];
        }

        return report.TopMistakeLabels
            .Select(item => new PlayerProfileStatItem(
                PlayerProfileTextFormatter.FormatMistakeLabel(item.Label),
                $"{PlayerProfileTextFormatter.FormatTimes(item.Count)} in highlighted mistakes"))
            .ToList();
    }

    private static IReadOnlyList<PlayerProfileStatItem> BuildCostliestMistakes(PlayerProfileReport report)
    {
        if (report.CostliestMistakeLabels.Count == 0)
        {
            return [new PlayerProfileStatItem("No costly pattern yet", "Need more analyzed mistakes with centipawn loss data.")];
        }

        return report.CostliestMistakeLabels
            .Select(item => new PlayerProfileStatItem(
                PlayerProfileTextFormatter.FormatMistakeLabel(item.Label),
                $"Total CPL {item.TotalCentipawnLoss} | avg {item.AverageCentipawnLoss?.ToString() ?? "n/a"}"))
            .ToList();
    }

    private static IReadOnlyList<PlayerProfileWorkItem> BuildWorkOnItems(PlayerProfileReport report)
    {
        if (report.Recommendations.Count == 0)
        {
            return
            [
                new PlayerProfileWorkItem(
                    "Review critical moments",
                    "No single dominant pattern stands out yet, so start with the biggest evaluation swings from your own games.",
                    "Focus on one question: what had to be checked before the move?")
            ];
        }

        return report.Recommendations
            .Take(3)
            .Select(item => new PlayerProfileWorkItem(
                item.Title,
                item.Description,
                BuildWorkContext(item)))
            .ToList();
    }

    private static string? BuildWorkContext(TrainingRecommendation recommendation)
    {
        List<string> parts = [];

        if (recommendation.EmphasisPhase.HasValue)
        {
            parts.Add(PlayerProfileTextFormatter.FormatPhase(recommendation.EmphasisPhase.Value));
        }

        if (recommendation.EmphasisSide.HasValue)
        {
            parts.Add(recommendation.EmphasisSide.Value == PlayerSide.White ? "Mostly as White" : "Mostly as Black");
        }

        if (recommendation.RelatedOpenings.Count > 0)
        {
            parts.Add(string.Join(" / ", recommendation.RelatedOpenings.Take(2).Select(PlayerProfileTextFormatter.FormatOpening)));
        }

        return parts.Count == 0
            ? null
            : "Shows up most in " + string.Join(" | ", parts);
    }

    private static PlayerProfileTrendViewModel BuildTrend(ProfileProgressSignal signal)
    {
        string comparison = signal.Recent is not null && signal.Previous is not null
            ? $"Recent sample: {BuildPeriodSummary(signal.Recent)} Earlier sample: {BuildPeriodSummary(signal.Previous)}"
            : "Need at least four dated games to compare recent form against earlier results.";

        return new PlayerProfileTrendViewModel(
            PlayerProfileTextFormatter.FormatTrendHeadline(signal.Direction),
            signal.Summary,
            comparison);
    }

    private static string BuildPeriodSummary(ProfileProgressPeriod period)
    {
        string cpl = period.AverageCentipawnLoss?.ToString() ?? "n/a";
        return $"{period.GamesAnalyzed} games, CPL {cpl}, highlighted mistakes/game {period.HighlightedMistakesPerGame:F2}.";
    }
}
