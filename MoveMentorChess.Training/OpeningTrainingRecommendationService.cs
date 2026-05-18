namespace MoveMentorChess.Training;

public sealed class OpeningTrainingRecommendationService
{
    private readonly IClock clock;

    public OpeningTrainingRecommendationService()
        : this(SystemClock.Instance)
    {
    }

    public OpeningTrainingRecommendationService(IClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public TrainingRecommendationCard? Recommend(
        string? playerKey,
        IReadOnlyList<OpeningLineCatalogItem> availableLines,
        IReadOnlyList<OpeningReviewItem> reviewItems,
        IReadOnlyList<OpeningTrainingSessionResult> sessionResults,
        IReadOnlyList<OpeningTrainingScheduledAction>? dueActions = null)
    {
        ArgumentNullException.ThrowIfNull(availableLines);
        ArgumentNullException.ThrowIfNull(reviewItems);
        ArgumentNullException.ThrowIfNull(sessionResults);

        if (availableLines.Count == 0)
        {
            return null;
        }

        TrainingRecommendationCard? dueRecommendation = BuildDueActionRecommendation(availableLines, dueActions);
        if (dueRecommendation is not null)
        {
            return dueRecommendation;
        }

        DateTime now = clock.UtcNow;
        Dictionary<OpeningLineKey, HashSet<string>> reviewedBranchesByLine = BuildReviewedBranchesByLine(reviewItems);
        HashSet<string> legacyReviewedBranchKeys = reviewItems
            .Where(item => !item.OpeningLineKey.HasValue)
            .Select(item => item.BranchKey.Value)
            .ToHashSet(StringComparer.Ordinal);

        Dictionary<string, int> wrongByEco = sessionResults
            .SelectMany(result => result.Attempts)
            .Where(attempt => attempt.Score == OpeningTrainingScore.Wrong)
            .GroupBy(attempt => attempt.Eco, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, DateTime> lastCompletedByEco = sessionResults
            .SelectMany(result => result.RelatedOpenings.Select(eco => new { Eco = eco, result.CompletedUtc }))
            .GroupBy(item => item.Eco, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Max(item => item.CompletedUtc), StringComparer.OrdinalIgnoreCase);

        ScoredRecommendation best = availableLines
            .Select(line => ScoreLine(line, reviewedBranchesByLine, legacyReviewedBranchKeys, wrongByEco, lastCompletedByEco, now, playerKey))
            .OrderByDescending(item => item.Priority)
            .ThenByDescending(item => item.Line.BookGameCount)
            .ThenBy(item => item.Line.DisplayName, StringComparer.OrdinalIgnoreCase)
            .First();

        return new TrainingRecommendationCard(
            best.Line,
            best.EstimatedDurationMinutes,
            best.Difficulty,
            best.ReasonCode,
            best.RecommendationType,
            best.Reason,
            "Start guided study",
            "Browse all openings",
            Math.Round(best.Priority, 2));
    }

    private static ScoredRecommendation ScoreLine(
        OpeningLineCatalogItem line,
        IReadOnlyDictionary<OpeningLineKey, HashSet<string>> reviewedBranchesByLine,
        HashSet<string> legacyReviewedBranchKeys,
        IReadOnlyDictionary<string, int> wrongByEco,
        IReadOnlyDictionary<string, DateTime> lastCompletedByEco,
        DateTime now,
        string? playerKey)
    {
        int wrongAttempts = wrongByEco.TryGetValue(line.Eco, out int wrong) ? wrong : 0;
        int reviewedForLine = GetReviewedCountForLine(line, reviewedBranchesByLine, legacyReviewedBranchKeys);
        int estimatedBranchGap = Math.Max(0, line.BookBranchCount - reviewedForLine);
        int theoryWeight = Math.Min(line.BookGameCount, 50);
        double staleDays = lastCompletedByEco.TryGetValue(line.Eco, out DateTime lastCompleted)
            ? Math.Max(0, (now - lastCompleted).TotalDays)
            : 21;

        double priority = theoryWeight * 0.35
            + estimatedBranchGap * 2.2
            + wrongAttempts * 30.0
            + Math.Min(staleDays, 21) * 0.7;

        TrainingRecommendationReasonCode reasonCode;
        TrainingRecommendationType recommendationType;
        string reason;

        if (wrongAttempts > 0)
        {
            reasonCode = TrainingRecommendationReasonCode.WeakRecentHistory;
            recommendationType = TrainingRecommendationType.Recovery;
            reason = $"{wrongAttempts} recent wrong attempt(s) in this ECO make it the best repair target today.";
        }
        else if (estimatedBranchGap > 0 && reviewedForLine > 0)
        {
            reasonCode = TrainingRecommendationReasonCode.CoverageGap;
            recommendationType = TrainingRecommendationType.Personalized;
            reason = $"Your history has not covered about {estimatedBranchGap} common branch(es) from this line yet.";
        }
        else if (lastCompletedByEco.ContainsKey(line.Eco))
        {
            reasonCode = TrainingRecommendationReasonCode.RevisitDue;
            recommendationType = TrainingRecommendationType.Personalized;
            reason = staleDays >= 1
                ? $"This line has been quiet for {Math.Round(staleDays)} day(s), so it is due for a short refresh."
                : "You trained this ECO recently; a quick repetition can make the line automatic.";
        }
        else if (!string.IsNullOrWhiteSpace(playerKey))
        {
            reasonCode = TrainingRecommendationReasonCode.HighValueTheory;
            recommendationType = TrainingRecommendationType.Exploration;
            reason = "No personal review data exists for this line yet, so the trainer picked a high-value theory branch.";
        }
        else
        {
            reasonCode = TrainingRecommendationReasonCode.StartHere;
            recommendationType = TrainingRecommendationType.General;
            reason = "This is a practical starting point from the local opening book.";
        }

        int estimatedDuration = Math.Clamp(4 + line.BookBranchCount / 2, 5, 15);
        TrainingRecommendationDifficulty difficulty = line.BookBranchCount switch
        {
            >= 8 => TrainingRecommendationDifficulty.Hard,
            >= 4 => TrainingRecommendationDifficulty.Medium,
            _ => TrainingRecommendationDifficulty.Easy
        };

        return new ScoredRecommendation(
            line,
            priority,
            estimatedDuration,
            difficulty,
            reasonCode,
            recommendationType,
            reason);
    }

    private sealed record ScoredRecommendation(
        OpeningLineCatalogItem Line,
        double Priority,
        int EstimatedDurationMinutes,
        TrainingRecommendationDifficulty Difficulty,
        TrainingRecommendationReasonCode ReasonCode,
        TrainingRecommendationType RecommendationType,
        string Reason);

    private static TrainingRecommendationCard? BuildDueActionRecommendation(
        IReadOnlyList<OpeningLineCatalogItem> availableLines,
        IReadOnlyList<OpeningTrainingScheduledAction>? dueActions)
    {
        OpeningTrainingScheduledAction? dueAction = dueActions?
            .Where(action => action.LineKey.HasValue)
            .Where(IsScheduledReminder)
            .OrderByDescending(action => action.Priority)
            .ThenBy(action => action.DueUtc)
            .FirstOrDefault(action => action.LineKey.HasValue && availableLines.Any(line => line.LineKey.Equals(action.LineKey.Value)));
        if (dueAction is null || !dueAction.LineKey.HasValue)
        {
            return null;
        }

        OpeningLineCatalogItem line = availableLines.First(item => item.LineKey.Equals(dueAction.LineKey.Value));
        return new TrainingRecommendationCard(
            line,
            Math.Clamp(4 + line.BookBranchCount / 2, 5, 15),
            TrainingRecommendationDifficulty.Easy,
            TrainingRecommendationReasonCode.RevisitDue,
            TrainingRecommendationType.Personalized,
            dueAction.Kind switch
            {
                TrainingNextActionKind.RepeatAfterBreak => "A scheduled short-break repetition is due now.",
                TrainingNextActionKind.ReturnTomorrow => "Yesterday's clean line is due for spaced repetition.",
                TrainingNextActionKind.RepairWeakBranches => "A scheduled weak-branch repair is due now.",
                _ => "A scheduled opening review is due now."
            },
            "Start due review",
            "Browse all openings",
            Math.Round(10_000d + dueAction.Priority, 2));
    }

    private static bool IsScheduledReminder(OpeningTrainingScheduledAction action)
        => action.DueUtc > action.CreatedUtc;

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

    private static int GetReviewedCountForLine(
        OpeningLineCatalogItem line,
        IReadOnlyDictionary<OpeningLineKey, HashSet<string>> reviewedBranchesByLine,
        HashSet<string> legacyReviewedBranchKeys)
    {
        if (reviewedBranchesByLine.TryGetValue(line.LineKey, out HashSet<string>? branchKeys))
        {
            return branchKeys.Count;
        }

        return reviewedBranchesByLine.Count == 0 ? legacyReviewedBranchKeys.Count : 0;
    }
}
