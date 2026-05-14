namespace MoveMentorChess.Training;

public sealed class OpeningTrainingNextActionService
{
    public IReadOnlyList<TrainingNextAction> BuildNextActions(TrainingSessionOutcomeSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        List<TrainingNextAction> actions = [];
        if (summary.WrongCount > 0)
        {
            bool heavyRepair = summary.WrongCount >= 3
                || (summary.CompletedCount > 0 && (double)summary.WrongCount / summary.CompletedCount >= 0.35);
            actions.Add(new TrainingNextAction(
                "repeat-now",
                TrainingNextActionKind.RepeatNow,
                heavyRepair ? "Repeat a smaller repair pass" : "Repeat this line now",
                BuildRepairRepeatReason(summary, heavyRepair),
                "Repeat now",
                100));
            actions.Add(new TrainingNextAction(
                "repair-weak-branches",
                TrainingNextActionKind.RepairWeakBranches,
                "Open priorities",
                "Use this later if you want to inspect the broader weak-branch list.",
                "Open priorities",
                90));
        }
        else if (summary.PlayableCount > 0 || summary.HintCount > 0)
        {
            actions.Add(new TrainingNextAction(
                "repeat-after-break",
                TrainingNextActionKind.RepeatAfterBreak,
                "Repeat after a short break",
                "Good session. The line is close, but hints or playable alternatives show it is not automatic yet.",
                "Repeat after break",
                90,
                10));
            actions.Add(new TrainingNextAction(
                "browse-another-opening",
                TrainingNextActionKind.BrowseAnotherOpening,
                "Train another opening",
                "Use the recommendation list if you want a fresh line instead of another repetition.",
                "Browse openings",
                60));
        }
        else
        {
            actions.Add(new TrainingNextAction(
                "return-tomorrow",
                TrainingNextActionKind.ReturnTomorrow,
                "Return tomorrow",
                "Clean session. Let spacing do its work and revisit the line tomorrow.",
                "Back to selection",
                80,
                1440));
            actions.Add(new TrainingNextAction(
                "browse-another-opening",
                TrainingNextActionKind.BrowseAnotherOpening,
                "Move to another branch",
                "Keep momentum by choosing another recommended opening or branch.",
                "Browse openings",
                70));
        }

        return actions
            .OrderByDescending(action => action.Priority)
            .ThenBy(action => action.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildRepairRepeatReason(TrainingSessionOutcomeSummary summary, bool heavyRepair)
    {
        if (heavyRepair)
        {
            return "Useful diagnostic session. Repeat fewer positions and focus on the first repair target.";
        }

        return summary.WrongCount == 1
            ? "Good session overall. One quick repeat will lock in the weak move while it is fresh."
            : "Good session overall. A quick repeat will lock in the weak moves while they are fresh.";
    }

    public IReadOnlyList<OpeningTrainingScheduledAction> BuildScheduledActions(
        string playerKey,
        OpeningTrainingSessionResult sessionResult,
        IReadOnlyList<TrainingNextAction> nextActions,
        DateTime createdUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerKey);
        ArgumentNullException.ThrowIfNull(sessionResult);
        ArgumentNullException.ThrowIfNull(nextActions);

        string normalizedPlayerKey = playerKey.Trim().ToLowerInvariant();
        DateTime created = createdUtc.ToUniversalTime();
        OpeningTrainingRecordedAttempt? focusAttempt = sessionResult.Attempts
            .OrderByDescending(attempt => attempt.Score == OpeningTrainingScore.Wrong)
            .ThenByDescending(attempt => attempt.RecordedUtc)
            .FirstOrDefault();
        OpeningLineKey? lineKey = focusAttempt?.OpeningLineKey
            ?? sessionResult.ReviewItems?.Select(item => item.OpeningLineKey).FirstOrDefault(key => key.HasValue);
        OpeningBranchKey? branchKey = focusAttempt?.BranchKey
            ?? sessionResult.ReviewItems?.Select(item => (OpeningBranchKey?)item.BranchKey).FirstOrDefault(key => key.HasValue);
        OpeningPositionKey? positionKey = focusAttempt?.PositionKey
            ?? sessionResult.ReviewItems?.Select(item => (OpeningPositionKey?)item.PositionKey).FirstOrDefault(key => key.HasValue);

        return nextActions
            .Where(action => action.DelayMinutes > 0
                || action.Kind is TrainingNextActionKind.RepeatNow
                    or TrainingNextActionKind.RepeatAfterBreak
                    or TrainingNextActionKind.ReturnTomorrow
                    or TrainingNextActionKind.RepairWeakBranches)
            .Select(action => new OpeningTrainingScheduledAction(
                BuildScheduledActionId(sessionResult.SessionId, action.Id),
                normalizedPlayerKey,
                sessionResult.SessionId,
                action.Kind,
                lineKey,
                branchKey,
                positionKey,
                created,
                created.AddMinutes(Math.Max(0, action.DelayMinutes)),
                OpeningTrainingScheduledActionStatus.Pending,
                null,
                action.Priority,
                action.Id))
            .ToList();
    }

    public static string BuildScheduledActionId(string sessionId, string sourceActionId)
    {
        return $"{sessionId}:{sourceActionId}";
    }
}
