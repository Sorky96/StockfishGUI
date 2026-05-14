namespace MoveMentorChess.Training;

public sealed class OpeningTrainingResultPlanService
{
    private const string DontKnowSubmittedMove = "I do not know";

    public TrainingResultLearningPlan BuildPlan(
        TrainingSessionOutcomeSummary summary,
        IReadOnlyList<OpeningTrainingAttemptResult> attempts,
        IReadOnlyList<TrainingNextAction> nextActions)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(nextActions);

        IReadOnlyList<TrainingResultReviewItem> reviewItems = BuildReviewItems(attempts);
        TrainingNextAction? primaryAction = nextActions
            .OrderByDescending(action => action.Priority)
            .FirstOrDefault();

        return new TrainingResultLearningPlan(
            $"Completed: {summary.CompletedCount}/{summary.PositionCount}",
            BuildRepeatText(reviewItems),
            BuildNextReviewText(primaryAction),
            BuildReasonText(summary, attempts),
            reviewItems);
    }

    private static IReadOnlyList<TrainingResultReviewItem> BuildReviewItems(IReadOnlyList<OpeningTrainingAttemptResult> attempts)
    {
        return attempts
            .Where(attempt => attempt.Score == OpeningTrainingScore.Wrong || attempt.ShouldRepeatImmediately)
            .GroupBy(attempt => attempt.PositionId, StringComparer.Ordinal)
            .Select(group =>
            {
                OpeningTrainingAttemptResult strongest = group
                    .OrderByDescending(GetReviewPriority)
                    .ThenByDescending(attempt => attempt.ShouldRepeatImmediately)
                    .First();

                return new TrainingResultReviewItem(
                    strongest.PositionId,
                    BuildMoveText(strongest),
                    BuildReviewReason(strongest),
                    GetReviewPriority(strongest),
                    BuildAttemptedMoveText(strongest),
                    BuildPreparedMoveText(strongest),
                    BuildPriorityText(strongest));
            })
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.MoveText, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildRepeatText(IReadOnlyList<TrainingResultReviewItem> reviewItems)
    {
        if (reviewItems.Count == 0)
        {
            return "To review: no urgent positions from this run.";
        }

        TrainingResultReviewItem first = reviewItems[0];
        return reviewItems.Count == 1
            ? $"Revisit: {first.MoveText}"
            : $"Revisit: {first.MoveText} and {FormatCount(reviewItems.Count - 1, "more position", "more positions")}";
    }

    private static string BuildNextReviewText(TrainingNextAction? action)
    {
        if (action is null)
        {
            return "Next review: no scheduled action yet.";
        }

        if (action.DelayMinutes <= 0)
        {
            return $"Next review: now - {action.Title}.";
        }

        if (action.DelayMinutes >= 1440)
        {
            return $"Next review: tomorrow - {action.Title}.";
        }

        return $"Next review: in {action.DelayMinutes} minutes - {action.Title}.";
    }

    private static string BuildReasonText(
        TrainingSessionOutcomeSummary summary,
        IReadOnlyList<OpeningTrainingAttemptResult> attempts)
    {
        int dontKnowCount = attempts.Count(IsDontKnowAttempt);
        if (dontKnowCount > 0)
        {
            return FormatCount(dontKnowCount, "position was", "positions were") + " marked I don't know.";
        }

        if (summary.WrongCount > 0)
        {
            return summary.WrongCount == 1
                ? "One wrong attempt needs reinforcement while the position is still fresh."
                : $"{summary.WrongCount} wrong attempts need reinforcement while the positions are still fresh.";
        }

        if (summary.HintCount > 0)
        {
            return FormatCount(summary.HintCount, "hint was", "hints were") + " used, so recall is close but not automatic.";
        }

        if (summary.PlayableCount > 0)
        {
            return "Playable alternatives were accepted, but the main line should still become automatic.";
        }

        return "Clean line, so spacing is enough for the next review.";
    }

    private static string BuildMoveText(OpeningTrainingAttemptResult attempt)
    {
        if (IsDontKnowAttempt(attempt))
        {
            return "Incorrect attempt: I don't know";
        }

        if (!string.IsNullOrWhiteSpace(attempt.ResolvedSan))
        {
            return $"Incorrect attempt: {attempt.ResolvedSan}";
        }

        if (!string.IsNullOrWhiteSpace(attempt.SubmittedMoveText))
        {
            return $"Incorrect attempt: {attempt.SubmittedMoveText}";
        }

        return attempt.PositionId;
    }

    private static string BuildAttemptedMoveText(OpeningTrainingAttemptResult attempt)
    {
        if (IsDontKnowAttempt(attempt))
        {
            return "I don't know";
        }

        return !string.IsNullOrWhiteSpace(attempt.ResolvedSan)
            ? attempt.ResolvedSan!
            : !string.IsNullOrWhiteSpace(attempt.SubmittedMoveText)
                ? attempt.SubmittedMoveText
                : "unknown";
    }

    private static string BuildPreparedMoveText(OpeningTrainingAttemptResult attempt)
    {
        OpeningTrainingMoveOption? preferred = attempt.PreferredReferences.FirstOrDefault()
            ?? attempt.ExpectedMoves.FirstOrDefault(move => move.IsPreferred)
            ?? attempt.ExpectedMoves.FirstOrDefault();
        return preferred?.DisplayText ?? preferred?.Uci ?? "not available";
    }

    private static string BuildReviewReason(OpeningTrainingAttemptResult attempt)
    {
        if (IsDontKnowAttempt(attempt))
        {
            return "I don't know";
        }

        if (attempt.ShouldRepeatImmediately)
        {
            return "wrong attempt";
        }

        return attempt.Score == OpeningTrainingScore.Wrong
            ? "wrong attempt"
            : "needs repeat";
    }

    private static string BuildPriorityText(OpeningTrainingAttemptResult attempt)
    {
        int priority = GetReviewPriority(attempt);
        return priority >= 100 ? "High" : priority >= 80 ? "Medium" : "Low";
    }

    private static int GetReviewPriority(OpeningTrainingAttemptResult attempt)
    {
        if (IsDontKnowAttempt(attempt))
        {
            return 120;
        }

        if (attempt.ShouldRepeatImmediately)
        {
            return 100;
        }

        return attempt.Score == OpeningTrainingScore.Wrong ? 90 : 50;
    }

    private static bool IsDontKnowAttempt(OpeningTrainingAttemptResult attempt)
        => string.Equals(attempt.SubmittedMoveText, DontKnowSubmittedMove, StringComparison.OrdinalIgnoreCase);

    private static string FormatCount(int count, string singular, string plural)
        => count == 1 ? $"1 {singular}" : $"{count} {plural}";
}
