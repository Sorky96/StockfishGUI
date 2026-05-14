namespace MoveMentorChess.Training;

public sealed class PlayerOpeningPlanService
{
    public PlayerOpeningPlan BuildPlan(
        string? playerKey,
        TrainingRecommendationCard? recommendation,
        IReadOnlyList<OpeningLineCatalogItem> availableLines,
        IReadOnlyList<OpeningReviewItem> reviewItems,
        IReadOnlyList<OpeningTrainingSessionResult> sessionResults,
        IReadOnlyList<OpeningTrainingScheduledAction>? dueActions = null)
    {
        ArgumentNullException.ThrowIfNull(availableLines);
        ArgumentNullException.ThrowIfNull(reviewItems);
        ArgumentNullException.ThrowIfNull(sessionResults);

        string normalizedKey = string.IsNullOrWhiteSpace(playerKey) ? "theory" : playerKey.Trim().ToLowerInvariant();
        string displayName = string.IsNullOrWhiteSpace(playerKey) ? "Theory profile" : playerKey.Trim();
        TrainingProgressSnapshot progress = BuildProgress(sessionResults);
        IReadOnlyList<PlayerOpeningPlanItem> today = BuildToday(recommendation, progress, dueActions);
        IReadOnlyList<PlayerOpeningPlanItem> thisWeek = BuildThisWeek(availableLines, sessionResults, reviewItems);
        IReadOnlyList<PlayerOpeningPlanItem> longTermGaps = BuildLongTermGaps(availableLines, reviewItems, sessionResults);
        string summary = progress.SessionCount == 0
            ? "No completed repertoire sessions yet. Today's plan starts from high-value theory and will personalize as history grows."
            : $"Based on your last session: {progress.AttemptCount} moves practiced, {progress.AccuracyPercent:0.#}% accepted.";

        return new PlayerOpeningPlan(
            normalizedKey,
            displayName,
            summary,
            today,
            thisWeek,
            longTermGaps,
            progress);
    }

    private static TrainingProgressSnapshot BuildProgress(IReadOnlyList<OpeningTrainingSessionResult> history)
    {
        IReadOnlyList<OpeningTrainingSessionResult> completed = history
            .Where(result => result.Outcome == OpeningTrainingSessionOutcome.Completed)
            .ToList();
        int attempts = completed.Sum(result => result.AttemptCount);
        int correct = completed.Sum(result => result.CorrectCount);
        int playable = completed.Sum(result => result.PlayableCount);
        int wrong = completed.Sum(result => result.WrongCount);
        double accuracy = attempts == 0
            ? 0
            : Math.Round((double)(correct + playable) / attempts * 100d, 1);

        return new TrainingProgressSnapshot(
            completed.Count,
            attempts,
            correct,
            playable,
            wrong,
            accuracy,
            completed.Count == 0 ? null : completed.Max(result => result.CompletedUtc));
    }

    private static IReadOnlyList<PlayerOpeningPlanItem> BuildToday(
        TrainingRecommendationCard? recommendation,
        TrainingProgressSnapshot progress,
        IReadOnlyList<OpeningTrainingScheduledAction>? dueActions)
    {
        IReadOnlyList<PlayerOpeningPlanItem> dueItems = (dueActions ?? [])
            .OrderByDescending(action => action.Priority)
            .ThenBy(action => action.DueUtc)
            .Take(3)
            .GroupBy(action => action.Kind)
            .Select((group, index) => new PlayerOpeningPlanItem(
                FormatDueActionTitle(group.Key),
                FormatDueActionDetail(group),
                string.Empty,
                null,
                TrainingPlanTopicCategory.CoreWeakness,
                index + 1,
                10))
            .ToList();

        if (recommendation is null)
        {
            if (dueItems.Count > 0)
            {
                return dueItems;
            }

            return
            [
                new PlayerOpeningPlanItem(
                    "Start with any imported line",
                    "Import or select an opening line to seed the daily plan.",
                    "No recommendation card is available yet.",
                    null,
                    TrainingPlanTopicCategory.CoreWeakness,
                    1,
                    10)
            ];
        }

        string evidence = progress.SessionCount == 0
            ? recommendation.Reason
            : $"{recommendation.Reason} Current opening-trainer accuracy: {progress.AccuracyPercent:0.#}%.";

        return dueItems.Concat(
        [
            new PlayerOpeningPlanItem(
                "Main line review",
                $"{recommendation.Difficulty} study, about {recommendation.EstimatedDurationMinutes} minutes",
                evidence,
                recommendation.OpeningLine.Eco,
                TrainingPlanTopicCategory.CoreWeakness,
                dueItems.Count + 1,
                recommendation.EstimatedDurationMinutes)
        ]).ToList();
    }

    private static string FormatDueActionTitle(TrainingNextActionKind kind)
        => kind switch
        {
            TrainingNextActionKind.RepeatAfterBreak => "Main line review",
            TrainingNextActionKind.ReturnTomorrow => "Next scheduled review",
            TrainingNextActionKind.RepairWeakBranches => "Weak branch repair",
            _ => "Opening line review"
        };

    private static string FormatDueActionDetail(IGrouping<TrainingNextActionKind, OpeningTrainingScheduledAction> actions)
    {
        DateTime earliestDueUtc = actions.Min(action => action.DueUtc);
        string dueSince = $"due since {earliestDueUtc.ToLocalTime():HH:mm}";
        int count = actions.Count();
        return count == 1 ? dueSince : $"{count} due items; earliest {dueSince}";
    }

