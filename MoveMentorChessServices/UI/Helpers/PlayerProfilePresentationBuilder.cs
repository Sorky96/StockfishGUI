namespace MoveMentorChessServices;

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
            BuildTrend(report.ProgressSignal),
            BuildTrainingPlan(report));
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
            new PlayerProfileSummaryItem("Training priority", BuildTrainingPrioritySummary(report)),
            new PlayerProfileSummaryItem("Weakest phase", BuildWeakestPhaseSummary(report)),
            new PlayerProfileSummaryItem("Most problematic opening", BuildOpeningSummary(report)),
            new PlayerProfileSummaryItem("Recent trend", PlayerProfileTextFormatter.FormatTrendHeadline(report.ProgressSignal.Direction))
        ];
    }

    private static string BuildTrainingPrioritySummary(PlayerProfileReport report)
    {
        TrainingPlanTopic? core = report.TrainingPlan.Topics
            .FirstOrDefault(topic => topic.Category == TrainingPlanTopicCategory.CoreWeakness);

        core ??= report.TrainingPlan.Topics.FirstOrDefault();

        return core is null
            ? "Not enough data yet"
            : $"{core.Title} ({core.FocusArea})";
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
        if (report.TrainingPlan.Topics.Count == 0)
        {
            return
            [
                new PlayerProfileWorkItem(
                    "Review critical moments",
                    "No single dominant pattern stands out yet, so start with the biggest evaluation swings from your own games.",
                    "Focus on one question: what had to be checked before the move?")
            ];
        }

        return report.TrainingPlan.Topics
            .Take(3)
            .Select(item => new PlayerProfileWorkItem(
                $"{BuildRoleLabel(item.Category)}: {item.Title}",
                item.Summary,
                BuildTopicContext(item)))
            .ToList();
    }

    private static string? BuildTopicContext(TrainingPlanTopic topic)
    {
        List<string> parts = [];

        if (topic.EmphasisPhase.HasValue)
        {
            parts.Add(PlayerProfileTextFormatter.FormatPhase(topic.EmphasisPhase.Value));
        }

        if (topic.EmphasisSide.HasValue)
        {
            parts.Add(topic.EmphasisSide.Value == PlayerSide.White ? "Mostly as White" : "Mostly as Black");
        }

        if (topic.RelatedOpenings.Count > 0)
        {
            parts.Add(string.Join(" / ", topic.RelatedOpenings.Take(2).Select(PlayerProfileTextFormatter.FormatOpening)));
        }

        if (parts.Count == 0)
        {
            return topic.WhyThisTopicNow;
        }

        return $"{topic.WhyThisTopicNow} Shows up most in {string.Join(" | ", parts)}";
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

    private static PlayerProfileTrainingPlanViewModel BuildTrainingPlan(PlayerProfileReport report)
    {
        IReadOnlyList<PlayerProfileTrainingTopicViewModel> topics = report.TrainingPlan.Topics
            .OrderBy(topic => topic.Priority)
            .Select(topic => new PlayerProfileTrainingTopicViewModel(
                BuildRoleLabel(topic.Category),
                topic.Title,
                topic.FocusArea,
                topic.Summary,
                topic.WhyThisTopicNow,
                topic.Rationale,
                BuildTopicDisplayContext(topic),
                (topic.Blocks ?? [])
                    .Select(block => new PlayerProfileTrainingBlockViewModel(
                        PlayerProfileTextFormatter.FormatTrainingBlockPurpose(block.Purpose),
                        PlayerProfileTextFormatter.FormatTrainingBlockKind(block.Kind),
                        block.Title,
                        block.Description,
                        block.EstimatedMinutes))
                    .ToList()))
            .ToList();

        IReadOnlyList<PlayerProfileTrainingPlanItemViewModel> items = report.TrainingPlan.Topics
            .OrderBy(topic => topic.Priority)
            .SelectMany(topic => (topic.Blocks ?? [])
                .OrderBy(block => GetBlockPurposeOrder(block.Purpose))
                .ThenBy(block => block.EstimatedMinutes)
                .Select(block => new PlayerProfileTrainingPlanItemViewModel(
                    topic.Priority,
                    $"Priority {topic.Priority}",
                    topic.Title,
                    PlayerProfileTextFormatter.FormatTrainingBlockKind(block.Kind),
                    PlayerProfileTextFormatter.FormatTrainingBlockPurpose(block.Purpose),
                    block.EstimatedMinutes,
                    block.Title,
                    topic.WhyThisTopicNow,
                    BuildTopicDisplayContext(topic))))
            .ToList();

        IReadOnlyList<PlayerProfileTrainingDayViewModel> days = report.TrainingPlan.WeeklyPlan.Days
            .Select(day => new PlayerProfileTrainingDayViewModel(
                day.DayNumber,
                day.Topic,
                day.WorkType,
                day.Goal,
                day.EstimatedMinutes,
                BuildRoleLabel(day.Category)))
            .ToList();

        string headline = topics.Count == 0
            ? "Training plan"
            : $"Training plan built from {string.Join(", ", topics.Select(topic => topic.Title))}.";

        return new PlayerProfileTrainingPlanViewModel(
            headline,
            report.TrainingPlan.Summary,
            report.TrainingPlan.WeeklyPlan.Budget.Summary,
            topics,
            items,
            days);
    }

    private static int GetBlockPurposeOrder(TrainingBlockPurpose purpose)
    {
        return purpose switch
        {
            TrainingBlockPurpose.Repair => 0,
            TrainingBlockPurpose.Maintain => 1,
            TrainingBlockPurpose.Checklist => 2,
            _ => 3
        };
    }

    private static string BuildRoleLabel(TrainingPlanTopicCategory category)
    {
        return category switch
        {
            TrainingPlanTopicCategory.CoreWeakness => "Core weakness",
            TrainingPlanTopicCategory.SecondaryWeakness => "Secondary weakness",
            TrainingPlanTopicCategory.MaintenanceTopic => "Maintenance topic",
            _ => "Training topic"
        };
    }

    private static string? BuildTopicDisplayContext(TrainingPlanTopic topic)
    {
        List<string> parts = [];

        if (topic.EmphasisPhase.HasValue)
        {
            parts.Add(PlayerProfileTextFormatter.FormatPhase(topic.EmphasisPhase.Value));
        }

        if (topic.EmphasisSide.HasValue)
        {
            parts.Add(topic.EmphasisSide.Value == PlayerSide.White ? "Mostly as White" : "Mostly as Black");
        }

        if (topic.RelatedOpenings.Count > 0)
        {
            parts.Add(string.Join(" / ", topic.RelatedOpenings.Take(2).Select(PlayerProfileTextFormatter.FormatOpening)));
        }

        return parts.Count == 0
            ? null
            : string.Join(" | ", parts);
    }
}