    private static IReadOnlyList<PlayerOpeningPlanItem> BuildThisWeek(
        IReadOnlyList<OpeningLineCatalogItem> availableLines,
        IReadOnlyList<OpeningTrainingSessionResult> history,
        IReadOnlyList<OpeningReviewItem> reviewItems)
    {
        Dictionary<string, int> wrongByEco = history
            .SelectMany(result => result.Attempts)
            .Where(attempt => attempt.Score == OpeningTrainingScore.Wrong)
            .GroupBy(attempt => attempt.Eco, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        Dictionary<OpeningLineKey, HashSet<string>> reviewedBranchesByLine = BuildReviewedBranchesByLine(reviewItems);
        HashSet<string> legacyReviewedBranches = BuildLegacyReviewedBranches(reviewItems);

        return availableLines
            .Select(line =>
            {
                int wrong = wrongByEco.TryGetValue(line.Eco, out int value) ? value : 0;
                int gap = Math.Max(0, line.BookBranchCount - GetReviewedCountForLine(line, reviewedBranchesByLine, legacyReviewedBranches));
                int score = wrong * 30 + gap * 3 + Math.Min(line.BookGameCount, 40);
                return new
                {
                    Line = line,
                    Wrong = wrong,
                    Gap = gap,
                    Score = score
                };
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Line.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .GroupBy(item => $"{item.Line.Eco}|{item.Line.DisplayName}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Wrong)
                .ThenByDescending(item => item.Gap)
                .First())
            .Select((item, index) => new PlayerOpeningPlanItem(
                item.Line.DisplayName,
                item.Wrong > 0 ? "Weak branch repair." : "Main line review.",
                item.Wrong > 0
                    ? $"{item.Wrong} moves are ready for review."
                    : $"{item.Gap} common branch gap(s) remain by current review history.",
                item.Line.Eco,
                index == 0 ? TrainingPlanTopicCategory.CoreWeakness : TrainingPlanTopicCategory.SecondaryWeakness,
                index + 1,
                Math.Clamp(8 + item.Line.BookBranchCount, 10, 25)))
            .ToList();
    }

    private static IReadOnlyList<PlayerOpeningPlanItem> BuildLongTermGaps(
        IReadOnlyList<OpeningLineCatalogItem> availableLines,
        IReadOnlyList<OpeningReviewItem> reviewItems,
        IReadOnlyList<OpeningTrainingSessionResult> history)
    {
        Dictionary<OpeningLineKey, HashSet<string>> reviewedBranchesByLine = BuildReviewedBranchesByLine(reviewItems);
        HashSet<string> legacyReviewedBranches = BuildLegacyReviewedBranches(reviewItems);
        HashSet<string> trainedEco = history
            .SelectMany(result => result.RelatedOpenings)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return availableLines
            .Select(line =>
            {
                int branchGap = Math.Max(0, line.BookBranchCount - GetReviewedCountForLine(line, reviewedBranchesByLine, legacyReviewedBranches));
                int noveltyBonus = trainedEco.Contains(line.Eco) ? 0 : 8;
                return new
                {
                    Line = line,
                    BranchGap = branchGap,
                    Score = branchGap * 5 + noveltyBonus + Math.Min(line.BookGameCount, 30)
                };
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Line.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select((item, index) => new PlayerOpeningPlanItem(
                item.Line.DisplayName,
                "Long-term repertoire gap.",
                trainedEco.Contains(item.Line.Eco)
                    ? $"{item.BranchGap} branch gap(s) remain in review history."
                    : "No completed session is recorded for this ECO yet.",
                item.Line.Eco,
                TrainingPlanTopicCategory.MaintenanceTopic,
                index + 1,
                15))
            .ToList();
    }

    private static Dictionary<OpeningLineKey, HashSet<string>> BuildReviewedBranchesByLine(IReadOnlyList<OpeningReviewItem> reviewItems)
    {
        Dictionary<OpeningLineKey, HashSet<string>> result = new();
        foreach (OpeningReviewItem item in reviewItems)
        {
            if (!item.OpeningLineKey.HasValue)
            {
                continue;
            }

            if (!result.TryGetValue(item.OpeningLineKey.Value, out HashSet<string>? branchKeys))
            {
                branchKeys = new HashSet<string>(StringComparer.Ordinal);
                result[item.OpeningLineKey.Value] = branchKeys;
            }

            branchKeys.Add(item.BranchKey.Value);
        }

        return result;
    }

    private static HashSet<string> BuildLegacyReviewedBranches(IReadOnlyList<OpeningReviewItem> reviewItems)
    {
        return reviewItems
            .Where(item => !item.OpeningLineKey.HasValue)
            .Select(item => item.BranchKey.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static int GetReviewedCountForLine(
        OpeningLineCatalogItem line,
        IReadOnlyDictionary<OpeningLineKey, HashSet<string>> reviewedBranchesByLine,
        HashSet<string> legacyReviewedBranches)
    {
        if (reviewedBranchesByLine.TryGetValue(line.LineKey, out HashSet<string>? branchKeys))
        {
            return branchKeys.Count;
        }

        return reviewedBranchesByLine.Count == 0 ? legacyReviewedBranches.Count : 0;
    }
}
